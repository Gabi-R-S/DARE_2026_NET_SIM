using CRDTApp.Messages;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{
    public class LinkStateMessagePayload : Parseable<LinkStateMessagePayload>
    {
        public ReplicaID OriginalSender { get; set; }
        public ReplicaID IntendedTarget { get; set; }
        public bool IsProtocolMessage { get; set; }
        public string Message { get; set; }

        public LinkStateMessagePayload()
        {

        }

        public LinkStateMessagePayload(ReplicaID intendedTarget, ReplicaID originalSender, Message? message, bool isProtocolMessage = false)
        {
            OriginalSender = originalSender;
            IntendedTarget = intendedTarget;
            Message = message?.Encode() ?? "";
            IsProtocolMessage = isProtocolMessage;

        }
        public LinkStateMessagePayload(ReplicaID intendedTarget, ReplicaID originalSender, string message, bool isProtocolMessage = false)
        {
            OriginalSender = originalSender;
            IntendedTarget = intendedTarget;
            Message = message;
            IsProtocolMessage = isProtocolMessage;

        }
    }

}
