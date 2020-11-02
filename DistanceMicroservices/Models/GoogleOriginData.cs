using System;
using System.Collections.Generic;
using System.Text;

namespace DistanceMicroservices.Models
{
    public class GoogleOriginData
    {
        public string BranchNumber { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string Address1 { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip { get; set; }
    }
}
