using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.NetworkLayers;
using CRDTApp.NetworkLayers.Composite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shared;

namespace Tests_1
{
    [TestClass]
    public class TimeoutAndHeartbeatBehaviourTests
    {
        [TestMethod]
        public void TimeoutLayer_SingleMessage_FiresConnectionInterruptedAfterTimeout()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var senderId = new ReplicaID(2);
            float timeout = 1.0f;

            var timeoutLayer = new TrackMessageReceptionTimeoutLayer(
                localId,
                timeout,
                scheduler,
                mockLower,
                trackSends: false);

            ReplicaID? interrupted = null;
            timeoutLayer.AddOnConnectionInterruptedHandler(r => interrupted = r);

            // Message arrives at t=0
            mockLower.InjectReceivedMessage(senderId, new Message("hello"));

            // Advance 0.5s -> not timed out yet
            scheduler.AdvanceSeconds(0.5f);
            Assert.IsNull(interrupted);

            // Advance another 0.6s (total 1.1s > 1.0s) -> timeout fires
            scheduler.AdvanceSeconds(0.6f);
            Assert.AreEqual(senderId, interrupted);
        }

        [TestMethod]
        public void TimeoutLayer_SubsequentMessageBeforeTimeout_ResetsTimerAndPreventsEarlyInterruption()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var senderId = new ReplicaID(2);
            float timeout = 1.0f;

            var timeoutLayer = new TrackMessageReceptionTimeoutLayer(
                localId,
                timeout,
                scheduler,
                mockLower,
                trackSends: false);

            int interruptCount = 0;
            timeoutLayer.AddOnConnectionInterruptedHandler(_ => interruptCount++);

            // Message 1 at t=0
            mockLower.InjectReceivedMessage(senderId, new Message("msg1"));

            // At t=0.6s, Message 2 arrives, resetting the timeout to t = 0.6 + 1.0 = 1.6s
            scheduler.AdvanceSeconds(0.6f);
            mockLower.InjectReceivedMessage(senderId, new Message("msg2"));

            // Advance past the first timeout mark (t=1.1s)
            scheduler.AdvanceSeconds(0.5f);
            Assert.AreEqual(0, interruptCount, "First scheduled timeout should have been cancelled by msg2");

            // Advance past the second timeout mark (t=1.7s > 1.6s)
            scheduler.AdvanceSeconds(0.6f);
            Assert.AreEqual(1, interruptCount, "Second timeout should now fire");
        }

        [TestMethod]
        public void TimeoutLayer_ConnectionRegained_FiresOnInitialContactAndAfterTimeoutOnly()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var senderId = new ReplicaID(2);
            float timeout = 1.0f;

            var timeoutLayer = new TrackMessageReceptionTimeoutLayer(
                localId,
                timeout,
                scheduler,
                mockLower,
                trackSends: false);

            int regainedCount = 0;
            int interruptedCount = 0;
            timeoutLayer.AddOnConnectionRegainedHandler(_ => regainedCount++);
            timeoutLayer.AddOnConnectionInterruptedHandler(_ => interruptedCount++);

            // Initial message -> regained should fire
            mockLower.InjectReceivedMessage(senderId, new Message("msg1"));
            Assert.AreEqual(1, regainedCount);

            // Message arriving within timeout -> should NOT refire regained
            scheduler.AdvanceSeconds(0.3f);
            mockLower.InjectReceivedMessage(senderId, new Message("msg2"));
            Assert.AreEqual(1, regainedCount);

            // Let it timeout
            scheduler.AdvanceSeconds(1.2f);
            Assert.AreEqual(1, interruptedCount);

            // Message arriving after timeout -> regained should fire again!
            mockLower.InjectReceivedMessage(senderId, new Message("msg3"));
            Assert.AreEqual(2, regainedCount);
        }

        [TestMethod]
        public void TimeoutLayer_MessageFromSelf_DoesNotTriggerConnectionEventsOrTimeout()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);

            var timeoutLayer = new TrackMessageReceptionTimeoutLayer(
                localId,
                1.0f,
                scheduler,
                mockLower,
                trackSends: false);

            bool regained = false;
            bool interrupted = false;
            timeoutLayer.AddOnConnectionRegainedHandler(_ => regained = true);
            timeoutLayer.AddOnConnectionInterruptedHandler(_ => interrupted = true);

            // Message from self
            mockLower.InjectReceivedMessage(localId, new Message("self message"));
            scheduler.AdvanceSeconds(2.0f);

            Assert.IsFalse(regained);
            Assert.IsFalse(interrupted);
        }

        [TestMethod]
        public async Task TimeoutLayer_TrackSendsTrue_SuccessfulSendPostponesTimeout()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var peer = new ReplicaID(2);

            var timeoutLayer = new TrackMessageReceptionTimeoutLayer(
                localId,
                1.0f,
                scheduler,
                mockLower,
                trackSends: true);

            int interruptedCount = 0;
            timeoutLayer.AddOnConnectionInterruptedHandler(_ => interruptedCount++);

            // Inbound message at t=0
            mockLower.InjectReceivedMessage(peer, new Message("init"));

            // At t=0.6s, outbound send to peer
            scheduler.AdvanceSeconds(0.6f);
            await timeoutLayer.SendMessage(peer, new Message("reply"));

            // Advance to t=1.1s (past initial 1.0s) -> send postponed timeout to t=1.6s
            scheduler.AdvanceSeconds(0.5f);
            Assert.AreEqual(0, interruptedCount);

            // Advance to t=1.7s -> timeout fires
            scheduler.AdvanceSeconds(0.6f);
            Assert.AreEqual(1, interruptedCount);
        }

        [TestMethod]
        public async Task TimeoutLayer_TrackSendsFalse_SendDoesNotPostponeTimeout()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var peer = new ReplicaID(2);

            var timeoutLayer = new TrackMessageReceptionTimeoutLayer(
                localId,
                1.0f,
                scheduler,
                mockLower,
                trackSends: false);

            int interruptedCount = 0;
            timeoutLayer.AddOnConnectionInterruptedHandler(_ => interruptedCount++);

            // Inbound message at t=0
            mockLower.InjectReceivedMessage(peer, new Message("init"));

            // Outbound send at t=0.6s
            scheduler.AdvanceSeconds(0.6f);
            await timeoutLayer.SendMessage(peer, new Message("reply"));

            // Advance to t=1.1s -> should timeout because trackSends is false
            scheduler.AdvanceSeconds(0.5f);
            Assert.AreEqual(1, interruptedCount);
        }

        [TestMethod]
        public async Task HeartbeatLayer_Run_BroadcastsInitialHeartbeatsToAllConnections()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var r2 = new ReplicaID(2);
            var r3 = new ReplicaID(3);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { r2 },
                new HashSet<ReplicaID> { r3 });

            var hbLayer = new HeartbeatLayer(tracker, mockLower, scheduler, 0.5f);
            await hbLayer.Run();

            Assert.AreEqual(2, mockLower.SentMessages.Count);
            foreach (var (_, sent) in mockLower.SentMessages)
            {
                var hb = HeartbeatMessage.FromJson(sent.Payload);
                Assert.IsNotNull(hb);
                Assert.IsTrue(hb.HeartBeat);
                Assert.IsNull(hb.Message);
            }
        }

        [TestMethod]
        public async Task HeartbeatLayer_ScheduledHeartbeat_SendsPeriodicallyUntilStopped()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var r2 = new ReplicaID(2);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { r2 },
                new HashSet<ReplicaID>());

            var hbLayer = new HeartbeatLayer(tracker, mockLower, scheduler, 0.5f);
            await hbLayer.Run();
            Assert.AreEqual(1, mockLower.SentMessages.Count);

            // Advance 0.5s -> should fire next heartbeat
            scheduler.AdvanceSeconds(0.5f);
            Assert.AreEqual(2, mockLower.SentMessages.Count);

            // Advance another 0.5s -> should fire next heartbeat
            scheduler.AdvanceSeconds(0.5f);
            Assert.AreEqual(3, mockLower.SentMessages.Count);

            // Stop layer and advance -> no further heartbeats
            hbLayer.Stop();
            scheduler.AdvanceSeconds(1.0f);
            Assert.AreEqual(3, mockLower.SentMessages.Count);
        }

        [TestMethod]
        public void HeartbeatLayer_IncomingHeartbeat_SuppressedFromUpperLayer()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>(), new HashSet<ReplicaID>());

            var hbLayer = new HeartbeatLayer(tracker, mockLower, scheduler, 0.5f);

            bool delivered = false;
            hbLayer.AddOnMessageReceivedHandler((_, _) => delivered = true);

            var hbMsg = new HeartbeatMessage(null, heartbeat: true);
            mockLower.InjectReceivedMessage(new ReplicaID(2), new Message(hbMsg.ToJson()));

            Assert.IsFalse(delivered, "Heartbeat packets must not be propagated to upper layers");
        }

        [TestMethod]
        public void HeartbeatLayer_IncomingApplicationMessage_UnwrappedAndDelivered()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>(), new HashSet<ReplicaID>());

            var hbLayer = new HeartbeatLayer(tracker, mockLower, scheduler, 0.5f);

            Message? delivered = null;
            ReplicaID? sender = null;
            hbLayer.AddOnMessageReceivedHandler((s, m) =>
            {
                sender = s;
                delivered = m;
            });

            var normalMsg = new HeartbeatMessage(new Message("app data"), heartbeat: false);
            mockLower.InjectReceivedMessage(new ReplicaID(2), new Message(normalMsg.ToJson()));

            Assert.IsNotNull(delivered);
            Assert.AreEqual("app data", delivered.Payload);
            Assert.AreEqual(new ReplicaID(2), sender);
        }

        [TestMethod]
        public async Task HeartbeatLayer_SendMessage_WrapsInHeartbeatMessageWithHeartBeatFalse()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>(), new HashSet<ReplicaID>());

            var hbLayer = new HeartbeatLayer(tracker, mockLower, scheduler, 0.5f);

            await hbLayer.SendMessage(new ReplicaID(3), new Message("payload"));

            Assert.AreEqual(1, mockLower.SentMessages.Count);
            var (target, msg) = mockLower.SentMessages[0];
            Assert.AreEqual(new ReplicaID(3), target);

            var parsed = HeartbeatMessage.FromJson(msg.Payload);
            Assert.IsNotNull(parsed);
            Assert.IsFalse(parsed.HeartBeat);
            Assert.AreEqual("payload", parsed.Message?.Payload);
        }

        [TestMethod]
        public async Task Integrated_HeartbeatKeepsTimeoutLayerAlive_StoppingHeartbeatCausesInterruption()
        {
            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var localId = new ReplicaID(1);
            var remoteId = new ReplicaID(2);

            var remoteTracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { localId },
                new HashSet<ReplicaID>());

            // Remote node has HeartbeatLayer that sends heartbeats every 0.3s
            var remoteHbLayer = new HeartbeatLayer(remoteTracker, mockLower, scheduler, 0.3f);

            // Local node has TrackMessageReceptionTimeoutLayer with 1.0s timeout
            var localTimeoutLayer = new TrackMessageReceptionTimeoutLayer(
                localId,
                1.0f,
                scheduler,
                mockLower,
                trackSends: false);

            bool interrupted = false;
            localTimeoutLayer.AddOnConnectionInterruptedHandler(r =>
            {
                if (r == remoteId) interrupted = true;
            });

            // Wire mockLower: when remote sends heartbeat to localId, deliver it to localTimeoutLayer
            mockLower.OnSendAsync = (recipient, msg) =>
            {
                if (recipient == localId)
                {
                    localTimeoutLayer.OnMessageReceivedHandlerPublic(remoteId, msg);
                }
                return Task.FromResult(true);
            };

            await remoteHbLayer.Run();

            // Run for 3 seconds of simulation time: heartbeats fire every 0.3s, so timeout of 1.0s never fires!
            for (int i = 0; i < 10; i++)
            {
                scheduler.AdvanceSeconds(0.3f);
            }
            Assert.IsFalse(interrupted, "Heartbeats should have kept the timeout layer from timing out");

            // Stop remote heartbeats
            remoteHbLayer.Stop();

            // Advance past 1.0s with no new heartbeats -> timeout must fire!
            scheduler.AdvanceSeconds(1.2f);
            Assert.IsTrue(interrupted, "After stopping heartbeats, timeout must trigger connection interruption");
        }
    }

    // Helper extension to trigger message received on a layer from lower layer
    public static class NetworkLayerTestExtensions
    {
        public static void OnMessageReceivedHandlerPublic(this NetworkLayer layer, ReplicaID sender, Message message)
        {
            var method = layer.GetType().GetMethod("OnLowerLevelMessageReceived",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                ?? layer.GetType().GetMethod("OnLowerLayerMessageReceived",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

            if (method != null)
            {
                method.Invoke(layer, new object[] { sender, message });
            }
        }
    }
}
