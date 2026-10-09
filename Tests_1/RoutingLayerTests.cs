using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.NetworkLayers;
using CRDTApp.NetworkLayers.Composite.Routing;
using CRDTApp.Schedulers;
using Shared;

namespace Tests_1;

[TestClass]
public sealed class RoutingLayerTests
{
    [TestMethod]
    public void FloodingLayerForwardsOnlyOnceAndDecrementsHopCount()
    {
        var source = new ReplicaID(1);
        var relay = new ReplicaID(2);
        var target = new ReplicaID(3);
        var running = new IsRunningTracker(true);
        var wire = new RecordingNetworkLayer(running);
        var tracker = new ActiveConnectionTracker(
            new HashSet<ReplicaID> { source, target },
            new HashSet<ReplicaID>(),
            relay);
        var flood = new FloodingRedirectionLayer(relay, running, new RecordingScheduler(), tracker, wire, 4, 60, "flood");
        var inner = new Message("app", "payload");
        var frame = new FloodMessagePayload(target, source, 8, inner, 4);

        flood.OnLowerLayerMessageReceived(source, new Message("flood", frame.Encode()));
        flood.OnLowerLayerMessageReceived(source, new Message("flood", frame.Encode()));

        var sent = wire.SentMessages.Single();
        Assert.AreEqual(target, sent.Recipient);
        Assert.AreEqual("flood", sent.Message.ProtocolTag);

        var forwarded = FloodMessagePayload.Decode(sent.Message.Payload)!;
        Assert.AreEqual(3, forwarded.NumberOfHops);
        Assert.AreEqual(target, forwarded.IntendedTarget);
        Assert.AreEqual("app", Message.Decode(forwarded.Message!.Encode())!.ProtocolTag);
    }

    [TestMethod]
    public async Task FloodingLayerEncapsulatesOutgoingMessagesForDirectTarget()
    {
        var source = new ReplicaID(1);
        var target = new ReplicaID(2);
        var running = new IsRunningTracker(true);
        var wire = new RecordingNetworkLayer(running);
        var tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>(), new HashSet<ReplicaID>(), source);
        var flood = new FloodingRedirectionLayer(source, running, new RecordingScheduler(), tracker, wire, 3, 30, "flood");
        var original = new Message("app", "payload");

        Assert.IsTrue(await flood.SendMessage(target, original));

        var sent = wire.SentMessages.Single();
        Assert.AreEqual(target, sent.Recipient);
        var envelope = FloodMessagePayload.Decode(sent.Message.Payload)!;
        Assert.AreEqual(source, envelope.OriginalSender);
        Assert.AreEqual(target, envelope.IntendedTarget);
        Assert.AreEqual(3, envelope.NumberOfHops);
        Assert.AreEqual(original.ProtocolTag, envelope.Message!.ProtocolTag);
        Assert.AreEqual(original.Payload, envelope.Message.Payload);
    }

    [TestMethod]
    public void FloodingLayerDeliversPayloadAtIntendedTarget()
    {
        var relay = new ReplicaID(2);
        var source = new ReplicaID(1);
        var target = relay;
        var running = new IsRunningTracker(true);
        var wire = new RecordingNetworkLayer(running);
        var tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>(), new HashSet<ReplicaID>(), relay);
        var flood = new FloodingRedirectionLayer(relay, running, new RecordingScheduler(), tracker, wire, 3, 30, "flood");
        var original = new Message("app", "payload");
        ReplicaID deliveredFrom = default;
        Message? delivered = null;
        flood.AddOnMessageReceivedHandler((from, message) =>
        {
            deliveredFrom = from;
            delivered = message;
        });

        flood.OnLowerLayerMessageReceived(
            source,
            new Message("flood", new FloodMessagePayload(target, source, 4, original, 0).Encode()));

        Assert.AreEqual(source, deliveredFrom);
        Assert.IsNotNull(delivered);
        Assert.AreEqual(original.ProtocolTag, delivered!.ProtocolTag);
        Assert.AreEqual(original.Payload, delivered.Payload);
        Assert.AreEqual(0, wire.SentMessages.Count);
    }

    [TestMethod]
    public void FloodingLayerIgnoresMessagesWithDifferentLayerTag()
    {
        var id = new ReplicaID(1);
        var running = new IsRunningTracker(true);
        var wire = new RecordingNetworkLayer(running);
        var tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>(), new HashSet<ReplicaID>(), id);
        var flood = new FloodingRedirectionLayer(id, running, new RecordingScheduler(), tracker, wire, 3, 30, "flood");
        var deliveredCount = 0;
        flood.AddOnMessageReceivedHandler((_, _) => deliveredCount++);
        var frame = new FloodMessagePayload(id, new ReplicaID(2), 1, new Message("app", "x"), 2);

        flood.OnLowerLayerMessageReceived(new ReplicaID(2), new Message("wrong", frame.Encode()));

        Assert.AreEqual(0, deliveredCount);
    }

    [TestMethod]
    public async Task DistanceVectorLayerEncapsulatesTargetAndDeliversAtDestination()
    {
        var source = new ReplicaID(1);
        var destination = new ReplicaID(2);
        var running = new IsRunningTracker(true);
        var sourceWire = new RecordingNetworkLayer(running);
        var sourceTracker = new ActiveConnectionTracker(
            new HashSet<ReplicaID> { destination },
            new HashSet<ReplicaID>(),
            source);
        var sourceLayer = new DistanceVectorRoutingLayer(
            source,
            running,
            sourceTracker,
            sourceWire,
            new RecordingScheduler(),
            8,
            tag: "dv");
        var original = new Message("app", "operation");

        Assert.IsTrue(await sourceLayer.SendMessage(destination, original));

        var sent = sourceWire.SentMessages.Single();
        Assert.AreEqual(destination, sent.Recipient);
        Assert.AreEqual("dv", sent.Message.ProtocolTag);
        var envelope = DistanceVectorMessagePayload.Decode(sent.Message.Payload)!;
        Assert.AreEqual(source, envelope.OriginalSender);
        Assert.AreEqual(destination, envelope.IntendedTarget);
        Assert.IsFalse(envelope.IsProtocolMessage);
        Assert.AreEqual("app", Message.Decode(envelope.Message)!.ProtocolTag);

        var receiverWire = new RecordingNetworkLayer(running);
        var receiverTracker = new ActiveConnectionTracker(
            new HashSet<ReplicaID> { source },
            new HashSet<ReplicaID>(),
            destination);
        var receiverLayer = new DistanceVectorRoutingLayer(
            destination,
            running,
            receiverTracker,
            receiverWire,
            new RecordingScheduler(),
            8,
            tag: "dv");
        Message? delivered = null;
        ReplicaID deliveredFrom = default;
        receiverLayer.AddOnMessageReceivedHandler((from, message) =>
        {
            deliveredFrom = from;
            delivered = message;
        });

        receiverLayer.OnLowerLayerMessageReceived(source, sent.Message);

        Assert.AreEqual(source, deliveredFrom);
        Assert.IsNotNull(delivered);
        Assert.AreEqual(original.ProtocolTag, delivered!.ProtocolTag);
        Assert.AreEqual(original.Payload, delivered.Payload);
    }

    [TestMethod]
    public async Task DistanceVectorLayerUsesNeighborLearnedFromRoutingUpdate()
    {
        var source = new ReplicaID(1);
        var neighbor = new ReplicaID(2);
        var destination = new ReplicaID(3);
        var running = new IsRunningTracker(true);
        var wire = new RecordingNetworkLayer(running);
        var tracker = new ActiveConnectionTracker(
            new HashSet<ReplicaID> { neighbor },
            new HashSet<ReplicaID>(),
            source);
        var layer = new DistanceVectorRoutingLayer(
            source,
            running,
            tracker,
            wire,
            new RecordingScheduler(),
            8,
            tag: "dv");

        var update = new DistanceVectorMessagePayload(
            source,
            neighbor,
            "{\"Distances\":{\"3\":1.0}}",
            true,
            1);
        layer.OnLowerLayerMessageReceived(neighbor, new Message("dv", update.Encode()));

        var original = new Message("app", "operation");
        Assert.IsTrue(await layer.SendMessage(destination, original));

        var sent = wire.SentMessages.Single();
        Assert.AreEqual(neighbor, sent.Recipient);
        var envelope = DistanceVectorMessagePayload.Decode(sent.Message.Payload)!;
        Assert.AreEqual(destination, envelope.IntendedTarget);
    }

    [TestMethod]
    public void DistanceVectorLayerDropsDataWhenTtlExpiresBeforeDestination()
    {
        var source = new ReplicaID(1);
        var relay = new ReplicaID(2);
        var destination = new ReplicaID(3);
        var running = new IsRunningTracker(true);
        var wire = new RecordingNetworkLayer(running);
        var tracker = new ActiveConnectionTracker(new HashSet<ReplicaID> { source }, new HashSet<ReplicaID>(), relay);
        var layer = new DistanceVectorRoutingLayer(relay, running, tracker, wire, new RecordingScheduler(), 4, tag: "dv");
        var frame = new DistanceVectorMessagePayload(destination, source, new Message("app", "data"), false, 1);

        layer.OnLowerLayerMessageReceived(source, new Message("dv", frame.Encode()));

        Assert.AreEqual(0, wire.SentMessages.Count);
    }

    [TestMethod]
    public void DistanceVectorLayerIgnoresMessagesWithWrongTag()
    {
        var id = new ReplicaID(1);
        var running = new IsRunningTracker(true);
        var wire = new RecordingNetworkLayer(running);
        var tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>(), new HashSet<ReplicaID>(), id);
        var layer = new DistanceVectorRoutingLayer(id, running, tracker, wire, new RecordingScheduler(), 4, tag: "dv");
        var deliveredCount = 0;
        layer.AddOnMessageReceivedHandler((_, _) => deliveredCount++);

        layer.OnLowerLayerMessageReceived(
            new ReplicaID(2),
            new Message("wrong", new DistanceVectorMessagePayload(id, new ReplicaID(2), "body", false, 4).Encode()));

        Assert.AreEqual(0, deliveredCount);
    }

    [TestMethod]
    public async Task LinkStateLayerUsesLearnedMultiHopNextHop()
    {
        var source = new ReplicaID(1);
        var relay = new ReplicaID(2);
        var destination = new ReplicaID(3);
        var running = new IsRunningTracker(true);
        var wire = new RecordingNetworkLayer(running);
        var connections = new ActiveConnectionTracker(
            new HashSet<ReplicaID> { relay },
            new HashSet<ReplicaID>(),
            source);
        var linkState = new LinkStateRoutingLayer(source, running, wire, connections, "ls");

        var relayLsa = new LSA(relay, 1, new List<(ReplicaID, float)>
        {
            (source, 1),
            (destination, 1)
        });
        linkState.OnLowerLevelMessageReceived(
            relay,
            new Message("ls", new LinkStateMessagePayload(relay, relay, relayLsa.Encode(), true).Encode()));

        var destinationLsa = new LSA(destination, 1, new List<(ReplicaID, float)>
        {
            (relay, 1)
        });
        linkState.OnLowerLevelMessageReceived(
            relay,
            new Message("ls", new LinkStateMessagePayload(destination, relay, destinationLsa.Encode(), true).Encode()));

        var original = new Message("app", "operation");
        Assert.IsTrue(await linkState.SendMessage(destination, original));

        var sent = wire.SentMessages.Last();
        Assert.AreEqual(relay, sent.Recipient);
        Assert.AreEqual("ls", sent.Message.ProtocolTag);
        var envelope = LinkStateMessagePayload.Decode(sent.Message.Payload)!;
        Assert.AreEqual(source, envelope.OriginalSender);
        Assert.AreEqual(destination, envelope.IntendedTarget);
        Assert.IsFalse(envelope.IsProtocolMessage);
        Assert.AreEqual("app", Message.Decode(envelope.Message)!.ProtocolTag);
    }

    [TestMethod]
    public async Task LinkStateLayerUsesDirectDestinationWhenNoRouteIsKnown()
    {
        var source = new ReplicaID(1);
        var destination = new ReplicaID(2);
        var running = new IsRunningTracker(true);
        var wire = new RecordingNetworkLayer(running);
        var tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>(), new HashSet<ReplicaID>(), source);
        var layer = new LinkStateRoutingLayer(source, running, wire, tracker, "ls");

        Assert.IsTrue(await layer.SendMessage(destination, new Message("app", "body")));

        Assert.AreEqual(destination, wire.SentMessages.Single().Recipient);
    }

    [TestMethod]
    public void LinkStateLayerDeliversDataMessageAtItsDestination()
    {
        var relay = new ReplicaID(2);
        var source = new ReplicaID(1);
        var running = new IsRunningTracker(true);
        var wire = new RecordingNetworkLayer(running);
        var tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>(), new HashSet<ReplicaID>(), relay);
        var layer = new LinkStateRoutingLayer(relay, running, wire, tracker, "ls");
        var original = new Message("app", "body");
        Message? delivered = null;
        ReplicaID deliveredFrom = default;
        layer.AddOnMessageReceivedHandler((from, message) =>
        {
            deliveredFrom = from;
            delivered = message;
        });

        layer.OnLowerLevelMessageReceived(
            source,
            new Message("ls", new LinkStateMessagePayload(relay, source, original).Encode()));

        Assert.AreEqual(source, deliveredFrom);
        Assert.IsNotNull(delivered);
        Assert.AreEqual(original.ProtocolTag, delivered!.ProtocolTag);
        Assert.AreEqual(original.Payload, delivered.Payload);
    }

    [TestMethod]
    public async Task RoutingLayersRejectSendsWhenStopped()
    {
        var id = new ReplicaID(1);
        var target = new ReplicaID(2);
        var running = new IsRunningTracker(false);
        var wire = new RecordingNetworkLayer(running);
        var connections = new ActiveConnectionTracker(new HashSet<ReplicaID>(), new HashSet<ReplicaID>(), id);
        var flood = new FloodingRedirectionLayer(id, running, new RecordingScheduler(), connections, wire, 2, 10, "flood");
        var distanceVector = new DistanceVectorRoutingLayer(id, running, connections, wire, new RecordingScheduler(), 2, tag: "dv");
        var linkState = new LinkStateRoutingLayer(id, running, wire, connections, "ls");
        var message = new Message("app", "body");

        Assert.IsFalse(await flood.SendMessage(target, message));
        Assert.IsFalse(await distanceVector.SendMessage(target, message));
        Assert.IsFalse(await linkState.SendMessage(target, message));
        Assert.AreEqual(0, wire.SentMessages.Count);
    }

    private sealed class RecordingNetworkLayer(IsRunningTracker runningTracker)
        : NetworkLayer("wire", runningTracker)
    {
        public List<(ReplicaID Recipient, Message Message)> SentMessages { get; } = new();

        public override Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            SentMessages.Add((recipient, message));
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingScheduler : IScheduler
    {
        public void ExecuteAfter(float time, Action action)
        {
        }

        public Task<(bool, T)> ExecuteTaskOrTimeout<T>(
            Func<Task<T>> operation,
            float time,
            T defaultValue) =>
            operation().ContinueWith(task =>
                task.IsCompletedSuccessfully ? (true, task.Result) : (false, defaultValue));

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
