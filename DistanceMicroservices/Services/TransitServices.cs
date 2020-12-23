using Dapper;
using DistanceMicroservices.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using Polly;
using System.Threading.Tasks;

namespace DistanceMicroservices.Services
{
    public class TransitServices
    {
        public ILogger _logger { get; set; }

        public TransitServices(ILogger log = null)
        {
            _logger = log;
        }


        public async Task<Dictionary<string, UPSTransitData>> GetBusinessDaysInTransit(List<string> branches, string destinationZip)
        {
            try
            {
                if (branches == null || branches.Count() == 0)
                {
                    var msg = "No Branch Numbers provided.";
                    _logger?.LogWarning(msg);
                    throw new ArgumentNullException("branches", msg);
                }

                var _locationServices = new LocationServices(_logger);

                // Init transitDict
                var transitDict = branches.ToDictionary(b => b, b => new UPSTransitData());

                // Query DB for existing transit data and set in transitDict
                await SetTransitData(destinationZip, branches, transitDict);

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
                        await SetMissingDaysInTransitData(destinationZip, branchesMissingData, transitDict);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(@"Exception in getting missing business days in transit: {0}", ex);
                    }
                }

                return transitDict;
            }
            catch (Exception ex)
            {
                var title = "Exception in GetBusinessDaysInTransit";
                _logger?.LogError(@"{0}: {1}", title, ex);
#if !DEBUG
                var teamsMessage = new TeamsMessage(title, $"Error message: {ex.Message}. Stacktrace: {ex.StackTrace}", "red", errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
#endif
                throw;
            }
        }


        public async Task SetTransitData(string zipCode, List<string> branches, Dictionary<string, UPSTransitData> transitDict)
        {
            try
            {
                _logger?.LogInformation("SetTransitData start");
                using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
                {
                    conn.Open();

                    var query = @"
                        SELECT BranchNumber, BusinessTransitDays, SaturdayDelivery 
                        FROM Data.DistributionCenterDistance 
                        WHERE ZipCode = @zipCode AND BranchNumber in @branches";

                    var results = await conn.QueryAsync<UPSTransitData>(query, new { zipCode, branches }, commandTimeout: 240);

                    conn.Close();

                    // Add results to transitDict
                    foreach (var row in results)
                    {
                        var branchNum = row.BranchNumber;

                        transitDict[branchNum] = new UPSTransitData(row.BranchNumber, row.BusinessTransitDays, row.SaturdayDelivery);
                    }
                }
                _logger?.LogInformation("SetTransitData finish");
            }
            catch (Exception ex)
            {
                _logger.LogError(@"Exception in RequestTransitData: {0}", ex);
                throw;
            }
        }


        public async Task SetMissingDaysInTransitData(string destinationZip, List<string> branchesMissingData, Dictionary<string, UPSTransitData> transitDict)
        {
            _logger?.LogInformation("SetMissingDaysInTransitData start");
            var transitTasks = new List<Task>();

            foreach (var branchNum in branchesMissingData)
            {
                var originZip = transitDict[branchNum].BranchZip;

                if (string.IsNullOrEmpty(originZip)) continue;

                transitTasks.Add(SetUPSTransitData(branchNum, originZip, destinationZip, transitDict));
            }

            await Task.WhenAll(transitTasks);
            _logger?.LogInformation("SetMissingDaysInTransitData finish");
        }


        public async Task SetUPSTransitData(string branchNum, string originZip, string destinationZip, Dictionary<string, UPSTransitData> transitDict)
        {
            try
            {
                _logger?.LogInformation("SetUPSTransitData start");
                _logger?.LogInformation($"Branch zip: {originZip}. Destination zip: {destinationZip}");

                var url = @"https://ups-microservices.azurewebsites.net/api/tnt";
                var client = new RestClient(url);

                var request = new RestRequest(Method.GET)
                    .AddQueryParameter("code", Environment.GetEnvironmentVariable("UPS_MICROSERVICE_KEY"))
                    .AddQueryParameter("originZip", originZip.Substring(0, 5))
                    .AddQueryParameter("destinationZip", destinationZip);

                // Call UPS to get time in transit
                var response = await client.ExecuteAsync(request);
                var jsonResponse = response.Content;

                if (string.IsNullOrEmpty(jsonResponse))
                {
                    _logger.LogWarning($"UPS returned null response for origin zip {originZip}.");
                    return;
                }

                var tnt = JsonConvert.DeserializeObject<TimeInTransitResponse>(jsonResponse);

                // Add business days in transit to transitDict
                transitDict[branchNum].BusinessTransitDays = tnt.BusinessTransitDays;
                _logger?.LogInformation($"Business days in transit {transitDict[branchNum].BusinessTransitDays}");
                transitDict[branchNum].SaturdayDelivery = tnt.SaturdayDelivery;
                transitDict[branchNum].RequiresSaving = true;

                _logger?.LogInformation("SetUPSTransitData finish");
            }
            catch (Exception ex)
            {
                _logger?.LogError(@"Exception in SetMissingDaysInTransitData: {0}", ex);
            }
        }
    }
}
