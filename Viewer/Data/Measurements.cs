namespace SmokViewer.Data
{
    public enum Metric
    {
        Temperature,
        Humidity,
        Pressure,
        PM1,
        PM25,
        PM10
    }

    public readonly record struct Stat(double Avg, double Min, double Max);

    /// <summary>
    /// One chart point: a raw reading (Min = Max = Avg) or a bucket of readings. Count is the number of readings;
    /// WeatherCount excludes readings whose temperature/humidity/pressure sensor returned garbage, and the weather
    /// stats are null when no valid reading is left in the bucket.
    /// </summary>
    public sealed record SeriesPoint(DateTime TimeUtc, int Count, int WeatherCount, Stat? Temperature, Stat? Humidity, Stat? Pressure, Stat PM1, Stat PM25, Stat PM10)
    {
        public Stat? this[Metric metric] => metric switch
        {
            Metric.Temperature => Temperature,
            Metric.Humidity => Humidity,
            Metric.Pressure => Pressure,
            Metric.PM1 => PM1,
            Metric.PM25 => PM25,
            Metric.PM10 => PM10,
            _ => throw new ArgumentOutOfRangeException(nameof(metric))
        };

        public int CountOf(Metric metric) => metric is Metric.Temperature or Metric.Humidity or Metric.Pressure ? WeatherCount : Count;
    }

    public sealed record SeriesResult(TimeRange Range, Resolution Resolution, IReadOnlyList<SeriesPoint> Points);

    public sealed record Weather(DateTime TimestampUtc, double Temperature, double Humidity, double Pressure);

    public sealed record LatestReading(
        DateTime TimestampUtc,
        // Newest valid weather reading from the last hour; null while the weather sensor reports garbage.
        Weather? Weather,
        // A valid weather reading about one hour before Weather, for the trend next to each value.
        Weather? WeatherHourAgo,
        // Particulate readings jump between samples; the air-quality index is defined on 1 h means.
        double PM1Hour,
        double PM25Hour,
        double PM10Hour);
}
