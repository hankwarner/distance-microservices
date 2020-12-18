using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Web;
using DistanceMicroservices.Services;
using System.Collections.Generic;
using System.Linq;
using DistanceMicroservices.Models;
using System.Net;
using AzureFunctions.Extensions.Swashbuckle.Attribute;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.IO;
using Newtonsoft.Json;

namespace DistanceMicroservices
{
    public class DistanceFunctions
    {
        public static string errorLogsUrl = Environment.GetEnvironmentVariable("ERR_LOGS_URL");
        public static IConfiguration _config { get; set; }

        public DistanceFunctions(IConfiguration config)
        {
            _config = config;
        }

        [FunctionName("GetBranchDistancesByZipCode")]
        [ProducesResponseType(typeof(Dictionary<string, double?>), 200)]
        [ProducesResponseType(typeof(BadRequestObjectResult), 400)]
        [ProducesResponseType(typeof(NotFoundObjectResult), 404)]
        [ProducesResponseType(typeof(ObjectResult), 500)]
        public static async Task<IActionResult> GetBranchDistancesByZipCode(
            [HttpTrigger(AuthorizationLevel.Function, "POST", Route = "distance/{destinationZip}"), RequestBodyType(typeof(List<string>), "branches")] HttpRequest req,
            string destinationZip,
            ILogger log)
        {
            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                log.LogInformation(@"Request body: {RequestBody}", requestBody);

                var branches = JsonConvert.DeserializeObject<List<string>>(requestBody);

                if (branches == null || branches.Count() == 0)
                {
                    log.LogWarning("No Branch Numbers provided.");
                    return new BadRequestObjectResult("Missing branch numbers")
                    {
                        Value = "Please provide at least one branch number in the request body.",
                        StatusCode = 400
                    };
                }

                var _services = new DistanceServices(log);
                var distanceDict = branches.ToDictionary(b => b, b => (double?)0.0);

                await _services.SetBranchDistances(destinationZip, branches, distanceDict);

                // Check for branches missing distance in meters
                var branchesMissingDistance = distanceDict.Where(b => b.Value == null || b.Value == 0)
                    .Select(b => b.Key).ToList();

                if (branchesMissingDistance.Any())
                {
                    log.LogInformation(@"Branches Missing Distance: {0}", branchesMissingDistance);

                    try
                    {
                        // Get missing distance from Google Distance Matrix API
                        var missingDistanceData = await _services.GetMissingBranchDistances(destinationZip, branchesMissingDistance);

                        // Add to distanceDict response
                        foreach(var distToAdd in missingDistanceData){ distanceDict[distToAdd.Key] = distToAdd.Value; };
                    }
                    catch(Exception ex)
                    {
                        log.LogError(@"Exception getting missing branch distances: {0}", ex);
                    }
                }

                return new OkObjectResult(distanceDict);
            }
            catch(Exception ex)
            {
                var title = "Exception in GetBranchDistancesByZipCode";
                log.LogError(@"{0}: {1}", title, ex);
#if !DEBUG
                var teamsMessage = new TeamsMessage(title, $"Error message: {ex.Message}. Stacktrace: {ex.StackTrace}", "red", errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
#endif
                return new ObjectResult(ex.Message) { StatusCode = 500, Value = "Failure" };
            }
        }
    }
}
