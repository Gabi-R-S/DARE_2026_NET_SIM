using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.NetworkLayers;
using CRDTApp.NetworkLayers.Composite.Routing;
using CRDTApp.Schedulers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shared;

namespace Tests_1
{
    /// <summary>
    /// Tests that specifically target the infinite-loop (message storm) behavior observed
    /// in FloodingRedirectionLayer. Every test in this class is designed to FAIL against the
    /// current buggy implementation and PASS only once the loop has been fixed.
    ///
    /// Root cause: FloodMessage carries no unique message ID, so transit nodes have no way
    /// to track which packets they have already forwarded. When a flood reaches a peer it
    /// re-floods back to the node that forwarded it (the sender filter only removes the
    /// *immediate* sender, not all nodes that already saw this packet), creating an
    /// exponentially growing packet storm that never terminates.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class FloodingLayerInfiniteLoopBugsTests
    {
        // -------------------------------------------------------------------------
        // Bug 1 – Transit node reflood storm (the primary infinite-loop trigger)
        // -------------------------------------------------------------------------

        [TestMethod]
        [Timeout(5000)]
        public async Task Flooding_TransitNode_MustNotRefloodPacketItAlreadyForwarded()
        {
            // EXPECTED BEHAVIOR:
            // When a transit node forwards a flood packet, and one of the downstream
            // peers echoes the same packet back (because *they* also flood to all
            // neighbours except the immediate sender), the transit node MUST recognise
            // the packet as already-seen and drop it.
            //
            // CURRENT BUG:
            // FloodMessage has no message ID. The transit node has no "seen set".
            // It re-forwards the echo, which causes the packet to travel in a cycle
            // until NumberOfHops reaches 0 — but by then many duplicate copies have
            // been injected into the network, producing an exponential message storm.
            //
            // HOW THE TEST DETECTS THE BUG:
            // We manually simulate the echo loop by calling OnLowerLayerMessageReceived
            // twice with the *same* flood packet (same NumberOfHops, same payload).
            // A correct implementation forwards 0 additional packets on the second call.
            // The buggy implementation forwards another wave of messages.

            var mockLower = new CountingMockLayer();
            var rSelf     = new ReplicaID(1);
            var rSender   = new ReplicaID(2);
            var rPeerA    = new ReplicaID(3);
            var rPeerB    = new ReplicaID(4);
            var rTarget   = new ReplicaID(99);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rSender, rPeerA, rPeerB },
                new HashSet<ReplicaID>());

            var floodLayer = new FloodingRedirectionLayer(rSelf, new ControllableTestScheduler(), tracker, mockLower, numHops: 5, forgettingTime: 60.0f);
            await floodLayer.Run();

            // First arrival: transit node forwards the packet normally.
            var packet = new FloodMessage(rTarget, new ReplicaID(10), 0u, new Message("hello"), numberOfHops: 4);
            floodLayer.OnLowerLayerMessageReceived(rSender, new Message(packet.ToJson()));

            int forwardsOnFirstArival = mockLower.SendCount;
            Assert.IsTrue(forwardsOnFirstArival > 0, "Transit node should forward the packet at least once.");

            // Reset counter then deliver an IDENTICAL echo (same content, same hop count)
            // simulating what happens when one of the downstream peers re-floods back.
            mockLower.ResetCount();
            floodLayer.OnLowerLayerMessageReceived(rPeerA, new Message(packet.ToJson()));

            // CORRECT behavior: already-seen packet must be silently dropped → 0 new sends.
            Assert.AreEqual(0, mockLower.SendCount,
                "BUG: Transit node re-forwarded a flood packet it had already forwarded. " +
                "This causes an infinite message storm. Implement a seen-message set.");
        }

        [TestMethod]
        [Timeout(5000)]
        public async Task Flooding_FullMeshCycle_TotalForwardCountIsBoundedByTopology()
        {
            // EXPECTED BEHAVIOR:
            // In a fully-connected triangle (A↔B↔C↔A), a single flood from an external
            // sender to a target that is not in the triangle should result in at most
            // (number of transit hops that make topological sense) message sends.
            // Specifically, with correct deduplication every transit node forwards a
            // given packet at most ONCE.
            //
            // CURRENT BUG:
            // Without a seen-message set each node re-forwards every copy it receives,
            // so the total number of SendMessage calls grows without bound (limited only
            // by NumberOfHops, and even then the count is O(branches^hops)).
            //
            // HOW THE TEST DETECTS THE BUG:
            // We manually step through the forwarding in a triangle:
            //   1) A receives from external → forwards to B and C (2 sends).
            //   2) B "echoes" the same packet back to A via rC (a different immediate sender).
            //      A has NOT filtered rC (rC was not the *immediate* sender of the original).
            //      A correct implementation recognises this is the same flood wave and drops it.
            //      A buggy implementation re-forwards → storm begins.
            // Total correct sends by A: 2. Total sends by A after echo: still 2.

            var counterA = new CountingMockLayer();
            var rA       = new ReplicaID(1);
            var rB       = new ReplicaID(2);
            var rC       = new ReplicaID(3);
            var rExternal = new ReplicaID(10);
            var rTarget   = new ReplicaID(99);

            var trackerA = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rB, rC }, new HashSet<ReplicaID>());

            var floodA = new FloodingRedirectionLayer(rA, new ControllableTestScheduler(), trackerA, counterA, numHops: 5, forgettingTime: 60.0f);
            await floodA.Run();

            // Step 1: A receives the original packet from the external sender.
            var original = new FloodMessage(rTarget, rExternal, 0u, new Message("data"), numberOfHops: 3);
            floodA.OnLowerLayerMessageReceived(rExternal, new Message(original.ToJson()));
            Assert.IsTrue(counterA.SendCount > 0, "Node A must forward the original packet.");

            // Step 2: Simulate the echo — rC delivers a copy of the same wave to A.
            // (In a triangle: A→C, C→B, B→A — the packet cycles back.)
            // The packet's hop count is decremented (as a transit node would do), so it's now 2.
            // Without deduplication, A sees this as a NEW packet (different hops, different
            // immediate sender) and re-forwards it — starting the storm.
            var echoFromC = new FloodMessage(rTarget, rExternal, 0u, new Message("data"), numberOfHops: 2);
            counterA.ResetCount();
            floodA.OnLowerLayerMessageReceived(rC, new Message(echoFromC.ToJson()));

            Assert.AreEqual(0, counterA.SendCount,
                $"BUG: Node A re-forwarded the echo from C ({counterA.SendCount} send(s)). " +
                $"In a triangle topology this echo is a copy of the wave A already forwarded. " +
                $"Implement a seen-message set to prevent the message storm / infinite loop.");
        }

        


        // -------------------------------------------------------------------------
        // Bug 4 – Hop-count alone is insufficient to prevent the storm
        // -------------------------------------------------------------------------

        [TestMethod]
        [Timeout(5000)]
        public async Task Flooding_HopCountAlone_IsInsufficientToPreventMessageStorm()
        {
            // EXPECTED BEHAVIOR:
            // Even with a strictly decreasing hop count the TOTAL number of messages sent
            // across all nodes must remain polynomial (O(nodes × hops)), not exponential.
            // In a 3-node ring with initial hops=2, the maximum correct total is 4:
            //   A→B, A→C (first wave, hops=2→1), then B→C and C→B (hops=1→0, stop).
            //
            // CURRENT BUG:
            // Because no deduplication exists, the hop-count merely bounds the storm's
            // *depth* but not its *width*. Nodes re-forward every copy they receive, so
            // the count is (branches)^hops, which grows quickly even for small values.
            //
            // HOW THE TEST DETECTS THE BUG:
            // We count sends in a 3-node system with hops=2 and assert the count is ≤ 4.

            var counter = new CountingMockLayer();

            var rA = new ReplicaID(1);
            var rB = new ReplicaID(2);
            var rC = new ReplicaID(3);
            var rTarget = new ReplicaID(99);

            var trackerA = new ActiveConnectionTracker(new HashSet<ReplicaID> { rB, rC }, new HashSet<ReplicaID>());
            var trackerB = new ActiveConnectionTracker(new HashSet<ReplicaID> { rA, rC }, new HashSet<ReplicaID>());
            var trackerC = new ActiveConnectionTracker(new HashSet<ReplicaID> { rA, rB }, new HashSet<ReplicaID>());

            var floodA = new FloodingRedirectionLayer(rA, new ControllableTestScheduler(), trackerA, counter, numHops: 2, forgettingTime: 60.0f);
            var floodB = new FloodingRedirectionLayer(rB, new ControllableTestScheduler(), trackerB, counter, numHops: 2, forgettingTime: 60.0f);
            var floodC = new FloodingRedirectionLayer(rC, new ControllableTestScheduler(), trackerC, counter, numHops: 2, forgettingTime: 60.0f);

            await floodA.Run();
            await floodB.Run();
            await floodC.Run();

            // Original sender is not in the ring; inject into A.
            var rOriginal = new ReplicaID(10);
            var packet = new FloodMessage(rTarget, rOriginal, 0u, new Message("payload"), numberOfHops: 2);
            string json = packet.ToJson();

            // Step 1 – A receives from external and forwards to B and C (2 sends, hops→1).
            floodA.OnLowerLayerMessageReceived(rOriginal, new Message(json));

            // With correct dedup A recorded this packet; further echoes are dropped.
            // Without dedup: simulate the echo loop manually.
            // (In the real system the forwarded messages would be received by B and C and
            //  re-forwarded back; here we simply note the total count after the initial wave.)

            // The initial wave alone must already be bounded (≤ 2 sends from A).
            int sendsAfterFirstWave = counter.SendCount;

            Assert.IsTrue(sendsAfterFirstWave <= 2,
                $"BUG: Even after the FIRST forward wave, the node sent {sendsAfterFirstWave} " +
                $"messages (expected ≤ 2 for a 2-neighbour node). " +
                $"This indicates uncontrolled message multiplication.");

            // Now simulate B echoing the same packet back to A (as it would in the real system).
            // A correct implementation drops it; a buggy one re-forwards it.
            // The packet B would forward is identical except hops decremented by 1.
            var echo = new FloodMessage(rTarget, rOriginal, 0u, new Message("payload"), numberOfHops: 1);
            floodA.OnLowerLayerMessageReceived(rB, new Message(echo.ToJson()));

            // Without a seen-set keyed on message ID, A cannot tell this is the same flood
            // wave (the hop count is different now), so it forwards again → storm starts.
            // With a seen-set keyed on message ID, A drops it immediately.
            int sendsAfterEcho = counter.SendCount - sendsAfterFirstWave;
            Assert.AreEqual(0, sendsAfterEcho,
                $"BUG: Node A re-forwarded the echo from B ({sendsAfterEcho} additional send(s)). " +
                $"Once a flood packet (identified by its unique ID) has been forwarded, " +
                $"any future copy must be silently dropped regardless of remaining hop count.");
        }

        // -------------------------------------------------------------------------
        // Bug 5 – OriginalSender guard is absent on transit-node forwarding
        // -------------------------------------------------------------------------

        [TestMethod]
        [Timeout(5000)]
        public async Task Flooding_TransitNode_WhenReceivingOwnForwardEchoedBack_MustDropIt()
        {
            // EXPECTED BEHAVIOR:
            // If a transit node T receives a copy of a packet that T already forwarded
            // (echoed back by a downstream node), it must drop it without any additional forwards.
            //
            // CURRENT BUG:
            // Forwarding only filters out `connection == senderID` (immediate sender) and
            // `connection == replicaID` (self). It does NOT check whether T has already
            // processed and forwarded this specific packet. When the echo arrives from a
            // *different* neighbour than the original sender, T re-forwards it fully.

            var mockLower = new CountingMockLayer();
            var rSelf   = new ReplicaID(1);
            var rPeer1  = new ReplicaID(2);  // original sender (first arrival)
            var rPeer2  = new ReplicaID(3);  // echo source (second arrival)
            var rPeer3  = new ReplicaID(4);  // another neighbour
            var rTarget = new ReplicaID(99);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rPeer1, rPeer2, rPeer3 },
                new HashSet<ReplicaID>());

            var floodLayer = new FloodingRedirectionLayer(rSelf, new ControllableTestScheduler(), tracker, mockLower, numHops: 5, forgettingTime: 60.0f);
            await floodLayer.Run();

            var packet = new FloodMessage(rTarget, new ReplicaID(50), 0u, new Message("msg"), numberOfHops: 3);
            string packetJson = packet.ToJson();

            // First arrival from Peer1 — normal forwarding to Peer2 and Peer3.
            floodLayer.OnLowerLayerMessageReceived(rPeer1, new Message(packetJson));
            int sendsFirstArrival = mockLower.SendCount;
            Assert.IsTrue(sendsFirstArrival > 0, "Should forward at least once on first arrival.");

            // Echo arrives from Peer2 (a different immediate sender than Peer1).
            // The current filter does NOT block this because connection != rPeer2 is true for Peer1 and Peer3.
            // Correct behavior: recognise same flood-wave ID and drop entirely.
            mockLower.ResetCount();
            floodLayer.OnLowerLayerMessageReceived(rPeer2, new Message(packetJson));

            Assert.AreEqual(0, mockLower.SendCount,
                "BUG: Transit node re-forwarded an echo of a packet it already forwarded. " +
                "This is the root cause of the infinite-loop / message storm. " +
                "Implement a seen-message set keyed on flood packet ID.");
        }

        // -------------------------------------------------------------------------
        // Bug 6 – Seen-set must use message ID, not payload equality
        // -------------------------------------------------------------------------

        [TestMethod]
        [Timeout(5000)]
        public async Task Flooding_SeenSet_MustNotConflateDifferentWavesWithIdenticalPayload()
        {
            // EXPECTED BEHAVIOR:
            // Two *different* flood waves (different message IDs) that happen to carry the
            // same payload must both be delivered / forwarded correctly. The deduplication
            // must be keyed on message ID, not payload content.
            //
            // CURRENT BUG (secondary):
            // If any future fix naively deduplicates by payload hash, legitimate retransmissions
            // of the same data (new wave, new ID) would be incorrectly silenced.

            var mockLower = new CountingMockLayer();
            var rSelf   = new ReplicaID(1);
            var rSender = new ReplicaID(2);
            var rPath2  = new ReplicaID(3); // alternative path for duplicate delivery
            var rTarget = new ReplicaID(1); // addressed to self so we can count deliveries

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rSender, rPath2 },
                new HashSet<ReplicaID>());

            var floodLayer = new FloodingRedirectionLayer(rSelf, new ControllableTestScheduler(), tracker, mockLower, numHops: 5, forgettingTime: 60.0f);
            await floodLayer.Run();

            int deliveryCount = 0;
            floodLayer.AddOnMessageReceivedHandler((_, _) => deliveryCount++);

            // Wave 1 — first arrival
            var wave1 = new FloodMessage(rTarget, rSender, 0u, new Message("identical"), numberOfHops: 2);
            string wave1Json = wave1.ToJson();
            floodLayer.OnLowerLayerMessageReceived(rSender, new Message(wave1Json));

            // Exact duplicate of wave 1 arrives via a different path (rPath2).
            // CORRECT behavior: recognise same flood-wave tag → skip delivery → count stays at 1.
            // BUGGY behavior:   no seen-set → delivers again → count becomes 2.
            floodLayer.OnLowerLayerMessageReceived(rPath2, new Message(wave1Json));

            Assert.AreEqual(1, deliveryCount,
                "BUG: Duplicate arrival of wave 1 (via a different path) was delivered again. " +
                "Target should deduplicate on message ID and deliver each wave exactly once.");

            // Wave 2 — genuinely different wave with identical payload (different tag).
            // CORRECT behavior: new tag → not in seen-set → deliver → count becomes 2.
            var wave2 = new FloodMessage(rTarget, rSender, 1u, new Message("identical"), numberOfHops: 2);
            floodLayer.OnLowerLayerMessageReceived(rSender, new Message(wave2.ToJson()));

            // Both distinct waves delivered, duplicate suppressed: total = 2.
            Assert.AreEqual(2, deliveryCount,
                "BUG: Two distinct flood waves with identical payload were not both delivered. " +
                "Deduplication must be keyed on unique message tag, not payload content.");
        }
    }

    // -------------------------------------------------------------------------
    // Helper: a layer that just counts how many times SendMessage is called.
    // -------------------------------------------------------------------------

    /// <summary>
    /// A lightweight NetworkLayer stub that only counts outgoing SendMessage calls
    /// and records recipients, without requiring the full ControllableMockLayer API.
    /// </summary>
    internal class CountingMockLayer : NetworkLayer
    {
        private int _sendCount = 0;

        public int SendCount => _sendCount;

        public List<ReplicaID> Recipients { get; } = new();

        public void ResetCount()
        {
            _sendCount = 0;
            Recipients.Clear();
        }

        public override Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            Interlocked.Increment(ref _sendCount);
            lock (Recipients)
            {
                Recipients.Add(recipient);
            }
            return Task.FromResult(true);
        }
    }
}
