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
    [TestClass]
    public class RoutingLayersBehaviourTests
    {
        #region FloodingRedirectionLayer Tests

        [TestMethod]
        public async Task FloodingLayer_TransitMessage_DecrementsHopsAndForwardsToActiveConnectionsExceptSender()
        {
            var mockLower = new ControllableMockLayer();
            var rSelf = new ReplicaID(1);
            var rSender = new ReplicaID(2);
            var rNeighbour1 = new ReplicaID(3);
            var rNeighbour2 = new ReplicaID(4);
            var rTarget = new ReplicaID(99);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rSender, rNeighbour1, rNeighbour2 },
                new HashSet<ReplicaID>());

            var floodLayer = new FloodingRedirectionLayer(rSelf, new ControllableTestScheduler(), tracker, mockLower, numHops: 5, forgettingTime: 60.0f);
            await floodLayer.Run();

            var transitPayload = new Message("payload");
            var inboundFlood = new FloodMessage(rTarget, rSender, 0u, transitPayload, numberOfHops: 3);
            
            floodLayer.OnLowerLayerMessageReceived(rSender, new Message(inboundFlood.ToJson()));

            // Should forward to rNeighbour1 and rNeighbour2, but NOT to rSender or rSelf
            Assert.AreEqual(2, mockLower.SentMessages.Count);
            var forwardedTargets = mockLower.SentMessages.Select(m => m.Recipient).ToList();
            CollectionAssert.Contains(forwardedTargets, rNeighbour1);
            CollectionAssert.Contains(forwardedTargets, rNeighbour2);
            CollectionAssert.DoesNotContain(forwardedTargets, rSender);
            CollectionAssert.DoesNotContain(forwardedTargets, rSelf);

            // Verify hops decremented from 3 to 2
            var forwardedMsg = FloodMessage.FromJson(mockLower.SentMessages[0].Message.Payload);
            Assert.IsNotNull(forwardedMsg);
            Assert.AreEqual((uint)2, forwardedMsg.NumberOfHops);
            Assert.AreEqual(rTarget, forwardedMsg.IntendedTarget);
            Assert.AreEqual(rSender, forwardedMsg.OriginalSender);
        }

        [TestMethod]
        public async Task FloodingLayer_ZeroHopsRemaining_DoesNotForward()
        {
            var mockLower = new ControllableMockLayer();
            var rSelf = new ReplicaID(1);
            var rSender = new ReplicaID(2);
            var rNeighbour = new ReplicaID(3);
            var rTarget = new ReplicaID(99);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rSender, rNeighbour },
                new HashSet<ReplicaID>());

            var floodLayer = new FloodingRedirectionLayer(rSelf, new ControllableTestScheduler(), tracker, mockLower, numHops: 5, forgettingTime: 60.0f);
            await floodLayer.Run();

            var inboundFlood = new FloodMessage(rTarget, rSender, 0u, new Message("data"), numberOfHops: 0);

            floodLayer.OnLowerLayerMessageReceived(rSender, new Message(inboundFlood.ToJson()));

            Assert.AreEqual(0, mockLower.SentMessages.Count, "Message with 0 hops must not be forwarded further");
        }

        [TestMethod]
        public async Task FloodingLayer_InactiveConnections_AreExcludedFromForwarding()
        {
            var mockLower = new ControllableMockLayer();
            var rSelf = new ReplicaID(1);
            var rSender = new ReplicaID(2);
            var rActive = new ReplicaID(3);
            var rInactive = new ReplicaID(4);
            var rTarget = new ReplicaID(99);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rSender, rActive },
                new HashSet<ReplicaID> { rInactive });

            var floodLayer = new FloodingRedirectionLayer(rSelf, new ControllableTestScheduler(), tracker, mockLower, numHops: 5, forgettingTime: 60.0f);
            await floodLayer.Run();

            var inboundFlood = new FloodMessage(rTarget, rSender, 0u, new Message("data"), numberOfHops: 2);
            floodLayer.OnLowerLayerMessageReceived(rSender, new Message(inboundFlood.ToJson()));

            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreEqual(rActive, mockLower.SentMessages[0].Recipient);
        }

        [TestMethod]
        public void FloodingLayer_NotRunning_DoesNotForwardTransitMessages()
        {
            var mockLower = new ControllableMockLayer();
            var rSelf = new ReplicaID(1);
            var rSender = new ReplicaID(2);
            var rNeighbour = new ReplicaID(3);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rNeighbour },
                new HashSet<ReplicaID>());

            var floodLayer = new FloodingRedirectionLayer(rSelf, new ControllableTestScheduler(), tracker, mockLower, numHops: 5, forgettingTime: 60.0f);
            // Notice: floodLayer.Run() is NOT called, so Running == false

            var inboundFlood = new FloodMessage(new ReplicaID(99), rSender, 0u, new Message("data"), numberOfHops: 2);
            floodLayer.OnLowerLayerMessageReceived(rSender, new Message(inboundFlood.ToJson()));

            Assert.AreEqual(0, mockLower.SentMessages.Count, "When layer is not running, transit messages must not be forwarded");
        }

        [TestMethod]
        public async Task FloodingLayer_SendMessage_ToSelf_SendsDirectlyToSelf()
        {
            var mockLower = new ControllableMockLayer();
            var rSelf = new ReplicaID(1);
            var tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>(), new HashSet<ReplicaID>());

            var floodLayer = new FloodingRedirectionLayer(rSelf, new ControllableTestScheduler(), tracker, mockLower, numHops: 5, forgettingTime: 60.0f);
            await floodLayer.Run();

            var result = await floodLayer.SendMessage(rSelf, new Message("self msg"));

            Assert.IsTrue(result);
            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreEqual(rSelf, mockLower.SentMessages[0].Recipient);

            var parsed = FloodMessage.FromJson(mockLower.SentMessages[0].Message.Payload);
            Assert.IsNotNull(parsed);
            Assert.AreEqual(rSelf, parsed.IntendedTarget);
            Assert.AreEqual(rSelf, parsed.OriginalSender);
        }

        #endregion

        #region DistanceVectorRoutingLayer Tests

        [TestMethod]
        public void DistanceVector_ProtocolMessageUpdate_UpdatesRoutingPathToDestination()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var rSelf = new ReplicaID(1);
            var rNeighborB = new ReplicaID(2);
            var rDestinationC = new ReplicaID(3);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rNeighborB },
                new HashSet<ReplicaID>());

            var dvLayer = new DistanceVectorRoutingLayer(rSelf, tracker, mockLower, scheduler, 10);

            // Neighbor B sends an update claiming distance 2.0 to destination C
            var updatePayload = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float> { { (int)rDestinationC.ID, 2.0f } },
                Timestamp = scheduler.GetCurrentTime()
            });

            var dvm = new DistanceVectorMessage(
                rSelf,
                rNeighborB,
                new Message(updatePayload),
                isProtocolMessage: true, 10);

            dvLayer.OnLowerLayerMessageReceived(rNeighborB, new Message(dvm.ToJson()));

            // Verify active status updated
            Assert.IsTrue(tracker.IsActive(rNeighborB));

            // Now send a data message to C. Since B is the advertised next hop, SendMessage to C must be forwarded to B!
            dvLayer.SendMessage(rDestinationC, new Message("hello C"));

            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreEqual(rNeighborB, mockLower.SentMessages[0].Recipient, "Should route message for C via next-hop B");
            
            var sentDv = DistanceVectorMessage.FromJson(mockLower.SentMessages[0].Message.Payload);
            Assert.IsNotNull(sentDv);
            Assert.AreEqual(rDestinationC, sentDv.IntendedTarget);
            Assert.AreEqual(rSelf, sentDv.OriginalSender);
        }

        [TestMethod]
        public void DistanceVector_TransitDataMessage_ForwardsToNextHop()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var rSelf = new ReplicaID(1);
            var rNeighborB = new ReplicaID(2);
            var rDestinationC = new ReplicaID(3);
            var rOriginA = new ReplicaID(4);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rNeighborB, rOriginA },
                new HashSet<ReplicaID>());

            var dvLayer = new DistanceVectorRoutingLayer(rSelf, tracker, mockLower, scheduler, 10);

            // B advertises path to C
            var updatePayload = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float> { { (int)rDestinationC.ID, 1.0f } },
                Timestamp = scheduler.GetCurrentTime()
            });
            var protoMsg = new DistanceVectorMessage(rSelf, rNeighborB, new Message(updatePayload), true, 5);
            dvLayer.OnLowerLayerMessageReceived(rNeighborB, new Message(protoMsg.ToJson()));

            // Now a data message from Origin A addressed to Destination C arrives at Self
            var dataMsg = new DistanceVectorMessage(rDestinationC, rOriginA, new Message("transit payload"), false, 10);
            var networkMsg = new Message(dataMsg.ToJson());

            dvLayer.OnLowerLayerMessageReceived(rOriginA, networkMsg);

            // Must forward to B
            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreEqual(rNeighborB, mockLower.SentMessages[0].Recipient);
        }

        [TestMethod]
        public void DistanceVector_MessageForSelf_DeliversLocallyAndDoesNotForward()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var rSelf = new ReplicaID(1);
            var rSender = new ReplicaID(2);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rSender },
                new HashSet<ReplicaID>());

            var dvLayer = new DistanceVectorRoutingLayer(rSelf, tracker, mockLower, scheduler, 10);

            Message? delivered = null;
            ReplicaID? sender = null;
            dvLayer.AddOnMessageReceivedHandler((s, m) =>
            {
                sender = s;
                delivered = m;
            });

            var dataMsg = new DistanceVectorMessage(rSelf, rSender, new Message("for you"), false, 10);
            dvLayer.OnLowerLayerMessageReceived(rSender, new Message(dataMsg.ToJson()));

            Assert.IsNotNull(delivered);
            Assert.AreEqual("for you", delivered.Payload);
            Assert.AreEqual(rSender, sender);
            Assert.AreEqual(0, mockLower.SentMessages.Count, "Message for self must not be forwarded");
        }

        [TestMethod]
        public void DistanceVector_ConnectionInterrupted_ResetsRouteThroughNeighbor()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var rSelf = new ReplicaID(1);
            var rNeighborB = new ReplicaID(2);
            var rDestinationC = new ReplicaID(3);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rNeighborB },
                new HashSet<ReplicaID>());

            var dvLayer = new DistanceVectorRoutingLayer(rSelf, tracker, mockLower, scheduler, 10);

            // B advertises route to C
            var updatePayload = JsonConvert.SerializeObject(new
            {
                Distances = new Dictionary<int, float> { { (int)rDestinationC.ID, 1.0f } },
                Timestamp = scheduler.GetCurrentTime()
            });
            var protoMsg = new DistanceVectorMessage(rSelf, rNeighborB, new Message(updatePayload), true, 10);
            dvLayer.OnLowerLayerMessageReceived(rNeighborB, new Message(protoMsg.ToJson()));

            // Neighbor B connection breaks
            mockLower.InjectConnectionInterrupted(rNeighborB);

            // Now sending to C should fall back to direct C (since B route was reset)
            dvLayer.SendMessage(rDestinationC, new Message("test after reset"));

            Assert.AreEqual(1, mockLower.SentMessages.Count);
            Assert.AreEqual(rDestinationC, mockLower.SentMessages[0].Recipient, "Reset connection should clear route via B");
        }

        #endregion

        #region LinkStateRoutingLayer Tests

        [TestMethod]
        public void LinkState_StaleLSAReceived_IgnoredAndNotFlooded()
        {
            var mockLower = new ControllableMockLayer();
            var rSelf = new ReplicaID(1);
            var rNeighborB = new ReplicaID(2);
            var rNodeC = new ReplicaID(3);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rNeighborB },
                new HashSet<ReplicaID>());

            var lsLayer = new LinkStateRoutingLayer(rSelf, mockLower, tracker);

            // Inject LSA #5 from Node C
            var lsa5 = new LSA(rNodeC, 5, new List<ReplicaID> { rNeighborB });
            var msg5 = new LinkStateMessage(rSelf, rNeighborB, new Message(lsa5.ToJson()), true);
            lsLayer.OnLowerLevelMessageReceived(rNeighborB, new Message(msg5.ToJson()));

            // Now inject stale LSA #3 and #5 from Node C
            mockLower.SentMessages.Clear();
            var lsa3 = new LSA(rNodeC, 3, new List<ReplicaID> { rNeighborB });
            var msg3 = new LinkStateMessage(rSelf, rNeighborB, new Message(lsa3.ToJson()), true);
            lsLayer.OnLowerLevelMessageReceived(rNeighborB, new Message(msg3.ToJson()));

            Assert.AreEqual(0, mockLower.SentMessages.Count, "Stale LSA must not be flooded");

            var lsa5dup = new LSA(rNodeC, 5, new List<ReplicaID> { rNeighborB });
            var msg5dup = new LinkStateMessage(rSelf, rNeighborB, new Message(lsa5dup.ToJson()), true);
            lsLayer.OnLowerLevelMessageReceived(rNeighborB, new Message(msg5dup.ToJson()));

            Assert.AreEqual(0, mockLower.SentMessages.Count, "Duplicate LSA number must not be flooded");
        }

        [TestMethod]
        public void LinkState_MultiHopTopology_ComputesShortestPath()
        {
            var mockLower = new ControllableMockLayer();
            var rA = new ReplicaID(1); // Self
            var rB = new ReplicaID(2);
            var rC = new ReplicaID(3);
            var rD = new ReplicaID(4);

            // A is directly connected to B and C
            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rB, rC },
                new HashSet<ReplicaID>());

            var lsLayer = new LinkStateRoutingLayer(rA, mockLower, tracker);

            // LSAs describe topology:
            // A connected to B, C
            // B connected to A, D
            // C connected to A, D
            // D connected to B, C
            var lsaB = new LSA(rB, 1, new List<ReplicaID> { rA, rD });
            var lsaC = new LSA(rC, 1, new List<ReplicaID> { rA, rD });
            var lsaD = new LSA(rD, 1, new List<ReplicaID> { rB, rC });

            lsLayer.OnLowerLevelMessageReceived(rB, new Message(new LinkStateMessage(rA, rB, new Message(lsaB.ToJson()), true).ToJson()));
            lsLayer.OnLowerLevelMessageReceived(rC, new Message(new LinkStateMessage(rA, rC, new Message(lsaC.ToJson()), true).ToJson()));
            lsLayer.OnLowerLevelMessageReceived(rB, new Message(new LinkStateMessage(rA, rD, new Message(lsaD.ToJson()), true).ToJson()));

            mockLower.SentMessages.Clear();

            // Send to D
            lsLayer.SendMessage(rD, new Message("hello D"));

            Assert.AreEqual(1, mockLower.SentMessages.Count);
            var routedTarget = mockLower.SentMessages[0].Recipient;
            // Shortest path to D is either via B or via C (distance = 2)
            Assert.IsTrue(routedTarget == rB || routedTarget == rC, "Must route towards D via B or C");
        }

        [TestMethod]
        public void LinkState_TransitMessage_ForwardedAlongShortestPath()
        {
            var mockLower = new ControllableMockLayer();
            var rA = new ReplicaID(1); // Self
            var rB = new ReplicaID(2);
            var rC = new ReplicaID(3);
            var rOrigin = new ReplicaID(99);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rB },
                new HashSet<ReplicaID>());

            var lsLayer = new LinkStateRoutingLayer(rA, mockLower, tracker);

            // B connects to C
            var lsaB = new LSA(rB, 1, new List<ReplicaID> { rA, rC });
            lsLayer.OnLowerLevelMessageReceived(rB, new Message(new LinkStateMessage(rA, rB, new Message(lsaB.ToJson()), true).ToJson()));

            mockLower.SentMessages.Clear();

            // A receives transit data message intended for C
            var transitData = new LinkStateMessage(rC, rOrigin, new Message("transit body"), false);
            lsLayer.OnLowerLevelMessageReceived(rOrigin, new Message(transitData.ToJson()));

            Console.WriteLine(rB.ToString()+" "+ mockLower.SentMessages[0].Recipient.ToString());
            Assert.AreEqual(rB, mockLower.SentMessages[0].Recipient, "Transit message for C should be forwarded to next-hop B");
        }

        [TestMethod]
        public void LinkState_OnConnectAndDisconnect_FloodsNewLSAAndTriggersEvents()
        {
            var mockLower = new ControllableMockLayer();
            var rSelf = new ReplicaID(1);
            var rPeer = new ReplicaID(2);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rPeer },
                new HashSet<ReplicaID>());

            var lsLayer = new LinkStateRoutingLayer(rSelf, mockLower, tracker);

            bool regainedFired = false;
            bool interruptedFired = false;
            lsLayer.AddOnConnectionRegainedHandler(r => { if (r == rPeer) regainedFired = true; });
            lsLayer.AddOnConnectionInterruptedHandler(r => { if (r == rPeer) interruptedFired = true; });

            // OnConnect
            mockLower.InjectConnectionRegained(rPeer);
            Assert.IsTrue(regainedFired);
            Assert.IsTrue(mockLower.SentMessages.Count > 0, "OnConnect must flood new LSA");

            mockLower.SentMessages.Clear();

            // OnDisconnect
            mockLower.InjectConnectionInterrupted(rPeer);
            Assert.IsTrue(interruptedFired);
            Assert.IsTrue(mockLower.SentMessages.Count > 0, "OnDisconnect must flood new LSA to active connections");
        }

        #endregion
    }
}
