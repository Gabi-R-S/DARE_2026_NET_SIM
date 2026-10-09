using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using Shared;
using Simulator;

namespace Tests_1;

[TestClass]
public sealed class NetworkModelTests
{
    [TestMethod]
    public void ConnectionMapStoresConnectionSymmetrically()
    {
        var left = new ReplicaID(1);
        var right = new ReplicaID(2);
        var connection = new NetworkConnection(0.25f);
        var map = new ConnectionMap();

        map.SetConnection(left, right, connection);

        Assert.AreSame(connection, map.GetConnection(left, right));
        Assert.AreSame(connection, map.GetConnection(right, left));
    }

    [TestMethod]
    public void ConnectionMapReportsKnownReplicasFromBothSides()
    {
        var left = new ReplicaID(1);
        var right = new ReplicaID(2);
        var map = new ConnectionMap();
        map.SetConnection(left, right, new NetworkConnection());

        CollectionAssert.AreEquivalent(new[] { left, right }, map.GetAllKnownReplicas().ToArray());
    }

    [TestMethod]
    public void ConnectionAvailabilityTracksActiveState()
    {
        var left = new ReplicaID(1);
        var right = new ReplicaID(2);
        var connection = new NetworkConnection(active: false);
        var map = new ConnectionMap();
        map.SetConnection(left, right, connection);

        Assert.IsFalse(map.IsConnectionAvailable(left, right));
        connection.Active = true;
        Assert.IsTrue(map.IsConnectionAvailable(right, left));
    }

    [TestMethod]
    public void MissingConnectionsAreUnavailable()
    {
        var map = new ConnectionMap();

        Assert.IsNull(map.GetConnection(new ReplicaID(1), new ReplicaID(2)));
        Assert.IsFalse(map.IsConnectionAvailable(new ReplicaID(1), new ReplicaID(2)));
    }

    [TestMethod]
    public void ConnectionMapRetainsIsolatedReplicasAndRemovesConnectionsSymmetrically()
    {
        var left = new ReplicaID(1);
        var right = new ReplicaID(2);
        var map = new ConnectionMap();
        map.SetConnection(left, right, new NetworkConnection());

        Assert.IsTrue(map.RemoveConnection(left, right));

        Assert.IsNull(map.GetConnection(left, right));
        Assert.IsNull(map.GetConnection(right, left));
        CollectionAssert.AreEquivalent(new[] { left, right }, map.GetAllKnownReplicas().ToArray());
    }

    [TestMethod]
    public void ConnectionMapRemovesReplicaAndItsIncidentConnections()
    {
        var removed = new ReplicaID(1);
        var firstNeighbor = new ReplicaID(2);
        var secondNeighbor = new ReplicaID(3);
        var map = new ConnectionMap();
        map.SetConnection(removed, firstNeighbor, new NetworkConnection());
        map.SetConnection(removed, secondNeighbor, new NetworkConnection());

        Assert.IsTrue(map.RemoveReplica(removed));

        CollectionAssert.AreEquivalent(new[] { firstNeighbor, secondNeighbor }, map.GetAllKnownReplicas().ToArray());
        Assert.IsNull(map.GetConnection(removed, firstNeighbor));
        Assert.IsNull(map.GetConnection(firstNeighbor, removed));
        Assert.IsNull(map.GetConnection(removed, secondNeighbor));
        Assert.IsNull(map.GetConnection(secondNeighbor, removed));
    }

    [TestMethod]
    public void ActiveConnectionTrackerTransitionsBetweenActiveAndInactive()
    {
        var self = new ReplicaID(1);
        var peer = new ReplicaID(2);
        var tracker = new ActiveConnectionTracker(
            new HashSet<ReplicaID>(),
            new HashSet<ReplicaID> { peer },
            self);

        Assert.IsTrue(tracker.SetActive(peer));
        Assert.IsTrue(tracker.IsActive(peer));
        Assert.IsTrue(tracker.SetInnactive(peer));
        Assert.IsFalse(tracker.IsActive(peer));
        CollectionAssert.AreEqual(new[] { peer }, tracker.InnactiveConnections);
    }

    [TestMethod]
    public void ActiveConnectionTrackerReturnsDistanceOneForActivePeerWithoutDistanceTracking()
    {
        var self = new ReplicaID(1);
        var peer = new ReplicaID(2);
        var tracker = new ActiveConnectionTracker(
            new HashSet<ReplicaID> { peer },
            new HashSet<ReplicaID>(),
            self);

        Assert.AreEqual(0f, tracker.GetDistance(self));
        Assert.AreEqual(1f, tracker.GetDistance(peer));
        Assert.IsTrue(float.IsPositiveInfinity(tracker.GetDistance(new ReplicaID(3))));
    }

    [TestMethod]
    public void ActiveConnectionTrackerUsesExplicitDistancesWhenEnabled()
    {
        var self = new ReplicaID(1);
        var peer = new ReplicaID(2);
        var tracker = new ActiveConnectionTracker(
            new HashSet<ReplicaID>(),
            new HashSet<ReplicaID> { peer },
            self,
            trackingDistances: true);

        tracker.SetDistance(peer, 2.5f);

        Assert.AreEqual(2.5f, tracker.GetDistance(peer));
        Assert.IsTrue(float.IsPositiveInfinity(tracker.GetDistance(new ReplicaID(3))));
    }

    [TestMethod]
    public void NetworkLayerSendFailsWhenThereIsNoLowerLayer()
    {
        var running = new IsRunningTracker(true);
        var layer = new BareNetworkLayer(running);

        Assert.IsFalse(layer.SendMessage(new ReplicaID(2), new Message("app", "body")).GetAwaiter().GetResult());
    }

    [TestMethod]
    public void NetworkLayerSendFailsWhenStopped()
    {
        var running = new IsRunningTracker(false);
        var lower = new RecordingNetworkLayer(running);
        var layer = new BareNetworkLayer(running, lower);

        Assert.IsFalse(layer.SendMessage(new ReplicaID(2), new Message("app", "body")).GetAwaiter().GetResult());
        Assert.AreEqual(0, lower.SentCount);
    }

    private sealed class BareNetworkLayer(IsRunningTracker runningTracker, NetworkLayer? below = null)
        : NetworkLayer("test", runningTracker, below);

    private sealed class RecordingNetworkLayer(IsRunningTracker runningTracker)
        : NetworkLayer("recording", runningTracker)
    {
        public int SentCount { get; private set; }

        public override Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            SentCount++;
            return Task.FromResult(true);
        }
    }
}
