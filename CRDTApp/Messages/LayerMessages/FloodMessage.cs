using CRDTApp.Messages;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{
    public class FloodMessage : NetworkLayerMessage<FloodMessage>
    {
        public ReplicaID OriginalSender { get; set; }
        public ReplicaID IntendedTarget { get; set; }
        public uint NumberOfHops { get; set; }
        public Message? Message { get; set; }

        public FloodMessage()
        {

        }

        public FloodMessage(ReplicaID intendedTarget, ReplicaID originalSender, Message? message, uint numberOfHops)
        {
            OriginalSender = originalSender;
            IntendedTarget = intendedTarget;
            Message = message;
            NumberOfHops = numberOfHops;

        }


    }

}
