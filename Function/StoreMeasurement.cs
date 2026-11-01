using System.Data.SqlClient;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Smok
{
    public class StoreMeasurement
    {
        private static readonly string ConnectionString = Environment.GetEnvironmentVariable("SmokSql")
            ?? throw new InvalidOperationException("Missing 'SmokSql' app setting.");

        // Timestamp is the server's UtcNow at insert, so Id order matches Timestamp order. The Viewer's range
        // queries rely on that (Viewer/Data/MeasurementsService.cs); storing device time or back-filling rows
        // would need an index on Timestamp there instead.
        private const string InsertSql = "INSERT INTO [dbo].[Measurements] " +
            "([Timestamp] ,[Temperature] ,[Humidity] ,[Pressure] ,[PM1] ,[PM25] ,[PM10]) " +
            "VALUES(@timestamp, @temp, @humidity, @pressure, @pm1, @pm25, @pm10); ";

        // Same leniency as the Newtonsoft.Json reader used by the in-process version: any key casing,
        // numbers sent as strings, and keys the firmware leaves out read as 0.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        private readonly ILogger<StoreMeasurement> _log;

        public StoreMeasurement(ILogger<StoreMeasurement> log)
        {
            _log = log;
        }

        [Function("store")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req)
        {
            MeasurementRequestDto? dto;
            try
            {
                dto = await JsonSerializer.DeserializeAsync<MeasurementRequestDto>(req.Body, JsonOptions, req.HttpContext.RequestAborted);
            }
            catch (JsonException ex)
            {
                _log.LogWarning(ex, "Rejected measurement with invalid JSON");
                return new BadRequestObjectResult("invalid JSON");
            }

            if (dto is null)
                return new BadRequestObjectResult("missing measurement");

            await using var db = new SqlConnection(ConnectionString);
            var affected = await db.ExecuteAsync(InsertSql, new { temp = dto.Temperature, humidity = dto.Humidity, pressure = dto.Pressure, pm1 = dto.PM1, pm25 = dto.PM25, pm10 = dto.PM10, timestamp = DateTime.UtcNow });

            return new OkObjectResult($"affected rows: {affected}");
        }
    }
}
