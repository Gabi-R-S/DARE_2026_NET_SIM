using CRDTApp.Messages;
using Shared;
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


        class HelloContent : NetworkLayerMessage<HelloContent>
        {
            public List<ReplicaID> Neighbours { get; set; }
            public HelloContent( List<ReplicaID> neighbours)
            {
                Neighbours = neighbours;
            }
        }
        class DatabaseDescriptionContent:NetworkLayerMessage<DatabaseDescriptionContent>
        {
            public List<string> KnownLSAs { get; set; }
            public DatabaseDescriptionContent(List<string> knownLSAs)
            {
                KnownLSAs = knownLSAs;
            }
        }
        class LinkStateRequestContent : NetworkLayerMessage<LinkStateRequestContent>
        {
            public List<string> MissingLSAs { get; set; }
            public LinkStateRequestContent(List<string> missingLSAs)
            {
                MissingLSAs = missingLSAs;
            }
        }
        class LinkStateUpdateContent :NetworkLayerMessage<LinkStateUpdateContent>
        {
            public List<LSA> LSAs { get; set; }
            public bool Flood { get; set; }
            public LinkStateUpdateContent(List<LSA> lsas,bool flood=false) {
                LSAs = lsas;
                Flood = Flood;

            }
        }
    
        class LinkStateProtocolMessage : NetworkLayerMessage<LinkStateProtocolMessage>
        {
            public string Type { get; set; }

            public string Content { get; set; }

            public LinkStateProtocolMessage(string type, string content)
            {
                Type = type;
                Content = content;
            }
        }

        class LSA
        {
            public string Tag { get; set; }

        }

        class LinkStateDB
        {

            public enum State
            {
                UNKNOWN,
                INIT,
                TWOWAY,
                EXCHANGE,
                LOADING,
                FULL
            }

            Dictionary<ReplicaID, List<(ReplicaID, float)>> edges = new();
            Dictionary<ReplicaID, State> directNeighbours = new();

            public bool IsStable() {
                foreach (var state in directNeighbours.Values) {
                    if (state != State.FULL) {
                        return false;
                    }
                }
                return true;
            }
            public void AddLSA(LSA lsa)
            {
                updates.Add(lsa.Tag, lsa);
            }

            public List<string> KnownLSATags() {
                return new(updates.Keys);
            }
            public LSA GetLSA(string tag) {
                return updates[tag];
            }
            Dictionary<string, LSA> updates = new();
            public void SetNeighbourState(ReplicaID replica, State state)
            {
                if (state == State.UNKNOWN)
                {
                    directNeighbours.Remove(replica);
                }
                else
                {
                    directNeighbours[replica] = state;
                }

            }
            public State GetStateOf(ReplicaID neighbour)
            {
                return directNeighbours.GetValueOrDefault(neighbour, State.UNKNOWN);
            }
            public List<ReplicaID> GetAllOurKnownNeighbours()
            {
                return new(directNeighbours.Keys);
            }

            public List<ReplicaID> GetNeighboursOf(ReplicaID replica)
            {

                List<ReplicaID> neighbours = new();
                if (edges.ContainsKey(replica))
                {
                    foreach (var (neighbour, cost) in edges[replica])
                    {
                        neighbours.Add(neighbour);
                    }
                }
                return neighbours;
            }

            public LSA CreateNextLSA()
            {
                throw new NotImplementedException("Aaaaaaaaaaaaaaaa");
            }
        }




        LinkStateDB database;

        void TryToRecalculateRoutingTable()
        {
            if (database.IsStable()) {
                throw new NotImplementedException("dikstra or whatever...");
            }
        }

        async Task HandleConnectionTo(ReplicaID replica) {
            throw new NotImplementedException();
        
        }


        public override Task Run()
        {
            var t = base.Run();

            foreach (var replica in connectionTracker.AllConnections) {
                HandleConnectionTo(replica);
            }

            return t;
        }

        ReplicaID replicaID;
        ActiveConnectionTracker connectionTracker;
        public LinkStateRoutingLayer(ReplicaID replicaID, NetworkLayer layerBelow,ActiveConnectionTracker connectionTracker) : base(layerBelow, false, true, true)
        {
            this.replicaID = replicaID;
            layerBelow.AddOnMessageReceivedHandler(OnLowerLevelMessageReceived);
            database = new LinkStateDB();
            this.connectionTracker = connectionTracker;
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

        void SendHelloTo(ReplicaID target)
        {
            layerBelow.SendMessage(target,
                new Message(
                    new LinkStateMessage(target, replicaID,
                    new Message(
                    new LinkStateProtocolMessage("hello",
                    new HelloContent(database.GetAllOurKnownNeighbours())
                    .ToJson())
                    .ToJson()), true)
                    .ToJson()));
        }

        void SendDatabaseDescription(ReplicaID target)
        {
            layerBelow.SendMessage(target,
                   new Message(
                       new LinkStateMessage(target, replicaID,
                       new Message(
                       new LinkStateProtocolMessage("dbd",
                       new DatabaseDescriptionContent(database.KnownLSATags())
                       .ToJson())
                       .ToJson()), true)
                       .ToJson()));
        }

        void SendLinkStateRequest(ReplicaID target, List<string> missingLSAs) {

            layerBelow.SendMessage(target,
                   new Message(
                       new LinkStateMessage(target, replicaID,
                       new Message(
                       new LinkStateProtocolMessage("lsr",
                       new LinkStateRequestContent(missingLSAs)
                       .ToJson())
                       .ToJson()), true)
                       .ToJson()));
        }

        void SendLinkStateUpdate(ReplicaID target, List<LSA> LSAs) {
            layerBelow.SendMessage(target,
                     new Message(
                         new LinkStateMessage(target, replicaID,
                         new Message(
                         new LinkStateProtocolMessage("lsr",
                         new LinkStateUpdateContent(LSAs)
                         .ToJson())
                         .ToJson()), true)
                         .ToJson()));
        }



        Dictionary<ReplicaID, Queue<LinkStateProtocolMessage>> protocolMessages;
        Dictionary<ReplicaID, Queue<LSA>> lsasToForward;

        public void OnLowerLevelMessageReceived(ReplicaID replica, Message message)
        {
            var linkMessage = LinkStateMessage.FromJson(message.Payload);

            if (linkMessage == null)
            {
                throw new Exception("Incorrect message format error in link state layer!");
            }

            if (linkMessage.IsProtocolMessage)
            {
                var protocolMessage = LinkStateProtocolMessage.FromJson(linkMessage.Message.Payload);

                if (protocolMessage == null)
                {
                    throw new Exception("incorrect message format in link state protocol message!");
                }

                switch (protocolMessage.Type)
                {
                    case "hello":
                        var helloMessage = HelloContent.FromJson(protocolMessage.Content);
                        if (helloMessage.Neighbours.Contains(replicaID))
                        {
                            if (database.GetStateOf(replica) == LinkStateDB.State.INIT)
                            {
                                database.SetNeighbourState(replica, LinkStateDB.State.TWOWAY);
                                SendHelloTo(replica);
                            } else if (database.GetStateOf(replica) == LinkStateDB.State.TWOWAY) {

                                database.SetNeighbourState(replica, LinkStateDB.State.EXCHANGE);
                                SendDatabaseDescription(replica);
                            }
                            else
                            {
                                Console.WriteLine("IDK, help me");
                            }
                        }
                        else
                        {
                            database.SetNeighbourState(replica, LinkStateDB.State.INIT);
                            SendHelloTo(replica);
                        }
                        break;
                    case "dbd":
                        var dbdMessage = DatabaseDescriptionContent.FromJson(protocolMessage.Content);
                        if (database.GetStateOf(replica) != LinkStateDB.State.UNKNOWN) {
                            database.SetNeighbourState(replica, LinkStateDB.State.LOADING);
                            List<string> missingTags = new();

                            var knownTags = database.KnownLSATags();
                            foreach (var tag in dbdMessage.KnownLSAs) {
                                if (!knownTags.Contains(tag)) {
                                    missingTags.Add(tag);
                                }
                            }
                            if (missingTags.Count == 0) {
                                database.SetNeighbourState(replica,LinkStateDB.State.FULL);
                                TryToRecalculateRoutingTable();
                            }
                            else {
                                SendLinkStateRequest(replica, missingTags);
                            } 
                        }
                        break;
                    case "lsr":
                        var lsrMessage = LinkStateRequestContent.FromJson(protocolMessage.Content);
                        if (database.GetStateOf(replica)!=LinkStateDB.State.UNKNOWN)
                        {
                            List<LSA> LSAs = new();

                            foreach (var tag in lsrMessage.MissingLSAs) {
                                LSAs.Add(database.GetLSA(tag));
                            }

                            SendLinkStateUpdate(replica,LSAs);
                        }

                        break;
                    case "lsu":
                        var lsuMessage = LinkStateUpdateContent.FromJson(protocolMessage.Content);
                        foreach (var lsa in lsuMessage.LSAs) {
                            var hasLSA = database.KnownLSATags().Contains(lsa.Tag);
                            database.AddLSA(lsa);
                            //If flood==true and is a new lsa,forward it to all! 
                            if (lsuMessage.Flood&&) {
                                foreach(var rep in )
                                lsasToForward[] 
                            }
                        }

                        break;
                    default:
                        throw new Exception("unknown link state protocol message type");
                }

                throw new NotImplementedException();
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
