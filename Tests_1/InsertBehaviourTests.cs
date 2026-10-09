using CRDTApp.Behaviours;
using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using CRDTApp.Schedulers;
using CRDTs;
using CRDTs.Basic;
using Shared;

namespace Tests_1;

[TestClass]
public sealed class InsertBehaviourTests
{
    [TestMethod]
    public void LaunchAppliesEachLocalOperationAndSignalsCompletionAtExpectedCount()
    {
        var running = new IsRunningTracker(true);
        var scheduler = new QueuedScheduler();
        var network = new RecordingNetworkLayer(running);
        var behaviour = new InsertBehaviour(
            running,
            network,
            scheduler,
            new ReplicaID(1),
            Array.Empty<ReplicaID>(),
            minNumber: 10,
            maxNumberInclusive: 11,
            expectedCount: 2,
            delayBetweenSends: 0,
            delayBetweenOps: 0.1f);
        var observedCounts = new List<int>();
        var completionCount = 0;
        behaviour.OperationApplied += (_, count, _) => observedCounts.Add(count);
        behaviour.AddBehaviourEndedHandler(() => completionCount++);

        behaviour.Launch();
        Assert.AreEqual(1, behaviour.CurrentCount);
        Assert.AreEqual(0, completionCount);

        scheduler.RunNextScheduledAction();

        Assert.AreEqual(2, behaviour.CurrentCount);
        Assert.AreEqual(1, completionCount);
        CollectionAssert.AreEqual(new[] { 1, 2 }, observedCounts);
    }

    [TestMethod]
    public void LaunchDoesNothingWhenApplicationIsStopped()
    {
        var running = new IsRunningTracker(false);
        var scheduler = new QueuedScheduler();
        var behaviour = new InsertBehaviour(
            running,
            new RecordingNetworkLayer(running),
            scheduler,
            new ReplicaID(1),
            Array.Empty<ReplicaID>(),
            minNumber: 0,
            maxNumberInclusive: 1,
            expectedCount: 2,
            delayBetweenSends: 0,
            delayBetweenOps: 0);
        var completionCount = 0;
        behaviour.AddBehaviourEndedHandler(() => completionCount++);

        behaviour.Launch();

        Assert.AreEqual(0, behaviour.CurrentCount);
        Assert.AreEqual(0, completionCount);
        Assert.AreEqual(0, scheduler.ScheduledActionCount);
    }

    [TestMethod]
    public void LaunchSignalsCompletionOnceForSingleExpectedOperation()
    {
        var running = new IsRunningTracker(true);
        var behaviour = new InsertBehaviour(
            running,
            new RecordingNetworkLayer(running),
            new QueuedScheduler(),
            new ReplicaID(1),
            Array.Empty<ReplicaID>(),
            minNumber: 7,
            maxNumberInclusive: 7,
            expectedCount: 1,
            delayBetweenSends: 0,
            delayBetweenOps: 0);
        var completionCount = 0;
        behaviour.AddBehaviourEndedHandler(() => completionCount++);

        behaviour.Launch();

        Assert.AreEqual(1, behaviour.CurrentCount);
        Assert.AreEqual(1, completionCount);
    }

    [TestMethod]
    public void InsertManualValueUpdatesLocalStateWhenStopped()
    {
        var running = new IsRunningTracker(false);
        var network = new RecordingNetworkLayer(running);
        var behaviour = new InsertBehaviour(
            running,
            network,
            new QueuedScheduler(),
            new ReplicaID(1),
            Array.Empty<ReplicaID>(),
            minNumber: 0,
            maxNumberInclusive: 0,
            expectedCount: 1,
            delayBetweenSends: 0,
            delayBetweenOps: 0);

        behaviour.InsertManualValue(42);

        Assert.AreEqual(1, behaviour.CurrentCount);
    }

    [TestMethod]
    public void InsertManualValueSignalsCompletionAtExpectedCount()
    {
        var running = new IsRunningTracker(false);
        var behaviour = new InsertBehaviour(
            running,
            new RecordingNetworkLayer(running),
            new QueuedScheduler(),
            new ReplicaID(1),
            Array.Empty<ReplicaID>(),
            minNumber: 0,
            maxNumberInclusive: 0,
            expectedCount: 1,
            delayBetweenSends: 0,
            delayBetweenOps: 0);
        var appliedCount = 0;
        var completionCount = 0;
        behaviour.OperationApplied += (_, _, _) => appliedCount++;
        behaviour.AddBehaviourEndedHandler(() => completionCount++);

        behaviour.InsertManualValue(42);

        Assert.AreEqual(1, appliedCount);
        Assert.AreEqual(1, completionCount);
    }

    [TestMethod]
    public void ReceivedApplicationMessageIsAppliedToLocalSet()
    {
        var running = new IsRunningTracker(true);
        var network = new RecordingNetworkLayer(running);
        var behaviour = new InsertBehaviour(
            running,
            network,
            new QueuedScheduler(),
            new ReplicaID(1),
            Array.Empty<ReplicaID>(),
            minNumber: 0,
            maxNumberInclusive: 0,
            expectedCount: 1,
            delayBetweenSends: 0,
            delayBetweenOps: 0);
        var remoteID = new ReplicaID(2);
        var remoteHistory = new CausalHistoryTracker(remoteID, tag: "app");
        var remoteSet = new ORSet<int>();
        remoteSet.Merge(remoteSet.CreateAddOperation(remoteID, 42));
        var trackedMessage = remoteHistory.TagMessage(remoteSet.ToJson(value => value.ToString()));
        var completionCount = 0;
        behaviour.AddBehaviourEndedHandler(() => completionCount++);

        network.Deliver(remoteID, new Message("app", trackedMessage.Encode()));

        Assert.AreEqual(1, behaviour.CurrentCount);
        Assert.AreEqual(1, completionCount);
    }

    [TestMethod]
    public void ReceivedNonApplicationMessageIsIgnored()
    {
        var running = new IsRunningTracker(true);
        var network = new RecordingNetworkLayer(running);
        var behaviour = new InsertBehaviour(
            running,
            network,
            new QueuedScheduler(),
            new ReplicaID(1),
            Array.Empty<ReplicaID>(),
            minNumber: 0,
            maxNumberInclusive: 0,
            expectedCount: 1,
            delayBetweenSends: 0,
            delayBetweenOps: 0);
        var remoteID = new ReplicaID(2);
        var remoteHistory = new CausalHistoryTracker(remoteID, tag: "app");
        var remoteSet = new ORSet<int>();
        remoteSet.Merge(remoteSet.CreateAddOperation(remoteID, 42));
        var trackedMessage = remoteHistory.TagMessage(remoteSet.ToJson(value => value.ToString()));

        network.Deliver(remoteID, new Message("unrelated", trackedMessage.Encode()));

        Assert.AreEqual(0, behaviour.CurrentCount);
    }

    [TestMethod]
    public void SameRemoteValueDoesNotIncreaseDistinctValueCount()
    {
        var running = new IsRunningTracker(true);
        var network = new RecordingNetworkLayer(running);
        var behaviour = new InsertBehaviour(
            running,
            network,
            new QueuedScheduler(),
            new ReplicaID(1),
            Array.Empty<ReplicaID>(),
            minNumber: 0,
            maxNumberInclusive: 0,
            expectedCount: 2,
            delayBetweenSends: 0,
            delayBetweenOps: 0);
        var remoteID = new ReplicaID(2);
        var remoteHistory = new CausalHistoryTracker(remoteID, tag: "app");
        var remoteSet = new ORSet<int>();
        remoteSet.Merge(remoteSet.CreateAddOperation(remoteID, 42));
        var first = remoteHistory.TagMessage(remoteSet.ToJson(value => value.ToString()));
        remoteSet.Merge(remoteSet.CreateAddOperation(remoteID, 42));
        var second = remoteHistory.TagMessage(remoteSet.ToJson(value => value.ToString()));

        network.Deliver(remoteID, new Message("app", first.Encode()));
        network.Deliver(remoteID, new Message("app", second.Encode()));

        Assert.AreEqual(1, behaviour.CurrentCount);
    }

    private sealed class RecordingNetworkLayer(IsRunningTracker runningTracker)
        : NetworkLayer("test", runningTracker)
    {
        public override Task<bool> SendMessage(ReplicaID recipient, Message message) =>
            Task.FromResult(true);

        public void Deliver(ReplicaID sender, Message message) =>
            CallMessageReceived(sender, message);
    }

    private sealed class QueuedScheduler : IScheduler
    {
        private readonly Queue<Action> actions = new();

        public int ScheduledActionCount => actions.Count;

        public void ExecuteAfter(float time, Action action) => actions.Enqueue(action);

        public void RunNextScheduledAction() => actions.Dequeue()();

        public Task<(bool, T)> ExecuteTaskOrTimeout<T>(
            Func<Task<T>> operation,
            float time,
            T defaultValue) =>
            Task.FromResult((false, defaultValue));

        public Task<bool> ExecuteTaskOrTimeout(Func<Task> operation, float time) =>
            Task.FromResult(false);

        public Task<bool> ExecuteTaskOrTimeout(Func<Task> operation, float time, Action timeoutAction) =>
            Task.FromResult(false);

        public DateTime GetCurrentTime() => DateTime.UnixEpoch;
    }
}
