using System.Globalization;
using SmokViewer.Data;

namespace SmokViewer.Shared
{
    public sealed record MetricInfo(Metric Metric, string Label, string ShortLabel, string Unit, int Decimals, string ColorVar);

    /// <summary>Labels, units and formatting shared by cards, charts and the table (Polish locale).</summary>
    public static class Display
    {
        public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pl-PL");

        public static readonly MetricInfo PM25 = new(Metric.PM25, "PM2.5", "PM2.5", "µg/m³", 0, "--series-pm25");
        public static readonly MetricInfo PM10 = new(Metric.PM10, "PM10", "PM10", "µg/m³", 0, "--series-pm10");
        public static readonly MetricInfo PM1 = new(Metric.PM1, "PM1", "PM1", "µg/m³", 0, "--series-pm1");
        public static readonly MetricInfo Temperature = new(Metric.Temperature, "Temperatura", "Temp.", "°C", 1, "--series-temperature");
        public static readonly MetricInfo Humidity = new(Metric.Humidity, "Wilgotność", "Wilg.", "%", 0, "--series-humidity");
        public static readonly MetricInfo Pressure = new(Metric.Pressure, "Ciśnienie", "Ciśn.", "hPa", 0, "--series-pressure");

        public static readonly IReadOnlyList<MetricInfo> All = new[] { PM25, PM10, PM1, Temperature, Humidity, Pressure };

        public static string Number(double value, int decimals) =>
            // No digit grouping (Polish leaves 4-digit numbers like 1025 hPa ungrouped); avoid "-0" for values that round to zero.
            (Math.Round(value, decimals) == 0 ? 0 : value).ToString("F" + decimals, Culture);

        public static string Value(this MetricInfo metric, double value) => $"{Number(value, metric.Decimals)} {metric.Unit}";

        public static string Time(DateTime utc) => Warsaw.ToLocal(utc).ToString("HH:mm", Culture);

        public static string Timestamp(DateTime utc) => Warsaw.ToLocal(utc).ToString("d MMM yyyy, HH:mm", Culture);

        public static string Date(DateTime local) => local.ToString("d MMM yyyy", Culture);

        public static string Ago(DateTime utc, DateTime nowUtc)
        {
            var age = nowUtc - utc;
            if (age < TimeSpan.FromMinutes(1))
                return "przed chwilą";
            if (age < TimeSpan.FromHours(1))
                return $"{(int)age.TotalMinutes} min temu";
            if (age < TimeSpan.FromDays(1))
                return $"{(int)age.TotalHours} godz. temu";
            return Timestamp(utc);
        }

        /// <summary>Human label for a range, e.g. "25 paź – 1 lis 2026" in local dates.</summary>
        public static string RangeLabel(TimeRange range)
        {
            var from = Warsaw.ToLocal(range.FromUtc);
            // The end is exclusive; a range ending at local midnight belongs to the previous day.
            var to = Warsaw.ToLocal(range.ToUtc).AddTicks(-1);
            if (range.Span <= TimeSpan.FromDays(2))
                return $"{from.ToString("d MMM, HH:mm", Culture)} – {to.ToString("d MMM, HH:mm", Culture)}";
            var fromFormat = from.Year == to.Year ? "d MMM" : "d MMM yyyy";
            return $"{from.ToString(fromFormat, Culture)} – {Date(to)}";
        }

        /// <summary>Bucket label for tables, e.g. "1 lis, 13:00–14:00" or "1 lis 2026".</summary>
        public static string Bucket(DateTime utc, Resolution resolution)
        {
            var local = Warsaw.ToLocal(utc);
            return resolution.BucketMinutes switch
            {
                0 => local.ToString("d MMM, HH:mm", Culture),
                < 1440 => $"{local.ToString("d MMM, HH:mm", Culture)}–{local.AddMinutes(resolution.BucketMinutes):HH:mm}",
                1440 => local.ToString("ddd d MMM yyyy", Culture),
                _ => $"tydz. od {local.ToString("d MMM yyyy", Culture)}"
            };
        }
    }
}
