using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.NetworkLayers.Composite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shared;

namespace Tests_1
{
    [TestClass]
    public class CausalAndTrackingBehaviourTests
    {
        #region CausalHistoryTrackerLayer Tests
        /*
        [TestMethod]
        public void CausalHistory_ReverseArrival_BuffersAndCascadesDelivery()
        {
            var mockLower = new ControllableMockLayer();
            var r1 = new ReplicaID(1);
            var r2 = new ReplicaID(2);

            var causalLayer = new CausalHistoryTrackerLayer(r1, mockLower);

            var deliveredPayloads = new List<string>();
            causalLayer.AddOnMessageReceivedHandler((_, msg) => deliveredPayloads.Add(msg.Payload));

            // Create 3 causal messages from r2
            var vv1 = new VersionVector(); vv1.Increment(r2);
            var vv2 = vv1.Clone(); vv2.Increment(r2);
            var vv3 = vv2.Clone(); vv3.Increment(r2);

            var msg1 = new CausalHistoryTrackerLayer.TrackerMessage(new Message("m1"), vv1);
            var msg2 = new CausalHistoryTrackerLayer.TrackerMessage(new Message("m2"), vv2);
            var msg3 = new CausalHistoryTrackerLayer.TrackerMessage(new Message("m3"), vv3);

            // Arrive in reverse order: 3, 2, 1
            mockLower.InjectReceivedMessage(r2, new Message(msg3.ToJson()));
            Assert.AreEqual(0, deliveredPayloads.Count, "m3 should be buffered");

            mockLower.InjectReceivedMessage(r2, new Message(msg2.ToJson()));
            Assert.AreEqual(0, deliveredPayloads.Count, "m2 should also be buffered");

            mockLower.InjectReceivedMessage(r2, new Message(msg1.ToJson()));
            Assert.AreEqual(3, deliveredPayloads.Count, "All 3 messages should cascade deliver once m1 arrives");

            Assert.AreEqual("m1", deliveredPayloads[0]);
            Assert.AreEqual("m2", deliveredPayloads[1]);
            Assert.AreEqual("m3", deliveredPayloads[2]);
        }

        [TestMethod]
        public void CausalHistory_CrossReplicaCausalDependency_DeliversInCausalOrder()
        {
            var mockLower = new ControllableMockLayer();
            var r1 = new ReplicaID(1); // Receiver
            var r2 = new ReplicaID(2);
            var r3 = new ReplicaID(3);

            var causalLayer = new CausalHistoryTrackerLayer(r1, mockLower);

            var deliveredOrder = new List<(ReplicaID, string)>();
            causalLayer.AddOnMessageReceivedHandler((src, msg) => deliveredOrder.Add((src, msg.Payload)));

            // r2 sends msgA
            var vvA = new VersionVector();
            vvA.Increment(r2);
            var msgA = new CausalHistoryTrackerLayer.TrackerMessage(new Message("msgA_from_r2"), vvA);

            // r3 receives msgA, then sends msgB depending on msgA: vvB = {r2: 1, r3: 1}
            var vvB = vvA.Clone();
            vvB.Increment(r3);
            var msgB = new CausalHistoryTrackerLayer.TrackerMessage(new Message("msgB_from_r3"), vvB);

            // r1 receives msgB first (before msgA)
            mockLower.InjectReceivedMessage(r3, new Message(msgB.ToJson()));
            Assert.AreEqual(0, deliveredOrder.Count, "msgB depends on msgA, must not be delivered yet");

            // r1 receives msgA
            mockLower.InjectReceivedMessage(r2, new Message(msgA.ToJson()));
            Assert.AreEqual(2, deliveredOrder.Count, "Both messages must now be delivered");

            Assert.AreEqual(r2, deliveredOrder[0].Item1);
            Assert.AreEqual("msgA_from_r2", deliveredOrder[0].Item2);

            Assert.AreEqual(r3, deliveredOrder[1].Item1);
            Assert.AreEqual("msgB_from_r3", deliveredOrder[1].Item2);
        }

        /*    [TestMethod]
            public void CausalHistory_MessageIssuedEvent_FiresOnDeliveryAndAllowsUnsubscribe()
                {
                    var mockLower = new ControllableMockLayer();
                    var r1 = new ReplicaID(1);
                    var r2 = new ReplicaID(2);

                    var causalLayer = new CausalHistoryTrackerLayer(r1, mockLower);

                    int issuedCount = 0;
                    VersionVector? recordedVector = null;

                    Action<ReplicaID, Message?, VersionVector> handler = (sender, msg, vv) =>
                    {
                        issuedCount++;
                        recordedVector = vv.Clone();
                    };

                    causalLayer.AddMessageIssuedHandler(handler);

                    var vv = new VersionVector();
                    vv.Increment(r2);
                    var trackerMsg = new CausalHistoryTrackerLayer.TrackerMessage(new Message("payload"), vv);

                    mockLower.InjectReceivedMessage(r2, new Message(trackerMsg.ToJson()));

                    Assert.AreEqual(1, issuedCount);
                    Assert.IsNotNull(recordedVector);
                    var expectedVv = new VersionVector();
                    expectedVv.Increment(r2);
                    Assert.IsTrue(expectedVv == recordedVector.Value);

                    // Unsubscribe
                    causalLayer.RemoveMessageIssuedHandler(handler);

                    var vv2 = vv.Clone();
                    vv2.Increment(r2);
                    var trackerMsg2 = new CausalHistoryTrackerLayer.TrackerMessage(new Message("payload2"), vv2);
                    mockLower.InjectReceivedMessage(r2, new Message(trackerMsg2.ToJson()));

                    Assert.AreEqual(1, issuedCount, "Unsubscribed handler must not fire again");
                }
        */
        /*        [TestMethod]
                public void CausalHistory_InitialVersionVector_HonorsPreExistingState()
                {
                    var mockLower = new ControllableMockLayer();
                    var r1 = new ReplicaID(1);
                    var r2 = new ReplicaID(2);

                    // Pre-seed latest with {r2: 5}
                    var initialVv = new VersionVector();
                    for (int i = 0; i < 5; i++) initialVv.Increment(r2);

                    var causalLayer = new CausalHistoryTrackerLayer(r1, mockLower, initialVv);

                    var delivered = new List<string>();
                    causalLayer.AddOnMessageReceivedHandler((_, m) => delivered.Add(m.Payload));

                    // Message with {r2: 6} should be delivered immediately because 6 depends only on <= 5
                    var vv6 = initialVv.Clone();
                    vv6.Increment(r2);
                    mockLower.InjectReceivedMessage(r2, new Message(new CausalHistoryTrackerLayer.TrackerMessage(new Message("msg6"), vv6).ToJson()));

                    Assert.AreEqual(1, delivered.Count);
                    Assert.AreEqual("msg6", delivered[0]);
                }
        */
        #endregion

        #region TrackActiveConnectionsLayer Tests

        [TestMethod]
        public void TrackActive_IncomingMessage_ActivatesSenderInTracker()
        {
            var mockLower = new ControllableMockLayer();
            var peer = new ReplicaID(2);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID>(),
                new HashSet<ReplicaID> { peer });

            var trackLayer = new TrackActiveConnectionsLayer(mockLower, tracker, trackSends: false);

            Assert.IsFalse(tracker.IsActive(peer));

            mockLower.InjectReceivedMessage(peer, new Message("ping"));

            Assert.IsTrue(tracker.IsActive(peer));
        }

        [TestMethod]
        public void TrackActive_LowerLayerInterrupted_SetsInactiveAndFiresOnlyOnStateChange()
        {
            var mockLower = new ControllableMockLayer();
            var peer = new ReplicaID(2);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { peer },
                new HashSet<ReplicaID>());

            var trackLayer = new TrackActiveConnectionsLayer(mockLower, tracker, trackSends: false);

            int interruptedCount = 0;
            trackLayer.AddOnConnectionInterruptedHandler(_ => interruptedCount++);

            // First interruption changes state from Active -> Inactive
            mockLower.InjectConnectionInterrupted(peer);
            Assert.IsFalse(tracker.IsActive(peer));
            Assert.AreEqual(1, interruptedCount);

            // Second interruption when already inactive must not re-trigger event
            mockLower.InjectConnectionInterrupted(peer);
            Assert.AreEqual(1, interruptedCount);
        }

        [TestMethod]
        public void TrackActive_LowerLayerRegained_SetsActiveAndFiresOnlyOnStateChange()
        {
            var mockLower = new ControllableMockLayer();
            var peer = new ReplicaID(2);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID>(),
                new HashSet<ReplicaID> { peer });

            var trackLayer = new TrackActiveConnectionsLayer(mockLower, tracker, trackSends: false);

            int regainedCount = 0;
            trackLayer.AddOnConnectionRegainedHandler(_ => regainedCount++);

            // First regained changes state from Inactive -> Active
            mockLower.InjectConnectionRegained(peer);
            Assert.IsTrue(tracker.IsActive(peer));
            Assert.AreEqual(1, regainedCount);

            // Second regained when already active must not re-trigger event
            mockLower.InjectConnectionRegained(peer);
            Assert.AreEqual(1, regainedCount);
        }

        [TestMethod]
        public async Task TrackActive_TrackSendsTrue_SuccessfulSendActivatesRecipient()
        {
            var mockLower = new ControllableMockLayer();
            var peer = new ReplicaID(2);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID>(),
                new HashSet<ReplicaID> { peer });

            var trackLayer = new TrackActiveConnectionsLayer(mockLower, tracker, trackSends: true);

            int regainedCount = 0;
            trackLayer.AddOnConnectionRegainedHandler(_ => regainedCount++);

            mockLower.OnSendAsync = (r, m) => Task.FromResult(true);

            var result = await trackLayer.SendMessage(peer, new Message("outbound"));

            Assert.IsTrue(result);
            Assert.IsTrue(tracker.IsActive(peer));
            Assert.AreEqual(1, regainedCount, "Should fire connection regained when send activates peer");
        }

        [TestMethod]
        public async Task TrackActive_TrackSendsFalse_SendDoesNotAlterTracker()
        {
            var mockLower = new ControllableMockLayer();
            var peer = new ReplicaID(2);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID>(),
                new HashSet<ReplicaID> { peer });

            var trackLayer = new TrackActiveConnectionsLayer(mockLower, tracker, trackSends: false);

            await trackLayer.SendMessage(peer, new Message("outbound"));

            Assert.IsFalse(tracker.IsActive(peer), "When trackSends is false, send should not alter active status");
        }

        #endregion

        #region Composite Pipeline Integration Test
/*
        [TestMethod]
        public async Task CompositeStack_TrackActive_CausalHistory_EnsureAck_FullPipeline()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var r1 = new ReplicaID(1);
            var r2 = new ReplicaID(2);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { r2 },
                new HashSet<ReplicaID>());

            int tag = 0;
            // Build stack on r1:
            // EnsureAckLayer (bottom) -> CausalHistoryTrackerLayer -> TrackActiveConnectionsLayer (top)
            var ackLayer = new EnsureAckNetworkLayer<int>(r1, mockLower, scheduler, _ => ++tag, waitTime: 0.1f);
            var causalLayer = new CausalHistoryTrackerLayer(r1, ackLayer);
            var activeLayer = new TrackActiveConnectionsLayer(causalLayer, tracker, trackSends: true);

            await activeLayer.Run();

            // Auto-respond with ACK from mockLower whenever EnsureAck message is sent
            mockLower.OnSendAsync = (recipient, msg) =>
            {
                var ackMsg = EnsureAckMessage<int>.FromJson(msg.Payload);
                if (ackMsg != null && !ackMsg.IsAck)
                {
                    var responseAck = new EnsureAckNetworkLayer<int>.EnsureAckMessage(ackMsg.Tag, new Message(""), true);
                    ackLayer.OnLowerLayerMessageReceived(recipient, new Message(responseAck.ToJson()));
                }
                return Task.FromResult(true);
            };

            // Send message from top layer to r2
            var sent = await activeLayer.SendMessage(r2, new Message("pipeline data"));
            Assert.IsTrue(sent);

            // Verify message structure down at mockLower:
            Assert.AreEqual(1, mockLower.SentMessages.Count);
            var (target, rawSent) = mockLower.SentMessages[0];
            Assert.AreEqual(r2, target);

            // Layer 1 unwrap: EnsureAckMessage
            var ackEnvelope = EnsureAckNetworkLayer<int>.EnsureAckMessage.FromJson(rawSent.Payload);
            Assert.IsNotNull(ackEnvelope);
            Assert.IsFalse(ackEnvelope.IsAck);

            // Layer 2 unwrap: TrackerMessage
            var causalEnvelope = CausalHistoryTrackerLayer.TrackerMessage.FromJson(ackEnvelope.Message?.Payload ?? "");
            Assert.IsNotNull(causalEnvelope);
            Assert.AreEqual("pipeline data", causalEnvelope.Message?.Payload);
            var expectedVv = new VersionVector();
            expectedVv.Increment(r1);
            Assert.IsTrue(expectedVv == causalEnvelope.CausalityTracker);
        }
*/
        #endregion
    }
}
