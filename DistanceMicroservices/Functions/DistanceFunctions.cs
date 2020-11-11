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

namespace DistanceMicroservices
{
    public class DistanceFunctions
    {
        public static string errorLogsUrl = Environment.GetEnvironmentVariable("ERR_LOGS_URL");

        [FunctionName("GetBranchDistancesByZipCode")]
        [QueryStringParameter("branch", "Branch Number", Required = true)]
        [ProducesResponseType((int)HttpStatusCode.OK, Type = typeof(Dictionary<string, double>))]
        [ProducesResponseType((int)HttpStatusCode.BadRequest, Type = typeof(BadRequestObjectResult))]
        [ProducesResponseType((int)HttpStatusCode.NotFound, Type = typeof(NotFoundObjectResult))]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError, Type = typeof(StatusCodeResult))]
        public static IActionResult GetBranchDistancesByZipCode(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "distance/{zipCode}")] HttpRequest req,
            string zipCode,
            ILogger log)
        {
            try
            {
                var query = HttpUtility.ParseQueryString(req.QueryString.ToString());
                var branchNumArr = query.Get("branch")?.Split(",");
                log.LogInformation(@"Branch numbers:", branchNumArr);

                if (branchNumArr == null)
                {
                    log.LogWarning("No Branch Numbers provided.");
                    return new BadRequestObjectResult("Missing branch numbers")
                    {
                        Value = "Please provide at least one branch number as a query parameter.",
                        StatusCode = 400
                    };
                }

                var branches = new List<string>(branchNumArr);

                var _services = new DistanceServices(log);

                var branchesWithDistance = _services.RequestBranchDistancesByZipCode(zipCode, branches);

                if (branchesWithDistance == null || branches.Count != branchesWithDistance.Count)
                {
                    try
                    {
                        var missingBranches = branches.Where(b1 => !branchesWithDistance.Any(b2 => b1 == b2.Key)).ToList();

                        var missingDistanceData = _services.GetMissingBranchDistances(zipCode, missingBranches);

                        foreach (var newDistance in missingDistanceData)
                        {
                            branchesWithDistance.Add(newDistance.Key, newDistance.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        string errorMessage = $"Exception in GetMissingBranchDistances: ";
                        log.LogError(ex, errorMessage);
                    }
                }

                // If distance data is null for each branch, return error
                var isMissingDistances = branchesWithDistance.Where(b => b.Value == null).Count() == branchesWithDistance.Count;

                if (branchesWithDistance != null && branchesWithDistance.Count() > 0 && !isMissingDistances) 
                    return new OkObjectResult(branchesWithDistance);

                var errMessage = $"No distances found for zip code: {zipCode} and branches: {branches}";
                log.LogError(errMessage);
                return new NotFoundObjectResult("Invalid branch number(s)")
                {
                    Value = errMessage,
                    StatusCode = 404
                };
            }
            catch(Exception ex)
            {
                var title = "Exception in GetBranchDistancesByZipCode";
                log.LogError(ex, title);
                var teamsMessage = new TeamsMessage(title, $"Error message: {ex.Message}. Stacktrace: {ex.StackTrace}", "red", errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);

                return new StatusCodeResult(500);
            }
        }
    }
}
