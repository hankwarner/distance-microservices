using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using DistanceMicroservices.Services;
using System.Collections.Generic;
using System.Linq;
using DistanceMicroservices.Models;
using AzureFunctions.Extensions.Swashbuckle.Attribute;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.IO;
using Newtonsoft.Json;

namespace DistanceMicroservices.Functions
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
            [HttpTrigger(AuthorizationLevel.Function, "POST", Route = "distance/zip/{destinationZip}"), RequestBodyType(typeof(List<string>), "branches")] HttpRequest req,
            string destinationZip,
            ILogger log)
        {
            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                log.LogInformation(@"Destination zip: {0}. Request body: {1}", destinationZip, requestBody);

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
                var distanceDict = branches.ToDictionary(b => b, b => new DistributionCenterDistance(b));

                await _services.SetBranchDistances(destinationZip, branches, distanceDict);

                // Check for branches missing distance in meters
                var branchesMissingDistance = distanceDict.Where(b => b.Value.DistanceInMeters == null)
                    .Select(b => b.Key).ToList();

                if (branchesMissingDistance.Any())
                {
                    log.LogInformation(@"Branches Missing Distance: {0}", branchesMissingDistance);

                    try
                    {
                        // Get missing distance from Google Distance Matrix API
                        var missingDistanceData = await _services.GetMissingBranchDistances(destinationZip, branchesMissingDistance);

                        // Add to distanceDict response
                        foreach (var distToAdd in missingDistanceData)
                        {
                            distanceDict[distToAdd.BranchNumber] = distToAdd;
                        }
                    }
                    catch (Exception ex)
                    {
                        log.LogError(@"Exception getting missing branch distances: {0}", ex);
                    }
                }

                var response = distanceDict.ToDictionary(d => d.Key, d => d.Value.DistanceInMeters);

                return new OkObjectResult(response);
            }
            catch (Exception ex)
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


        [FunctionName("GetDistanceAndTransitDataByZipCode")]
        [ProducesResponseType(typeof(Dictionary<string, DistanceAndTransitData>), 200)]
        [ProducesResponseType(typeof(BadRequestObjectResult), 400)]
        [ProducesResponseType(typeof(NotFoundObjectResult), 404)]
        [ProducesResponseType(typeof(ObjectResult), 500)]
        public static async Task<IActionResult> GetDistanceAndTransitDataByZipCode(
            [HttpTrigger(AuthorizationLevel.Function, "POST", Route = "distance/transit/{destinationZip}"), RequestBodyType(typeof(List<string>), "branches")] HttpRequest req,
            string destinationZip,
            ILogger log)
        {
            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                log.LogInformation(@"Destination zip: {0}. Request body: {1}", destinationZip, requestBody);

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

                var _transitServices = new TransitServices(log);
                var _locationServices = new DistanceServices(log);

                // Call google and UPS async
                var googleDistanceDataTask = _locationServices.GetBranchDistances(branches, destinationZip);
                var transitDataTask = _transitServices.GetBusinessDaysInTransit(branches, destinationZip);

                await Task.WhenAll(googleDistanceDataTask, transitDataTask);

                // TODO: handle exceptions

                // Combine data from Google and UPS into a single dict
                var distanceDict = googleDistanceDataTask.Result
                    .Join(transitDataTask.Result,
                        dist => dist.Key,
                        trans => trans.Key,
                        (dist, trans) => new DistanceAndTransitData(dist.Key, dist.Value.DistanceInMeters, trans.Value.BusinessTransitDays, trans.Value.SaturdayDelivery, destinationZip) 
                        { 
                            RequiresSaving = dist.Value.RequiresSaving || trans.Value.RequiresSaving
                        })
                    .ToDictionary(d => d.BranchNumber, d => d);

                // async save data
                var distDataToSave = distanceDict.Where(d => d.Value.RequiresSaving).Select(d => d.Value).ToList();

                if (distDataToSave.Any())
                {
                    _ = _locationServices.SaveBranchDistanceData(distDataToSave);
                }

                return new OkObjectResult(distanceDict);
            }
            catch (Exception ex)
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
