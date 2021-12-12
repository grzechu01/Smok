namespace SmokViewer.Data
{
    public record Measurement(DateOnly Date, TimeOnly Time, string Temperature, string Pressure, string Humidity, string PM1, string PM25, string PM10);
}