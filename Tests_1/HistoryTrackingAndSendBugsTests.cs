using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CRDTApp.Messages;
using CRDTs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Shared;

namespace Tests_1
{
    /// <summary>
    /// Test suite asserting the expected correct behavior of causal history tracking and send behaviour,
    /// detecting bugs that prevent more than 1 message per replica from being received and processed correctly.
    /// 
    /// These tests fail against the current codebase bugs and will pass once those bugs are corrected.
    /// Per user instructions, no existing files were changed.
    /// </summary>
    [TestClass]
    public class HistoryTrackingAndSendBugsTests
    {
        private static readonly Type CausalHistoryTrackerType = typeof(CausalHistoryTracker);
        private static readonly Type HistoryTrackedMessageType = typeof(CRDTApp.Behaviours.InsertBehaviour).Assembly.GetType("CRDTApp.Messages.HistoryTrackedMessage")!;

        #region Reflection Helpers

        private static object CreateTracker(ReplicaID replicaId, VersionVector? latest = null)
        {
            var ctor = CausalHistoryTrackerType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(ReplicaID), typeof(VersionVector) },
                null);

            if (ctor != null)
            {
                return ctor.Invoke(new object?[] { replicaId, latest });
            }

            var ctorOpt = CausalHistoryTrackerType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];
            return ctorOpt.Invoke(new object?[] { replicaId, latest });
        }

        private static VersionVector GetLatest(object tracker)
        {
            var prop = CausalHistoryTrackerType.GetProperty("Latest", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return (VersionVector)prop!.GetValue(tracker)!;
        }

        private static Message TagMessageAndGetWireMessage(object tracker, Message message)
        {
            var tagMethod = CausalHistoryTrackerType.GetMethod("TagMessage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var historyTrackedMsg = tagMethod!.Invoke(tracker, new object[] { message });

            var asMessageMethod = historyTrackedMsg!.GetType().GetMethod("AsMessage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (asMessageMethod != null)
            {
                return (Message)asMessageMethod.Invoke(historyTrackedMsg, null)!;
            }

            var toJsonMethod = historyTrackedMsg.GetType().GetMethod("ToJson", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            string json = (string)toJsonMethod!.Invoke(historyTrackedMsg, null)!;
            return new Message(json);
        }

        private static void DeliverToTracker(object tracker, ReplicaID senderId, Message wireMessage)
        {
            var onReceivedMethod = CausalHistoryTrackerType.GetMethod("OnMessageReceived", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            onReceivedMethod!.Invoke(tracker, new object[] { senderId, wireMessage });
        }

        private static void SubscribeMessageIssued(object tracker, Action<ReplicaID, Message?, VersionVector> handler)
        {
            var addHandler = CausalHistoryTrackerType.GetMethod("AddMessageIssuedHandler", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            addHandler!.Invoke(tracker, new object[] { handler });
        }

        #endregion

        #region Bug 1 & 5: VersionVector JSON Serialization & Struct Roundtrip in HistoryTrackedMessage

        [TestMethod]
        public void Bug_Serialization_HistoryTrackedMessage_MustRoundtripCausalityVectorIntact()
        {
            // EXPECTED BEHAVIOR:
            // When a message is tagged with a causality VersionVector (e.g. {r1: 1, r2: 2}),
            // serializing it to JSON and deserializing it via HistoryTrackedMessage.FromJson
            // must preserve the causality vector intact.
            //
            // CURRENT BUG:
            // VersionVector is a struct whose internal dictionary is not properly serialized or restored
            // through HistoryTrackedMessage deserialization, resulting in empty/zeroed CausalityTracker vectors.
            // This prevents causal dependency checks from functioning correctly when more than 1 message is sent.

            Assert.IsNotNull(HistoryTrackedMessageType, "HistoryTrackedMessage type must exist");

            var r1 = new ReplicaID(1);
            var r2 = new ReplicaID(2);

            var vv = new VersionVector();
            vv.Increment(r1);
            vv.Increment(r2);
            vv.Increment(r2);

            var ctor = HistoryTrackedMessageType.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(Message), typeof(VersionVector) },
                null);

            Assert.IsNotNull(ctor, "Constructor HistoryTrackedMessage(Message, VersionVector) must exist");

            var originalMsg = new Message("payload_test");
            var original = ctor.Invoke(new object[] { originalMsg, vv });

            var toJsonMethod = HistoryTrackedMessageType.GetMethod("ToJson", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            string json = (string)toJsonMethod!.Invoke(original, null)!;

            var fromJsonMethod = HistoryTrackedMessageType.GetMethod("FromJson", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            var deserialized = fromJsonMethod!.Invoke(null, new object[] { json });

            Assert.IsNotNull(deserialized, "Deserialized HistoryTrackedMessage must not be null");

            var prop = HistoryTrackedMessageType.GetProperty("CausalityTracker", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var restoredVv = (VersionVector)prop!.GetValue(deserialized)!;

            Assert.AreEqual(VersionVector.Comparison.Equal, restoredVv.Compare(vv),
                "CORRECT BEHAVIOR: CausalityTracker must survive JSON roundtrip intact. " +
                "BUG: VersionVector is empty or corrupted after serialization.");
        }

        #endregion

        #region Bug 2: Multiple Messages from the Same Replica Must All Be Delivered

        [TestMethod]
        public void Bug_CausalTracker_SequentialMessagesFromSameReplica_MustDeliverAllMessages()
        {
            // EXPECTED BEHAVIOR:
            // When sender replica sends 3 sequential messages ("msg1", "msg2", "msg3"),
            // the receiver must deliver all 3 messages in sequence.
            //
            // CURRENT BUG:
            // Currently, only the 1st message is delivered; subsequent messages (>1) from the same replica
            // fail to be delivered or get stuck in pending due to broken history tracking/LacksDependencies checks.

            var senderId = new ReplicaID(1);
            var receiverId = new ReplicaID(2);

            var senderTracker = CreateTracker(senderId);
            var receiverTracker = CreateTracker(receiverId);

            var deliveredPayloads = new List<string>();
            SubscribeMessageIssued(receiverTracker, (src, msg, vv) =>
            {
                if (msg != null) deliveredPayloads.Add(msg.Payload);
            });

            var wire1 = TagMessageAndGetWireMessage(senderTracker, new Message("msg1"));
            var wire2 = TagMessageAndGetWireMessage(senderTracker, new Message("msg2"));
            var wire3 = TagMessageAndGetWireMessage(senderTracker, new Message("msg3"));

            DeliverToTracker(receiverTracker, senderId, wire1);
            DeliverToTracker(receiverTracker, senderId, wire2);
            DeliverToTracker(receiverTracker, senderId, wire3);

            Assert.AreEqual(3, deliveredPayloads.Count,
                $"CORRECT BEHAVIOR: All 3 sequential messages from replica {senderId} must be delivered. " +
                $"BUG: Only {deliveredPayloads.Count} message(s) delivered because messages beyond the 1st are blocked.");
            Assert.AreEqual("msg1", deliveredPayloads[0]);
            Assert.AreEqual("msg2", deliveredPayloads[1]);
            Assert.AreEqual("msg3", deliveredPayloads[2]);
        }

        #endregion

        #region Bug 3: Out-of-Order Message Buffering and Causal Cascade Delivery

        [TestMethod]
        public void Bug_CausalTracker_OutOfOrderArrival_BuffersAndCascadesWhenMissingDependencyArrives()
        {
            // EXPECTED BEHAVIOR:
            // If message 2 arrives before message 1, message 2 must be buffered.
            // When message 1 arrives, message 1 must be delivered first, immediately followed by message 2.
            //
            // CURRENT BUG:
            // Messages arriving out of order are dropped or never cascaded due to incorrect dependency checks
            // or failing to advance Latest vector properly.

            var senderId = new ReplicaID(1);
            var receiverId = new ReplicaID(2);

            var senderTracker = CreateTracker(senderId);
            var receiverTracker = CreateTracker(receiverId);

            var deliveredPayloads = new List<string>();
            SubscribeMessageIssued(receiverTracker, (src, msg, vv) =>
            {
                if (msg != null) deliveredPayloads.Add(msg.Payload);
            });

            var wire1 = TagMessageAndGetWireMessage(senderTracker, new Message("msg1"));
            var wire2 = TagMessageAndGetWireMessage(senderTracker, new Message("msg2"));

            // Deliver msg2 first (out of order)
            DeliverToTracker(receiverTracker, senderId, wire2);
            Assert.AreEqual(0, deliveredPayloads.Count, "Message 2 must be buffered because it depends on message 1");

            // Deliver msg1 (missing dependency)
            DeliverToTracker(receiverTracker, senderId, wire1);

            Assert.AreEqual(2, deliveredPayloads.Count,
                "CORRECT BEHAVIOR: Both messages must be delivered once dependency is satisfied. " +
                $"BUG: Only {deliveredPayloads.Count} delivered.");
            Assert.AreEqual("msg1", deliveredPayloads[0]);
            Assert.AreEqual("msg2", deliveredPayloads[1]);
        }

        #endregion

        #region Bug 4: Duplicate Messages From Retransmissions Must Not Be Delivered Twice

        [TestMethod]
        public void Bug_CausalTracker_DuplicateMessage_MustBeDeduplicatedAndDeliveredOnce()
        {
            // EXPECTED BEHAVIOR:
            // If the same message (same causal version) is received twice (e.g. via network flooding or retransmission),
            // it must be deduplicated and delivered only once.
            //
            // CURRENT BUG:
            // Either duplicates are re-added to pending and delivered multiple times, or Latest is not updated.

            var senderId = new ReplicaID(1);
            var receiverId = new ReplicaID(2);

            var senderTracker = CreateTracker(senderId);
            var receiverTracker = CreateTracker(receiverId);

            int deliveryCount = 0;
            SubscribeMessageIssued(receiverTracker, (src, msg, vv) => deliveryCount++);

            var wire1 = TagMessageAndGetWireMessage(senderTracker, new Message("msg1"));

            DeliverToTracker(receiverTracker, senderId, wire1);
            DeliverToTracker(receiverTracker, senderId, wire1); // Duplicate

            Assert.AreEqual(1, deliveryCount,
                "CORRECT BEHAVIOR: Duplicate message must be delivered exactly once. " +
                $"BUG: Delivered {deliveryCount} times.");
        }

        #endregion

        #region Bug 5: Latest Vector Must Advance Upon Delivering Messages

        [TestMethod]
        public void Bug_CausalTracker_LatestVectorAdvancesWithEachDeliveredMessage()
        {
            // EXPECTED BEHAVIOR:
            // As messages are delivered, the receiver's Latest vector must advance to match the causal history.
            //
            // CURRENT BUG:
            // Latest does not advance, causing subsequent messages to be evaluated against an empty/stale vector.

            var senderId = new ReplicaID(1);
            var receiverId = new ReplicaID(2);

            var senderTracker = CreateTracker(senderId);
            var receiverTracker = CreateTracker(receiverId);

            var wire1 = TagMessageAndGetWireMessage(senderTracker, new Message("msg1"));
            var wire2 = TagMessageAndGetWireMessage(senderTracker, new Message("msg2"));

            DeliverToTracker(receiverTracker, senderId, wire1);
            DeliverToTracker(receiverTracker, senderId, wire2);

            var expected = new VersionVector();
            expected.Increment(senderId);
            expected.Increment(senderId);

            var actualLatest = GetLatest(receiverTracker);
            Assert.AreEqual(VersionVector.Comparison.Equal, actualLatest.Compare(expected),
                $"CORRECT BEHAVIOR: Receiver Latest vector must be {{ {senderId}: 2 }}. " +
                "BUG: Latest vector failed to advance with received messages.");
        }

        #endregion

        #region Bug 6: Multiple Replicas Sending Messages to Same Receiver

        [TestMethod]
        public void Bug_CausalTracker_MultipleReplicasSendingMultipleMessages_AllDelivered()
        {
            // EXPECTED BEHAVIOR:
            // Multiple replicas (r1 and r2) each sending multiple messages to r3.
            // All messages from all replicas must be delivered without deadlock or premature drop.
            //
            // CURRENT BUG:
            // Receiving >1 message per replica fails due to faulty LacksDependencies or history tracking bugs.

            var r1 = new ReplicaID(1);
            var r2 = new ReplicaID(2);
            var r3 = new ReplicaID(3);

            var tracker1 = CreateTracker(r1);
            var tracker2 = CreateTracker(r2);
            var receiverTracker = CreateTracker(r3);

            var delivered = new List<(ReplicaID, string)>();
            SubscribeMessageIssued(receiverTracker, (src, msg, vv) =>
            {
                if (msg != null) delivered.Add((src, msg.Payload));
            });

            var r1_m1 = TagMessageAndGetWireMessage(tracker1, new Message("r1_1"));
            var r1_m2 = TagMessageAndGetWireMessage(tracker1, new Message("r1_2"));
            var r2_m1 = TagMessageAndGetWireMessage(tracker2, new Message("r2_1"));
            var r2_m2 = TagMessageAndGetWireMessage(tracker2, new Message("r2_2"));

            // Interleaved delivery
            DeliverToTracker(receiverTracker, r1, r1_m1);
            DeliverToTracker(receiverTracker, r2, r2_m1);
            DeliverToTracker(receiverTracker, r1, r1_m2);
            DeliverToTracker(receiverTracker, r2, r2_m2);

            Assert.AreEqual(4, delivered.Count,
                $"CORRECT BEHAVIOR: All 4 messages (2 per sender) must be delivered. " +
                $"BUG: Only {delivered.Count} message(s) delivered.");
        }

        #endregion

        

        
        #region Bug 8: Constructor Sharing Dictionary Reference Mutating Caller State

        [TestMethod]
        public void Bug_CausalTracker_Constructor_MustCloneInitialVersionVector()
        {
            // EXPECTED BEHAVIOR:
            // Passing an initial VersionVector to CausalHistoryTracker must clone its internal dictionary
            // so that modifying tracker's Latest does not silently mutate the caller's vector.
            //
            // CURRENT BUG:
            // CausalHistoryTracker assigns `this.Latest = latest ?? new VersionVector();`.
            // Because VersionVector is a struct holding a Dictionary reference, struct assignment
            // copies the reference, sharing the dictionary with the caller.

            var r1 = new ReplicaID(1);
            var initialVv = new VersionVector();
            initialVv.Increment(r1);

            var snapshot = initialVv.Clone();

            var tracker = CreateTracker(r1, initialVv);
            TagMessageAndGetWireMessage(tracker, new Message("test"));

            Assert.AreEqual(VersionVector.Comparison.Equal, initialVv.Compare(snapshot),
                "CORRECT BEHAVIOR: Tracker constructor must clone initial VersionVector. " +
                "BUG: Caller's VersionVector was mutated when tracker incremented its version.");
        }

        #endregion

        #region Bug 9: Send Behaviour (InsertBehaviour) Must Advance Numbers And Not Stagnate

        [TestMethod]
        public async Task Bug_SendBehaviour_InsertBehaviour_MustAdvanceNumbersUpToMax()
        {
            // EXPECTED BEHAVIOR:
            // InsertBehaviour with minNumber=1 and maxNumberInclusive=3 must send distinct operations
            // for numbers 1, 2, and 3.
            //
            // CURRENT BUG:
            // In InsertBehaviour.SendUpdateUntil:
            //   scheduler.ExecuteAfter(delayBetweenOps, () => { SendUpdateUntil(currentNumber++, maxNumber); });
            // The post-increment operator (currentNumber++) evaluates to the OLD value, so the lambda
            // continuously schedules SendUpdateUntil with the same currentNumber (1)!
            // As a result, subsequent numbers (>1) are never sent, preventing more than 1 distinct message
            // per replica from ever being transmitted.

            var scheduler = new ControllableTestScheduler();
            var mockNetwork = new ControllableMockLayer();
            var r1 = new ReplicaID(1);
            var r2 = new ReplicaID(2);

            var insertBehaviour = new CRDTApp.Behaviours.InsertBehaviour(
                mockNetwork,
                scheduler,
                r1,
                new[] { r2 },
                minNumber: 1,
                maxNumberInclusive: 3,
                expectedCount: 3,
                delayBetweenSends: 0.1f,
                delayBetweenOps: 0.1f);

            await insertBehaviour.Launch();

            // Advance scheduler to allow all operations to trigger
            for (int step = 0; step < 10; step++)
            {
                scheduler.AdvanceSeconds(0.15f);
            }

            // Extract the CRDT payloads from the sent messages
            // HistoryTrackedMessage -> Message -> ORSet
            var sentValues = new HashSet<int>();
            foreach (var (recipient, rawMsg) in mockNetwork.SentMessages)
            {
                var historyTracked = HistoryTrackedMessageType
                    .GetMethod("FromJson", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy)!
                    .Invoke(null, new object[] { rawMsg.Payload });

                if (historyTracked != null)
                {
                    var innerMsg = (Message)HistoryTrackedMessageType.GetProperty("Message", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
                        .GetValue(historyTracked)!;

                    var op = CRDTs.Basic.ORSet<int>.FromJSON(innerMsg.Payload, int.Parse);
                    foreach (var val in op.GetValues())
                    {
                        sentValues.Add(val);
                    }
                }
            }

            Assert.IsTrue(sentValues.Contains(1), "Must have sent value 1");
            Assert.IsTrue(sentValues.Contains(2),
                "CORRECT BEHAVIOR: Must send value 2. " +
                "BUG: Post-increment (currentNumber++) prevents currentNumber from advancing, so only 1 is repeatedly sent.");
            Assert.IsTrue(sentValues.Contains(3),
                "CORRECT BEHAVIOR: Must send value 3. " +
                "BUG: Post-increment (currentNumber++) prevents currentNumber from advancing.");
        }

        #endregion

        #region Bug 10: Multi-Replica InsertBehaviour Integration Test

        [TestMethod]
        public async Task Bug_SendBehaviour_TwoReplicas_MustReceiveAllMessagesAndEndBehaviour()
        {
            // EXPECTED BEHAVIOR:
            // When two replicas (r1 and r2) run InsertBehaviour each adding 2 numbers (r1: 1..2, r2: 3..4),
            // total expectedCount = 4. Both replicas must exchange messages, receive more than 1 message
            // from the other replica, and invoke BehaviourEnded when expectedCount is reached.
            //
            // CURRENT BUG:
            // Due to send behaviour bugs (currentNumber++) and causal history tracking bugs,
            // replicas fail to receive and process more than 1 message per replica, so neither replica
            // ever reaches expectedCount and neither BehaviourEnded event fires.

            var scheduler = new ControllableTestScheduler();
            var net1 = new ControllableMockLayer();
            var net2 = new ControllableMockLayer();

            var r1 = new ReplicaID(1);
            var r2 = new ReplicaID(2);

            bool r1Ended = false;
            bool r2Ended = false;

            var b1 = new CRDTApp.Behaviours.InsertBehaviour(net1, scheduler, r1, new[] { r2 }, 1, 2, expectedCount: 4, delayBetweenSends: 0.1f, delayBetweenOps: 0.1f);
            var b2 = new CRDTApp.Behaviours.InsertBehaviour(net2, scheduler, r2, new[] { r1 }, 3, 4, expectedCount: 4, delayBetweenSends: 0.1f, delayBetweenOps: 0.1f);

            b1.AddBehaviourEndedHandler(() => r1Ended = true);
            b2.AddBehaviourEndedHandler(() => r2Ended = true);

            // Wire networks together
            net1.OnSendAsync = (target, msg) =>
            {
                if (target == r2) net2.InjectReceivedMessage(r1, msg);
                return Task.FromResult(true);
            };

            net2.OnSendAsync = (target, msg) =>
            {
                if (target == r1) net1.InjectReceivedMessage(r2, msg);
                return Task.FromResult(true);
            };

            await b1.Launch();
            await b2.Launch();

            // Run simulation steps
            for (int i = 0; i < 20; i++)
            {
                scheduler.AdvanceSeconds(0.15f);
            }

            Assert.IsTrue(r1Ended,
                "CORRECT BEHAVIOR: Replica 1 must receive all messages from Replica 2 and complete. " +
                "BUG: Stagnated send or history tracking prevents receiving >1 message per replica.");
            Assert.IsTrue(r2Ended,
                "CORRECT BEHAVIOR: Replica 2 must receive all messages from Replica 1 and complete. " +
                "BUG: Stagnated send or history tracking prevents receiving >1 message per replica.");
        }

        #endregion

        #region Bug 11: Cross-Replica Dependencies Block Subsequent Messages (>1) From Same Sender

        [TestMethod]
        public void Bug_CausalTracker_CrossReplicaDependency_BlocksSubsequentMessagesFromSameReplica()
        {
            // EXPECTED BEHAVIOR:
            // Replica 1 sends msg1 to Replica 2 -> Replica 2 delivers msg1.
            // Replica 1 then receives an event from Replica 3 ({r3: 1}), updating Replica 1's Latest to {r1: 1, r3: 1}.
            // Replica 1 now sends msg2 to Replica 2 (tagged with {r1: 2, r3: 1}).
            // Replica 2 receives msg2. If Replica 2 is in point-to-point communication or hasn't received from r3 yet,
            // or if r3 subsequently delivers its initial event ({r3: 1}), Replica 2 must deliver msg2.
            //
            // CURRENT BUG:
            // If Replica 2 receives {r3: 1} and msg2 {r1: 2, r3: 1}:
            // In VersionVector.LacksDependencies:
            //   When checking msg2 {r1: 2, r3: 1} against Latest {r1: 1}:
            //   numIncreases increments for r1 (1 -> 2) AND for r3 (0 -> 1).
            //   numIncreases > 1 triggers immediately on the second entry, permanently rejecting msg2!
            // Even when r3's event arrives, if both increments are in the vector, the message is never unblocked.

            var r1 = new ReplicaID(1);
            var r2 = new ReplicaID(2);
            var r3 = new ReplicaID(3);

            var tracker1 = CreateTracker(r1);
            var tracker2 = CreateTracker(r2);
            var tracker3 = CreateTracker(r3);

            var deliveredAtR2 = new List<string>();
            SubscribeMessageIssued(tracker2, (src, msg, vv) =>
            {
                if (msg != null) deliveredAtR2.Add(msg.Payload);
            });

            // 1. r1 sends msg1 to r2
            var wire1 = TagMessageAndGetWireMessage(tracker1, new Message("r1_msg1"));
            DeliverToTracker(tracker2, r1, wire1);
            Assert.AreEqual(1, deliveredAtR2.Count, "r1_msg1 must be delivered");

            // 2. r3 sends an event to r1
            var wireR3 = TagMessageAndGetWireMessage(tracker3, new Message("r3_event"));
            DeliverToTracker(tracker1, r3, wireR3);

            // 3. r1 now sends msg2 (its 2nd message) to r2, which carries causality vector with both r1 and r3
            var wire2 = TagMessageAndGetWireMessage(tracker1, new Message("r1_msg2"));

            // 4. r3 also delivers its event to r2
            DeliverToTracker(tracker2, r3, wireR3);

            // 5. r1 delivers msg2 to r2
            DeliverToTracker(tracker2, r1, wire2);

            Assert.AreEqual(3, deliveredAtR2.Count,
                "CORRECT BEHAVIOR: Replica 2 must deliver r1_msg1, r3_event, and r1_msg2 (>1 message from r1). " +
                $"BUG: Only {deliveredAtR2.Count} message(s) delivered because r1_msg2 was blocked.");
            Assert.IsTrue(deliveredAtR2.Contains("r1_msg2"),
                "CORRECT BEHAVIOR: Replica 2 must deliver the second message from r1. " +
                "BUG: Causal tracker prevents more than 1 message per replica from being received when cross-replica history is present.");
        }

        #endregion
    }
}
