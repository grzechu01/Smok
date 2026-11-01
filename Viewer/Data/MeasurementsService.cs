using System.Data;
using System.Data.SqlClient;
using System.Text;
using Dapper;
using Microsoft.Extensions.Caching.Memory;

namespace SmokViewer.Data
{
    /// <summary>
    /// Reads the Measurements table on an Azure SQL Basic (5 DTU) database, so every query is aggregated
    /// server-side, bounded by a clustered-index seek and cached for a few minutes.
    /// </summary>
    public class MeasurementsService
    {
        // The table has only a clustered PK on Id. Rows are inserted with Timestamp = UtcNow, so Id order
        // matches Timestamp order; scanning backwards from the newest row to the first row older than @from
        // gives the Id where the range starts, and the main query then seeks on Id instead of scanning the table.
        // If the Function ever stores device timestamps or rows get back-filled, this breaks silently: switch to an
        // index on Timestamp instead (see SmokViewer_UX_Audit.md).
        private const string LowerIdBound =
            "DECLARE @lo int = ISNULL((SELECT TOP (1) Id FROM [dbo].[Measurements] WHERE [Timestamp] < @from ORDER BY Id DESC), 0);\n";

        private const string RangeFilter = "m.Id > @lo AND m.[Timestamp] >= @from AND m.[Timestamp] < @to";

        // The weather sensor (temperature/humidity/pressure) sometimes reports zeros or nonsense such as 0 or
        // 2000 hPa; those three values are treated as missing for the row. Particulate readings are kept.
        private const string Clean =
            "CROSS APPLY (SELECT CASE WHEN m.Pressure BETWEEN 950 AND 1060 AND m.Humidity > 0 AND m.Humidity <= 100 " +
            "AND m.Temperature BETWEEN -35 AND 50 THEN 1 ELSE 0 END AS Ok) v ";

        private const string CleanColumns =
            "IIF(v.Ok = 1, m.Temperature, NULL) AS Temperature, IIF(v.Ok = 1, m.Humidity, NULL) AS Humidity, " +
            "IIF(v.Ok = 1, m.Pressure, NULL) AS Pressure, m.PM1, m.PM25, m.PM10";

        private const string RawSql = LowerIdBound +
            "SELECT m.[Timestamp], " + CleanColumns + " FROM [dbo].[Measurements] m " + Clean +
            "WHERE " + RangeFilter + " ORDER BY m.Id;";

        // ~2 hours of readings: enough for the 1 h particulate mean, the trend and a weather fallback.
        private const string LatestSql =
            "SELECT TOP (13) m.[Timestamp], " + CleanColumns + " FROM [dbo].[Measurements] m " + Clean + "ORDER BY m.Id DESC;";

        private static readonly string[] Columns = { "Temperature", "Humidity", "Pressure", "PM1", "PM25", "PM10" };

        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _cache;
        // Basic tier has 5 DTU: range queries queue up here instead of running side by side when many visitors
        // (or a crawler walking custom ranges) arrive at once.
        private readonly SemaphoreSlim _queryGate = new(2, 2);

        public MeasurementsService(IConfiguration configuration, IMemoryCache cache)
        {
            _configuration = configuration;
            _cache = cache;
        }

        public Task<LatestReading?> GetLatestAsync(CancellationToken cancellationToken = default) =>
            _cache.GetOrCreateAsync("latest", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                entry.Size = 1;

                await using var db = Connect();
                var rows = (await db.QueryAsync<RawRow>(new CommandDefinition(LatestSql, cancellationToken: cancellationToken))).ToList();
                if (rows.Count == 0)
                    return null;

                var newest = rows[0].Timestamp;
                var lastHour = rows.Where(r => r.Timestamp > newest.AddMinutes(-60)).ToList();
                var weather = lastHour.FirstOrDefault(r => r.Temperature.HasValue);
                var weatherHourAgo = weather is null
                    ? null
                    : rows.FirstOrDefault(r => r.Temperature.HasValue && r.Timestamp <= weather.Timestamp.AddMinutes(-50) && r.Timestamp >= weather.Timestamp.AddMinutes(-75));

                return new LatestReading(
                    DateTime.SpecifyKind(newest, DateTimeKind.Utc),
                    weather?.ToWeather(),
                    weatherHourAgo?.ToWeather(),
                    PM1Hour: lastHour.Average(r => r.PM1),
                    PM25Hour: lastHour.Average(r => r.PM25),
                    PM10Hour: lastHour.Average(r => r.PM10));
            });

        public async Task<SeriesResult> GetSeriesAsync(TimeRange range, CancellationToken cancellationToken = default)
        {
            var resolution = Resolution.For(range);
            var key = $"series:{range.FromUtc:O}:{range.ToUtc:O}:{resolution.BucketMinutes}";

            // The cached SeriesResult instance itself is returned, so callers can tell unchanged data by reference.
            if (_cache.TryGetValue(key, out SeriesResult? cached))
                return cached!;

            await _queryGate.WaitAsync(cancellationToken);
            try
            {
                // Another caller may have filled the cache while this one waited.
                if (_cache.TryGetValue(key, out cached))
                    return cached!;

                var points = resolution.IsRaw
                    ? await QueryRawAsync(range, cancellationToken)
                    : await QueryBucketsAsync(range, resolution.BucketMinutes, cancellationToken);
                var result = new SeriesResult(range, resolution, points);

                _cache.Set(key, result, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheDuration(range),
                    Size = Math.Max(1, points.Count)
                });
                return result;
            }
            finally
            {
                _queryGate.Release();
            }
        }

        private static TimeSpan CacheDuration(TimeRange range)
        {
            // A range that ended a while ago never changes; recent ranges get a new reading every ~10 min.
            if (range.ToUtc < DateTime.UtcNow.AddHours(-1))
                return TimeSpan.FromHours(6);
            return range.Span <= TimeSpan.FromDays(7) ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(15);
        }

        private async Task<IReadOnlyList<SeriesPoint>> QueryRawAsync(TimeRange range, CancellationToken cancellationToken)
        {
            await using var db = Connect();
            var rows = await db.QueryAsync<RawRow>(new CommandDefinition(RawSql, RangeParameters(range), cancellationToken: cancellationToken));

            return rows.Select(r => new SeriesPoint(
                DateTime.SpecifyKind(r.Timestamp, DateTimeKind.Utc), 1, r.Temperature.HasValue ? 1 : 0,
                Point(r.Temperature), Point(r.Humidity), Point(r.Pressure), Pm(r.PM1), Pm(r.PM25), Pm(r.PM10))).ToList();

            static Stat? Point(double? value) => value is { } v ? new(v, v, v) : null;
            static Stat Pm(double value) => new(value, value, value);
        }

        private async Task<IReadOnlyList<SeriesPoint>> QueryBucketsAsync(TimeRange range, int bucketMinutes, CancellationToken cancellationToken)
        {
            // Buckets are cut in Warsaw local time so that days start at local midnight. SQL gets the UTC
            // offset as a CASE over the DST changes inside the range, which is far cheaper than AT TIME ZONE per row.
            var origin = BucketOrigin(range, bucketMinutes);

            // Start at the first whole bucket: a bucket cut by the range start would hold only part of its
            // readings, and its average and min/max would not be comparable with the others.
            var fromLocal = Warsaw.ToLocal(range.FromUtc);
            var skipped = (long)Math.Ceiling((fromLocal - origin).TotalMinutes / bucketMinutes);
            var queryRange = range with { FromUtc = Warsaw.ToUtc(origin.AddMinutes(skipped * bucketMinutes)) };

            var parameters = RangeParameters(queryRange);
            var periods = OffsetPeriods(queryRange);
            var offset = new StringBuilder(periods.Count > 1 ? "CASE" : "");
            for (var i = 0; i < periods.Count; i++)
            {
                parameters.Add($"o{i}", periods[i].OffsetMinutes);
                if (i > 0)
                    parameters.Add($"t{i}", periods[i].StartUtc, DbType.DateTime2);
            }
            for (var i = 1; i < periods.Count; i++)
                offset.Append($" WHEN m.[Timestamp] < @t{i} THEN @o{i - 1}");
            offset.Append(periods.Count > 1 ? $" ELSE @o{periods.Count - 1} END" : "@o0");

            parameters.Add("origin", origin, DbType.DateTime2);
            parameters.Add("bucket", bucketMinutes);

            var aggregates = string.Join(", ", Columns.Select(c => $"AVG(r.{c}) AS {c}Avg, MIN(r.{c}) AS {c}Min, MAX(r.{c}) AS {c}Max"));
            var sql = LowerIdBound +
                $"SELECT r.Bucket, COUNT(*) AS Count, COUNT(r.Temperature) AS WeatherCount, {aggregates} FROM (" +
                $"SELECT DATEDIFF(minute, @origin, DATEADD(minute, {offset}, m.[Timestamp])) / @bucket AS Bucket, {CleanColumns} " +
                $"FROM [dbo].[Measurements] m {Clean}WHERE {RangeFilter}) r " +
                "GROUP BY r.Bucket ORDER BY r.Bucket;";

            await using var db = Connect();
            var rows = await db.QueryAsync<BucketRow>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

            return rows.Select(r => new SeriesPoint(
                Warsaw.ToUtc(origin.AddMinutes((long)r.Bucket * bucketMinutes)), r.Count, r.WeatherCount,
                r.WeatherCount > 0 ? new(r.TemperatureAvg!.Value, r.TemperatureMin!.Value, r.TemperatureMax!.Value) : null,
                r.WeatherCount > 0 ? new(r.HumidityAvg!.Value, r.HumidityMin!.Value, r.HumidityMax!.Value) : null,
                r.WeatherCount > 0 ? new(r.PressureAvg!.Value, r.PressureMin!.Value, r.PressureMax!.Value) : null,
                new(r.PM1Avg, r.PM1Min, r.PM1Max),
                new(r.PM25Avg, r.PM25Min, r.PM25Max),
                new(r.PM10Avg, r.PM10Min, r.PM10Max))).ToList();
        }

        /// <summary>Local midnight at or before the range start; Monday for weekly buckets.</summary>
        private static DateTime BucketOrigin(TimeRange range, int bucketMinutes)
        {
            var day = Warsaw.ToLocal(range.FromUtc).Date;
            if (bucketMinutes >= 10080)
                day = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
            return day;
        }

        /// <summary>The range split at every Warsaw DST change; Poland switches on whole UTC hours.</summary>
        private static List<(DateTime StartUtc, int OffsetMinutes)> OffsetPeriods(TimeRange range)
        {
            var periods = new List<(DateTime, int)>();
            var hour = new DateTime(range.FromUtc.Year, range.FromUtc.Month, range.FromUtc.Day, range.FromUtc.Hour, 0, 0, DateTimeKind.Utc);
            var current = int.MinValue;
            for (; hour < range.ToUtc; hour = hour.AddHours(1))
            {
                var offset = (int)Warsaw.Zone.GetUtcOffset(hour).TotalMinutes;
                if (offset != current)
                {
                    periods.Add((hour, offset));
                    current = offset;
                }
            }
            return periods;
        }

        private static DynamicParameters RangeParameters(TimeRange range)
        {
            var parameters = new DynamicParameters();
            parameters.Add("from", range.FromUtc, DbType.DateTime2);
            parameters.Add("to", range.ToUtc, DbType.DateTime2);
            return parameters;
        }

        private SqlConnection Connect() => new(_configuration.GetConnectionString("SmokSql"));

        private sealed class RawRow
        {
            public DateTime Timestamp { get; set; }
            public double? Temperature { get; set; }
            public double? Humidity { get; set; }
            public double? Pressure { get; set; }
            public double PM1 { get; set; }
            public double PM25 { get; set; }
            public double PM10 { get; set; }

            public Weather ToWeather() =>
                new(DateTime.SpecifyKind(Timestamp, DateTimeKind.Utc), Temperature!.Value, Humidity!.Value, Pressure!.Value);
        }

        private sealed class BucketRow
        {
            public int Bucket { get; set; }
            public int Count { get; set; }
            public int WeatherCount { get; set; }
            public double? TemperatureAvg { get; set; }
            public double? TemperatureMin { get; set; }
            public double? TemperatureMax { get; set; }
            public double? HumidityAvg { get; set; }
            public double? HumidityMin { get; set; }
            public double? HumidityMax { get; set; }
            public double? PressureAvg { get; set; }
            public double? PressureMin { get; set; }
            public double? PressureMax { get; set; }
            public double PM1Avg { get; set; }
            public double PM1Min { get; set; }
            public double PM1Max { get; set; }
            public double PM25Avg { get; set; }
            public double PM25Min { get; set; }
            public double PM25Max { get; set; }
            public double PM10Avg { get; set; }
            public double PM10Min { get; set; }
            public double PM10Max { get; set; }
        }
    }
}
