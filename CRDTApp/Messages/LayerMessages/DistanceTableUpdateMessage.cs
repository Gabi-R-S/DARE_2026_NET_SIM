using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{
    class DistanceTableUpdateMessage : NetworkLayerMessage<DistanceTableUpdateMessage>
    {
        public Dictionary<int, float> Distances { get; set; } = new();
        
        public DistanceTableUpdateMessage(Dictionary<int, float> distances, DateTime timestamp)
        {
            Distances = distances;
        }

    }
}
