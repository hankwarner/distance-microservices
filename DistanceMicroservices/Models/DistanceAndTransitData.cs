using Newtonsoft.Json;

namespace DistanceMicroservices.Models
{
    public class DistanceAndTransitData
    {
        public DistanceAndTransitData(string branchNum, decimal? distance, int? transitDays, bool? saturdayDelivery, string zip)
        {
            BranchNumber = branchNum;
            DistanceInMeters = distance;
            BusinessTransitDays = transitDays;
            SaturdayDelivery = saturdayDelivery;
            ZipCode = zip;
        }

        public DistanceAndTransitData() { }

        public string BranchNumber { get; set; }

        public string ZipCode { get; set; }

        [JsonProperty("distanceFromZip")]
        public decimal? DistanceInMeters { get; set; }

        public int? BusinessTransitDays { get; set; }

        public bool? SaturdayDelivery { get; set; }

        [JsonIgnore]
        public bool RequiresSaving { get; set; } = true;
    }
}
