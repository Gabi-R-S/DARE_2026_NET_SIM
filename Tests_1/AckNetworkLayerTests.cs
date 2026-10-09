using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.NetworkLayers;
using CRDTApp.NetworkLayers.Composite;
using CRDTApp.Schedulers;
using Shared;

namespace Tests_1;

[TestClass]
public sealed class AckNetworkLayerTests
{
    [TestMethod]
    public async Task SendMessageReturnsTrueWhenMatchingAckArrives()
    {
        var local = new ReplicaID(1);
        var peer = new ReplicaID(2);
        var running = new IsRunningTracker(true);
        EnsureAckNetworkLayer<int>? ackLayer = null;
        var wire = new CallbackNetworkLayer(running, (recipient, message) =>
        {
            Assert.AreEqual(peer, recipient);
            var sent = AckMessagePayload<int>.Decode(message.Payload)!;
            ackLayer!.OnLowerLayerMessageReceived(
                peer,
                new Message("ack", new AckMessagePayload<int>(sent.Tag, string.Empty, true).Encode()));
            return true;
        });
        ackLayer = new EnsureAckNetworkLayer<int>(
            local,
            running,
            wire,
            new ImmediateScheduler(),
            _ => 123,
            tag: "ack");

        var result = await ackLayer.SendMessage(peer, new Message("app", "payload"));

        Assert.IsTrue(result);
        Assert.AreEqual(1, wire.SendCount);
    }

    [TestMethod]
    public void ReceivingDataSendsAckAndDeliversNestedMessage()
    {
        var local = new ReplicaID(1);
        var peer = new ReplicaID(2);
        var running = new IsRunningTracker(true);
        var wire = new RecordingNetworkLayer(running);
        var ackLayer = new EnsureAckNetworkLayer<int>(
            local,
            running,
            wire,
            new ImmediateScheduler(),
            _ => 1,
            tag: "ack");
        var original = new Message("app", "value");
        Message? delivered = null;
        ReplicaID deliveredFrom = default;
        ackLayer.AddOnMessageReceivedHandler((from, message) =>
        {
            deliveredFrom = from;
            delivered = message;
        });

        ackLayer.OnLowerLayerMessageReceived(
            peer,
            new Message("ack", new AckMessagePayload<int>(77, original).Encode()));

        var acknowledgement = AckMessagePayload<int>.Decode(wire.SentMessages.Single().Payload)!;
        Assert.IsTrue(acknowledgement.IsAck);
        Assert.AreEqual(77, acknowledgement.Tag);
        Assert.AreEqual(peer, deliveredFrom);
        Assert.IsNotNull(delivered);
        Assert.AreEqual(original.ProtocolTag, delivered!.ProtocolTag);
        Assert.AreEqual(original.Payload, delivered.Payload);
    }

    [TestMethod]
    public void ReceivingAckDoesNotDeliverAnApplicationMessage()
    {
        var local = new ReplicaID(1);
        var peer = new ReplicaID(2);
        var running = new IsRunningTracker(true);
        var wire = new RecordingNetworkLayer(running);
        var ackLayer = new EnsureAckNetworkLayer<int>(
            local,
            running,
            wire,
            new ImmediateScheduler(),
            _ => 1,
            tag: "ack");
        var deliveredCount = 0;
        ackLayer.AddOnMessageReceivedHandler((_, _) => deliveredCount++);

        ackLayer.OnLowerLayerMessageReceived(
            peer,
            new Message("ack", new AckMessagePayload<int>(77, string.Empty, true).Encode()));

        Assert.AreEqual(0, deliveredCount);
        Assert.AreEqual(0, wire.SentMessages.Count);
    }

    [TestMethod]
    public void AckLayerIgnoresMessagesWithWrongProtocolTag()
    {
        var local = new ReplicaID(1);
        var peer = new ReplicaID(2);
        var running = new IsRunningTracker(true);
        var wire = new RecordingNetworkLayer(running);
        var ackLayer = new EnsureAckNetworkLayer<int>(
            local,
            running,
            wire,
            new ImmediateScheduler(),
            _ => 1,
            tag: "ack");

        ackLayer.OnLowerLayerMessageReceived(
            peer,
            new Message("other", new AckMessagePayload<int>(77, new Message("app", "body")).Encode()));

        Assert.AreEqual(0, wire.SentMessages.Count);
    }

    private sealed class RecordingNetworkLayer(IsRunningTracker runningTracker)
        : NetworkLayer("wire", runningTracker)
    {
        public List<Message> SentMessages { get; } = new();

        public override Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            SentMessages.Add(message);
            return Task.FromResult(true);
        }
    }

    private sealed class CallbackNetworkLayer(
        IsRunningTracker runningTracker,
        Func<ReplicaID, Message, bool> callback)
        : NetworkLayer("wire", runningTracker)
    {
        public int SendCount { get; private set; }

        public override Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            SendCount++;
            return Task.FromResult(callback(recipient, message));
        }
    }

    private sealed class ImmediateScheduler : IScheduler
    {
        public void ExecuteAfter(float time, Action action)
        {
        }

        public async Task<(bool, T)> ExecuteTaskOrTimeout<T>(
            Func<Task<T>> operation,
            float time,
            T defaultValue) => (true, await operation());

        public async Task<bool> ExecuteTaskOrTimeout(Func<Task> operation, float time)
        {
            await operation();
            return true;
        }

        public async Task<bool> ExecuteTaskOrTimeout(Func<Task> operation, float time, Action timeoutAction)
        {
            await operation();
            return true;
        }

        public DateTime GetCurrentTime() => DateTime.UnixEpoch;
    }
}
