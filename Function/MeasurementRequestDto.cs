namespace Smok
{
    public record MeasurementRequestDto(        
        double Temperature,
        double Humidity,
        double Pressure,
        double PM1,
        double PM25,
        double PM10);
}
