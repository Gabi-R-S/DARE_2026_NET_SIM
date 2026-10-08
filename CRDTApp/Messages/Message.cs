using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages
{
    public class Message:Parseable<Message>
    {

        public string ProtocolTag { get; set; }
        public string Payload { get; private set; }

        public Message(string tag, string payload)
        {
            ProtocolTag = payload;
            Payload = payload;
        }

        public override string ToString() {
            return Payload;
        }

    }
}
