using Newtonsoft.Json;

namespace DistanceMicroservices.Models
{
    public class DistanceData
    {
        [JsonProperty("distanceFromZip")]
        public decimal DistanceInMiles { get; set; }

        public string BranchNumber { get; set; }

        public string Zip { get; set; }

        public int? BusinessTransitDays { get; set; }

        public bool SaturdayDelivery { get; set; }
    }
}
