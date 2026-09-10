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
        public object Payload { get; private set; }
        public NetworkMessage(ReplicaID source, ReplicaID destination, object payload)
        {
            Source = source;
            Recipient = destination;
            Payload = payload;
        }
    }
}
