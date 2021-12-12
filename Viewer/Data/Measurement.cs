namespace SmokViewer.Data
{
    public record Measurement(
        DateOnly Date, 
        TimeOnly Time, 
        double Temperature, 
        double Pressure, 
        double Humidity,
        double PM1,
        double PM25,
        double PM10);
}