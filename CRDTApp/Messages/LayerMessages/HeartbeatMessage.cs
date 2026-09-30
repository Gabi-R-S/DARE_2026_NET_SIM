using CRDTApp.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{
    public class HeartbeatMessage : NetworkLayerMessage<HeartbeatMessage>
    {
        public bool HeartBeat { get; set; }
        public Message? Message { get; set; }

        public HeartbeatMessage()
        {

        }

        public HeartbeatMessage(Message? message, bool heartbeat = false)
        {
            Message = message;

            HeartBeat = heartbeat;
        }

    }
}
