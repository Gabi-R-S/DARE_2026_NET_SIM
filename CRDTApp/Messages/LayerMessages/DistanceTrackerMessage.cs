using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{
    internal class DistanceTrackerMessage : NetworkLayerMessage<DistanceTrackerMessage>
    {
        public DateTime Timestamp { get; set; }
        public Message Message { get; set; }

        public DistanceTrackerMessage()
        {
        }
        public DistanceTrackerMessage(DateTime timestamp, Message message)
        {
            Timestamp = timestamp;
            Message = message;
        }
    }
}
