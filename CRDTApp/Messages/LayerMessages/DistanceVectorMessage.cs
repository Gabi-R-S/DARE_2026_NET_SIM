using CRDTApp.Messages;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{
    public class DistanceVectorMessage : NetworkLayerMessage<DistanceVectorMessage>
    {
        public ReplicaID OriginalSender { get; set; }
        public ReplicaID IntendedTarget { get; set; }
        public bool IsProtocolMessage { get; set; }
        public Message? Message { get; set; }
        public uint TTLhops { get; set; }
        public DistanceVectorMessage()
        {

        }

        public DistanceVectorMessage(ReplicaID intendedTarget, ReplicaID originalSender, Message? message, bool isProtocolMessage, uint ttlHops)
        {
            OriginalSender = originalSender;
            IntendedTarget = intendedTarget;
            Message = message;
            IsProtocolMessage = isProtocolMessage;
            TTLhops = ttlHops;

        }
    }

}
