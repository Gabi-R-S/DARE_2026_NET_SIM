using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Simulator
{
    public class NetworkMessage
    {
        public ReplicaID Source { get; private set; }
        public ReplicaID Recipient { get; private set; }
        public string Payload { get; private set; }
        public NetworkMessage(ReplicaID source, ReplicaID destination, string payload)
        {
            Source = source;
            Recipient = destination;
            Payload = payload;
        }
    }
}
