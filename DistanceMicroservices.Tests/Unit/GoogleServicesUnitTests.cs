using Xunit;
using DistanceMicroservices.Services;
using System.Collections.Generic;
using DistanceMicroservices.Models;
using System.Linq;
using System.Threading.Tasks;

namespace DistanceMicroservices.Tests
{
    public class GoogleServicesUnitTests
    {
        private static GoogleServices _googleServices = new GoogleServices();
        public static string zip = "30316";
        public static List<GoogleOriginData> origins = new List<GoogleOriginData>()
        {
            new GoogleOriginData(){ BranchNumber = "6" }
        };


        [Fact]
        public async Task Test_GetDistanceDataFromGoogle_LongLat()
        {
            origins[0].Latitude = (decimal?)37.809832;
            origins[0].Longitude = (decimal?)-122.285855;

            var googleDistanceData = await _googleServices.GetDistanceDataFromGoogle(zip, origins);
            var distance = googleDistanceData.FirstOrDefault(d => d.BranchNumber == "6").DistanceInMeters;

            Assert.InRange((double)distance, 3969194, 3971194);
        }


        [Fact]
        public async Task Test_GetDistanceDataFromGoogle_CityState()
        {
            origins[0].City = "Oakland";
            origins[0].State = "CA";
            origins[0].Zip = "94607";

            var googleDistanceData = await _googleServices.GetDistanceDataFromGoogle(zip, origins);
            var distance = googleDistanceData.FirstOrDefault(d => d.BranchNumber == "6").DistanceInMeters;

            Assert.InRange((double)distance, 3969194, 3980252);
        }
    }
}
