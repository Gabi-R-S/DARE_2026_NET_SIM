using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp
{
    public class Message
    {
        public string Payload { get; private set; }

        public Message(string payload)
        {
            Payload = payload;
        }

        public override string ToString() {
            return Payload;
        }

    }
}
