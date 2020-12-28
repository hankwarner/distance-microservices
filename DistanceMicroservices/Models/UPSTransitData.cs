using Newtonsoft.Json;

namespace DistanceMicroservices.Models
{
    public class UPSTransitData
    {
        public UPSTransitData(string branchNum, int? transitDays, bool? saturdayDelivery)
        {
            BranchNumber = branchNum;
            BusinessTransitDays = transitDays;
            SaturdayDelivery = saturdayDelivery;
        }

        public UPSTransitData() { }


        public string BranchNumber { get; set; }

        [JsonIgnore]
        public string BranchZip { get; set; }

        public int? BusinessTransitDays { get; set; }

        public bool? SaturdayDelivery { get; set; }

        public bool RequiresSaving { get; set; } = false;
    }
}
