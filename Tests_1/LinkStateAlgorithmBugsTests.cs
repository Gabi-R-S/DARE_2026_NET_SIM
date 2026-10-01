using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.NetworkLayers.Composite.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shared;

namespace Tests_1
{
    /// <summary>
    /// Test suite asserting the CORRECT, expected behavior of the Link State routing algorithm.
    /// Each test is designed to catch an undetected bug or algorithmic violation in LinkStateRoutingLayer.
    /// These tests will FAIL against the current buggy implementation, and will
    /// ONLY PASS once the underlying link state algorithm bugs have been corrected.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class LinkStateAlgorithmBugsTests
    {
        private static (LinkStateMessage? LinkMsg, LSA? Lsa) ParseSentProtocolMessage(Message msg)
        {
            var linkMsg = LinkStateMessage.FromJson(msg.Payload);
            if (linkMsg == null || !linkMsg.IsProtocolMessage || linkMsg.Message == null)
            {
                return (linkMsg, null);
            }

            var lsa = LSA.FromJson(linkMsg.Message.Payload);
            return (linkMsg, lsa);
        }

        [TestMethod]
        public void LinkState_LSAFlooding_MustNotEchoBackToSenderNeighbor()
        {
            // EXPECTED BEHAVIOR:
            // In Link State routing protocols, when an LSA is received from neighbor B,
            // the node should flood the LSA to all active connections EXCEPT neighbor B (the sender).
            //
            // CURRENT BUG:
            // LinkStateRoutingLayer.FloodLSA iterates connectionTracker.ActiveConnections without
            // filtering out the neighbor that transmitted the LSA. As a result, the LSA is echoed
            // directly back to the sender neighbor, wasting bandwidth and triggering redundant processing.

            var mockLower = new ControllableMockLayer();
            var rA = new ReplicaID(1); // Self
            var rB = new ReplicaID(2); // Neighbor who sends LSA
            var rC = new ReplicaID(3); // Other neighbor

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rB, rC },
                new HashSet<ReplicaID>(),
                rA);

            var lsLayer = new LinkStateRoutingLayer(rA, mockLower, tracker);

            // Node D advertises its connectivity
            var rD = new ReplicaID(4);
            var lsaD = new LSA(rD, 1, new List<(ReplicaID, float)> { (rB, 1f) });
            var msgD = new LinkStateMessage(rA, rB, new Message(lsaD.ToJson()), isProtocolMessage: true);

            mockLower.SentMessages.Clear();
            lsLayer.OnLowerLevelMessageReceived(rB, new Message(msgD.ToJson()));

            // Assert: LSA must be flooded to other neighbor C, but MUST NOT be echoed back to sender B
            var sentToB = mockLower.SentMessages.Any(m => m.Recipient == rB);
            var sentToC = mockLower.SentMessages.Any(m => m.Recipient == rC);

            Assert.IsTrue(sentToC, "LSA should be flooded to other active neighbor C");
            Assert.IsFalse(sentToB, "BUG: Node echoed LSA back to neighbor B from which it was received");
        }

        [TestMethod]
        public void LinkState_SelfLSA_MustRejectAndNotRefloodForeignSpoofedSelfLSA()
        {
            // EXPECTED BEHAVIOR:
            // A router is the sole authority for its own Link State Advertisement (LSA).
            // If a node receives an LSA claiming to originate from itself (lsa.Replica == replicaID),
            // it must not accept foreign advertisements about its own links, and must NEVER
            // re-flood this foreign self-LSA to other neighbors.
            //
            // CURRENT BUG:
            // LinkStateRoutingLayer.OnLowerLevelMessageReceived calls AddLSA without checking whether
            // lsa.Replica == replicaID. It accepts the external LSA, overwrites its own authoritative
            // LSA in mostRecentLSAs, and calls FloodLSA to broadcast the spoofed/reflected LSA to all neighbors.

            var mockLower = new ControllableMockLayer();
            var rA = new ReplicaID(1); // Self
            var rB = new ReplicaID(2); // Neighbor sending spoofed LSA
            var rC = new ReplicaID(3); // Other neighbor

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rB, rC },
                new HashSet<ReplicaID>(),
                rA);

            var lsLayer = new LinkStateRoutingLayer(rA, mockLower, tracker);

            // Neighbor B sends an LSA claiming Node A is connected only to B (excluding C)
            var spoofedSelfLsa = new LSA(rA, 10, new List<(ReplicaID, float)> { (rB, 1f) });
            var msg = new LinkStateMessage(rA, rB, new Message(spoofedSelfLsa.ToJson()), isProtocolMessage: true);

            mockLower.SentMessages.Clear();
            lsLayer.OnLowerLevelMessageReceived(rB, new Message(msg.ToJson()));

            // Assert: Node A must not re-flood a received LSA that claims to be from Node A
            var refloodedSelfLsa = mockLower.SentMessages
                .Select(m => ParseSentProtocolMessage(m.Message).Lsa)
                .Any(lsa => lsa != null && lsa.Replica == rA);

            Assert.IsFalse(refloodedSelfLsa, "BUG: Node accepted and re-flooded an externally received LSA claiming to be from self");
        }





        [TestMethod]
        public void LinkState_OnConnect_MustNotSendDuplicateSelfLSAToConnectingPeer()
        {
            // EXPECTED BEHAVIOR:
            // When a neighbor connects (OnConnect), the local node generates its new LSA and
            // synchronizes with the peer. The connecting peer must receive the new self LSA EXACTLY ONCE.
            //
            // CURRENT BUG:
            // In OnConnect:
            //   var lsa = CreateNextLSA(); // AddLSA(lsa) adds it to mostRecentLSAs
            //   foreach (var oldLSA in mostRecentLSAs.Values) SendLSA(replica, oldLSA); // sends lsa to replica
            //   FloodLSA(lsa); // sends lsa to all active connections, including replica again!
            // The connecting peer is sent two identical copies of the self LSA on connection.

            var mockLower = new ControllableMockLayer();
            var rA = new ReplicaID(1); // Self
            var rB = new ReplicaID(2); // Connecting peer

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rB },
                new HashSet<ReplicaID>(),rA);

            var lsLayer = new LinkStateRoutingLayer(rA, mockLower, tracker);

            mockLower.SentMessages.Clear();
            lsLayer.OnConnect(rB);

            var selfLsaCopiesSentToB = mockLower.SentMessages
                .Where(m => m.Recipient == rB)
                .Select(m => ParseSentProtocolMessage(m.Message).Lsa)
                .Count(lsa => lsa != null && lsa.Replica == rA);

            Assert.AreEqual(1, selfLsaCopiesSentToB, "BUG: OnConnect sent duplicate copies of self LSA to the connecting peer");
        }

        [TestMethod]
        public void LinkState_OnConnect_MustNotSendPeerItsOwnStaleLSABackToPeer()
        {
            // EXPECTED BEHAVIOR:
            // When neighbor B reconnects, Node A exchanges stored LSAs to sync B with the rest
            // of the network. Node A must NOT transmit neighbor B's own stale LSA back to neighbor B.
            //
            // CURRENT BUG:
            // OnConnect iterates all `mostRecentLSAs.Values` and calls `SendLSA(replica, oldLSA)`.
            // It does not filter out `oldLSA.Replica == replica`, sending the peer's own prior LSA back to it.

            var mockLower = new ControllableMockLayer();
            var rA = new ReplicaID(1); // Self
            var rB = new ReplicaID(2); // Peer reconnecting
            var rC = new ReplicaID(3); // Other node

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rB, rC },
                new HashSet<ReplicaID>(),
                rA);

            var lsLayer = new LinkStateRoutingLayer(rA, mockLower, tracker);

            // Prior to reconnection, Node A has stored an LSA from Node B and Node C
            var oldLsaB = new LSA(rB, 1, new List<(ReplicaID, float)> { (rA, 1f) });
            var oldLsaC = new LSA(rC, 1, new List<(ReplicaID, float)> { (rA, 1f) });
            lsLayer.OnLowerLevelMessageReceived(rB, new Message(new LinkStateMessage(rA, rB, new Message(oldLsaB.ToJson()), true).ToJson()));
            lsLayer.OnLowerLevelMessageReceived(rC, new Message(new LinkStateMessage(rA, rC, new Message(oldLsaC.ToJson()), true).ToJson()));

            mockLower.SentMessages.Clear();
            lsLayer.OnConnect(rB);

            var sentPeerOwnLsa = mockLower.SentMessages
                .Where(m => m.Recipient == rB)
                .Select(m => ParseSentProtocolMessage(m.Message).Lsa)
                .Any(lsa => lsa != null && lsa.Replica == rB);

            Assert.IsFalse(sentPeerOwnLsa, "BUG: OnConnect sent peer B its own stale LSA back to peer B");
        }

        [TestMethod]
        public void LinkState_OnDisconnect_MustPurgeMultiHopRoutesThroughDisconnectedNeighbor()
        {
            // EXPECTED BEHAVIOR:
            // When the link to neighbor B is interrupted (OnDisconnect), any multi-hop routes
            // that routed through neighbor B (e.g. route to destination D via B) must be purged.
            //
            // CURRENT BUG:
            // In RecalculateRoutingTable, `nextNodeGivenTarget` is never cleared.
            // When B disconnects, destination D becomes unreachable and is absent from distancesTo.Keys.
            // nextNodeGivenTarget[rD] retains its stale mapping to disconnected neighbor B,
            // causing subsequent traffic for D to be sent to disconnected neighbor B (black-hole routing).

            var mockLower = new ControllableMockLayer();
            var rA = new ReplicaID(1); // Self
            var rB = new ReplicaID(2); // Neighbor providing path to D
            var rC = new ReplicaID(3); // Other neighbor
            var rD = new ReplicaID(4); // Destination behind B

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rB, rC },
                new HashSet<ReplicaID>(),rA);

            var lsLayer = new LinkStateRoutingLayer(rA, mockLower, tracker);

            // Initial topology: A connected to B, C; B connected to A, D; D connected to B
            lsLayer.OnConnect(rB);

            var lsaB = new LSA(rB, 1, new List<(ReplicaID, float)> { (rA, 1f), (rD, 1f) });
            lsLayer.OnLowerLevelMessageReceived(rB, new Message(new LinkStateMessage(rA, rB, new Message(lsaB.ToJson()), true).ToJson()));

            var lsaD = new LSA(rD, 1, new List<(ReplicaID, float)> { (rB, 1f) });
            lsLayer.OnLowerLevelMessageReceived(rB, new Message(new LinkStateMessage(rA, rD, new Message(lsaD.ToJson()), true).ToJson()));

            // Verify initial route to D is via B
            mockLower.SentMessages.Clear();
            lsLayer.SendMessage(rD, new Message("msg to D"));
            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreEqual(rB, mockLower.SentMessages[0].Recipient);

            // Neighbor B disconnects
            tracker.SetInnactive(rB);
            mockLower.InjectConnectionInterrupted(rB);

            // Now send to D again
            mockLower.SentMessages.Clear();
            lsLayer.SendMessage(rD, new Message("msg after disconnect"));

            // Assert: Must NOT route to disconnected neighbor B
            var sentToDisconnectedB = mockLower.SentMessages.Any(m => m.Recipient == rB);
            Assert.IsFalse(sentToDisconnectedB, "BUG: Route to destination 4 through disconnected neighbor 2 was not purged, sending to disconnected neighbor 2");
        }

        [TestMethod]
        public void LinkState_Dijkstra_AsymmetricLink_MustNotRouteOverOneWayAdjacency()
        {
            // EXPECTED BEHAVIOR:
            // In Link State routing (Dijkstra algorithm), an edge between two nodes X and Y
            // is only valid if the link is bidirectional (two-way connectivity): X must list Y,
            // and Y must list X in their respective LSAs.
            // If neighbor B claims connectivity to destination D, but D does NOT list B (one-way/broken link),
            // and neighbor C has a confirmed bidirectional path to D (both C and D list each other),
            // the algorithm must route towards D via C, NOT via B.
            //
            // CURRENT BUG:
            // RecalculateRoutingTable traverses any neighbor listed in mostRecentLSAs[closest].Neighbours
            // without validating whether that neighbor's LSA also contains `closest`.
            // It blindly accepts one-way advertisements, leading to paths over broken or non-existent links.

            var mockLower = new ControllableMockLayer();
            var rA = new ReplicaID(1); // Self
            var rB = new ReplicaID(2); // Neighbor with broken/one-way link to D
            var rC = new ReplicaID(3); // Neighbor with valid bidirectional link to D
            var rD = new ReplicaID(4); // Destination

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rB, rC },
                new HashSet<ReplicaID>(),rA);

            var lsLayer = new LinkStateRoutingLayer(rA, mockLower, tracker);

            // Self LSA
            lsLayer.OnConnect(rB);

            // Node B claims: connected to A and D
            var lsaB = new LSA(rB, 1, new List<(ReplicaID, float)> { (rA, 1f), (rD, 1f) });
            lsLayer.OnLowerLevelMessageReceived(rB, new Message(new LinkStateMessage(rA, rB, new Message(lsaB.ToJson()), true).ToJson()));

            // Node C claims: connected to A and D
            var lsaC = new LSA(rC, 1, new List<(ReplicaID, float)> { (rA, 1f), (rD, 1f) });
            lsLayer.OnLowerLevelMessageReceived(rC, new Message(new LinkStateMessage(rA, rC, new Message(lsaC.ToJson()), true).ToJson()));

            // Node D claims: connected ONLY to C (D does NOT have a link to B!)
            var lsaD = new LSA(rD, 1, new List<(ReplicaID, float)> { (rC, 1f) });
            lsLayer.OnLowerLevelMessageReceived(rC, new Message(new LinkStateMessage(rA, rD, new Message(lsaD.ToJson()), true).ToJson()));

            mockLower.SentMessages.Clear();
            lsLayer.SendMessage(rD, new Message("data for D"));

            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreEqual(rC, mockLower.SentMessages[0].Recipient,
                "BUG: Dijkstra routed via neighbor B over an unconfirmed one-way link B->D, instead of valid bidirectional path via C");
        }

        [TestMethod]
        public void LinkState_TransitMessage_LoopPrevention_MustNotHairpinBackToSender()
        {
            // EXPECTED BEHAVIOR:
            // In hop-by-hop mesh routing, a router must not forward a transit data packet
            // directly back out the same neighbor interface from which it just arrived (hairpinning).
            // Forwarding back to the incoming sender immediately forms a two-node routing loop (ping-pong).
            //
            // CURRENT BUG:
            // In OnLowerLevelMessageReceived:
            //   LayerBelow?.SendMessage(GetNextNodeInPathTo(linkMessage.IntendedTarget), message);
            // If nextNodeGivenTarget points to the same neighbor `replica` that sent the message
            // (e.g. during a routing convergence transient), the message is reflected directly back to `replica`.

            var mockLower = new ControllableMockLayer();
            var rA = new ReplicaID(1); // Self
            var rB = new ReplicaID(2); // Neighbor
            var rD = new ReplicaID(4); // Destination

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rB },
                new HashSet<ReplicaID>(), rA);

            var lsLayer = new LinkStateRoutingLayer(rA, mockLower, tracker);

            // Topology: A connected to B, B connected to D
            var lsaA = new LSA(rA, 0, new List<(ReplicaID, float)> { (rB, 1f) });
            lsLayer.OnLowerLevelMessageReceived(rA, new Message(new LinkStateMessage(rA, rA, new Message(lsaA.ToJson()), true).ToJson()));

            var lsaB = new LSA(rB, 1, new List<(ReplicaID, float)> { (rA, 1f), (rD, 1f) });
            lsLayer.OnLowerLevelMessageReceived(rB, new Message(new LinkStateMessage(rA, rB, new Message(lsaB.ToJson()), true).ToJson()));

            // Transit packet arrives from B intended for D (where A's route to D points back to B)
            var transitMsg = new LinkStateMessage(rD, new ReplicaID(99), new Message("loop payload"), isProtocolMessage: false);

            mockLower.SentMessages.Clear();
            lsLayer.OnLowerLevelMessageReceived(rB, new Message(transitMsg.ToJson()));

            // Assert: Must not reflect transit packet back to sender B
            var echoedBackToB = mockLower.SentMessages.Any(m => m.Recipient == rB);
            Assert.IsFalse(echoedBackToB, "BUG: Router hairpinned transit packet directly back to sender neighbor B, creating a routing loop");
        }
    }
}
