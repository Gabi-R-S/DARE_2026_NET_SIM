using CRDTApp.Messages;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkLayers.Composite.Routing
{
    public class LinkStateRoutingLayer : NetworkLayer
    {
        public class LinkStateMessage : NetworkLayerMessage<LinkStateMessage>
        {
            public ReplicaID OriginalSender { get; set; }
            public ReplicaID IntendedTarget { get; set; }
            public bool IsProtocolMessage { get; set; }
            public Message? Message { get; set; }

            public LinkStateMessage()
            {

            }

            public LinkStateMessage(ReplicaID intendedTarget, ReplicaID originalSender, Message? message, bool isProtocolMessage = false)
            {
                OriginalSender = originalSender;
                IntendedTarget = intendedTarget;
                Message = message;
                IsProtocolMessage = isProtocolMessage;

            }
        }




        public class LSA : NetworkLayerMessage<LSA>
        {
            public ReplicaID Replica { get; set; }
            public int Number { get; set; }

            public List<ReplicaID> Neighbours { get; set; }
            public LSA(ReplicaID replica, int number, List<ReplicaID> neighbours)
            {
                Replica = replica;
                Number = number;
                Neighbours = neighbours;
            }
        }


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

        }




        ReplicaID replicaID;
        ActiveConnectionTracker connectionTracker;
        public LinkStateRoutingLayer(ReplicaID replicaID, NetworkLayer layerBelow, ActiveConnectionTracker connectionTracker) : base(layerBelow)
        {
            this.replicaID = replicaID;
            layerBelow.AddOnMessageReceivedHandler(OnLowerLevelMessageReceived);
            layerBelow.AddOnConnectionInterruptedHandler(OnConnect);
            layerBelow.AddOnConnectionRegainedHandler(OnDisconnect);
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
            layerBelow.SendMessage(target,
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
                    layerBelow?.SendMessage(GetNextNodeInPathTo(linkMessage.IntendedTarget), message);
                }
            }

        }

        public override Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            var newMessage = new Message(new LinkStateMessage(recipient, replicaID, message).ToJson());

            return layerBelow.SendMessage(GetNextNodeInPathTo(recipient), newMessage);
        }
    }
}
