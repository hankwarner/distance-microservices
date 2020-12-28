using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using DistanceMicroservices.Models;
using Microsoft.Extensions.Logging;
using Polly;

namespace DistanceMicroservices.Services
{
    public class DistanceServices
    {
        public ILogger _logger { get; set; }

        public DistanceServices(ILogger log = null)
        {
            _logger = log;
        }


        public async Task<Dictionary<string, DistributionCenterDistance>> GetBranchDistances(List<string> branches, string destinationZip)
        {
            try
            {
                if (branches == null || branches.Count() == 0)
                {
                    var msg = "No Branch Numbers provided.";
                    _logger?.LogWarning(msg);
                    throw new ArgumentNullException("branches", msg);
                }

                var distanceDict = branches.ToDictionary(b => b, b => new DistributionCenterDistance(b));

                await SetBranchDistances(destinationZip, branches, distanceDict);

                // Check for branches missing distance in meters
                var branchesMissingDistance = distanceDict.Where(b => b.Value.DistanceInMeters == null)
                    .Select(b => b.Key).ToList();

                if (branchesMissingDistance.Any())
                {
                    _logger?.LogInformation(@"Branches Missing Distance: {0}", branchesMissingDistance);

                    try
                    {
                        // Get missing distance from Google Distance Matrix API
                        var missingDistanceData = await GetMissingBranchDistances(destinationZip, branchesMissingDistance);

                        // Add to distanceDict response
                        foreach (var distToAdd in missingDistanceData)
                        {
                            distanceDict[distToAdd.BranchNumber] = distToAdd;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(@"Exception getting missing branch distances: {0}", ex);
                    }
                }

                return distanceDict;
            }
            catch (Exception ex)
            {
                var title = "Exception in GetBranchDistances";
                _logger?.LogError(@"{0}: {1}", title, ex);
#if !DEBUG
                var teamsMessage = new TeamsMessage(title, $"Error message: {ex.Message}. Stacktrace: {ex.StackTrace}", "red", errorLogsUrl);
                teamsMessage.LogToTeams(teamsMessage);
#endif
                throw;
            }
        }


        public async Task SetBranchDistances(string destinationZip, List<string> branches, Dictionary<string, DistributionCenterDistance> distanceDict)
        {
            try
            {
                _logger?.LogInformation("SetBranchDistances start");
                using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
                {
                    conn.Open();

                    var query = @"
                        SELECT BranchNumber, DistanceInMeters  
                        FROM Data.DistributionCenterDistance 
                        WHERE ZipCode = @destinationZip AND BranchNumber in @branches";

                    var results = await conn.QueryAsync<DistributionCenterDistance>(query, new { destinationZip, branches }, commandTimeout: 240);

                    conn.Close();

                    // Add results to distanceDict
                    foreach (var row in results)
                    {
                        distanceDict[row.BranchNumber] = row;
                    }
                    _logger?.LogInformation("SetBranchDistances finish");
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


        public async Task<List<DistributionCenterDistance>> GetMissingBranchDistances(string zipCode, List<string> missingBranches)
        {
            try
            {
                _logger?.LogInformation("GetMissingBranchDistances start");
                var _locationServices = new LocationServices(_logger);
                var _googleServices = new GoogleServices(_logger);

                var origins = await _locationServices.GetOriginDataForGoogle(missingBranches);

                var branchDistances = await _googleServices.GetDistanceDataFromGoogle(zipCode, origins);

                _logger?.LogInformation("GetMissingBranchDistances finish");
                return branchDistances;
            }
            catch(Exception ex)
            {
                _logger?.LogError(@"Exception in GetMissingBranchDistances: {0}", ex);
                throw;
            }
        }


        public async Task SaveBranchDistanceData(List<DistanceAndTransitData> branchDistances)
        {
            try
            {
                _logger?.LogInformation("SaveBranchDistanceData start");

                await InsertBranchDistanceToStagingTable(branchDistances);

                await MergeBranchDistance();

                await TruncateStagingTable();

                _logger?.LogInformation("SaveBranchDistanceData finish");
            }
            catch (Exception ex)
            {
                _logger?.LogError(@"Exception in SaveBranchDistanceData: {0}", ex);
            }
        }


        public async Task InsertBranchDistanceToStagingTable(List<DistanceAndTransitData> branchDistances)
        {
            var retryPolicy = Policy.Handle<SqlException>().WaitAndRetry(5, _ => TimeSpan.FromMinutes(1), (ex, ts, count, context) =>
            {
                var errMessage = "Error in InsertBranchDistanceToStagingTable";
                _logger.LogWarning("{0}. Retrying... : {1}", errMessage, ex);
                if (count == 5)
                {
#if !DEBUG
                    var teamsMessage = new TeamsMessage(errMessage, $"Error: {ex.Message}. Stacktrace: {ex.StackTrace}", "yellow", DistanceFunctions.errorLogsUrl);
                    teamsMessage.LogToTeams(teamsMessage);
#endif
                    _logger.LogError(@"{0}: {1}", errMessage, ex);
                }
            });

            await retryPolicy.Execute(async () =>
            {
                using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
                {
                    conn.Open();

                    var insertQuery = @"
	                    INSERT INTO Staging.DistributionCenterDistance (BranchNumber, ZipCode, DistanceInMeters, BusinessTransitDays) 
	                    VALUES (@BranchNumber, @ZipCode, @DistanceInMeters, @BusinessTransitDays)";

                    await conn.ExecuteAsync(insertQuery, branchDistances, commandTimeout: 120);

                    conn.Close();
                }
            });
        }


        public async Task MergeBranchDistance()
        {
            var retryPolicy = Policy.Handle<SqlException>().WaitAndRetry(5, _ => TimeSpan.FromMinutes(1), (ex, ts, count, context) =>
            {
                var errMessage = "Error in MergeBranchDistance";
                _logger.LogWarning("{0}. Retrying... : {1}", errMessage, ex);
                if (count == 5)
                {
#if !DEBUG
                    var teamsMessage = new TeamsMessage(errMessage, $"Error: {ex.Message}. Stacktrace: {ex.StackTrace}", "yellow", DistanceFunctions.errorLogsUrl);
                    teamsMessage.LogToTeams(teamsMessage);
#endif
                    _logger.LogError(@"{0}: {1}", errMessage, ex);
                }
            });

            await retryPolicy.Execute(async () =>
            {
                _logger?.LogInformation("MergeBranchDistance start");
                using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
                {
                    conn.Open();

                    var mergeQuery = @"
                        MERGE Data.DistributionCenterDistance target
                        USING Staging.DistributionCenterDistance source 
                        ON target.ZipCode = source.ZipCode AND target.BranchNumber = source.BranchNumber
                        WHEN MATCHED THEN 
	                        UPDATE SET target.DistanceInMeters = source.DistanceInMeters, target.BusinessTransitDays = source.BusinessTransitDays
                        WHEN NOT MATCHED THEN 
	                        INSERT (BranchNumber, ZipCode, DistanceInMeters, BusinessTransitDays) 
	                        VALUES (source.BranchNumber, source.ZipCode, source.DistanceInMeters, source.BusinessTransitDays);";

                    await conn.ExecuteAsync(mergeQuery, commandTimeout: 240);

                    conn.Close();
                }
                _logger?.LogInformation("MergeBranchDistance finish");
            });
        }


        public async Task TruncateStagingTable()
        {
            var retryPolicy = Policy.Handle<SqlException>().WaitAndRetry(5, _ => TimeSpan.FromMinutes(1), (ex, ts, count, context) =>
            {
                var errMessage = "Error in TruncateStagingTable";
                _logger.LogWarning("{0}. Retrying... : {1}", errMessage, ex);
                if (count == 5)
                {
#if !DEBUG
                    var teamsMessage = new TeamsMessage(errMessage, $"Error: {ex.Message}. Stacktrace: {ex.StackTrace}", "yellow", DistanceFunctions.errorLogsUrl);
                    teamsMessage.LogToTeams(teamsMessage);
#endif
                    _logger.LogError(@"{0}: {1}", errMessage, ex);
                }
            });

            await retryPolicy.Execute(async () =>
            {
                _logger?.LogInformation("TruncateStagingTable start");
                using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
                {
                    conn.Open();

                    var truncateQuery = "TRUNCATE TABLE Staging.DistributionCenterDistance";

                    await conn.ExecuteAsync(truncateQuery, commandTimeout: 120);

                    conn.Close();
                }
                _logger?.LogInformation("TruncateStagingTable finish");
            });
        }
    }
}
