using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Simulator
{
    public class ConnectionMap
    {
        Dictionary<ReplicaID, Dictionary<ReplicaID, NetworkConnection>> connectionMap = new ();

        public void SetConnection(ReplicaID source, ReplicaID destination, NetworkConnection connection) {
            if (!connectionMap.ContainsKey(source)) {
                connectionMap[source] = new Dictionary<ReplicaID, NetworkConnection>();
            }
            connectionMap[source][destination] = connection;
        }
        public NetworkConnection? GetConnection(ReplicaID source, ReplicaID destination) {
            if (!connectionMap.ContainsKey(source)) {
                return null;
            }
            if (!connectionMap[source].ContainsKey(destination)) {
                return null;
            }
            return connectionMap[source][destination];
        }

        public bool IsConnectionAvailable(ReplicaID source, ReplicaID destination) {
            if (connectionMap.ContainsKey(source))
            {
                if (connectionMap[source].ContainsKey(destination)) {
                    return connectionMap[source][destination].Active;
                }
            }
            return false;
        }
    }
}
