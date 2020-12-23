using DistanceMicroservices.Services;
using DistanceMicroservices.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using System.Threading.Tasks;

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
            Assert.True(transitDict[fakeBranches[0]].RequiresSaving);
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
    }
}
