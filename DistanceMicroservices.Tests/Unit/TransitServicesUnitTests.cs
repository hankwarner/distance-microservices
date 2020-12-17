using DistanceMicroservices.Services;
using DistanceMicroservices.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using System.Threading.Tasks;
using System.Data.SqlClient;
using Dapper;

namespace DistanceMicroservices.Tests.Unit
{
    public class TransitServicesUnitTests
    {
        public static TransitServices _transitServices = new TransitServices();
        public static List<string> branches = new List<string>() { "58", "474", "2920" };
        public static string originZip = "11362";
        public static string destinationZip = "30316";
        public static List<string> fakeBranches = new List<string>() { "123Test", "456Test", "789Test" };


        [Fact]
        public async Task Test_SetUPSTransitData()
        {
            branches.Add(fakeBranches[0]);
            var transitDict = branches.ToDictionary(b => b, b => new UPSTransitData());

            await _transitServices.SetUPSTransitData(fakeBranches[0], originZip, destinationZip, transitDict);

            Assert.Equal(2, transitDict[fakeBranches[0]].BusinessTransitDays);
        }


        [Fact]
        public async Task Test_SetTransitData()
        {
            branches.Add(fakeBranches[0]);

            var transitDict = branches.ToDictionary(b => b, b => new UPSTransitData());

            await _transitServices.SetTransitData(destinationZip, branches, transitDict);

            Assert.Equal(1, transitDict[branches[0]].BusinessTransitDays);
            Assert.Equal(2, transitDict[branches[1]].BusinessTransitDays);
            Assert.Equal(3, transitDict[branches[2]].BusinessTransitDays);
            Assert.Null(transitDict[branches[3]].BusinessTransitDays);
        }


        [Fact]
        public async Task Test_SaveTransitData_Insert()
        {
            var tnt = new TimeInTransitResponse(){ BusinessTransitDays = 4 };

            await _transitServices.SaveTransitData(tnt, fakeBranches[0], destinationZip);

            var results = GetTransitDataFromDB(fakeBranches[0], destinationZip);

            Assert.Equal(4, results[0].BusinessTransitDays);

            DeleteTestDistanceData();
        }


        [Fact]
        public async Task Test_SaveTransitData_Update()
        {
            InsertTestDistanceData(fakeBranches[0], destinationZip, 5000);

            var tnt = new TimeInTransitResponse() { BusinessTransitDays = 4 };

            await _transitServices.SaveTransitData(tnt, fakeBranches[0], destinationZip);

            var results = GetTransitDataFromDB(fakeBranches[0], destinationZip);

            Assert.Equal(4, results[0].BusinessTransitDays);

            DeleteTestDistanceData();
        }


        [Fact]
        public async Task Test_SetMissingDaysInTransitData()
        {
            var transitDict = branches.ToDictionary(b => b, b => new UPSTransitData());
            transitDict.Add(fakeBranches[0], new UPSTransitData() { BranchZip = "22630" });
            transitDict.Add(fakeBranches[1], new UPSTransitData() { BranchZip = "92571" });
            transitDict.Add(fakeBranches[2], new UPSTransitData() { BranchZip = "99354-5317" });

            var branchesMissingData = new List<string>() { fakeBranches[0], fakeBranches[1], fakeBranches[2] };

            branchesMissingData.ForEach(branch => InsertTestDistanceData(branch, destinationZip, 5000));

            await _transitServices.SetMissingDaysInTransitData(destinationZip, branchesMissingData, transitDict);

            Assert.Equal(2, transitDict[fakeBranches[0]].BusinessTransitDays);
            Assert.Equal(5, transitDict[fakeBranches[1]].BusinessTransitDays);
            Assert.Equal(5, transitDict[fakeBranches[2]].BusinessTransitDays);

            // Check DB for distance in meters still being there
            var results = GetDistanceDataFromDB(branchesMissingData, destinationZip);

            results.ForEach(res => Assert.Equal(4, res.DistanceInMiles));

            DeleteTestDistanceData();
        }


        private List<DistanceData> GetDistanceDataFromDB(List<string> branchNums, string zipCode)
        {
            using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
            {
                conn.Open();

                var query = @"
                    SELECT BranchNumber, CEILING(DistanceInMeters * 0.0006213712) DistanceInMiles
                    FROM Data.DistributionCenterDistance
                    WHERE BranchNumber in @branchNums AND ZipCode = @zipCode";

                var results = conn.Query<DistanceData>(query, new { branchNums, zipCode }, commandTimeout: 30);

                conn.Close();

                return results.ToList();
            }
        }


        private List<UPSTransitData> GetTransitDataFromDB(string branchNum, string zipCode)
        {
            using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
            {
                conn.Open();

                var query = @"
                    SELECT BranchNumber, ZipCode, BusinessTransitDays, SaturdayDelivery
                    FROM Data.DistributionCenterDistance
                    WHERE BranchNumber = @branchNum AND ZipCode = @zipCode";

                var results = conn.Query<UPSTransitData>(query, new { branchNum, zipCode }, commandTimeout: 30);

                conn.Close();

                return results.ToList();
            }
        }


        private void InsertTestDistanceData(string branchNum, string zipCode, int distance)
        {
            using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
            {
                conn.Open();

                var query = @"
                    INSERT INTO Data.DistributionCenterDistance (BranchNumber, ZipCode, DistanceInMeters)
                    VALUES (@branchNum, @zipCode, @distance)";

                conn.Execute(query, new { branchNum, zipCode, distance }, commandTimeout: 15);

                conn.Close();
            }
        }


        private void DeleteTestDistanceData()
        {
            using (var conn = new SqlConnection(Environment.GetEnvironmentVariable("AZ_SOURCING_DB_CONN")))
            {
                conn.Open();

                var query = @"
                    DELETE FROM Data.DistributionCenterDistance
                    WHERE BranchNumber in @fakeBranches";

                conn.Execute(query, new { fakeBranches }, commandTimeout: 30);

                conn.Close();
            }
        }
    }
}
