using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{
    class DistanceTableUpdateMessagePayload : Parseable<DistanceTableUpdateMessagePayload>
    {
        public Dictionary<int, float> Distances { get; set; } = new();
        
        public DistanceTableUpdateMessagePayload(Dictionary<int, float> distances, DateTime timestamp)
        {
            Distances = distances;
        }

    }
}
