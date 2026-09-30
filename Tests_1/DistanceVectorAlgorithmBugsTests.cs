using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.NetworkLayers.Composite.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Shared;

namespace Tests_1
{
    /// <summary>
    /// Test suite asserting the CORRECT, expected behavior of the Distance Vector routing algorithm.
    /// Each test is designed to catch an undetected bug or algorithmic violation in DistanceVectorRoutingLayer.
    /// These tests will FAIL against the current buggy implementation, and will
    /// ONLY PASS once the underlying distance vector algorithm bugs have been corrected.
    /// </summary>
    [TestClass]
    public class DistanceVectorAlgorithmBugsTests
    {
        [TestMethod]
        public void RouteWithdrawal_WhenNeighborStopsAdvertisingDestination_MustPurgeStaleRoute()
        {
            // EXPECTED BEHAVIOR:
            // In Distance Vector routing, when a next-hop neighbor stops advertising reachability
            // to a destination in its periodic distance table update (e.g. downstream link broke),
            // the route through that neighbor must be invalidated/purged.
            //
            // CURRENT BUG:
            // DistanceTable.Update only iterates over the keys present in update.Distances.
            // It never detects destinations that were previously routed through sender but are now omitted.
            // As a result, when a neighbor withdraws a destination, the node retains the stale route
            // to that destination via the neighbor indefinitely (black hole routing).

            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var r1 = new ReplicaID(1); // Self
            var r2 = new ReplicaID(2); // Neighbor
            var r3 = new ReplicaID(3); // Downstream destination

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { r2 },
                new HashSet<ReplicaID>());

            var dvLayer = new DistanceVectorRoutingLayer(r1, tracker, mockLower, scheduler,10);

            // Initial update: Neighbor 2 advertises destination 3
            var update1 = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float>
                {
                    { (int)r2.ID, 0.0f },
                    { (int)r3.ID, 1.0f }
                },
                Timestamp = scheduler.GetCurrentTime()
            });
            dvLayer.OnLowerLayerMessageReceived(r2, new Message(new DistanceVectorMessage(r1, r2, new Message(update1), true,5).ToJson()));

            // Verify route was learned via r2
            mockLower.SentMessages.Clear();
            dvLayer.SendMessage(r3, new Message("hello"));
            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreEqual(r2, mockLower.SentMessages[0].Recipient);

            // Subsequent periodic update: Neighbor 2 has lost connection to 3 and OMITS destination 3!
            scheduler.AdvanceSeconds(1.0f);
            var update2 = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float>
                {
                    { (int)r2.ID, 0.0f }
                },
                Timestamp = scheduler.GetCurrentTime()
            });
            dvLayer.OnLowerLayerMessageReceived(r2, new Message(new DistanceVectorMessage(r1, r2, new Message(update2), true,5).ToJson()));

            // Now send to 3 again. Because r2 no longer advertises 3, the stale route via r2 MUST NOT be used!
            mockLower.SentMessages.Clear();
            dvLayer.SendMessage(r3, new Message("after withdrawal"));

            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreNotEqual(r2, mockLower.SentMessages[0].Recipient,
                "BUG: Node retained stale route to destination 3 via neighbor 2 after neighbor 2 stopped advertising destination 3!");
        }

        [TestMethod]
        public async Task SplitHorizon_MustNotAdvertiseRouteBackToNextHop()
        {
            // EXPECTED BEHAVIOR:
            // Distance Vector routing requires Split Horizon (or Split Horizon with Poison Reverse)
            // to prevent two-node routing loops and count-to-infinity:
            // A router MUST NOT advertise a finite route to destination D back to neighbor N
            // if neighbor N is the next hop for destination D.
            //
            // CURRENT BUG:
            // DistanceTable.GetUpdate generates the exact same table update for all neighbors,
            // unconditionally advertising all destinations to everyone.
            // If r1 routes to r3 via r2, r1 advertises reachability to r3 back to r2.
            // If r2's link to r3 breaks, r2 adopts r1's advertised path, creating an immediate loop.

            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var r1 = new ReplicaID(1); // Self
            var r2 = new ReplicaID(2); // Neighbor who is next-hop to r3
            var r3 = new ReplicaID(3); // Multi-hop destination

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { r2 },
                new HashSet<ReplicaID>());

            var dvLayer = new DistanceVectorRoutingLayer(r1, tracker, mockLower, scheduler, 10);

            // Neighbor 2 advertises route to destination 3
            var update = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float>
                {
                    { (int)r2.ID, 0.0f },
                    { (int)r3.ID, 1.0f }
                },
                Timestamp = scheduler.GetCurrentTime()
            });
            dvLayer.OnLowerLayerMessageReceived(r2, new Message(new DistanceVectorMessage(r1, r2, new Message(update), true,5).ToJson()));

            // Run layer and advance scheduler to trigger heartbeat to neighbor 2
            mockLower.SentMessages.Clear();
            await dvLayer.Run();
            scheduler.AdvanceSeconds(1.1f);

            // Find the protocol heartbeat sent to neighbor 2
            var sentToR2 = mockLower.SentMessages
                .Where(m => m.Recipient == r2)
                .Select(m => DistanceVectorMessage.FromJson(m.Message.Payload))
                .FirstOrDefault(m => m != null && m.IsProtocolMessage);

            Assert.IsNotNull(sentToR2, "Heartbeat protocol message should have been sent to neighbor 2");

            var updateObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(sentToR2.Message?.Payload ?? "");
            Assert.IsNotNull(updateObj);
            var advertisedDistances = JsonConvert.DeserializeObject<Dictionary<int, float>>(updateObj["Distances"].ToString() ?? "");
            Assert.IsNotNull(advertisedDistances);

            // Assert Split Horizon: Destination 3 was learned from r2, so it MUST NOT be advertised back to r2
            // with a valid finite cost! (Under Split Horizon it must be omitted; under Poison Reverse it must be marked unreachable/infinity).
            bool advertisesR3AsReachable = advertisedDistances.TryGetValue((int)r3.ID, out float cost) && cost < 9999.0f && !float.IsPositiveInfinity(cost);
            Assert.IsFalse(advertisesR3AsReachable,
                $"BUG: Split Horizon violated! Node 1 advertised reachable route to destination 3 (cost={cost}) back to neighbor 2, from which it learned the route.");
        }

        [TestMethod]
        public void ProtocolMessage_Update_MustBindNextHopToImmediateNeighborNotOriginalSender()
        {
            // EXPECTED BEHAVIOR:
            // In Distance Vector routing, the next-hop for any learned route MUST be the immediate
            // physical/link neighbor (senderID) that delivered the packet on the lower layer,
            // NEVER a distant originalSender.
            //
            // CURRENT BUG:
            // In OnLowerLayerMessageReceived:
            //   table.Update(replicaID, message.OriginalSender, distanceTableUpdate, scheduler);
            // It passes message.OriginalSender instead of senderID!
            // If a protocol message arrives from direct neighbor r2, but message.OriginalSender
            // is set to an arbitrary node rOrigin (e.g., bridged, tunneled, or spoofed),
            // next-hop is erroneously set to rOrigin. Subsequent sends attempt to transmit directly
            // to rOrigin on LayerBelow instead of routing through direct neighbor r2.

            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var r1 = new ReplicaID(1);      // Self
            var r2 = new ReplicaID(2);      // Direct physical neighbor (senderID)
            var rOrigin = new ReplicaID(99); // Distant origin node
            var rTarget = new ReplicaID(5);  // Advertised destination

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { r2 },
                new HashSet<ReplicaID>());

            var dvLayer = new DistanceVectorRoutingLayer(r1, tracker, mockLower, scheduler, 10);

            // Protocol message received on lower layer from direct neighbor r2,
            // but with OriginalSender = rOrigin (e.g. relayed/bridged).
            var updatePayload = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float> { { (int)rTarget.ID, 1.0f } },
                Timestamp = scheduler.GetCurrentTime()
            });

            var protoMsg = new DistanceVectorMessage(r1, rOrigin, new Message(updatePayload), true,5);
            dvLayer.OnLowerLayerMessageReceived(r2, new Message(protoMsg.ToJson()));

            mockLower.SentMessages.Clear();
            dvLayer.SendMessage(rTarget, new Message("data packet"));

            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreEqual(r2, mockLower.SentMessages[0].Recipient,
                $"BUG: Next-hop was set to OriginalSender ({mockLower.SentMessages[0].Recipient}) instead of immediate neighbor ({r2})!");
        }


        [TestMethod]
        public async Task ConnectionRegained_MustStartHeartbeatsToNewlyConnectedNeighbor()
        {
            // EXPECTED BEHAVIOR:
            // In Distance Vector routing, when a neighbor is discovered or connection is regained
            // after the layer has started, the router must initiate periodic distance vector
            // updates (heartbeats) to that neighbor.
            //
            // CURRENT BUG:
            // DistanceVectorRoutingLayer only initiates Hearbeat(replica) inside Run() for
            // connections already present in connectionTracker.AllConnections at startup.
            // It never listens to ConnectionRegained events (no AddOnConnectionRegainedHandler).
            // A node that connects or reconnects after Run() will never receive heartbeats.

            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var r1 = new ReplicaID(1); // Self
            var rNewNeighbor = new ReplicaID(9); // Joins after Run()

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID>(),
                new HashSet<ReplicaID>());

            var dvLayer = new DistanceVectorRoutingLayer(r1, tracker, mockLower, scheduler, 10);
            await dvLayer.Run();

            // Neighbor 9 connects after startup
            mockLower.InjectConnectionRegained(rNewNeighbor);
            mockLower.SentMessages.Clear();

            // Advance time to allow heartbeat interval to trigger
            scheduler.AdvanceSeconds(1.5f);

            bool sentHeartbeatToNewNeighbor = mockLower.SentMessages.Any(m =>
                m.Recipient == rNewNeighbor &&
                DistanceVectorMessage.FromJson(m.Message.Payload)?.IsProtocolMessage == true);

            Assert.IsTrue(sentHeartbeatToNewNeighbor,
                "BUG: DistanceVectorRoutingLayer never initiated heartbeats to neighbor 9 connected after startup!");
        }

        [TestMethod]
        public void DegradedRoute_WhenAlternativePathIsShorter_MustSwitchToBetterNeighbor()
        {
            // EXPECTED BEHAVIOR:
            // In Distance Vector (Bellman-Ford) routing, a node calculates the shortest path
            // over all neighbors: D(target) = min_v { c(us, v) + D_v(target) }.
            // When the currently selected neighbor reports a degraded/higher distance, the node
            // must switch to another known neighbor if that neighbor offers a shorter path.
            //
            // CURRENT BUG:
            // DistanceTable does not store the distance vectors advertised by each neighbor.
            // It only stores a single (currentDistance, nextHop) tuple.
            // When current nextHop r2 reports an increased distance (e.g. from 2 to 20),
            // DistanceTable.Update simply overwrites the distance with 20, completely forgetting
            // that neighbor r3 previously offered a path of distance 5!
            // Packets continue to route through degraded neighbor r2 instead of switching to r3.

            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var r1 = new ReplicaID(1); // Self
            var r2 = new ReplicaID(2); // Neighbor 2
            var r3 = new ReplicaID(3); // Neighbor 3
            var rDest = new ReplicaID(4); // Destination

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { r2, r3 },
                new HashSet<ReplicaID>());

            var dvLayer = new DistanceVectorRoutingLayer(r1, tracker, mockLower, scheduler, 10);

            // Neighbor 3 advertises path to rDest with cost 5.0
            var updateR3 = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float>
                {
                    { (int)r3.ID, 0.0f },
                    { (int)rDest.ID, 5.0f }
                },
                Timestamp = scheduler.GetCurrentTime()
            });
            dvLayer.OnLowerLayerMessageReceived(r3, new Message(new DistanceVectorMessage(r1, r3, new Message(updateR3), true, 5).ToJson()));

            // Neighbor 2 advertises shorter path to rDest with cost 2.0
            var updateR2_fast = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float>
                {
                    { (int)r2.ID, 0.0f },
                    { (int)rDest.ID, 2.0f }
                },
                Timestamp = scheduler.GetCurrentTime()
            });
            dvLayer.OnLowerLayerMessageReceived(r2, new Message(new DistanceVectorMessage(r1, r2, new Message(updateR2_fast), true, 5).ToJson()));

            // Verify route is currently through r2 (cost 2 < 5)
            mockLower.SentMessages.Clear();
            dvLayer.SendMessage(rDest, new Message("probe"));
            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreEqual(r2, mockLower.SentMessages[0].Recipient);

            // Now Neighbor 2's link to rDest degrades significantly: cost jumps from 2.0 to 30.0!
            var updateR2_degraded = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float>
                {
                    { (int)r2.ID, 0.0f },
                    { (int)rDest.ID, 30.0f }
                },
                Timestamp = scheduler.GetCurrentTime()
            });
            dvLayer.OnLowerLayerMessageReceived(r2, new Message(new DistanceVectorMessage(r1, r2, new Message(updateR2_degraded), true, 5).ToJson()));

            // Now send packet to rDest: it should switch to neighbor r3 (cost 5), NOT stay on degraded r2 (cost 30)!
            mockLower.SentMessages.Clear();
            dvLayer.SendMessage(rDest, new Message("data after degradation"));

            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreEqual(r3, mockLower.SentMessages[0].Recipient,
                $"BUG: After neighbor 2 degraded (cost 30), node failed to switch to neighbor 3 (cost 5) and stayed on neighbor {mockLower.SentMessages[0].Recipient}!");
        }

        [TestMethod]
        public async Task ClockSkew_MustNotAcceptNegativeLinkDistanceMetric()
        {
            // EXPECTED BEHAVIOR:
            // Link distance metrics in Distance Vector routing must be non-negative.
            // In a distributed mesh network, clock skew or timestamp differences between nodes
            // must never cause link distances to become negative, which corrupts shortest-path trees.
            //
            // CURRENT BUG:
            // In DistanceTable.Update:
            //   var timeDifference = (float)(timeNow - update.Timestamp).TotalSeconds;
            //   var newDistance = oldDistance + timeDifference;
            // If sender's clock is slightly ahead of receiver's clock, timeDifference is negative.
            // newDistance becomes negative, and DistanceTable stores a negative distance,
            // which is subsequently advertised in heartbeats as a negative metric.

            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var r1 = new ReplicaID(1); // Self
            var r2 = new ReplicaID(2); // Neighbor
            var r3 = new ReplicaID(3); // Neighbor

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { r2, r3 },
                new HashSet<ReplicaID>());

            var dvLayer = new DistanceVectorRoutingLayer(r1, tracker, mockLower, scheduler, 10);

            // Neighbor 2 sends an update with timestamp 1.5 seconds in the future (clock skew)
            var updatePayload = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float> { { (int)r2.ID, 0.0f } },
                Timestamp = scheduler.GetCurrentTime().AddSeconds(1.5)
            });

            var protoMsg = new DistanceVectorMessage(r1, r2, new Message(updatePayload), true, 5);
            dvLayer.OnLowerLayerMessageReceived(r2, new Message(protoMsg.ToJson()));

            // Trigger heartbeat to inspect advertised distance
            mockLower.SentMessages.Clear();
            await dvLayer.Run();
            scheduler.AdvanceSeconds(1.1f);

            var sentProto = mockLower.SentMessages
                .Select(m => DistanceVectorMessage.FromJson(m.Message.Payload))
                .FirstOrDefault(m => m != null && m.IsProtocolMessage && m.IntendedTarget==r3);

            Assert.IsNotNull(sentProto);
            var updateObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(sentProto.Message?.Payload ?? "");
            Assert.IsNotNull(updateObj);
            var advertisedDistances = JsonConvert.DeserializeObject<Dictionary<int, float>>(updateObj["Distances"].ToString() ?? "");
            Assert.IsNotNull(advertisedDistances);

            Assert.IsTrue(advertisedDistances.ContainsKey((int)r2.ID));
            float distanceToR2 = advertisedDistances[(int)r2.ID];

            Assert.IsTrue(distanceToR2 >= 0.0f,
                $"BUG: Stored and advertised negative distance metric ({distanceToR2}) due to clock skew / future timestamp!");
        }
    }
}
