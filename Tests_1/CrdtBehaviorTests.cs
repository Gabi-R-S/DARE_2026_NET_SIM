using CRDTApp.Messages;
using CRDTs;
using CRDTs.Basic;
using Shared;

namespace Tests_1;

[TestClass]
public sealed class CrdtBehaviorTests
{
    [TestMethod]
    public void GCounterMergeCombinesReplicaCountsAndIsIdempotent()
    {
        var replicaA = new ReplicaID(1);
        var replicaB = new ReplicaID(2);
        var a = new GCounter();
        var b = new GCounter();

        a.Merge(a.CreateAddOperation(replicaA));
        a.Merge(a.CreateAddOperation(replicaA));
        b.Merge(b.CreateAddOperation(replicaB));

        a.Merge(b);
        b.Merge(a);
        a.Merge(b);

        Assert.AreEqual(3, a.Value);
        Assert.AreEqual(a.Value, b.Value);
    }

    [TestMethod]
    public void PNCounterMergeCombinesComponentsAndIsIdempotent()
    {
        var replicaA = new ReplicaID(1);
        var replicaB = new ReplicaID(2);
        var a = new PNCounter(
            new Dictionary<ReplicaID, int> { [replicaA] = 3 },
            new Dictionary<ReplicaID, int> { [replicaA] = 1 });
        var b = new PNCounter(
            new Dictionary<ReplicaID, int> { [replicaB] = 4 },
            new Dictionary<ReplicaID, int> { [replicaB] = 2 });

        a.Merge(b);
        b.Merge(a);
        a.Merge(b);

        Assert.AreEqual(4, a.Value);
        Assert.AreEqual(a.Value, b.Value);
    }

    [TestMethod]
    public void PNCounterRepeatedIncrementOperationsAccumulateForSameReplica()
    {
        var replicaA = new ReplicaID(1);
        var replicaB = new ReplicaID(2);
        var a = new PNCounter();
        var b = new PNCounter();

        a.Merge(a.CreateIncrementOperation(replicaA));
        a.Merge(a.CreateIncrementOperation(replicaA));
        b.Merge(b.CreateIncrementOperation(replicaB));
        b.Merge(b.CreateDecrementOperation(replicaB));

        a.Merge(b);
        b.Merge(a);

        Assert.AreEqual(2, a.Value);
        Assert.AreEqual(a.Value, b.Value);
    }

    [TestMethod]
    public void ORSetMergePreservesConcurrentAddWhenObservedAddIsRemoved()
    {
        var replicaA = new ReplicaID(1);
        var replicaB = new ReplicaID(2);
        var state = new ORSet<string>();
        var concurrentAdd = new ORSet<string>();

        var firstAdd = state.CreateAddOperation(replicaA, "item");
        state.Merge(firstAdd);
        var removeObservedAdd = state.CreateRemoveOperation(replicaA, "item");
        concurrentAdd.Merge(concurrentAdd.CreateAddOperation(replicaB, "item"));

        state.Merge(removeObservedAdd);
        state.Merge(concurrentAdd);

        CollectionAssert.AreEqual(new[] { "item" }, state.GetValues());
    }

    [TestMethod]
    public void ORSetJsonRoundTripPreservesVisibleValues()
    {
        var replica = new ReplicaID(7);
        var set = new ORSet<int>();
        set.Merge(set.CreateAddOperation(replica, 42));
        set.Merge(set.CreateAddOperation(replica, 99));

        var decoded = ORSet<int>.FromJSON(set.ToJson(value => value.ToString()), int.Parse);

        CollectionAssert.AreEquivalent(new[] { 42, 99 }, decoded.GetValues());
    }

    [TestMethod]
    public void LWWRegisterChoosesNewerTimestampAndBreaksTimestampTiesDeterministically()
    {
        var timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var register = new LWWRegister<string>(timestamp, "alpha");

        register.Merge(register.CreateWriteOperation(timestamp, "beta"));
        Assert.AreEqual("beta", register.Value);

        register.Merge(register.CreateWriteOperation(timestamp.AddSeconds(-1), "zeta"));
        Assert.AreEqual("beta", register.Value);
    }

    [TestMethod]
    public void CausalHistoryTrackerBuffersMessagesUntilTheirDependenciesArrive()
    {
        var sender = new CausalHistoryTracker(new ReplicaID(1), tag: "app");
        var receiver = new CausalHistoryTracker(new ReplicaID(2), tag: "app");
        var issued = new List<string?>();
        receiver.AddMessageIssuedHandler((_, message, _) => issued.Add(message));

        var first = sender.TagMessage("first");
        var second = sender.TagMessage("second");

        receiver.OnMessageReceived(new ReplicaID(1), new Message("app", second.Encode()));
        CollectionAssert.AreEqual(Array.Empty<string>(), issued.Cast<string>().ToArray());

        receiver.OnMessageReceived(new ReplicaID(1), new Message("app", first.Encode()));

        CollectionAssert.AreEqual(new[] { "first", "second" }, issued);
    }
}
