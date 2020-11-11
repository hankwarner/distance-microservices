using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using AzureFunctions.Extensions.Swashbuckle.Attribute;
using System.Net;
using System.Collections.Generic;
using System.Web;
using DistanceMicroservices.Services;
using DistanceMicroservices.Models;
using System.Linq;

namespace DistanceMicroservices.Functions
{
    public static class TransitFunctions
    {
        [FunctionName("GetDistanceAndTransitDataByZipCode")]
        [QueryStringParameter("branch", "Branch Number", Required = true)]
        [ProducesResponseType((int)HttpStatusCode.OK, Type = typeof(Dictionary<string, DistanceData>))]
        [ProducesResponseType((int)HttpStatusCode.BadRequest, Type = typeof(BadRequestObjectResult))]
        [ProducesResponseType((int)HttpStatusCode.NotFound, Type = typeof(NotFoundObjectResult))]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError, Type = typeof(StatusCodeResult))]
        public static IActionResult GetTransitDataByZipCode(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "transit/{zipCode}")] HttpRequest req,
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
                var _transitServices = new TransitServices(log);
                var _distanceServices = new DistanceServices(log);

                var distanceDataDict = _transitServices.RequestDistanceAndTransitDataByZipCode(zipCode, branches);

                if (distanceDataDict == null || branches.Count != distanceDataDict.Count)
                {
                    try
                    {
                        var branchesWithData = distanceDataDict.Keys.ToList();
                        var branchesMissingDistance = branches.Except(branchesWithData).ToList();

                        var missingDistanceData = _distanceServices.GetMissingBranchDistances(zipCode, branchesMissingDistance);

                        foreach (var newDistance in missingDistanceData)
                        {
                            distanceDataDict.Add(newDistance.Key, new DistanceData
                            {
                                DistanceInMiles = (decimal)newDistance.Value,
                                BranchNumber = newDistance.Key
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, $"Exception when adding missing distance data.");
                        // TODO: send to Teams
                    }
                }

                // If Google did not return any distance data, initialize an empty dictionary with branch numbers
                if (distanceDataDict == null || distanceDataDict.Count() == 0)
                {
                    distanceDataDict = branches
                        .ToDictionary(branchNum => branchNum, branchNum => new DistanceData() { BranchNumber = branchNum });
                }

                if (branches.Count() != distanceDataDict.Count())
                {
                    // Add missing branches to dict
                    foreach (var branch in branches.Except(distanceDataDict.Keys))
                    {
                        distanceDataDict.TryAdd(branch, new DistanceData() { BranchNumber = branch });
                    }
                }

                // Handle missing business days in transit
                var branchesMissingDaysInTransit = distanceDataDict.Values.Where(d => d.BusinessTransitDays == null);

                if (branchesMissingDaysInTransit.Any())
                {
                    try
                    {
                        var _locationServices = new LocationServices(log);
                        // Get the zip codes for the missing branches
                        var originZipCodeDict = _locationServices.GetBranchZipCodes(branchesMissingDaysInTransit
                            .Select(d => d.BranchNumber).ToList());

                        // set origin zip codes
                        foreach (var branch in branchesMissingDaysInTransit)
                        {
                            originZipCodeDict.TryGetValue(branch.BranchNumber, out DistanceData originData);
                            branch.Zip = originData.Zip.Substring(0, 5);
                        }

                        // Call UPS to get business days in transit that are missing
                        _transitServices.SetMissingDaysInTransitData(zipCode, branchesMissingDaysInTransit, distanceDataDict);
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Exception while getting missing business days in transit.");
                    }
                }

                if (distanceDataDict != null && distanceDataDict.Count() > 0)
                    return new OkObjectResult(distanceDataDict);

                var errMessage = $"No distances found for zip code: {zipCode} and branches: {branches}";
                log.LogError(errMessage);

                return new NotFoundObjectResult("Invalid branch number(s)")
                {
                    Value = errMessage,
                    StatusCode = 404
                };
            }
            catch (Exception ex)
            {
                var title = "Exception in GetTransitDataByZipCode";
                log.LogError(ex, title);
                var teamsMessage = new TeamsMessage(title, $"Error message: {ex.Message}. Stacktrace: {ex.StackTrace}", "red", DistanceFunctions.errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);

                return new StatusCodeResult(500);
            }
        }
    }
}
