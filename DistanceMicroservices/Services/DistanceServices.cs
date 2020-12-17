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

        public async Task SetBranchDistances(string destinationZip, List<string> branches, Dictionary<string, double?> distanceDict)
        {
            try
            {
                using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
                {
                    conn.Open();

                    var query = @"
                        SELECT BranchNumber, CEILING(DistanceInMeters * 0.0006213712) DistanceInMiles  
                        FROM Data.DistributionCenterDistance 
                        WHERE ZipCode = @destinationZip AND BranchNumber in @branches";

                    var results = await conn.QueryAsync<DistanceData>(query, new { destinationZip, branches }, commandTimeout: 10);

                    conn.Close();

                    // Add results to distanceDict
                    foreach (var row in results){ distanceDict[row.BranchNumber] = (double?)row.DistanceInMiles; };

                    return;
                }
            }
            catch (SqlException ex)
            {
                var errMessage = $"SqlException in SetBranchDistances";
                _logger.LogError(@"{0}: {1}", errMessage, ex);
#if !DEBUG
                var teamsMessage = new TeamsMessage(errMessage, $"Error: {ex.Message}. Stacktrace: {ex.StackTrace}", "yellow", DistanceFunctions.errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
#endif
                throw;
            }
            catch (Exception ex)
            {
                var errMessage = $"Exception in SetBranchDistances";
                _logger.LogError(@"{0}: {1}", errMessage, ex);
#if !DEBUG
                var teamsMessage = new TeamsMessage(errMessage, $"Error: {ex.Message}. Stacktrace: {ex.StackTrace}", "yellow", DistanceFunctions.errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
#endif
                throw;
            }
        }

        public Dictionary<string, double?> RequestBranchDistancesByZipCode(string zipCode, List<string> branches)
        {
            try
            {
                using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
                {
                    conn.Open();

                    var query = @"
                        SELECT BranchNumber, CEILING(DistanceInMeters * 0.0006213712) DistanceInMiles  
                        FROM Data.DistributionCenterDistance 
                        WHERE ZipCode = @zipCode AND BranchNumber in @branches";

                    var branchesWithDistance = conn.Query(query, new { zipCode, branches }, commandTimeout: 6)
                        .ToDictionary(row => (string)row.BranchNumber,
                                      row => (double?)row.DistanceInMiles);
                    
                    conn.Close();

                    return branchesWithDistance;
                }
            }
            catch (SqlException ex)
            {
                var errMessage = $"SqlException in RequestBranchDistancesByZipCode";
                _logger.LogError(@"{0}: {1}", errMessage, ex);
#if !DEBUG
                var teamsMessage = new TeamsMessage(errMessage, $"Error: {ex.Message}. Stacktrace: {ex.StackTrace}", "yellow", DistanceFunctions.errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
#endif
                throw;
            }
            catch (Exception ex)
            {
                var errMessage = $"Exception in RequestBranchDistancesByZipCode";
                _logger.LogError(@"{0}: {1}", errMessage, ex);
#if !DEBUG
                var teamsMessage = new TeamsMessage(errMessage, $"Error: {ex.Message}. Stacktrace: {ex.StackTrace}", "yellow", DistanceFunctions.errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
#endif
                throw;
            }
        }


        public async Task<Dictionary<string, double?>> GetMissingBranchDistances(string zipCode, List<string> missingBranches)
        {
            var _locationServices = new LocationServices(_logger);
            var _googleServices = new GoogleServices(_logger);

            var origins = await _locationServices.GetOriginDataForGoogle(missingBranches);

            var branchDistances = await _googleServices.GetDistanceDataFromGoogle(zipCode, origins);

            if (branchDistances.Any())
            {
                _ = SaveBranchDistanceData(branchDistances);
            }

            // Create dictionary where key = branch number
            var response = branchDistances.ToDictionary(b => b.BranchNumber, b => (double?)b.DistanceInMeters);

            return response;
        }


        public async Task SaveBranchDistanceData(List<DistributionCenterDistance> branchDistances)
        {
            try
            {
                foreach(var distanceData in branchDistances)
                {
                    var branchNum = distanceData.BranchNumber;
                    var zip = distanceData.ZipCode;
                    var distance = distanceData.DistanceInMeters;

                    try
                    {
                        using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
                        {
                            conn.Open();

                            // Upsert
                            var query = @"
                            IF NOT EXISTS (SELECT * FROM Data.DistributionCenterDistance WHERE BranchNumber = @branchNum AND ZipCode = @zip)
                                INSERT INTO Data.DistributionCenterDistance (BranchNumber, ZipCode, DistanceInMeters)
                                VALUES (@branchNum, @zip, @distance)
                            ELSE
                                UPDATE Data.DistributionCenterDistance 
                                SET DistanceInMeters = @distance  
                                WHERE BranchNumber = @branchNum AND ZipCode = @zip";

                            await conn.ExecuteAsync(query, new { branchNum, zip, distance }, commandTimeout: 120);

                            conn.Close();
                            _logger?.LogInformation($"Saved distance data for branch number {branchNum}.");
                        }
                    }
                    catch (SqlException ex)
                    {
                        _logger?.LogError(@"SqlException saving data for branch {0}: {1}", branchNum, ex);
                    }
                }
            }
            catch (SqlException ex)
            {
                _logger?.LogError(@"SqlException saving data: {0}", ex);
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogError(@"Exception in SaveBranchDistanceData: {0}", ex);
                throw;
            }
        }
    }
}
