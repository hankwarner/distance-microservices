using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using DistanceMicroservices.Models;
using Microsoft.Extensions.Logging;

namespace DistanceMicroservices.Services
{
    public class DistanceServices
    {
        public ILogger _logger { get; set; }

        public DistanceServices(ILogger log = null)
        {
            _logger = log;
        }

        public Dictionary<string, double> RequestBranchDistancesByZipCode(string zipCode, List<string> branches)
        {
            try
            {
                var connString = Environment.GetEnvironmentVariable("DIST_DB_CONN");

                using (var conn = new SqlConnection(connString))
                {
                    conn.Open();

                    // TODO: update this when new DB is created
                    var query = @"
                        SELECT BranchNumber, BusinessTransitDays, CEILING(DistanceInMeters * 0.0006213712) DistanceInMiles, SaturdayDelivery 
                        FROM FergusonIntegration.sourcing.DistributionCenterDistance 
                        WHERE ZipCode = @zipCode AND BranchNumber in @branches";

                    var branchesWithDistanceList = conn.Query(query, new { zipCode, branches }, commandTimeout: 6)
                        .ToDictionary(
                            row => (string)row.BranchNumber,
                            row => (double)row.DistanceInMiles)
                        .ToList();

                    var branchesWithDistance = branchesWithDistanceList.ToDictionary(pair => pair.Key, pair => pair.Value);

                    conn.Close();

                    return branchesWithDistance;
                }
            }
            catch (SqlException ex)
            {
                string errorMessage = $"Sql Exception in RequestBranchDistancesByZipCode";
                var teamsMessage = new TeamsMessage(errorMessage, $"Error: {ex.Message}. Stacktrace: {ex.StackTrace}", "yellow", DistanceFunctions.errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
                _logger.LogError(ex, errorMessage);
                throw;
            }
            catch (Exception ex)
            {
                string errorMessage = $"Error in RequestBranchDistancesByZipCode";
                var teamsMessage = new TeamsMessage(errorMessage, $"Error: {ex.Message}. Stacktrace: {ex.StackTrace}", "yellow", DistanceFunctions.errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
                _logger.LogError(ex, errorMessage);
                throw;
            }
        }


        public IEnumerable<KeyValuePair<string, double>> GetMissingBranchDistances(string zipCode, List<string> missingBranches)
        {
            var _locationServices = new LocationServices(_logger);
            var _googleServices = new GoogleServices(_logger);

            var origins = _locationServices.GetOriginDataForGoogle(missingBranches);

            var branchDistances = _googleServices.GetDistanceDataFromGoogle(zipCode, origins);

            _ = Task.Run(() =>
            {
                SaveBranchDistanceData(branchDistances);
            });

            return (from distributionCenterDistance in branchDistances
                    let miles = Math.Ceiling(distributionCenterDistance.DistanceInMeters * 0.0006213712)
                    select new KeyValuePair<string, double>(distributionCenterDistance.BranchNumber, miles)).ToList();
        }


        public bool SaveBranchDistanceData(List<DistributionCenterDistance> branchDistances)
        {
            try
            {
                var connString = Environment.GetEnvironmentVariable("DIST_DB_CONN");

                var insertStatement = "INSERT INTO FergusonIntegration.sourcing.DistributionCenterDistance (BranchNumber, ZipCode, DistanceInMeters, BusinessTransitDays, SaturdayDelivery) VALUES";
                
                for (var i = 0; i < branchDistances.Count(); i++)
                {
                    insertStatement += $"('{branchDistances[i].BranchNumber}','{branchDistances[i].ZipCode}',{branchDistances[i].DistanceInMeters},null,null)";
                    if (i != (branchDistances.Count() - 1))
                    {
                        insertStatement += ",";
                    }
                }

                using (var conn = new SqlConnection(connString))
                {
                    conn.Open();
                    var newRows = conn.Execute(insertStatement, commandTimeout: 6);
                    if (newRows != branchDistances.Count())
                    {
                        return false;
                    }
                    conn.Close();

                    return true;
                }
            }
            catch (SqlException ex)
            {
                var errorMessage = $"Sql Exception in SaveBranchDistanceData: ";
                _logger.LogError(ex, errorMessage);
                throw;
            }
            catch (Exception ex)
            {
                var errorMessage = $"Error in SaveBranchDistanceData: ";
                _logger.LogError(ex, errorMessage);
                throw;
            }
        }
    }
}
