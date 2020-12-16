using Dapper;
using DistanceMicroservices.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DistanceMicroservices.Services
{
    public class LocationServices
    {
        public ILogger _logger { get; set; }

        public LocationServices(ILogger log = null)
        {
            _logger = log;
        }

        public async Task<List<GoogleOriginData>> GetOriginDataForGoogle(List<string> branches)
        {
            try
            {
                using (var conn = new SqlConnection("Server=tcp:feiazprdspsrcengdb001.database.windows.net,1433;Initial Catalog=feiazprdspsrcengdb1;Persist Security Info=False;User ID=supply-utility-account;Password=K99Z5GhV3znHjM7kPX;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"))
                //using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
                {
                    conn.Open();

                    var query = @"
                        SELECT BranchNumber, Latitude, Longitude, Address1, City, State, Zip 
                        FROM Data.DistributionCenter 
                        WHERE BranchNumber in @branches";

                    var originData = await conn.QueryAsync<GoogleOriginData>(query, new { branches }, commandTimeout: 6);

                    conn.Close();

                    return originData.ToList();
                }
            }
            catch (SqlException ex)
            {
                _logger.LogError(@"SqlException in GetOriginDataForGoogle: {0}", ex);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(@"Exception in GetOriginDataForGoogle: {0}", ex);
                throw;
            }
        }


        public Dictionary<string, DistanceData> GetBranchZipCodes(List<string> branches)
        {
            try
            {
                var connString = Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN");

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
