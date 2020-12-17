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
        [QueryStringParameter("branch", "Branch Number", Required = true)]
        [ProducesResponseType(typeof(Dictionary<string, double?>), 200)]
        [ProducesResponseType(typeof(BadRequestObjectResult), 400)]
        [ProducesResponseType(typeof(NotFoundObjectResult), 404)]
        [ProducesResponseType(typeof(ObjectResult), 500)]
        public static async Task<IActionResult> GetBranchDistancesByZipCode(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "distance/{zipCode}")] HttpRequest req,
            string zipCode,
            ILogger log)
        {
            try
            {
                var query = HttpUtility.ParseQueryString(req.QueryString.ToString());
                var branchNumArr = query.Get("branch")?.Split(",");
                log.LogInformation(@"Branch numbers: {0}", branchNumArr);

                if (branchNumArr == null)
                {
                    log.LogWarning("No Branch Numbers provided.");
                    return new BadRequestObjectResult("Missing branch numbers")
                    {
                        Value = "Please provide at least one branch number as a query parameter.",
                        StatusCode = 400
                    };
                }

                var _services = new DistanceServices(log);
                var branches = new List<string>(branchNumArr);

                var distanceDict = branches.ToDictionary(b => b, b => (double?)0.0);

                await _services.SetBranchDistances(zipCode, branches, distanceDict);

                // Check for branches missing distance in meters
                var branchesMissingDistance = distanceDict.Where(b => b.Value == null || b.Value == 0)
                    .Select(b => b.Key).ToList();

                if (branchesMissingDistance.Any())
                {
                    log.LogInformation(@"Branches Missing Distance: {0}", branchesMissingDistance);

                    try
                    {
                        // Get missing distance from Google Distance Matrix API
                        var missingDistanceData = await _services.GetMissingBranchDistances(zipCode, branchesMissingDistance);

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
