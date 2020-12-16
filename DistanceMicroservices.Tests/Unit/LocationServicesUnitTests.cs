using Xunit;
using System.Collections.Generic;
using DistanceMicroservices.Services;
using System.Linq;
using System.Threading.Tasks;

namespace DistanceMicroservices.Tests
{
    public class LocationServicesUnitTests
    {
        public static LocationServices _locationServices = new LocationServices();
        public static List<string> branches = new List<string>() { "533" };
        public static string zip = "30316";


        [Fact]
        public async Task Test_SetBranchDistances()
        {
            var origins = await _locationServices.GetOriginDataForGoogle(branches);

            var branchData = origins.FirstOrDefault(o => o.BranchNumber == branches[0]);

            Assert.Equal((decimal?)34.419376, branchData.Latitude);
            Assert.Equal((decimal?)-85.769447, branchData.Longitude);
            Assert.Equal("Fort Payne", branchData.City);
            Assert.Equal("AL", branchData.State);
            Assert.Equal("35968", branchData.Zip);
        }
    }
}
