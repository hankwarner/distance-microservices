using Xunit;
using System.Collections.Generic;
using DistanceMicroservices.Services;
using System.Linq;
using DistanceMicroservices.Models;
using System.Threading.Tasks;
using System.Data.SqlClient;
using System;
using Dapper;

namespace DistanceMicroservices.Tests
{
    public class DistanceServicesUnitTests
    {
        public static DistanceServices _distanceServices = new DistanceServices();
        public static List<string> branches = new List<string>() { "58", "533" };
        public static string zip = "30316";


        [Fact]
        public void Test_SetBranchDistances()
        {
            var fakeBranch = "9999";
            branches.Add(fakeBranch);
            var distanceDict = branches.ToDictionary(b => b, b => (double?)0.0);

            _distanceServices.SetBranchDistances(zip, branches, distanceDict);

            Assert.Equal(3, distanceDict[branches[0]].Value);
            Assert.Equal(129, distanceDict[branches[1]].Value);
            Assert.Equal(0, distanceDict[branches[2]].Value);
        }


        [Fact]
        public async Task Test_SaveBranchDistanceData()
        {
            var branchDistances = new List<DistributionCenterDistance>()
            {
                new DistributionCenterDistance()
                {
                    DistanceInMeters = 3529531,
                    BranchNumber = "123Test",
                    ZipCode = "30316"
                }
            };

            await _distanceServices.SaveBranchDistanceData(branchDistances);

            var results = GetDistanceDataFromDB(branchDistances[0]);

            Assert.Equal(branchDistances[0].DistanceInMeters, results.DistanceInMeters);

            if (results != null)
            {
                DeleteTestDistanceData(branchDistances[0]);
            }
        }


        private DistributionCenterDistance GetDistanceDataFromDB(DistributionCenterDistance distanceData)
        {
            var branchNum = distanceData.BranchNumber;
            var zipCode = distanceData.ZipCode;

            using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
            {
                conn.Open();

                var query = @"
                    SELECT BranchNumber, ZipCode, DistanceInMeters
                    FROM Data.DistributionCenterDistance
                    WHERE BranchNumber = @branchNum AND ZipCode = @zipCode";

                var results = conn.QueryFirstOrDefault<DistributionCenterDistance>(query, new { branchNum, zipCode }, commandTimeout: 30);

                conn.Close();
             
                return results;
            }
        }


        private void DeleteTestDistanceData(DistributionCenterDistance distanceData)
        {
            var branchNum = distanceData.BranchNumber;
            var zipCode = distanceData.ZipCode;

            using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
            {
                conn.Open();

                var query = @"
                    DELETE FROM Data.DistributionCenterDistance
                    WHERE BranchNumber = @branchNum AND ZipCode = @zipCode";

                conn.Execute(query, new { branchNum, zipCode }, commandTimeout: 12);

                conn.Close();
            }
        }
    }
}
