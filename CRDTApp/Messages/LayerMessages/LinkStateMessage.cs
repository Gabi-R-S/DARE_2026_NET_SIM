using CRDTApp.Messages;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{
    public class LinkStateMessage : NetworkLayerMessage<LinkStateMessage>
    {
        public ReplicaID OriginalSender { get; set; }
        public ReplicaID IntendedTarget { get; set; }
        public bool IsProtocolMessage { get; set; }
        public Message? Message { get; set; }

        public LinkStateMessage()
        {

        }

        public LinkStateMessage(ReplicaID intendedTarget, ReplicaID originalSender, Message? message, bool isProtocolMessage = false)
        {
            OriginalSender = originalSender;
            IntendedTarget = intendedTarget;
            Message = message;
            IsProtocolMessage = isProtocolMessage;

        }
    }

}
