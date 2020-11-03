using System;
using Xunit;
using DistanceMicroservices.Services;
using System.Collections.Generic;
using DistanceMicroservices.Models;
using System.Linq;

namespace DistanceMicroservices.Tests.Unit
{
    public class GoogleServicesUnitTests
    {
        private static GoogleServices _googleServices = new GoogleServices();

        [Fact]
        public void Test_GetDistanceDataFromGoogle()
        {
            var zip = "30316";
            var origins = new List<GoogleOriginData>()
            {
                new GoogleOriginData()
                {
                    Latitude = (decimal?)37.809832,
                    Longitude = (decimal?)-122.285855,
                    BranchNumber = "6"
                }
            };

            var branchDistances = _googleServices.GetDistanceDataFromGoogle(zip, origins);

            Assert.Equal(3970194, branchDistances.FirstOrDefault(d => d.BranchNumber == "6").DistanceInMeters);
        }
    }
}
