using CRDTApp.Messages;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{
    public class DistanceVectorMessagePayload : Parseable<DistanceVectorMessagePayload>
    {
        public ReplicaID OriginalSender { get; set; }
        public ReplicaID IntendedTarget { get; set; }
        public bool IsProtocolMessage { get; set; }
        public string Message { get; set; }
        public int TTLhops { get; set; }
        public DistanceVectorMessagePayload()
        {

        }

        public DistanceVectorMessagePayload(ReplicaID intendedTarget, ReplicaID originalSender, Message? message, bool isProtocolMessage, int ttlHops)
        {
            OriginalSender = originalSender;
            IntendedTarget = intendedTarget;
            Message = message?.Encode()??"";
            IsProtocolMessage = isProtocolMessage;
            TTLhops = ttlHops;

        }
        public DistanceVectorMessagePayload(ReplicaID intendedTarget, ReplicaID originalSender, string message, bool isProtocolMessage, int ttlHops)
        {
            OriginalSender = originalSender;
            IntendedTarget = intendedTarget;
            Message = message;
            IsProtocolMessage = isProtocolMessage;
            TTLhops = ttlHops;

        }
    }

}
