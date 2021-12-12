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

            var queryResults = await db.QueryAsync<MeasurementEntity>("SELECT * FROM [dbo].[Measurements] ORDER BY Id DESC");

            return queryResults.Select(x => new Measurement(
                  Date: DateOnly.FromDateTime(GetLocalTime(x.Timestamp)),
                Time: TimeOnly.FromDateTime(GetLocalTime(x.Timestamp)),
                Temperature: string.Format("{0:0.0}", x.Temperature),
                Humidity: string.Format("{0:0.0}", x.Humidity),
                Pressure: string.Format("{0:0}", x.Pressure),
                PM1: string.Format("{0:0}", x.PM1),
                PM25: string.Format("{0:0}", x.PM25),
                PM10: string.Format("{0:0}", x.PM10)
               )).ToArray();
        }

        private DateTime GetLocalTime(DateTime dateTime) =>
            TimeZoneInfo.ConvertTime(dateTime, TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time"));
    }
}