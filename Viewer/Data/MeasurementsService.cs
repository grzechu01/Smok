using Dapper;
using System.Data.SqlClient;

namespace SmokViewer.Data
{
    public class MeasurementsService
    {
        private readonly IConfiguration _configuration;

        public MeasurementsService(IConfiguration configuration)
        {

            _configuration = configuration;
        }

        public async Task<IEnumerable<Measurement>> GetMeasurements()
        {
            await using var db = new SqlConnection(_configuration.GetConnectionString("SmokSql"));

            var queryResults = await db.QueryAsync<MeasurementEntity>("SELECT * FROM [dbo].[Measurements] WHERE Timestamp > DATEADD(day, -7, GETDATE()) ORDER BY Id DESC");

            return queryResults?.Select(x => new Measurement(
                  Date: DateOnly.FromDateTime(GetLocalTime(x.Timestamp)),
                Time: TimeOnly.FromDateTime(GetLocalTime(x.Timestamp)),
                Temperature: x.Temperature,
                Humidity: x.Humidity,
                Pressure: x.Pressure,
                PM1: x.PM1,
                PM25: x.PM25,
                PM10: x.PM10)
               ).ToArray() ?? Enumerable.Empty<Measurement>();
        }

        private DateTime GetLocalTime(DateTime dateTime) =>
            TimeZoneInfo.ConvertTime(dateTime, TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"));
    }
}