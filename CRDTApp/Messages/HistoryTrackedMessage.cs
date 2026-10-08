using CRDTApp.Messages.LayerMessages;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages
{
    public class HistoryTrackedMessage : Parseable<HistoryTrackedMessage>
    {

        public string Message { get; set; }

        public VersionVector CausalityTracker { get; set; }

        public HistoryTrackedMessage()
        {
        }
        
        public HistoryTrackedMessage(string message, VersionVector causalityTracker)
        {

            Message = message;
            CausalityTracker = causalityTracker;

        }


    }
}
