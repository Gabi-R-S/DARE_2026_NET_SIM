using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkLayers.Composite.Routing
{
    //TODO: dynamically adjust distances/link cost based on latency
    public class LinkStateRoutingLayer : NetworkLayer
    {
        
        

        Dictionary<ReplicaID, LSA> mostRecentLSAs = new();

        object lsaHelperLock = new object();

        int num = 0;
        bool AddLSA(LSA lsa)
        {
            if (mostRecentLSAs.ContainsKey(lsa.Replica))
            {
                var mostRecentBefore = mostRecentLSAs[lsa.Replica];

                if (lsa.Number > mostRecentBefore.Number)
                {
                    mostRecentLSAs[lsa.Replica] = lsa;
                    return true;
                }

                return false;
            }
            else
            {
                mostRecentLSAs[lsa.Replica] = lsa;
                return true;
            }
        }
        LSA CreateNextLSA()
        {
            lock (lsaHelperLock)
            {
                var lsa = new LSA(replicaID, num, connectionTracker.ActiveConnections);
                num++;
                return lsa;
            }
        }

        




        void RecalculateRoutingTable()
        {
            Dictionary<ReplicaID,float> distancesTo = new();
            Dictionary<ReplicaID, ReplicaID> previous = new();

            distancesTo[replicaID] = 0;
            
            List<ReplicaID> processed = new List<ReplicaID>();

            var getClosestReplica = () =>
            {
                ReplicaID rep= new ReplicaID(int.MaxValue);
                float distance=float.PositiveInfinity;

                foreach (var repl in distancesTo.Keys) {
                    var dist = distancesTo[repl];

                    if (dist < distance && !processed.Contains(repl)) {
                        rep = repl;
                        distance = dist;
                    }
                }

                return (rep,distance);
            };

            while (processed.Count < distancesTo.Count) {
                (ReplicaID closest, float distance) = getClosestReplica();
                if (float.IsPositiveInfinity(distance)) break;

                processed.Add(closest);

                if (mostRecentLSAs.ContainsKey(closest))
                {
                    var neighbours = mostRecentLSAs[closest].Neighbours;

                    foreach (var neighbour in neighbours)
                    {
                        var distanceToNeighbour = distance + 1;
                        if (distanceToNeighbour < distancesTo.GetValueOrDefault(neighbour,float.PositiveInfinity))
                        {
                            distancesTo[neighbour] = distanceToNeighbour;
                            previous[neighbour] = closest;
                        }
                    }
                }
            }


             
            foreach (var dest in distancesTo.Keys) {
                var current = dest;
                var path = new List<ReplicaID>();
                
                while (current != replicaID && previous.ContainsKey(current)) {
                    path.Insert(0, current);
                    current = previous[current];
                }

                if (current == replicaID)
                {
                    if (path.Count > 0) {
                        nextNodeGivenTarget[dest] = path[0];
                    } else {
                        nextNodeGivenTarget[dest] = replicaID;
                    }
                }
                else {
                    nextNodeGivenTarget[dest] = dest;
                }
            }




        }









        ReplicaID replicaID;
        ActiveConnectionTracker connectionTracker;
        public LinkStateRoutingLayer(ReplicaID replicaID, NetworkLayer layerBelow, ActiveConnectionTracker connectionTracker) : base(layerBelow)
        {
            this.replicaID = replicaID;
            layerBelow.AddOnMessageReceivedHandler(OnLowerLevelMessageReceived);
            layerBelow.AddOnConnectionInterruptedHandler(OnDisconnect);
            layerBelow.AddOnConnectionRegainedHandler(OnConnect);
            this.connectionTracker = connectionTracker;
        }

        public void OnConnect(ReplicaID replica)
        {
            var lsa = CreateNextLSA();
            foreach (var oldLSA in mostRecentLSAs.Values)
            {
                SendLSA(replica, oldLSA);
            }
            FloodLSA(lsa);

            CallConnectionRegained(replica);
        }
        public void OnDisconnect(ReplicaID replica)
        {
            var lsa = CreateNextLSA();
            FloodLSA(lsa);
            CallConnectionInterrupted(replica);
        }

        Dictionary<ReplicaID, ReplicaID> nextNodeGivenTarget = new();


        ReplicaID GetNextNodeInPathTo(ReplicaID target)
        {
            if (nextNodeGivenTarget.ContainsKey(target))
            {
                return nextNodeGivenTarget[target];
            }
            return target;
        }




        void SendLSA(ReplicaID target, LSA lsa)
        {
            LayerBelow.SendMessage(target,
                new Message(
                    new LinkStateMessage(replicaID, target,
                        new Message(lsa.ToJson())
                        , true).ToJson()
                    )
                );
        }

        void FloodLSA(LSA lsa)
        {
            foreach (var replica in connectionTracker.ActiveConnections)
            {
                SendLSA(replica, lsa);
            }
        }

        public void OnLowerLevelMessageReceived(ReplicaID replica, Message message)
        {
            var linkMessage = LinkStateMessage.FromJson(message.Payload);
            if (linkMessage == null)
            {
                throw new Exception("Incorrect message format error in link state layer!");
            }

            if (linkMessage.IsProtocolMessage)
            {
                var lsa = LSA.FromJson(linkMessage.Message.Payload);

                if (lsa == null)
                {
                    throw new Exception("incorrect lsa format in link state protocol message!");
                }

                if (AddLSA(lsa))
                {
                    FloodLSA(lsa);
                    RecalculateRoutingTable();
                }

            }
            else
            {
                if (linkMessage.IntendedTarget == replicaID)
                {
                    CallMessageReceived(linkMessage.OriginalSender, linkMessage.Message);
                }
                else
                {
                    LayerBelow?.SendMessage(GetNextNodeInPathTo(linkMessage.IntendedTarget), message);
                }
            }

        }

        public override Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            var newMessage = new Message(new LinkStateMessage(recipient, replicaID, message).ToJson());

            return LayerBelow.SendMessage(GetNextNodeInPathTo(recipient), newMessage);
        }
    }
}
