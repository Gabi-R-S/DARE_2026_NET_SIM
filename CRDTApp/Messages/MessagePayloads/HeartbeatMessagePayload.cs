using CRDTApp.Messages;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{
    public class HeartbeatMessagePayload : Parseable<HeartbeatMessagePayload>
    {
        public DateTime Timestamp { get; set; }

        public HeartbeatMessagePayload()
        {

        }

        public HeartbeatMessagePayload(DateTime timestamp)
        {
            Timestamp = timestamp;
        }

    }
}
