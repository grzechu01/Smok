namespace SmokViewer.Data
{
    public record MeasurementEntity(DateTime Timestamp, double Temperature, double Pressure, double Humidity, double PM1, double PM25, double PM10)
    {
        public MeasurementEntity()
            : this(default!, default!, default!, default!, default!, default!, default!)
        {

        }
    }
}
