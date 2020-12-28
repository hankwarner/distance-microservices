
namespace DistanceMicroservices.Models
{
    public class DistributionCenterDistance
    {
        public DistributionCenterDistance(string branchNum)
        {
            BranchNumber = branchNum;
        }
        public DistributionCenterDistance() { }


        public string BranchNumber { get; set; }
        public string ZipCode { get; set; }
        public decimal? DistanceInMeters { get; set; } = null;
        public bool RequiresSaving { get; set; } = false;
    }
}
