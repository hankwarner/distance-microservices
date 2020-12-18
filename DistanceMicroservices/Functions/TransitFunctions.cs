using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using AzureFunctions.Extensions.Swashbuckle.Attribute;
using System.Collections.Generic;
using System.Web;
using DistanceMicroservices.Services;
using DistanceMicroservices.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.IO;
using Newtonsoft.Json;

namespace DistanceMicroservices.Functions
{
    public class TransitFunctions
    {
        public static string errorLogsUrl = Environment.GetEnvironmentVariable("ERR_LOGS_URL");
        public static IConfiguration _config { get; set; }

        public TransitFunctions(IConfiguration config)
        {
            _config = config;
        }


        [FunctionName("GetBusinessDaysInTransit")]
        [ProducesResponseType(typeof(Dictionary<string, UPSTransitData>), 200)]
        [ProducesResponseType(typeof(BadRequestObjectResult), 400)]
        [ProducesResponseType(typeof(NotFoundObjectResult), 404)]
        [ProducesResponseType(typeof(ObjectResult), 500)]
        public static async Task<IActionResult> GetBusinessDaysInTransit(
            [HttpTrigger(AuthorizationLevel.Function, "POST", Route = "transit/{destinationZip}"), RequestBodyType(typeof(List<string>), "branches")] HttpRequest req,
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
                var _locationServices = new LocationServices(log);

                // Init transitDict
                var transitDict = branches.ToDictionary(b => b, b => new UPSTransitData());

                // Query DB for existing transit data and set in transitDict
                await _transitServices.SetTransitData(destinationZip, branches, transitDict);

                // Check if any branches are missing business days in transit
                var branchesMissingData = transitDict.Where(b => b.Value.BusinessTransitDays == null || b.Value.BusinessTransitDays == 0)
                    .Select(b => b.Key).ToList();

                // If any are missing, call UPS and add to transitDict
                if (branchesMissingData.Any())
                {
                    try
                    {
                        // Set the zip codes for the missing branches for the UPS call
                        await _locationServices.SetBranchZipCodes(branchesMissingData, transitDict);

                        // Call UPS to get business days in transit that are missing
                        await _transitServices.SetMissingDaysInTransitData(destinationZip, branchesMissingData, transitDict);
                    }
                    catch (Exception ex)
                    {
                        log.LogError(@"Exception in getting missing business days in transit: {0}", ex);
                    }
                }

                return new OkObjectResult(transitDict);
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
