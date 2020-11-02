using System;
using System.Collections.Generic;
using System.Text;

namespace DistanceMicroservices.Models
{
    public class TimeInTransitResponse
    {
        public int BusinessTransitDays { get; set; }

        public bool SaturdayDelivery { get; set; }
    }
}
