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
    public class EnsureAckLayerBehaviourTests
    {
        [TestMethod]
        public async Task SendMessage_SuccessfulAckOnFirstAttempt_ReturnsTrue()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var remoteId = new ReplicaID(2);
            int tagCounter = 100;

            var ackLayer = new EnsureAckNetworkLayer<int>(
                localId,
                mockLower,
                scheduler,
                _ => ++tagCounter,
                waitTime: 0.1f);

            await ackLayer.Launch();

            mockLower.OnSendAsync = (recipient, msg) =>
            {
                var ackMsg = AckMessagePayload<int>.Decode(msg.Payload);
                if (ackMsg != null && !ackMsg.IsAck)
                {
                    var responseAck = new AckMessagePayload<int>(ackMsg.Tag, new Message(""), true);
                    ackLayer.OnLowerLayerMessageReceived(recipient, new Message(responseAck.Encode()));
                }
                return Task.FromResult(true);
            };

            var sendResult = await ackLayer.SendMessage(remoteId, new Message("hello world"));

            Assert.IsTrue(sendResult);
            Assert.AreEqual(1, mockLower.SentMessages.Count);
            var (target, sent) = mockLower.SentMessages[0];
            Assert.AreEqual(remoteId, target);

            var parsed = AckMessagePayload<int>.Decode(sent.Payload);
            Assert.IsNotNull(parsed);
            Assert.IsFalse(parsed.IsAck);
            Assert.AreEqual("hello world", parsed.Message?.Payload);
            Assert.AreEqual(101, parsed.Tag);
        }

        [TestMethod]
        public async Task SendMessage_AckLostOnce_RetriesAndSucceedsOnSecondAttempt()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var remoteId = new ReplicaID(2);
            int attemptCount = 0;

            var ackLayer = new EnsureAckNetworkLayer<int>(
                localId,
                mockLower,
                scheduler,
                _ => 42,
                waitTime: 0.05f);

            await ackLayer.Launch();

            scheduler.TimeoutOverride = (task, waitTime) =>
            {
                if (attemptCount == 1)
                {
                    // First attempt: simulate timeout
                    return Task.FromResult(false);
                }
                // Second attempt: task completed with ack
                return Task.FromResult(task.IsCompleted);
            };

            mockLower.OnSendAsync = (recipient, msg) =>
            {
                attemptCount++;
                if (attemptCount == 2)
                {
                    // Deliver ACK on 2nd attempt
                    var ack = new AckMessagePayload<int>(42, new Message(""), true);
                    ackLayer.OnLowerLayerMessageReceived(recipient, new Message(ack.Encode()));
                }
                return Task.FromResult(true);
            };

            var sendResult = await ackLayer.SendMessage(remoteId, new Message("retry test"));

            Assert.IsTrue(sendResult);
            Assert.AreEqual(2, attemptCount);
            Assert.AreEqual(2, mockLower.SentMessages.Count);
        }

        [TestMethod]
        public async Task SendMessage_AckLostTwice_RetriesAndSucceedsOnThirdAttempt()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var remoteId = new ReplicaID(2);
            int attemptCount = 0;

            var ackLayer = new EnsureAckNetworkLayer<int>(
                localId,
                mockLower,
                scheduler,
                _ => 999,
                waitTime: 0.05f);

            await ackLayer.Launch();

            scheduler.TimeoutOverride = (task, waitTime) =>
            {
                if (attemptCount < 3)
                {
                    return Task.FromResult(false);
                }
                return Task.FromResult(task.IsCompleted);
            };

            mockLower.OnSendAsync = (recipient, msg) =>
            {
                attemptCount++;
                if (attemptCount == 3)
                {
                    var ack = new AckMessagePayload<int>(999, new Message(""), true);
                    ackLayer.OnLowerLayerMessageReceived(recipient, new Message(ack.Encode()));
                }
                return Task.FromResult(true);
            };

            var sendResult = await ackLayer.SendMessage(remoteId, new Message("attempt 3 success"));

            Assert.IsTrue(sendResult);
            Assert.AreEqual(3, attemptCount);
            Assert.AreEqual(3, mockLower.SentMessages.Count);
        }

        [TestMethod]
        public async Task SendMessage_AllAttemptsTimeout_CallsConnectionInterruptedAndReturnsFalse()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var remoteId = new ReplicaID(2);
            bool connectionInterruptedCalled = false;
            ReplicaID? interruptedReplica = null;

            var ackLayer = new EnsureAckNetworkLayer<int>(
                localId,
                mockLower,
                scheduler,
                _ => 1,
                waitTime: 0.01f);

            await ackLayer.Launch();

            ackLayer.AddOnConnectionInterruptedHandler(r =>
            {
                connectionInterruptedCalled = true;
                interruptedReplica = r;
            });

            // Force all timeout attempts to fail
            scheduler.ForceTimeout = true;

            var sendResult = await ackLayer.SendMessage(remoteId, new Message("will timeout"));

            Assert.IsFalse(sendResult);
            Assert.AreEqual(3, mockLower.SentMessages.Count, "Must have retried up to 3 times before failing");
            Assert.IsTrue(connectionInterruptedCalled);
            Assert.AreEqual(remoteId, interruptedReplica);
        }

        [TestMethod]
        public async Task SendMessage_LowerLayerFailsImmediately_CallsConnectionInterruptedAndDoesNotRetry()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var remoteId = new ReplicaID(2);
            bool connectionInterrupted = false;

            mockLower.OnSendAsync = (recipient, msg) => Task.FromResult(false);

            var ackLayer = new EnsureAckNetworkLayer<int>(
                localId,
                mockLower,
                scheduler,
                _ => 1,
                waitTime: 0.1f);

            await ackLayer.Launch();

            ackLayer.AddOnConnectionInterruptedHandler(r =>
            {
                if (r == remoteId) connectionInterrupted = true;
            });

            var sendResult = await ackLayer.SendMessage(remoteId, new Message("fail immediately"));

            Assert.IsFalse(sendResult);
            Assert.AreEqual(1, mockLower.SentMessages.Count, "Should fail on first send attempt without retrying");
            Assert.IsTrue(connectionInterrupted);
        }

        [TestMethod]
        public async Task SendMessage_StoppedDuringExecution_TerminatesEarly()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var remoteId = new ReplicaID(2);
            int attempts = 0;

            var ackLayer = new EnsureAckNetworkLayer<int>(
                localId,
                mockLower,
                scheduler,
                _ => 1,
                waitTime: 0.05f);

            await ackLayer.Launch();

            mockLower.OnSendAsync = (recipient, msg) =>
            {
                attempts++;
                // Stop the layer during the first attempt timeout
                ackLayer.Stop();
                return Task.FromResult(true);
            };

            scheduler.ForceTimeout = true;

            var result = await ackLayer.SendMessage(remoteId, new Message("stopping"));

            Assert.IsFalse(result);
            Assert.AreEqual(1, attempts, "Once stopped, running loop terminates without making further attempts");
        }

        [TestMethod]
        public void ReceiveMessage_NormalMessage_DeliversUnderlyingAndSendsAck()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var remoteId = new ReplicaID(2);

            var ackLayer = new EnsureAckNetworkLayer<int>(
                localId,
                mockLower,
                scheduler,
                _ => 1);

            Message? deliveredMessage = null;
            ReplicaID? deliveredSender = null;
            ackLayer.AddOnMessageReceivedHandler((s, m) =>
            {
                deliveredSender = s;
                deliveredMessage = m;
            });

            var originalMsg = new Message("important data");
            var inboundWrap = new AckMessagePayload<int>(555, originalMsg, isAck: false);

            mockLower.InjectReceivedMessage(remoteId, new Message(inboundWrap.Encode()));

            Assert.IsNotNull(deliveredMessage);
            Assert.AreEqual("important data", deliveredMessage.Payload);
            Assert.AreEqual(remoteId, deliveredSender);

            // Verify ACK was sent back down
            Assert.AreEqual(1, mockLower.SentMessages.Count);
            var (ackTarget, ackNetworkMsg) = mockLower.SentMessages[0];
            Assert.AreEqual(remoteId, ackTarget);

            var sentAck = AckMessagePayload<int>.Decode(ackNetworkMsg.Payload);
            Assert.IsNotNull(sentAck);
            Assert.IsTrue(sentAck.IsAck);
            Assert.AreEqual(555, sentAck.Tag);
        }

        [TestMethod]
        public void ReceiveMessage_UnmatchedAck_HandledGracefullyWithoutException()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);

            var ackLayer = new EnsureAckNetworkLayer<int>(
                localId,
                mockLower,
                scheduler,
                _ => 1);

            var ghostAck = new AckMessagePayload<int>(99999, new Message(""), isAck: true);
            
            // Should not throw
            mockLower.InjectReceivedMessage(new ReplicaID(99), new Message(ghostAck.Encode()));
            Assert.AreEqual(0, mockLower.SentMessages.Count);
        }

        [TestMethod]
        public async Task ConcurrentSends_MultipleInFlightMessages_TrackedSeparately()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var node2 = new ReplicaID(2);
            var node3 = new ReplicaID(3);
            int tagCounter = 0;

            var ackLayer = new EnsureAckNetworkLayer<int>(
                localId,
                mockLower,
                scheduler,
                _ => ++tagCounter,
                waitTime: 0.1f);

            await ackLayer.Launch();

            mockLower.OnSendAsync = (recipient, msg) =>
            {
                var ackMsg = AckMessagePayload<int>.Decode(msg.Payload);
                if (ackMsg != null && !ackMsg.IsAck)
                {
                    var responseAck = new AckMessagePayload<int>(ackMsg.Tag, new Message(""), true);
                    ackLayer.OnLowerLayerMessageReceived(recipient, new Message(responseAck.Encode()));
                }
                return Task.FromResult(true);
            };

            var sendTask1 = ackLayer.SendMessage(node2, new Message("msg1"));
            var sendTask2 = ackLayer.SendMessage(node3, new Message("msg2"));

            var results = await Task.WhenAll(sendTask1, sendTask2);

            Assert.IsTrue(results[0]);
            Assert.IsTrue(results[1]);
            Assert.AreEqual(2, mockLower.SentMessages.Count);
        }

        [TestMethod]
        public void ConnectionEvents_PropagateFromLowerLayer()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var peer = new ReplicaID(5);

            var ackLayer = new EnsureAckNetworkLayer<int>(
                localId,
                mockLower,
                scheduler,
                _ => 1);

            bool interrupted = false;
            bool regained = false;

            ackLayer.AddOnConnectionInterruptedHandler(r =>
            {
                if (r == peer) interrupted = true;
            });

            ackLayer.AddOnConnectionRegainedHandler(r =>
            {
                if (r == peer) regained = true;
            });

            mockLower.InjectConnectionInterrupted(peer);
            Assert.IsTrue(interrupted);

            mockLower.InjectConnectionRegained(peer);
            Assert.IsTrue(regained);
        }
    }
}
