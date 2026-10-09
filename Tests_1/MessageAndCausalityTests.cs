using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTs;
using Shared;

namespace Tests_1;

[TestClass]
public sealed class MessageAndCausalityTests
{
    [TestMethod]
    public void MessageSerializationPreservesProtocolTagAndPayload()
    {
        var original = new Message("application", "{\"value\":42}");

        var decoded = Message.Decode(original.Encode());

        Assert.IsNotNull(decoded);
        Assert.AreEqual(original.ProtocolTag, decoded.ProtocolTag);
        Assert.AreEqual(original.Payload, decoded.Payload);
    }

    [TestMethod]
    public void AckPayloadRoundTripPreservesNestedMessage()
    {
        var original = new AckMessagePayload<int>(17, new Message("app", "body"));

        var decoded = AckMessagePayload<int>.Decode(original.Encode());

        Assert.IsNotNull(decoded);
        Assert.AreEqual(17, decoded.Tag);
        Assert.IsFalse(decoded.IsAck);
        var nested = Message.Decode(decoded.Message);
        Assert.IsNotNull(nested);
        Assert.AreEqual("app", nested.ProtocolTag);
        Assert.AreEqual("body", nested.Payload);
    }

    [TestMethod]
    public void AckPayloadCanRepresentAcknowledgementWithoutMessageBody()
    {
        var original = new AckMessagePayload<string>("ack-tag", "", true);

        var decoded = AckMessagePayload<string>.Decode(original.Encode());

        Assert.IsNotNull(decoded);
        Assert.AreEqual("ack-tag", decoded.Tag);
        Assert.IsTrue(decoded.IsAck);
        Assert.AreEqual(string.Empty, decoded.Message);
    }

    [TestMethod]
    public void CausalHistoryTrackerIgnoresMessagesWithDifferentProtocolTag()
    {
        var tracker = new CausalHistoryTracker(new ReplicaID(1), tag: "causal");
        var issuedCount = 0;
        tracker.AddMessageIssuedHandler((_, _, _) => issuedCount++);

        tracker.OnMessageReceived(
            new ReplicaID(2),
            new Message("wrong-tag", new CausalHistoryTracker(new ReplicaID(2)).TagMessage("ignored").Encode()));

        Assert.AreEqual(0, issuedCount);
    }

    [TestMethod]
    public void CausalHistoryTrackerIssuesFirstMessageImmediately()
    {
        var sender = new CausalHistoryTracker(new ReplicaID(1), tag: "app");
        var receiver = new CausalHistoryTracker(new ReplicaID(2), tag: "app");
        string? issued = null;
        receiver.AddMessageIssuedHandler((_, message, _) => issued = message);

        receiver.OnMessageReceived(
            new ReplicaID(1),
            new Message("app", sender.TagMessage("first").Encode()));

        Assert.AreEqual("first", issued);
    }

    [TestMethod]
    public void CausalHistoryTrackerDoesNotIssueDuplicateAlreadyAppliedMessage()
    {
        var sender = new CausalHistoryTracker(new ReplicaID(1), tag: "app");
        var receiver = new CausalHistoryTracker(new ReplicaID(2), tag: "app");
        var issued = new List<string?>();
        var message = new Message("app", sender.TagMessage("one").Encode());
        receiver.AddMessageIssuedHandler((_, value, _) => issued.Add(value));

        receiver.OnMessageReceived(new ReplicaID(1), message);
        receiver.OnMessageReceived(new ReplicaID(1), message);

        CollectionAssert.AreEqual(new[] { "one" }, issued);
    }

    [TestMethod]
    public void CausalHistoryTrackerDoesNotIssueMessageWithMissingPriorVersion()
    {
        var sender = new CausalHistoryTracker(new ReplicaID(1), tag: "app");
        var receiver = new CausalHistoryTracker(new ReplicaID(2), tag: "app");
        var issued = new List<string?>();
        sender.TagMessage("first");
        var second = sender.TagMessage("second");
        receiver.AddMessageIssuedHandler((_, message, _) => issued.Add(message));

        receiver.OnMessageReceived(new ReplicaID(1), new Message("app", second.Encode()));

        Assert.AreEqual(0, issued.Count);
    }

    [TestMethod]
    public void CausalHistoryTrackerReleasesBufferedMessageWhenDependencyArrives()
    {
        var sender = new CausalHistoryTracker(new ReplicaID(1), tag: "app");
        var receiver = new CausalHistoryTracker(new ReplicaID(2), tag: "app");
        var issued = new List<string?>();
        var first = sender.TagMessage("first");
        var second = sender.TagMessage("second");
        receiver.AddMessageIssuedHandler((_, message, _) => issued.Add(message));

        receiver.OnMessageReceived(new ReplicaID(1), new Message("app", second.Encode()));
        receiver.OnMessageReceived(new ReplicaID(1), new Message("app", first.Encode()));

        CollectionAssert.AreEqual(new[] { "first", "second" }, issued);
    }
}
