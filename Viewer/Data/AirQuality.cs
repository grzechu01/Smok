namespace SmokViewer.Data
{
    public enum AirQualityLevel
    {
        VeryGood,
        Good,
        Moderate,
        Sufficient,
        Bad,
        VeryBad
    }

    /// <summary>Polish air-quality index (GIOŚ) bands for 1-hour means, in µg/m³.</summary>
    public static class AirQuality
    {
        private static readonly double[] PM25Limits = { 13, 35, 55, 75, 110 };
        private static readonly double[] PM10Limits = { 20, 50, 80, 110, 150 };

        // WHO 2021 air-quality guidelines, 24-hour mean.
        public const double WhoPM25Daily = 15;
        public const double WhoPM10Daily = 45;

        public static AirQualityLevel ForPM25(double value) => Classify(value, PM25Limits);

        public static AirQualityLevel ForPM10(double value) => Classify(value, PM10Limits);

        public static AirQualityLevel Overall(double pm25, double pm10) =>
            (AirQualityLevel)Math.Max((int)ForPM25(pm25), (int)ForPM10(pm10));

        private static AirQualityLevel Classify(double value, double[] limits)
        {
            var index = Array.FindIndex(limits, limit => value <= limit);
            return (AirQualityLevel)(index < 0 ? limits.Length : index);
        }

        public static string Label(this AirQualityLevel level) => level switch
        {
            AirQualityLevel.VeryGood => "Bardzo dobra",
            AirQualityLevel.Good => "Dobra",
            AirQualityLevel.Moderate => "Umiarkowana",
            AirQualityLevel.Sufficient => "Dostateczna",
            AirQualityLevel.Bad => "Zła",
            _ => "Bardzo zła"
        };

        public static string Advice(this AirQualityLevel level) => level switch
        {
            AirQualityLevel.VeryGood or AirQualityLevel.Good => "Dobre warunki na aktywność na zewnątrz.",
            AirQualityLevel.Moderate => "Osoby wrażliwe mogą ograniczyć długi wysiłek na zewnątrz.",
            AirQualityLevel.Sufficient => "Ogranicz aktywność na zewnątrz, zwłaszcza osoby wrażliwe.",
            _ => "Unikaj przebywania na zewnątrz i wietrzenia mieszkania."
        };

        /// <summary>Maps the six index bands onto the four status colours used by the UI.</summary>
        public static string StatusClass(this AirQualityLevel level) => level switch
        {
            AirQualityLevel.VeryGood or AirQualityLevel.Good => "status-good",
            AirQualityLevel.Moderate => "status-warning",
            AirQualityLevel.Sufficient => "status-serious",
            _ => "status-critical"
        };
    }
}
