using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CRDTApp.Messages;
using CRDTApp.NetworkLayers.Composite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shared;

namespace Tests_1
{
    /// <summary>
    /// Test suite specifically asserting the CORRECT, expected behavior of CausalHistoryTrackerLayer
    /// and catching bugs in its causal delivery, dependency tracking, concurrency, and serialization.
    /// Each test is designed to catch a specific bug in CausalHistoryTrackerLayer.
    /// These tests will FAIL against the current implementation and will only pass once corrected.
    /// </summary>
    /*  [TestClass]
      public class CausalHistoryTrackerBugsTests
        {
            [TestMethod]
            public void CausalHistory_DuplicatePacketWhilePending_MustNotDeliverTwice()
            {
                // EXPECTED BEHAVIOR:
                // When a message arrives out-of-order, it is buffered in pendingReceivedMessages.
                // If network retransmission or flooding delivers a duplicate copy of that message
                // while it is still pending, the layer must deduplicate it.
                // When the causal dependency finally arrives, the pending message must be delivered EXACTLY ONCE.
                //
                // CURRENT BUG:
                // In OnLowerLayerMessageReceived:
                //   var comp = trackerMessage.CausalityTracker.Compare(Latest);
                //   if (comp != ThisHappenedBeforeOther && comp != Equal)
                //       pendingReceivedMessages.Add((senderID, trackerMessage));
                // It only compares against Latest! Because the pending message has not been delivered,
                // Latest does not yet include it. The duplicate message is also added to pendingReceivedMessages!
                // When dependency arrives, both copies are delivered to CallMessageReceived!

                var mockLower = new ControllableMockLayer();
                var r1 = new ReplicaID(1); // Receiver
                var r2 = new ReplicaID(2); // Sender

                var causalLayer = new CausalHistoryTrackerLayer(r1, mockLower);

                var deliveredPayloads = new List<string>();
                causalLayer.AddOnMessageReceivedHandler((_, msg) => deliveredPayloads.Add(msg.Payload));

                var vv1 = new VersionVector();
                vv1.Increment(r2); // {2: 1}

                var vv2 = vv1.Clone();
                vv2.Increment(r2); // {2: 2}

                var msg1 = new CausalHistoryTrackerLayer.TrackerMessage(new Message("msg1"), vv1);
                var msg2 = new CausalHistoryTrackerLayer.TrackerMessage(new Message("msg2"), vv2);

                // Step 1: msg2 arrives first (out-of-order). Buffered in pending.
                mockLower.InjectReceivedMessage(r2, new Message(msg2.ToJson()));
                Assert.AreEqual(0, deliveredPayloads.Count);

                // Step 2: Duplicate copy of msg2 arrives while still in pending.
                mockLower.InjectReceivedMessage(r2, new Message(msg2.ToJson()));
                Assert.AreEqual(0, deliveredPayloads.Count);

                // Step 3: msg1 arrives. Causal dependency is satisfied.
                mockLower.InjectReceivedMessage(r2, new Message(msg1.ToJson()));

                // Assert CORRECT behavior: Exactly 2 messages delivered ("msg1" and "msg2" once each).
                // BUG: deliveredPayloads.Count is 3 because msg2 was delivered twice!
                Assert.AreEqual(2, deliveredPayloads.Count,
                    "CORRECT BEHAVIOR: Duplicate message arriving while pending must be deduplicated and delivered only once");
                Assert.AreEqual("msg1", deliveredPayloads[0]);
                Assert.AreEqual("msg2", deliveredPayloads[1]);
            }

            [TestMethod]
            public async Task CausalHistory_FailedSend_MustNotCreatePermanentSequenceGapAndDeadlockReceiver()
            {
                // EXPECTED BEHAVIOR:
                // If LayerBelow.SendMessage fails (returns false), the message was not transmitted.
                // The sender should not permanently advance its causal version sequence for an unsent message,
                // or the receiver should not be deadlocked forever waiting for a message that was never sent.
                //
                // CURRENT BUG:
                // In SendMessage:
                //   var vector = IncrementVersion(replicaID);
                //   ...
                //   return await LayerBelow.SendMessage(recipient, networkMessage);
                // It calls IncrementVersion before sending! If LayerBelow.SendMessage returns false,
                // Latest[replicaID] remains incremented. The next message sent carries a version that skips a number.
                // When receiver receives it, LacksDependencies detects version > versions[sender] + 1 and waits
                // for the missing version forever, permanently deadlocking the receiver!

                var mockSenderLower = new ControllableMockLayer();
                var mockReceiverLower = new ControllableMockLayer();
                var rSender = new ReplicaID(1);
                var rReceiver = new ReplicaID(2);

                var senderLayer = new CausalHistoryTrackerLayer(rSender, mockSenderLower);
                var receiverLayer = new CausalHistoryTrackerLayer(rReceiver, mockReceiverLower);

                var deliveredAtReceiver = new List<string>();
                receiverLayer.AddOnMessageReceivedHandler((_, m) => deliveredAtReceiver.Add(m.Payload));

                // Message 1: succeeds
                mockSenderLower.OnSendAsync = (rec, msg) =>
                {
                    receiverLayer.OnLowerLayerMessageReceived(rSender, msg);
                    return Task.FromResult(true);
                };
                var res1 = await senderLayer.SendMessage(rReceiver, new Message("msg1"));
                Assert.IsTrue(res1);
                Assert.AreEqual(1, deliveredAtReceiver.Count);

                // Message 2: fails at lower layer (e.g. link dropped)
                mockSenderLower.OnSendAsync = (rec, msg) => Task.FromResult(false);
                var res2 = await senderLayer.SendMessage(rReceiver, new Message("msg2_failed"));
                Assert.IsFalse(res2);

                // Message 3: network recovers, succeeds
                mockSenderLower.OnSendAsync = (rec, msg) =>
                {
                    receiverLayer.OnLowerLayerMessageReceived(rSender, msg);
                    return Task.FromResult(true);
                };
                var res3 = await senderLayer.SendMessage(rReceiver, new Message("msg3"));
                Assert.IsTrue(res3);

                // Assert CORRECT behavior: Receiver should receive msg3 and not deadlock waiting for failed msg2
                // BUG: deliveredAtReceiver.Count is 1 because receiver is permanently waiting for missing version 2!
                Assert.AreEqual(2, deliveredAtReceiver.Count,
                    "CORRECT BEHAVIOR: Receiver should deliver msg3; failed msg2 should not create permanent deadlock gap");
            }

            [TestMethod]
            public async Task CausalHistory_PointToPointSendsToDifferentRecipients_CausesDeadlockOnSecondRecipient()
            {
                // EXPECTED BEHAVIOR:
                // When using point-to-point unicast SendMessage(recipient, message),
                // sending a message to Peer A should not cause Peer B to deadlock waiting for Peer A's message.
                //
                // CURRENT BUG:
                // CausalHistoryTrackerLayer maintains a single global Latest vector.
                // SendMessage(Peer A, msg1) increments Latest[Sender] to 1.
                // SendMessage(Peer B, msg2) increments Latest[Sender] to 2.
                // Peer B receives msg2 with {Sender: 2}. Peer B's Latest has {Sender: 0}.
                // LacksDependencies on Peer B sees version > 1 and concludes that Peer B lacks {Sender: 1}.
                // Peer B buffers msg2 forever waiting for msg1 that was never addressed to Peer B!

                var mockSenderLower = new ControllableMockLayer();
                var mockPeerALower = new ControllableMockLayer();
                var mockPeerBLower = new ControllableMockLayer();

                var rSender = new ReplicaID(1);
                var rPeerA = new ReplicaID(2);
                var rPeerB = new ReplicaID(3);

                var senderLayer = new CausalHistoryTrackerLayer(rSender, mockSenderLower);
                var peerALayer = new CausalHistoryTrackerLayer(rPeerA, mockPeerALower);
                var peerBLayer = new CausalHistoryTrackerLayer(rPeerB, mockPeerBLower);

                var peerADelivered = new List<string>();
                var peerBDelivered = new List<string>();
                peerALayer.AddOnMessageReceivedHandler((_, m) => peerADelivered.Add(m.Payload));
                peerBLayer.AddOnMessageReceivedHandler((_, m) => peerBDelivered.Add(m.Payload));

                // Wire lower layer routing: sends to A go to A, sends to B go to B
                mockSenderLower.OnSendAsync = (recipient, msg) =>
                {
                    if (recipient == rPeerA) peerALayer.OnLowerLayerMessageReceived(rSender, msg);
                    if (recipient == rPeerB) peerBLayer.OnLowerLayerMessageReceived(rSender, msg);
                    return Task.FromResult(true);
                };

                // Sender sends msg1 to Peer A
                await senderLayer.SendMessage(rPeerA, new Message("hello A"));
                Assert.AreEqual(1, peerADelivered.Count);

                // Sender sends msg2 to Peer B
                await senderLayer.SendMessage(rPeerB, new Message("hello B"));

                // Assert CORRECT behavior: Peer B should deliver "hello B"
                // BUG: Peer B receives {Sender: 2}, sees Sender: 2 > 1, buffers it waiting for {Sender: 1} forever!
                Assert.AreEqual(1, peerBDelivered.Count,
                    "CORRECT BEHAVIOR: Peer B must deliver message addressed to it, not deadlock waiting for Peer A's message");
            }

            [TestMethod]
            public async Task CausalHistory_ConstructorSharesDictionaryReference_MutatingCallerVector()
            {
                // EXPECTED BEHAVIOR:
                // When an initial VersionVector is passed to CausalHistoryTrackerLayer constructor,
                // the layer should clone it to isolate its internal state. Mutating the layer's
                // vector must not mutate the caller's initial vector.
                //
                // CURRENT BUG:
                // In CausalHistoryTrackerLayer constructor:
                //   this.Latest = latest ?? new VersionVector();
                // VersionVector is a struct containing a reference-type Dictionary<ReplicaID, ulong>.
                // Struct assignment copies the dictionary reference, NOT the contents!
                // When layer increments or merges, the caller's dictionary is mutated in-place!

                var mockLower = new ControllableMockLayer();
                var r1 = new ReplicaID(1);
                var r2 = new ReplicaID(2);

                var initialVector = new VersionVector();
                initialVector.Increment(r1); // {1: 1}

                // Create an independent snapshot of initialVector for comparison
                var snapshotVector = initialVector.Clone();

                var causalLayer = new CausalHistoryTrackerLayer(r1, mockLower, initialVector);

                // Layer sends a message, which increments version for r1
                await causalLayer.SendMessage(r2, new Message("test"));

                // Assert CORRECT behavior: The caller's initialVector must remain {1: 1} and NOT be mutated to {1: 2}
                // BUG: initialVector was mutated in-place to {1: 2}!
                Assert.IsTrue(snapshotVector == initialVector,
                    "CORRECT BEHAVIOR: Constructor must clone initial VersionVector; caller's vector must not be mutated");
            }

            [TestMethod]
            public void CausalHistory_EmptyOrUnsequencedVector_MustNotBeDroppedAsDuplicate()
            {
                // EXPECTED BEHAVIOR:
                // If an unsequenced control or initialization message arrives with an empty VersionVector,
                // it should be delivered to upper layers, not silently dropped as a duplicate.
                //
                // CURRENT BUG:
                // In OnLowerLayerMessageReceived:
                //   var comp = trackerMessage.CausalityTracker.Compare(Latest);
                // If Latest has seen any messages (e.g. Latest = {r2: 1}), Latest.Dominates({}) is true.
                // Compare returns ThisHappenedBeforeOther, causing the empty-vector message to be silently dropped!

                var mockLower = new ControllableMockLayer();
                var r1 = new ReplicaID(1);
                var r2 = new ReplicaID(2);

                var initialVv = new VersionVector();
                initialVv.Increment(r2); // Latest has {2: 1}

                var causalLayer = new CausalHistoryTrackerLayer(r1, mockLower, initialVv);

                int deliveredCount = 0;
                causalLayer.AddOnMessageReceivedHandler((_, _) => deliveredCount++);

                // Send message with empty VersionVector
                var emptyVv = new VersionVector();
                var emptyMsg = new CausalHistoryTrackerLayer.TrackerMessage(new Message("control_msg"), emptyVv);

                mockLower.InjectReceivedMessage(r2, new Message(emptyMsg.ToJson()));

                // Assert CORRECT behavior: The message should be delivered
                // BUG: It is silently dropped because empty vector is treated as ThisHappenedBeforeOther!
                Assert.AreEqual(1, deliveredCount,
                    "CORRECT BEHAVIOR: Message with empty VersionVector should be delivered, not discarded as duplicate");
            }

            [TestMethod]
            public void CausalHistory_ZeroVersionInVector_MustNotPreventDominance()
            {
                // EXPECTED BEHAVIOR:
                // In VersionVector causality comparison, a replica having version 0 means
                // 0 events have occurred. A vector with {A: 2} should dominate {A: 1, B: 0}.
                //
                // CURRENT BUG:
                // In VersionVector.Dominates(other):
                //   foreach (var (k,v) in other.versions) {
                //       if (!versions.ContainsKey(k)) return false;
                //   }
                // It strictly checks if this.versions contains every key in other, even if other's value is 0!
                // Because this does not contain B, Dominates returns false, and Compare reports them as Concurrent!

                var rA = new ReplicaID(1);
                var rB = new ReplicaID(2);

                var vvAdvanced = new VersionVector();
                vvAdvanced.Increment(rA);
                vvAdvanced.Increment(rA); // {A: 2}

                var vvOlderWithZero = new VersionVector();
                vvOlderWithZero.Increment(rA); // {A: 1}
                // Manually set or deserialize B: 0
                var vvWithZero = new VersionVector();
                vvWithZero.JSON = $"{{\"{rA.ID}\":\"1\",\"{rB.ID}\":\"0\"}}";

                // Assert CORRECT behavior: {A: 2} dominates {A: 1, B: 0}
                // BUG: Returns false because B is absent from vvAdvanced
                Assert.IsTrue(vvAdvanced.Dominates(vvWithZero),
                    "CORRECT BEHAVIOR: Vector with higher version should dominate vector with lower version and zero-valued replica");
                Assert.AreEqual(VersionVector.Comparison.ThisHappenedAfterOther, vvAdvanced.Compare(vvWithZero),
                    "CORRECT BEHAVIOR: {A: 2} should be ThisHappenedAfterOther compared to {A: 1, B: 0}");
            }
}*/
}
