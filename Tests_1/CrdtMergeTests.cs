using CRDTs;
using CRDTs.Basic;
using Shared;

namespace Tests_1;

[TestClass]
public sealed class CrdtMergeTests
{
    [TestMethod]
    public void GCounterStartsAtZero()
    {
        Assert.AreEqual(0, new GCounter().Value);
    }

    [TestMethod]
    public void GCounterMergeUsesPerReplicaMaximum()
    {
        var replica = new ReplicaID(1);
        var left = new GCounter(new Dictionary<ReplicaID, int> { [replica] = 5 });
        var right = new GCounter(new Dictionary<ReplicaID, int> { [replica] = 3 });

        left.Merge(right);

        Assert.AreEqual(5, left.Value);
    }

    [TestMethod]
    public void GCounterMergeIsCommutativeForIndependentReplicas()
    {
        var aId = new ReplicaID(1);
        var bId = new ReplicaID(2);
        var a = new GCounter(new Dictionary<ReplicaID, int> { [aId] = 4 });
        var b = new GCounter(new Dictionary<ReplicaID, int> { [bId] = 7 });
        var aThenB = new GCounter(new Dictionary<ReplicaID, int> { [aId] = 4 });
        var bThenA = new GCounter(new Dictionary<ReplicaID, int> { [bId] = 7 });

        aThenB.Merge(b);
        bThenA.Merge(a);

        Assert.AreEqual(11, aThenB.Value);
        Assert.AreEqual(aThenB.Value, bThenA.Value);
    }

    [TestMethod]
    public void GCounterCreateAddOperationDoesNotMutateSource()
    {
        var counter = new GCounter();
        var operation = counter.CreateAddOperation(new ReplicaID(4));

        Assert.AreEqual(0, counter.Value);
        Assert.AreEqual(1, operation.Value);
    }

    [TestMethod]
    public void PNCounterStartsAtZero()
    {
        Assert.AreEqual(0, new PNCounter().Value);
    }

    [TestMethod]
    public void PNCounterIncrementAndDecrementOperationsHaveExpectedSingleStepValues()
    {
        var id = new ReplicaID(3);
        var counter = new PNCounter();

        counter.Merge(counter.CreateIncrementOperation(id));
        Assert.AreEqual(1, counter.Value);

        counter.Merge(counter.CreateDecrementOperation(id));
        Assert.AreEqual(0, counter.Value);
    }

    [TestMethod]
    public void PNCounterMergeIsIdempotentWhenMergingExistingComponents()
    {
        var id = new ReplicaID(3);
        var left = new PNCounter(
            new Dictionary<ReplicaID, int> { [id] = 4 },
            new Dictionary<ReplicaID, int> { [id] = 2 });
        var right = new PNCounter(
            new Dictionary<ReplicaID, int> { [id] = 3 },
            new Dictionary<ReplicaID, int> { [id] = 1 });

        left.Merge(right);
        left.Merge(right);

        Assert.AreEqual(2, left.Value);
    }

    [TestMethod]
    public void ORSetStartsEmpty()
    {
        Assert.AreEqual(0, new ORSet<int>().GetValues().Count);
    }

    [TestMethod]
    public void ORSetRemoveOperationRemovesObservedAdd()
    {
        var id = new ReplicaID(1);
        var state = new ORSet<string>();
        state.Merge(state.CreateAddOperation(id, "value"));
        var remove = state.CreateRemoveOperation(id, "value");

        state.Merge(remove);

        Assert.AreEqual(0, state.GetValues().Count);
    }

    [TestMethod]
    public void ORSetConcurrentSameValueAddsRemainVisibleAfterRemovingOneObservedTag()
    {
        var firstReplica = new ReplicaID(1);
        var secondReplica = new ReplicaID(2);
        var state = new ORSet<string>();
        var otherReplicaAdd = new ORSet<string>();

        state.Merge(state.CreateAddOperation(firstReplica, "shared"));
        var remove = state.CreateRemoveOperation(firstReplica, "shared");
        otherReplicaAdd.Merge(otherReplicaAdd.CreateAddOperation(secondReplica, "shared"));
        state.Merge(remove);
        state.Merge(otherReplicaAdd);

        CollectionAssert.AreEqual(new[] { "shared" }, state.GetValues());
    }

    [TestMethod]
    public void ORSetMergeIsIdempotent()
    {
        var id = new ReplicaID(5);
        var state = new ORSet<int>();
        var operation = state.CreateAddOperation(id, 10);

        state.Merge(operation);
        state.Merge(operation);

        CollectionAssert.AreEqual(new[] { 10 }, state.GetValues());
    }

    [TestMethod]
    public void ORSetMergeIsCommutativeForIndependentAdds()
    {
        var leftOperation = new ORSet<string>().CreateAddOperation(new ReplicaID(1), "left");
        var rightOperation = new ORSet<string>().CreateAddOperation(new ReplicaID(2), "right");
        var leftThenRight = new ORSet<string>();
        var rightThenLeft = new ORSet<string>();

        leftThenRight.Merge(leftOperation);
        leftThenRight.Merge(rightOperation);
        rightThenLeft.Merge(rightOperation);
        rightThenLeft.Merge(leftOperation);

        CollectionAssert.AreEquivalent(leftThenRight.GetValues(), rightThenLeft.GetValues());
    }

    [TestMethod]
    public void ORSetJsonRoundTripPreservesRemovals()
    {
        var id = new ReplicaID(6);
        var state = new ORSet<int>();
        state.Merge(state.CreateAddOperation(id, 23));
        state.Merge(state.CreateRemoveOperation(id, 23));

        var decoded = ORSet<int>.FromJSON(state.ToJson(value => value.ToString()), int.Parse);

        Assert.AreEqual(0, decoded.GetValues().Count);
    }

    [TestMethod]
    public void LWWRegisterIgnoresOlderWrite()
    {
        var later = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);
        var register = new LWWRegister<int>(later, 2);

        register.Merge(register.CreateWriteOperation(later.AddTicks(-1), 1));

        Assert.AreEqual(2, register.Value);
    }

    [TestMethod]
    public void LWWRegisterRetainsValueWhenTimestampAndValueTie()
    {
        var timestamp = DateTime.UnixEpoch;
        var register = new LWWRegister<string>(timestamp, "same");

        register.Merge(register.CreateWriteOperation(timestamp, "same"));

        Assert.AreEqual("same", register.Value);
    }

    [TestMethod]
    public void LWWRegisterMergeIsIndependentOfWriteOrderWhenTimestampsDiffer()
    {
        var timestamp = DateTime.UnixEpoch;
        var older = new LWWRegister<int>(timestamp, 1);
        var newer = new LWWRegister<int>(timestamp.AddSeconds(1), 2);
        var left = new LWWRegister<int>(timestamp, 1);
        var right = new LWWRegister<int>(timestamp.AddSeconds(1), 2);

        left.Merge(newer);
        right.Merge(older);

        Assert.AreEqual(2, left.Value);
        Assert.AreEqual(left.Value, right.Value);
    }

    [TestMethod]
    public void VersionVectorIncrementAndCloneAreIndependent()
    {
        var id = new ReplicaID(1);
        var original = new VersionVector();
        original.Increment(id);
        var clone = original.Clone();
        clone.Increment(id);

        Assert.AreEqual(VersionVector.Comparison.ThisHappenedBeforeOther, original.Compare(clone));
    }

    [TestMethod]
    public void VersionVectorMergeTakesMaximumForEachReplica()
    {
        var a = new ReplicaID(1);
        var b = new ReplicaID(2);
        var left = new VersionVector(new Dictionary<ReplicaID, ulong> { [a] = 2, [b] = 1 });
        var right = new VersionVector(new Dictionary<ReplicaID, ulong> { [a] = 1, [b] = 4 });

        left.Merge(right);

        Assert.AreEqual(VersionVector.Comparison.ThisHappenedAfterOther, left.Compare(right));
    }

    [TestMethod]
    public void VersionVectorDetectsConcurrentHistories()
    {
        var left = new VersionVector();
        var right = new VersionVector();
        left.Increment(new ReplicaID(1));
        right.Increment(new ReplicaID(2));

        Assert.AreEqual(VersionVector.Comparison.Concurrent, left.Compare(right));
    }

    [TestMethod]
    public void VersionVectorJsonRoundTripPreservesReplicaVersions()
    {
        var original = new VersionVector(new Dictionary<ReplicaID, ulong>
        {
            [new ReplicaID(1)] = 3,
            [new ReplicaID(9)] = 7
        });
        var decoded = new VersionVector { JSON = original.JSON };

        Assert.AreEqual(VersionVector.Comparison.Equal, original.Compare(decoded));
    }
}
