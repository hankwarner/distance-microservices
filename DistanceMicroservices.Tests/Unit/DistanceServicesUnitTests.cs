using System;
using Xunit;
using DistanceMicroservices.Services;
using System.Collections.Generic;

namespace DistanceMicroservices.Tests
{
    public class DistanceServicesUnitTests
    {
        private static DistanceServices _distanceServices = new DistanceServices();


        [Fact]
        public void Test_GetMissingBranchDistances()
        {
            var zip = "30316";
            var missingBranches = new List<string>() { "58", "533" };

            var missingDistanceData = _distanceServices.GetMissingBranchDistances(zip, missingBranches);


        }

    }
}
