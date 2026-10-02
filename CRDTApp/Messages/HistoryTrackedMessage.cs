using CRDTApp.Messages.LayerMessages;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages
{
    public class HistoryTrackedMessage:NetworkLayerMessage<HistoryTrackedMessage>
    {

        public Message? Message { get; set; }

        public VersionVector CausalityTracker { get; set; }

        HistoryTrackedMessage()
        {
        }
        public HistoryTrackedMessage(Message? message, VersionVector causalityTracker)
        {

            Message = message;
            CausalityTracker = causalityTracker;


        }

    }
}
