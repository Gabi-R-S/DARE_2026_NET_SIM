using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Simulator
{
    public class ConnectionMap
    {
        Dictionary<ReplicaID, Dictionary<ReplicaID, NetworkConnection>> connectionMap = new ();

        public HashSet<ReplicaID> GetAllKnownReplicas()
        {
            return new HashSet<ReplicaID>(connectionMap.Keys);
        }

        public void AddReplica(ReplicaID replica)
        {
            if (!connectionMap.ContainsKey(replica))
            {
                connectionMap[replica] = new Dictionary<ReplicaID, NetworkConnection>();
            }
        }

        public bool RemoveReplica(ReplicaID replica)
        {
            if (!connectionMap.TryGetValue(replica, out var connections)) return false;

            foreach (var neighbor in new List<ReplicaID>(connections.Keys))
            {
                connectionMap[neighbor].Remove(replica);
            }

            connectionMap.Remove(replica);
            return true;
        }

        public void SetConnection(ReplicaID source, ReplicaID destination, NetworkConnection connection) {
            if (!connectionMap.ContainsKey(source)) {
                connectionMap[source] = new Dictionary<ReplicaID, NetworkConnection>();
            }
            if (!connectionMap.ContainsKey(destination))
            {
                connectionMap[destination] = new Dictionary<ReplicaID, NetworkConnection>();
            }
            connectionMap[source][destination] = connection;
            connectionMap[destination][source] = connection;
        }

        public bool RemoveConnection(ReplicaID source, ReplicaID destination)
        {
            bool removed = connectionMap.TryGetValue(source, out var sourceConnections) &&
                sourceConnections.Remove(destination);

            if (connectionMap.TryGetValue(destination, out var destinationConnections))
            {
                removed |= destinationConnections.Remove(source);
            }

            return removed;
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
