using CRDTApp.Messages;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{
    public class FloodMessagePayload : Parseable<FloodMessagePayload>
    {
        public ReplicaID OriginalSender { get; set; }
        public ReplicaID IntendedTarget { get; set; }
        public int NumberOfHops { get; set; }
        public uint Tag { get; set; }
        public Message? Message { get; set; }

        public FloodMessagePayload()
        {

        }

        public FloodMessagePayload(ReplicaID intendedTarget, ReplicaID originalSender, uint tag, Message? message, int numberOfHops)
        {
            OriginalSender = originalSender;
            IntendedTarget = intendedTarget;
            Tag = tag;
            Message = message;
            NumberOfHops = numberOfHops;

        }


    }

}
