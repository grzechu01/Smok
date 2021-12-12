using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Data.SqlClient;
using System.IO;
using System.Threading.Tasks;

namespace Smok
{
    public static class StoreMeasurement
    {
        private const string ConnectionString = "Server=tcp:smok-database-server.database.windows.net,1433;Initial Catalog=smok-database;Persist Security Info=False;User ID=smok;Password=4987dd70-5cdc-4c1f-adc9-166b1972917c;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
        private const string InsertSql = "INSERT INTO [dbo].[Measurements] " +
            "([Timestamp] ,[Temperature] ,[Humidity] ,[Pressure] ,[PM1] ,[PM25] ,[PM10]) " +
            "VALUES(@timestamp, @temp, @humidity, @pressure, @pm1, @pm25, @pm10); ";

        [FunctionName("store")]
        public static async Task<ActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req, ILogger log)
        {
            var reader = new StreamReader(req.Body);
            var requestBody = await reader.ReadToEndAsync();
            var dto = JsonConvert.DeserializeObject<MeasurementRequestDto>(requestBody);

            await using var db = new SqlConnection(ConnectionString);
            var affected = await db.ExecuteAsync(InsertSql, new { temp = dto.Temperature, humidity = dto.Humidity, pressure = dto.Pressure, pm1 = dto.PM1, pm25 = dto.PM25, pm10 = dto.PM10, timestamp = DateTime.UtcNow });

            return new OkObjectResult($"affected rows: {affected}");
        }
    }
}