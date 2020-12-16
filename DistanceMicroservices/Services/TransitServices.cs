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

namespace DistanceMicroservices.Services
{
    public class TransitServices
    {
        public ILogger _logger { get; set; }

        public TransitServices(ILogger log = null)
        {
            _logger = log;
        }


        public Dictionary<string, DistanceData> RequestDistanceAndTransitDataByZipCode(string zipCode, List<string> branches)
        {
            var retryPolicy = Policy.Handle<SqlException>().Retry(3, (ex, count) =>
            {
                const string errorMessage = "SqlException6 in RequestDistanceAndTransitDataByZipCode";
                _logger.LogWarning(ex, $"{errorMessage} . Retrying...");
                if (count == 3)
                {
                    var teamsMessage = new TeamsMessage(errorMessage, $"Error: {ex.Message}. Stacktrace: {ex.StackTrace}", "yellow", DistanceFunctions.errorLogsUrl);
                    teamsMessage.LogToTeams(teamsMessage);
                    _logger.LogError(ex, errorMessage);
                }
            });

            return retryPolicy.Execute(() =>
            {
                var connString = Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN");

                using (var conn = new SqlConnection(connString))
                {
                    conn.Open();

                    var query = @"
                        SELECT BranchNumber, BusinessTransitDays, CEILING(DistanceInMeters * 0.0006213712) DistanceInMiles, SaturdayDelivery 
                        FROM FergusonIntegration.sourcing.DistributionCenterDistance 
                        WHERE ZipCode = @zipCode AND BranchNumber in @branches";

                    var branchesWithDistance = conn.Query<DistanceData>(query, new { zipCode, branches }, commandTimeout: 6)
                        .ToDictionary(row => row.BranchNumber, row => row);

                    conn.Close();

                    return branchesWithDistance;
                }
            });
        }


        public void SetMissingDaysInTransitData(string destinationZip, IEnumerable<DistanceData> branchesMissingDaysInTransit, Dictionary<string, DistanceData> distanceDataDict)
        {
            var url = @"https://ups-microservices.azurewebsites.net/api/tnt";
            var client = new RestClient(url);

            foreach (var branch in branchesMissingDaysInTransit)
            {
                try
                {
                    if (string.IsNullOrEmpty(branch.Zip)) continue;

                    var request = new RestRequest(Method.GET)
                        .AddQueryParameter("code", Environment.GetEnvironmentVariable("AZ_GET_BUSINESS_DAYS_IN_TRANSIT_KEY"))
                        .AddQueryParameter("originZip", branch.Zip)
                        .AddQueryParameter("destinationZip", destinationZip);

                    // Call UPS to get time in transit
                    var response = client.Execute(request).Content;

                    if (string.IsNullOrEmpty(response)) continue;

                    var tnt = JsonConvert.DeserializeObject<TimeInTransitResponse>(response);

                    // Add business days in transit to distance dict
                    distanceDataDict[branch.BranchNumber].BusinessTransitDays = tnt.BusinessTransitDays;
                    distanceDataDict[branch.BranchNumber].SaturdayDelivery = tnt.SaturdayDelivery;

                    // Write back to table
                    SaveDaysInTransitData(tnt, branch.BranchNumber, destinationZip);
                }
                catch (Exception ex)
                {
                    var title = "Exception in SetMissingDaysInTransitData";
                    _logger.LogError(ex, title);
                    var teamsMessage = new TeamsMessage(title, $"Error message: {ex.Message}. Stacktrace: {ex.StackTrace}", "red", DistanceFunctions.errorLogsUrl);
                    teamsMessage.LogToTeams(teamsMessage);
                }
            }
        }


        public void SaveDaysInTransitData(TimeInTransitResponse tnt, string branchNumber, string destinationZip)
        {

            var retryPolicy = Policy.Handle<SqlException>().Retry(3, (ex, count) =>
            {
                const string errorMessage = "SqlException6 in SaveDaysInTransitData";
                _logger.LogWarning(ex, $"{errorMessage} . Retrying...");
                if (count == 3)
                {
                    var teamsMessage = new TeamsMessage(errorMessage, $"Error: {ex.Message}. Stacktrace: {ex.StackTrace}", "yellow", DistanceFunctions.errorLogsUrl);
                    teamsMessage.LogToTeams(teamsMessage);
                    _logger.LogError(ex, errorMessage);
                }
            });

            retryPolicy.Execute(() =>
            {
                var businessDaysInTransit = tnt.BusinessTransitDays;
                var saturdayDelivery = tnt.SaturdayDelivery;

                var connString = Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN");

                using (var conn = new SqlConnection(connString))
                {
                    // Upsert
                    var query = @"
                        IF NOT EXISTS (SELECT * FROM FergusonIntegration.sourcing.DistributionCenterDistance WHERE BranchNumber = @branchNumber AND ZipCode = @destinationZip)
                            INSERT INTO FergusonIntegration.sourcing.DistributionCenterDistance (BranchNumber, ZipCode, BusinessTransitDays)
                            VALUES (@branchNumber, @destinationZip, @businessDaysInTransit)
                        ELSE
                            UPDATE FergusonIntegration.sourcing.DistributionCenterDistance 
                            SET BusinessTransitDays = @businessDaysInTransit";

                    conn.Open();

                    conn.Execute(query,
                        new { businessDaysInTransit, saturdayDelivery, branchNumber, destinationZip },
                        commandTimeout: 10);

                    conn.Close();
                }
            });
        }
    }
}
