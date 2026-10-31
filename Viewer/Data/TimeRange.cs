namespace SmokViewer.Data
{
    public enum RangePreset
    {
        Day,
        Week,
        Month,
        Year,
        Custom
    }

    /// <summary>A half-open time window [FromUtc, ToUtc) chosen by the user.</summary>
    public sealed record TimeRange(RangePreset Preset, DateTime FromUtc, DateTime ToUtc)
    {
        public TimeSpan Span => ToUtc - FromUtc;

        public static TimeRange ForPreset(RangePreset preset, DateTime nowUtc)
        {
            var span = preset switch
            {
                RangePreset.Day => TimeSpan.FromDays(1),
                RangePreset.Week => TimeSpan.FromDays(7),
                RangePreset.Month => TimeSpan.FromDays(30),
                RangePreset.Year => TimeSpan.FromDays(365),
                _ => throw new ArgumentOutOfRangeException(nameof(preset))
            };

            // Snap the end up to a short step so that visitors within the step share one cached query.
            var step = (preset <= RangePreset.Week ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(15)).Ticks;
            var toUtc = new DateTime((nowUtc.Ticks + step - 1) / step * step, DateTimeKind.Utc);

            return new TimeRange(preset, toUtc - span, toUtc);
        }

        /// <summary>Whole local (Warsaw) days, both ends inclusive.</summary>
        public static TimeRange ForDays(DateOnly fromLocal, DateOnly toLocal) =>
            new(RangePreset.Custom,
                Warsaw.ToUtc(fromLocal.ToDateTime(TimeOnly.MinValue)),
                Warsaw.ToUtc(toLocal.AddDays(1).ToDateTime(TimeOnly.MinValue)));
    }

    /// <summary>How many minutes of readings are folded into one chart point.</summary>
    public sealed record Resolution(int BucketMinutes, string Description)
    {
        public bool IsRaw => BucketMinutes == 0;

        public static readonly Resolution Raw = new(0, "pomiary co ok. 10 min");

        private const int TargetPoints = 400;

        private static readonly Resolution[] Buckets =
        {
            new(60, "średnie godzinowe"),
            new(180, "średnie 3-godzinne"),
            new(360, "średnie 6-godzinne"),
            new(720, "średnie 12-godzinne"),
            new(1440, "średnie dobowe"),
            new(10080, "średnie tygodniowe")
        };

        public static Resolution For(TimeRange range)
        {
            if (range.Span <= TimeSpan.FromDays(2))
                return Raw;

            return Buckets.FirstOrDefault(b => range.Span.TotalMinutes / b.BucketMinutes <= TargetPoints)
                ?? Buckets[^1];
        }
    }

    public static class Warsaw
    {
        public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

        public static DateTime ToLocal(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

        public static DateTime ToUtc(DateTime local)
        {
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            // Local times skipped by the spring DST jump do not exist; move them past the gap.
            if (Zone.IsInvalidTime(local))
                local = local.AddHours(1);
            return TimeZoneInfo.ConvertTimeToUtc(local, Zone);
        }

        public static DateOnly Today(DateTime nowUtc) => DateOnly.FromDateTime(ToLocal(nowUtc));
    }
}
