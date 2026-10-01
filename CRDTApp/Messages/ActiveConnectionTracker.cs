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

        Dictionary<ReplicaID, float> distances = new();
        ReplicaID replicaID;
        bool trackingDistances;


        public ActiveConnectionTracker(HashSet<ReplicaID> activeConnections, HashSet<ReplicaID> innactiveConnections, ReplicaID replicaID, bool trackingDistances = false)
        {
            this.activeConnections = activeConnections;
            this.innactiveConnections = innactiveConnections;
            this.replicaID = replicaID;
            this.trackingDistances = trackingDistances;
        }

        public void SetDistance(ReplicaID replica, float distance)
        {
            if(trackingDistances)
            {
                lock (connectionLock)
                {
                    distances[replica] = distance;
                }
            }
        }
        public float GetDistance(ReplicaID replica)
        {
            if (replica == replicaID)
            {
                return 0.0f;
            }

            if (!trackingDistances)
            {


                lock (connectionLock)
                {
                    if (activeConnections.Contains(replica))
                    {
                        return 1.0f;
                    }
                    return float.PositiveInfinity;
                }
            }
            lock (connectionLock)
            {
                return distances.ContainsKey(replica) ? distances[replica] : float.PositiveInfinity;
            }
        }


        public List<ReplicaID> ActiveConnections
        {
            get
            {

                List<ReplicaID> replicas = new();
                lock (connectionLock)
                {
                    foreach (var connection in activeConnections)
                    {
                        replicas.Add(connection);
                    }
                }
                return replicas;


            }
        }
        public List<ReplicaID> InnactiveConnections
        {
            get
            {
                List<ReplicaID> replicas = new();
                lock (connectionLock)
                {
                    foreach (var connection in innactiveConnections)
                    {
                        replicas.Add(connection);
                    }
                }
                return replicas;

            }
        }

        public List<ReplicaID> AllConnections
        {
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
        public bool SetActive(ReplicaID replica)
        {
            lock (connectionLock)
            {
                if (innactiveConnections.Contains(replica))
                {
                    innactiveConnections.Remove(replica);
                    activeConnections.Add(replica);
                    return true;
                }
            }
            return false;
        }
        public bool SetInnactive(ReplicaID replica)
        {
            lock (connectionLock)
            {
                if (activeConnections.Contains(replica))
                {
                    activeConnections.Remove(replica);
                    innactiveConnections.Add(replica);
                    return true;
                }
            }
            return false;
        }
        public bool IsActive(ReplicaID replica)
        {
            bool b;
            lock (connectionLock)
            {
                b = activeConnections.Contains(replica);
            }
            return b;
        }
    }
}
