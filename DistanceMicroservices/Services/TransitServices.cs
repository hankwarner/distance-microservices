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


        public async Task SetTransitData(string zipCode, List<string> branches, Dictionary<string, UPSTransitData> transitDict)
        {
            try
            {
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
                var upsTask = client.ExecuteAsync(request);

                var response = await upsTask;
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

                // Write back to table
                _ = SaveTransitData(tnt, branchNum, destinationZip);

                _logger?.LogInformation("SetUPSTransitData finish");
            }
            catch (Exception ex)
            {
                _logger?.LogError(@"Exception in SetMissingDaysInTransitData: {0}", ex);
            }
        }


        public async Task SaveTransitData(TimeInTransitResponse tnt, string branchNumber, string destinationZip)
        {

            var retryPolicy = Policy.Handle<SqlException>().Retry(3, (ex, count) =>
            {
                var title = "SqlException in SaveTransitData";
                _logger.LogWarning(@"{0}. Retrying...: {1}", title, ex);
                if (count == 3)
                {
#if !DEBUG
                    var teamsMessage = new TeamsMessage(title, $"Error: {ex.Message}. Stacktrace: {ex.StackTrace}", "yellow", DistanceFunctions.errorLogsUrl);
                    teamsMessage.LogToTeams(teamsMessage);
#endif
                    _logger.LogError(@"{0}: {1}", title, ex);
                }
            });

            await retryPolicy.Execute(async () =>
            {
                _logger?.LogInformation("SaveTransitData start");
                var businessDaysInTransit = tnt.BusinessTransitDays;
                _logger?.LogInformation($"businessDaysInTransit { businessDaysInTransit}.");

                using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
                {
                    // Upsert
                    var query = @"
                        IF NOT EXISTS (SELECT * FROM Data.DistributionCenterDistance WHERE BranchNumber = @branchNumber AND ZipCode = @destinationZip)
                            INSERT INTO Data.DistributionCenterDistance (BranchNumber, ZipCode, BusinessTransitDays)
                            VALUES (@branchNumber, @destinationZip, @businessDaysInTransit)
                        ELSE
                            UPDATE Data.DistributionCenterDistance 
                            SET BusinessTransitDays = @businessDaysInTransit
                            WHERE BranchNumber = @branchNumber AND ZipCode = @destinationZip";

                    conn.Open();

                    await conn.ExecuteAsync(query,
                        new { businessDaysInTransit, branchNumber, destinationZip },
                        commandTimeout: 240);

                    conn.Close();
                    _logger?.LogInformation($"Saved data for Branch zip: {branchNumber}. Destination zip: {destinationZip}");
                    _logger?.LogInformation("SaveTransitData finish");
                }
            });
        }
    }
}
