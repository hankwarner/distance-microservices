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
        public static List<DistanceAndTransitData> branchDistances = new List<DistanceAndTransitData>()
        {
            new DistanceAndTransitData()
            {
                DistanceInMeters = 3529531,
                BranchNumber = "123Test",
                BusinessTransitDays = 3,
                ZipCode = zip
            }
        };


        [Fact]
        public async Task Test_SetBranchDistances()
        {
            var fakeBranch = "9999";
            branches.Add(fakeBranch);
            var distanceDict = branches.ToDictionary(b => b, b => new DistributionCenterDistance(b));

            await _distanceServices.SetBranchDistances(zip, branches, distanceDict);

            Assert.Equal(3953, distanceDict[branches[0]].DistanceInMeters);
            Assert.Equal(206792, distanceDict[branches[1]].DistanceInMeters);
            Assert.Null(distanceDict[branches[2]].DistanceInMeters);
        }


        [Fact]
        public async Task Test_InsertBranchDistanceToStagingTable()
        {
            await _distanceServices.InsertBranchDistanceToStagingTable(branchDistances);

            var results = GetDistanceDataFromStagingTable(branchDistances[0]);

            Assert.Equal(branchDistances[0].DistanceInMeters, results.DistanceInMeters);

            DeleteTestDistanceData(branchDistances[0]);
        }


        [Fact]
        public async Task Test_MergeBranchDistance()
        {
            await _distanceServices.InsertBranchDistanceToStagingTable(branchDistances);

            await _distanceServices.MergeBranchDistance();

            var results = GetDistanceDataFromProductionTable(branchDistances[0]);

            Assert.Equal(branchDistances[0].DistanceInMeters, results.DistanceInMeters);

            DeleteTestDistanceData(branchDistances[0]);
        }


        [Fact]
        public async Task Test_SaveBranchDistanceData()
        {
            await _distanceServices.SaveBranchDistanceData(branchDistances);

            var productionResults = GetDistanceDataFromProductionTable(branchDistances[0]);
            var stagingResults = GetDistanceDataFromStagingTable(branchDistances[0]);

            Assert.Equal(branchDistances[0].DistanceInMeters, productionResults.DistanceInMeters);
            Assert.Null(stagingResults);

            DeleteTestDistanceData(branchDistances[0]);
        }

        private DistanceAndTransitData GetDistanceDataFromProductionTable(DistanceAndTransitData distanceData)
        {
            var branchNum = distanceData.BranchNumber;

            using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
            {
                conn.Open();

                var query = @"
                    SELECT BranchNumber, ZipCode, DistanceInMeters
                    FROM Data.DistributionCenterDistance
                    WHERE BranchNumber = @branchNum AND ZipCode = @zip";

                var results = conn.QueryFirstOrDefault<DistanceAndTransitData>(query, new { branchNum, zip }, commandTimeout: 30);

                conn.Close();

                return results;
            }
        }

        private DistanceAndTransitData GetDistanceDataFromStagingTable(DistanceAndTransitData distanceData)
        {
            var branchNum = distanceData.BranchNumber;

            using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
            {
                conn.Open();

                var query = @"
                    SELECT BranchNumber, BusinessTransitDays, DistanceInMeters
                    FROM Staging.DistributionCenterDistance
                    WHERE BranchNumber = @branchNum AND ZipCode = @zip";

                var results = conn.QueryFirstOrDefault<DistanceAndTransitData>(query, new { branchNum, zip }, commandTimeout: 30);

                conn.Close();

                return results;
            }
        }


        private void DeleteTestDistanceData(DistanceAndTransitData distanceData)
        {
            var branchNum = distanceData.BranchNumber;

            using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
            {
                conn.Open();

                var query = @"
                    DELETE FROM Staging.DistributionCenterDistance
                    WHERE BranchNumber = @branchNum AND ZipCode = @zip";

                conn.Execute(query, new { branchNum, zip }, commandTimeout: 60);

                query = @"
                    DELETE FROM Data.DistributionCenterDistance
                    WHERE BranchNumber = @branchNum AND ZipCode = @zip";

                conn.Execute(query, new { branchNum, zip }, commandTimeout: 60);

                conn.Close();
            }
        }
    }
}
