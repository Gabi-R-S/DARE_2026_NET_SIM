using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.NetworkLayers.Composite.Routing;
using CRDTApp.Messages.LayerMessages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Shared;

namespace Tests_1
{
    /// <summary>
    /// Test suite asserting the CORRECT, expected behavior of routing algorithms.
    /// Each test is designed to catch a specific bug in the current implementation.
    /// These tests will FAIL against the current buggy implementation, and will
    /// ONLY PASS once the underlying routing bugs have been corrected.
    /// </summary>
    [TestClass]
    public class RoutingAlgorithmBugsTests
    {
        #region DistanceVectorRoutingLayer Bug Tests

        [TestMethod]
        public async Task DistanceVector_GetUpdate_MustAdvertiseDestinationsNotNextHops()
        {
            // EXPECTED BEHAVIOR:
            // When advertising distance vector table updates to neighbors, the advertised
            // dictionary keys must be the destination ReplicaIDs, NOT the next-hop ReplicaIDs.
            //
            // CURRENT BUG:
            // DistanceTable.GetUpdate iterates distances.Values as (float k, ReplicaID v)
            // and sets messgDistances[(int)v.ID] = k, which puts the nextHop ID (v)
            // as the dictionary key instead of the destination ID. Destinations sharing
            // a next hop overwrite each other, and the actual destination IDs are never advertised.

            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var r1 = new ReplicaID(1); // Self
            var r2 = new ReplicaID(2); // Next hop
            var r3 = new ReplicaID(3); // Destination 1
            var r4 = new ReplicaID(4); // Destination 2
            var r5 = new ReplicaID(5); // neighbour

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { r2,r5 },
                new HashSet<ReplicaID>());

            var dvLayer = new DistanceVectorRoutingLayer(r1, tracker, mockLower, scheduler, 10);

            // Neighbor 2 advertises reachability to destination 3 (cost 2) and destination 4 (cost 5)
            var updatePayload = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float>
                {
                    { (int)r3.ID, 2.0f },
                    { (int)r4.ID, 5.0f }
                },
                Timestamp = scheduler.GetCurrentTime()
            });

            var updateMsg = new DistanceVectorMessage(r1, r2, new Message(updatePayload), isProtocolMessage: true, 5);
            dvLayer.OnLowerLayerMessageReceived(r2, new Message(updateMsg.ToJson()));
            
            mockLower.SentMessages.Clear();
            await dvLayer.Run();
            scheduler.AdvanceSeconds(1.1f);
            
            var sentProtoMsg = mockLower.SentMessages
                .Select(m => DistanceVectorMessage.FromJson(m.Message.Payload))
                .FirstOrDefault(m => m != null && m.IsProtocolMessage && m.IntendedTarget==r5);

            Assert.IsNotNull(sentProtoMsg, "Heartbeat protocol message should have been sent");

            var updateObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(sentProtoMsg.Message?.Payload ?? "");
            Assert.IsNotNull(updateObj);

            var distancesJson = JsonConvert.DeserializeObject<Dictionary<int, float>>(updateObj["Distances"].ToString() ?? "");
            Assert.IsNotNull(distancesJson);

            Console.WriteLine(distancesJson.Values.ToList()[0] +" ");
            // Assert CORRECT behavior: destinations 3 and 4 should be in the advertised table!
            Assert.IsTrue(distancesJson.ContainsKey((int)r3.ID), 
                "CORRECT BEHAVIOR: Distance update must contain destination 3 as an advertised key");
            Assert.IsTrue(distancesJson.ContainsKey((int)r4.ID), 
                "CORRECT BEHAVIOR: Distance update must contain destination 4 as an advertised key");
            Assert.IsFalse(distancesJson.ContainsKey((int)r2.ID) && !distancesJson.ContainsKey((int)r3.ID),
                "BUG: Update advertised next-hop 2 instead of destinations 3 and 4");
        }

        [TestMethod]
        public void DistanceVector_Update_WhenNextHopSendsNewDistance_MustMaintainNextHop()
        {
            // EXPECTED BEHAVIOR:
            // When our current next hop (r2) for destination r3 reports a new distance metric,
            // the next hop in the routing table should remain r2.
            //
            // CURRENT BUG:
            // In DistanceTable.Update:
            //   if (sender == distances[other.Key].Item2)
            //       distances[other.Key] = (other.Value, other.Key);
            // It sets the next hop to other.Key (destination 3 itself!), corrupting the route
            // so subsequent sends attempt to send directly to destination 3 rather than via neighbor 2.

            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var r1 = new ReplicaID(1); // Self
            var r2 = new ReplicaID(2); // Neighbor / Next-hop
            var r3 = new ReplicaID(3); // Multi-hop destination

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { r2 },
                new HashSet<ReplicaID>());

            var dvLayer = new DistanceVectorRoutingLayer(r1, tracker, mockLower, scheduler, 10);

            // Initial update: Neighbor 2 advertises path to 3 with cost 4
            var update1 = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float> { { (int)r3.ID, 4.0f } },
                Timestamp = scheduler.GetCurrentTime()
            });
            dvLayer.OnLowerLayerMessageReceived(r2, new Message(new DistanceVectorMessage(r1, r2, new Message(update1), true, 5).ToJson()));

            // Now Neighbor 2 sends an updated distance metric (e.g. link degraded to cost 6)
            var update2 = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float> { { (int)r3.ID, 6.0f } },
                Timestamp = scheduler.GetCurrentTime()
            });
            dvLayer.OnLowerLayerMessageReceived(r2, new Message(new DistanceVectorMessage(r1, r2, new Message(update2), true, 5).ToJson()));

            mockLower.SentMessages.Clear();
            dvLayer.SendMessage(r3, new Message("data for 3"));

            // Assert CORRECT behavior: SendMessage to destination 3 should route through next-hop r2
            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreEqual(r2, mockLower.SentMessages[0].Recipient,
                "CORRECT BEHAVIOR: Message for destination 3 must continue to route via next-hop 2, NOT send directly to 3");
        }

        [TestMethod]
        public void DistanceVector_ConnectionInterrupted_MustPurgeMultiHopRoutesThroughNeighbor()
        {
            // EXPECTED BEHAVIOR:
            // When connection to neighbor r2 is interrupted, all routes that route THROUGH r2
            // (e.g. route to destination r3 via r2) must be purged or invalidated from the routing table.
            //
            // CURRENT BUG:
            // In DistanceTable.ResetConnection(replica):
            //   distances.Remove(replica);
            // It only removes neighbor r2 itself. Multi-hop destination r3 remains in the table
            // pointing to disconnected neighbor r2, causing black-hole routing.

            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var r1 = new ReplicaID(1);
            var r2 = new ReplicaID(2);
            var r3 = new ReplicaID(3);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { r2 },
                new HashSet<ReplicaID>());

            var dvLayer = new DistanceVectorRoutingLayer(r1, tracker, mockLower, scheduler, 10);

            // R2 advertises route to R3
            var update = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float> { { (int)r3.ID, 2.0f } },
                Timestamp = scheduler.GetCurrentTime()
            });
            dvLayer.OnLowerLayerMessageReceived(r2, new Message(new DistanceVectorMessage(r1, r2, new Message(update), true, 5).ToJson()));

            // Connection to R2 is interrupted
            mockLower.InjectConnectionInterrupted(r2);

            mockLower.SentMessages.Clear();
            dvLayer.SendMessage(r3, new Message("black hole test"));

            // Assert CORRECT behavior: Should NOT send to the disconnected neighbor r2
            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreNotEqual(r2, mockLower.SentMessages[0].Recipient,
                "CORRECT BEHAVIOR: Route to destination 3 via disconnected neighbor 2 must be purged, NOT sent to disconnected neighbor 2");
        }



        #endregion

        #region LinkStateRoutingLayer Bug Tests

        [TestMethod]
        public void LinkState_Disconnection_MustPurgeStaleRoutesFromRoutingTable()
        {
            // EXPECTED BEHAVIOR:
            // When an LSA update indicates that a destination (rC) is no longer connected / reachable,
            // RecalculateRoutingTable must clear previous routing entries and remove rC from nextNodeGivenTarget.
            //
            // CURRENT BUG:
            // RecalculateRoutingTable never clears nextNodeGivenTarget!
            // When rC becomes unreachable, it is absent from distancesTo.Keys, so
            // nextNodeGivenTarget[rC] is never overwritten and remains pointing to rB forever.

            var mockLower = new ControllableMockLayer();
            var rA = new ReplicaID(1); // Self
            var rB = new ReplicaID(2); // Neighbor
            var rC = new ReplicaID(3); // Multi-hop node

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rB },
                new HashSet<ReplicaID>());

            var lsLayer = new LinkStateRoutingLayer(rA, mockLower, tracker);

            // Feed self LSA
            var lsaA = new LSA(rA, 0, new List<ReplicaID> { rB });
            lsLayer.OnLowerLevelMessageReceived(rA, new Message(new LinkStateMessage(rA, rA, new Message(lsaA.ToJson()), true).ToJson()));

            // LSA #1: B connects to A and C
            var lsaB1 = new LSA(rB, 1, new List<ReplicaID> { rA, rC });
            lsLayer.OnLowerLevelMessageReceived(rB, new Message(new LinkStateMessage(rA, rB, new Message(lsaB1.ToJson()), true).ToJson()));

            // Now C disconnects! B sends LSA #2 stating it is only connected to A
            var lsaB2 = new LSA(rB, 2, new List<ReplicaID> { rA });
            lsLayer.OnLowerLevelMessageReceived(rB, new Message(new LinkStateMessage(rA, rB, new Message(lsaB2.ToJson()), true).ToJson()));

            mockLower.SentMessages.Clear();
            lsLayer.SendMessage(rC, new Message("msg to disconnected C"));

            // Assert CORRECT behavior: rC is disconnected, so it should NOT route via rB anymore
            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreNotEqual(rB, mockLower.SentMessages[0].Recipient,
                "CORRECT BEHAVIOR: Stale route to disconnected node C must be cleared, NOT forwarded via B");
        }

        [TestMethod]
        public void LinkState_SelfLSA_MustBeIncludedInLSAsToEnableMultiHopDijkstra()
        {
            // EXPECTED BEHAVIOR:
            // LinkStateRoutingLayer must add its own LSA to mostRecentLSAs (either upon initialization,
            // CreateNextLSA, or OnConnect) so that Dijkstra starting from replicaID explores its neighbors.
            //
            // CURRENT BUG:
            // LinkStateRoutingLayer never calls AddLSA on its own LSA. In RecalculateRoutingTable:
            //   closest = replicaID;
            //   if (mostRecentLSAs.ContainsKey(closest)) -> evaluates to FALSE!
            // Dijkstra halts at step 1, finding 0 neighbors and 0 paths for multi-hop nodes.

            var mockLower = new ControllableMockLayer();
            var rA = new ReplicaID(1); // Self
            var rB = new ReplicaID(2); // Neighbor
            var rC = new ReplicaID(3); // Multi-hop node

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rB },
                new HashSet<ReplicaID>());

            var lsLayer = new LinkStateRoutingLayer(rA, mockLower, tracker);

            // B advertises connection to A and C
            var lsaB = new LSA(rB, 1, new List<ReplicaID> { rA, rC });
            lsLayer.OnLowerLevelMessageReceived(rB, new Message(new LinkStateMessage(rA, rB, new Message(lsaB.ToJson()), true).ToJson()));

            // Without manually injecting self LSA over the wire, LinkState should be able to route to C via B
            mockLower.SentMessages.Clear();
            lsLayer.SendMessage(rC, new Message("route to C"));

            // Assert CORRECT behavior: should route to C via neighbor B
            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreEqual(rB, mockLower.SentMessages[0].Recipient,
                "CORRECT BEHAVIOR: Dijkstra should discover neighbors from self LSA and route to C via B");
        }

        [TestMethod]
        public void LinkState_SendLSA_MustSetCorrectIntendedTargetAndOriginalSender()
        {
            // EXPECTED BEHAVIOR:
            // When sending an LSA to a peer, IntendedTarget should be the peer, and
            // OriginalSender should be self (replicaID).
            //
            // CURRENT BUG:
            // In LinkStateRoutingLayer.SendLSA:
            //   new LinkStateMessage(replicaID, target, ...)
            // Constructor signature:
            //   LinkStateMessage(ReplicaID intendedTarget, ReplicaID originalSender, ...)
            // The parameters are swapped, so IntendedTarget is self and OriginalSender is the peer!

            var mockLower = new ControllableMockLayer();
            var rSelf = new ReplicaID(1);
            var rPeer = new ReplicaID(2);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rPeer },
                new HashSet<ReplicaID>());

            var lsLayer = new LinkStateRoutingLayer(rSelf, mockLower, tracker);

            lsLayer.OnConnect(rPeer);

            Assert.IsTrue(mockLower.SentMessages.Count > 0);
            var lsaNetworkMsg = mockLower.SentMessages[0].Message;
            var parsed = LinkStateMessage.FromJson(lsaNetworkMsg.Payload);

            Assert.IsNotNull(parsed);
            // Assert CORRECT behavior:
            Assert.AreEqual(rPeer, parsed.IntendedTarget, 
                "CORRECT BEHAVIOR: IntendedTarget of LSA message must be the peer (2)");
            Assert.AreEqual(rSelf, parsed.OriginalSender, 
                "CORRECT BEHAVIOR: OriginalSender of LSA message must be self (1)");
        }

        #endregion

        #region FloodingRedirectionLayer Bug Tests

        [TestMethod]
        public void Flooding_Target_MustDeduplicateAndDeliverMessageOnlyOnce()
        {
            // EXPECTED BEHAVIOR:
            // In a mesh network where a flood message arrives at the destination via multiple paths,
            // the destination must deduplicate and deliver the message payload to the upper layer ONLY ONCE.
            //
            // CURRENT BUG:
            // FloodingRedirectionLayer does not track seen messages or message IDs. Every duplicate
            // flood packet that arrives at the target invokes CallMessageReceived again.

            var mockLower = new ControllableMockLayer();
            var rSelf = new ReplicaID(1);
            var rSender = new ReplicaID(2);
            var rPath1 = new ReplicaID(3);
            var rPath2 = new ReplicaID(4);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rPath1, rPath2 },
                new HashSet<ReplicaID>());

            var floodLayer = new FloodingRedirectionLayer(rSelf, new ControllableTestScheduler(), tracker, mockLower, numHops: 5, forgettingTime: 60.0f);

            int deliveryCount = 0;
            floodLayer.AddOnMessageReceivedHandler((_, _) => deliveryCount++);

            var originalMsg = new Message("payload123");
            var floodMsg = new FloodMessage(rSelf, rSender, 0u, originalMsg, numberOfHops: 3);

            // Message arrives via Path 1
            floodLayer.OnLowerLayerMessageReceived(rPath1, new Message(floodMsg.ToJson()));

            // Duplicate copy arrives via Path 2
            floodLayer.OnLowerLayerMessageReceived(rPath2, new Message(floodMsg.ToJson()));

            // Assert CORRECT behavior: Must only deliver once
            Assert.AreEqual(1, deliveryCount, 
                "CORRECT BEHAVIOR: Target should deduplicate redundant flood packets and deliver only once");
        }

        [TestMethod]
        public void Flooding_OriginNode_MustDropLoopedMessageAndNotRefloodOwnMessage()
        {
            // EXPECTED BEHAVIOR:
            // When a flood packet that was originally sent by this node circles back in a cycle,
            // the node must recognize that floodMessage.OriginalSender == replicaID and drop it,
            // rather than reflooding its own message.
            //
            // CURRENT BUG:
            // Forwarding checks if (connection != senderID && connection != replicaID), but
            // never checks if floodMessage.OriginalSender == replicaID. It decrements hops
            // and refloods its own message back out into the network.

            var mockLower = new ControllableMockLayer();
            var rSelf = new ReplicaID(1);
            var rNeighbourB = new ReplicaID(2);
            var rNeighbourC = new ReplicaID(3);
            var rTarget = new ReplicaID(99);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rNeighbourB, rNeighbourC },
                new HashSet<ReplicaID>());

            var floodLayer = new FloodingRedirectionLayer(rSelf, new ControllableTestScheduler(), tracker, mockLower, numHops: 5, forgettingTime: 60.0f);
            floodLayer.SetRunning(true);

            // Packet originated by rSelf circles back from neighbor B
            var loopedPacket = new FloodMessage(rTarget, rSelf, 0u, new Message("loop"), numberOfHops: 3);
            floodLayer.OnLowerLayerMessageReceived(rNeighbourB, new Message(loopedPacket.ToJson()));

            // Assert CORRECT behavior: Must drop packet originated by self
            Assert.AreEqual(0, mockLower.SentMessages.Count,
                "CORRECT BEHAVIOR: Node must drop looped flood packets that originated from itself and NOT reflood them");
        }

        #endregion
    }
}
