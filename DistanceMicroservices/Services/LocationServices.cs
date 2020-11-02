using Dapper;
using DistanceMicroservices.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;

namespace DistanceMicroservices.Services
{
    public class LocationServices
    {
        public ILogger _logger { get; set; }

        public LocationServices(ILogger log = null)
        {
            _logger = log;
        }

        public List<GoogleOriginData> GetOriginDataForGoogle(List<string> branches)
        {
            try
            {
                var connString = Environment.GetEnvironmentVariable("DC_DB_CONN");

                using (var conn = new SqlConnection(connString))
                {
                    conn.Open();

                    var query = @"
                        SELECT BranchNumber, Latitude, Longitude, Address1, City, State, Zip 
                        FROM [FergusonIntegration].[sourcing].[DistributionCenter] 
                        WHERE BranchNumber in @branches";

                    var originData = conn.Query<GoogleOriginData>(query, new { branches }, commandTimeout: 6).ToList();

                    conn.Close();

                    return originData;
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "Sql Exception in GetOriginDataForGoogle");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in GetOriginDataForGoogle");
                throw;
            }
        }


        public Dictionary<string, DistanceData> GetBranchZipCodes(List<string> branches)
        {
            try
            {
                var connString = Environment.GetEnvironmentVariable("DC_DB_CONN");

                using (var conn = new SqlConnection(connString))
                {
                    conn.Open();
                    var query = @"
                        SELECT Zip, BranchNumber 
                        FROM FergusonIntegration.sourcing.DistributionCenter 
                        WHERE BranchNumber in @branches";

                    var zipCodeDict = conn.Query<DistanceData>(query, new { branches }, commandTimeout: 3)
                        .ToDictionary(row => row.BranchNumber, row => row);

                    conn.Close();

                    return zipCodeDict;
                }
            }
            catch (SqlException ex)
            {
                string errorMessage = $"SQL Exception in GetBranchZipCodes";
                _logger.LogError(ex, errorMessage);
                throw;
            }
            catch (Exception ex)
            {
                string errorMessage = $"Exception in GetBranchZipCodes";
                _logger.LogError(ex, errorMessage);
                throw;
            }
        }
    }
}
