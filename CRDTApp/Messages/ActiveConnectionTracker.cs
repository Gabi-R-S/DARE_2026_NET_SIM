using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages
{
    public class ActiveConnectionTracker
    {
        object connectionLock = new object();
        HashSet<ReplicaID> activeConnections = new();
        HashSet<ReplicaID> innactiveConnections = new();

        public ActiveConnectionTracker(HashSet<ReplicaID> activeConnections, HashSet<ReplicaID> innactiveConnections) {
            this.activeConnections = activeConnections;
            this.innactiveConnections = innactiveConnections;
        }

        public List<ReplicaID> ActiveConnections { get {
                
                List<ReplicaID> replicas = new();
                lock (connectionLock)
                {
                    foreach (var connection in activeConnections) {
                        replicas.Add(connection);
                    }
                }
                return replicas;
                
            
            } }
        public List<ReplicaID> InnactiveConnections { get {
                List<ReplicaID> replicas = new();
                lock (connectionLock)
                {
                    foreach (var connection in innactiveConnections)
                    {
                        replicas.Add(connection);
                    }
                }
                return replicas;

            } }

        public List<ReplicaID> AllConnections {
            get
            {
                List<ReplicaID> replicas = new();
                lock (connectionLock)
                {
                    foreach (var connection in innactiveConnections)
                    {
                        replicas.Add(connection);
                    }
                    foreach (var connection in activeConnections)
                    {
                        replicas.Add(connection);
                    }
                }
                return replicas;

            }
        }
        public void SetActive(ReplicaID replica) {
            lock (connectionLock)
            {
                if (innactiveConnections.Contains(replica))
                {
                    innactiveConnections.Remove(replica);
                    activeConnections.Add(replica);
                }
            }
        }
        public void SetInnactive(ReplicaID replica)
        {
            lock (connectionLock)
            {
                if (activeConnections.Contains(replica))
                {
                    activeConnections.Remove(replica);
                    innactiveConnections.Add(replica);
                }
            }
        }
        public bool IsActive(ReplicaID replica) {
            bool b;
            lock (connectionLock)
            {
                b = activeConnections.Contains(replica);
            }
            return b;
        }
    }
}
