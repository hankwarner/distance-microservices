using Newtonsoft.Json;

namespace DistanceMicroservices.Models
{
    public class DistanceData
    {
        [JsonProperty("locationNetSuiteId")]
        public int NetSuiteListId { get; set; }

        [JsonProperty("distanceFromZip")]
        public decimal DistanceInMiles { get; set; }

        public string BranchNumber { get; set; }

        public string Zip { get; set; }

        public int? BusinessTransitDays { get; set; }

        public bool SaturdayDelivery { get; set; }

        [JsonProperty("masterProductNumber")]
        public string MPID { get; set; }

        public int DistributionCenterNetSuiteId { get; set; }

        public int Quantity { get; set; }

        public string Error { get; set; } = null;
    }
}
