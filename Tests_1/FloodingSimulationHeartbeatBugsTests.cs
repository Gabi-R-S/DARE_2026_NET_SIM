using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CRDTApp;
using CRDTApp.Behaviours;
using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.NetworkLayers;
using CRDTApp.NetworkLayers.Base;
using CRDTApp.NetworkLayers.Composite;
using CRDTApp.NetworkLayers.Composite.Routing;
using CRDTApp.Schedulers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shared;
using SimSharp;
using Simulator;

namespace Tests_1
{
    /// <summary>
    /// Tests that detect the source of the bug causing flooding simulations to continue
    /// and slow down dramatically even after the task has finished and heartbeats should have stopped.
    ///
    /// Every test in this class is designed to FAIL against the current buggy implementation,
    /// and PASS only once the underlying bugs are corrected.
    ///
    /// Root Causes:
    /// 1. In RunSimulations/Experiment.cs (BasicMetricCreator):
    ///    When all app behaviours finish (count == apps.Count), it logs "Task ended!" and calls
    ///    `app.Close()`. Because of closure over the foreach loop variable `app`, ONLY the single
    ///    app that triggered the final callback is closed! The remaining apps.Count - 1 apps are
    ///    never closed, their NetworkComponent is never stopped, and their HeartbeatLayer continues
    ///    to fire heartbeats every 0.1s. In discrete event simulation, this keeps the event loop
    ///    running all the way to maxExperimentDuration (200.0s), generating tens of thousands of
    ///    unnecessary heartbeat messages with JSON serialization, causing massive slowdown.
    ///
    /// 2. In CRDTApp/NetworkLayers/Composite/HeartbeatLayer.cs:
    ///    `ReplicaID replicaID;` is declared but never accepted in constructor or initialized,
    ///    defaulting to ID 0 for every instance. Consequently:
    ///    - Nodes with non-zero IDs (e.g. 1, 2, 3) send heartbeats to THEMSELVES.
    ///    - Nodes with non-zero IDs NEVER send heartbeats to replica 0 (0 != 0 is false), causing
    ///      connection timeouts on node 0 (center of star topologies) and broken routing tables.
    ///
    /// 3. In RunSimulations/Experiment.cs (CreateBasicFloodRoutingStack):
    ///    `TrackActiveConnectionsLayer trackerLayer` is instantiated but orphaned; `HeartbeatLayer`
    ///    is wired directly to `timeoutLayer`, bypassing `trackerLayer` from the layer hierarchy.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class FloodingSimulationHeartbeatBugsTests
    {
        private static Assembly? LoadRunSimulationsAssembly()
        {
            var testDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(testDir, "RunSimulations.dll"),
                Path.Combine(testDir, "..", "..", "..", "..", "RunSimulations", "bin", "Debug", "net10.0", "RunSimulations.dll"),
                Path.Combine(testDir, "..", "..", "..", "..", "RunSimulations", "bin", "Release", "net10.0", "RunSimulations.dll"),
                @"D:\DARE_PROJECT\DistributedMeshSimulation\RunSimulations\bin\Debug\net10.0\RunSimulations.dll"
            };

            foreach (var candidate in candidates)
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    return Assembly.LoadFrom(full);
                }
            }

            return null;
        }

        // =========================================================================
        // Bug 1: BasicMetricCreator only closes 1 app instead of all apps
        // =========================================================================

        [TestMethod]
        [Timeout(5000)]
        public void BasicMetricCreator_WhenAllBehavioursEnd_MustCloseAllApps_SoHeartbeatsStopOnAllNodes()
        {
            // EXPECTED BEHAVIOR:
            // When all apps finish their behaviour (the "task" has ended), BasicMetricCreator
            // must close ALL apps in the simulation:
            //   foreach (var a in apps) { a.Close(); }
            // This ensures every app's NetworkComponent is stopped and recurring heartbeats cease.
            //
            // CURRENT BUG:
            // BasicMetricCreator does:
            //   if (count == apps.Count) { Console.WriteLine("Task ended!"); endedSuccessfully.Value = true; app.Close(); }
            // Inside the `foreach (var app in apps)` loop, `app.Close()` closes only the single
            // app instance that happened to finish last. The other apps.Count - 1 apps are NEVER
            // closed, so their NetworkComponents remain Running == true, and their HeartbeatLayers
            // continue generating heartbeats indefinitely.
            //
            // HOW THE TEST DETECTS THE BUG:
            // We create 4 apps with mock running network layers and dummy behaviours.
            // We invoke BasicMetricCreator on the apps list, then signal behaviour completion on all 4.
            // A correct implementation closes all 4 apps (all NetworkComponent.Running == false).
            // The buggy implementation closes only 1 app, leaving 3 running.

            var asm = LoadRunSimulationsAssembly();
            Assert.IsNotNull(asm, "Could not load RunSimulations.dll for simulation completion test");

            var expType = asm.GetType("RunSimulations.FullyConnectedExperiment");
            Assert.IsNotNull(expType, "Could not find FullyConnectedExperiment type");

            var exp = Activator.CreateInstance(expType, 10.0f, 0.0f, 1, 4, 0.1f, 0.1f);
            Assert.IsNotNull(exp);

            var method = expType.GetMethod("BasicMetricCreator", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "Could not find BasicMetricCreator method on Experiment");

            const int numApps = 4;
            var apps = new List<App>();
            var behaviours = new List<StubTaskBehaviour>();
            var networkLayers = new List<CountingMockLayer>();

            for (int i = 0; i < numApps; i++)
            {
                var beh = new StubTaskBehaviour();
                var net = new CountingMockLayer();
                net.SetRunning(true);
                var app = new App(beh, new ControllableTestScheduler(), net);

                apps.Add(app);
                behaviours.Add(beh);
                networkLayers.Add(net);
            }

            // Register completion callbacks via BasicMetricCreator
            method.Invoke(exp, new object?[] { null, apps });

            // Complete tasks sequentially across all nodes
            for (int i = 0; i < numApps; i++)
            {
                behaviours[i].CompleteTask();
            }

            // Verify ALL apps were closed
            var unclosedAppIndices = new List<int>();
            for (int i = 0; i < numApps; i++)
            {
                if (networkLayers[i].IsRunning)
                {
                    unclosedAppIndices.Add(i);
                }
            }

            Assert.AreEqual(0, unclosedAppIndices.Count,
                $"BUG: BasicMetricCreator failed to close all apps when the task ended! " +
                $"Apps [{string.Join(", ", unclosedAppIndices)}] are still running with active network components. " +
                $"Only 1 app was closed due to foreach variable capture (app.Close() instead of closing all apps). " +
                $"This causes recurring heartbeats to continue indefinitely after the task finished.");
        }

        // =========================================================================
        // Bug 2: HeartbeatLayer uninitialized replicaID causes self-heartbeats
        // =========================================================================

        [TestMethod]
        [Timeout(5000)]
        public async Task HeartbeatLayer_NonZeroReplica_MustNotSendHeartbeatsToItself()
        {
            // EXPECTED BEHAVIOR:
            // A node running HeartbeatLayer must NEVER send heartbeats to its own ReplicaID.
            //
            // CURRENT BUG:
            // In HeartbeatLayer.cs:
            //   ReplicaID replicaID;
            // The field is private and never initialized in the constructor (defaults to 0).
            // When replica 1 starts HeartbeatLayer:
            //   foreach (var replica in connectionTracker.AllConnections) {
            //       if (replica != replicaID) { sendingTo.Add(replica); Hearbeat(replica); }
            //   }
            // For replica 1, replica != replicaID (1 != 0) evaluates to TRUE!
            // Replica 1 sends heartbeats to ITSELF every heartbeatTime!
            // In discrete simulation, self-messages are scheduled at delay 0, flooding the queue.
            //
            // HOW THE TEST DETECTS THE BUG:
            // Create HeartbeatLayer for replica 1 where AllConnections contains {1, 2}.
            // Start the layer and inspect SentMessages.
            // If any sent message has Recipient == replica 1, the bug is present.

            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var rSelf = new ReplicaID(1);
            var rPeer = new ReplicaID(2);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { rSelf, rPeer },
                new HashSet<ReplicaID>(),
                rSelf);

            var hbLayer = new HeartbeatLayer(tracker, mockLower, scheduler, 0.5f, rSelf);
            await hbLayer.Launch();

            var selfHeartbeats = mockLower.SentMessages
                .Where(m => m.Recipient == rSelf)
                .ToList();

            Assert.AreEqual(0, selfHeartbeats.Count,
                $"BUG: HeartbeatLayer sent {selfHeartbeats.Count} heartbeat(s) to ITSELF ({rSelf})! " +
                $"Field `replicaID` in HeartbeatLayer is uninitialized (0), so non-zero nodes fail " +
                $"to recognize their own ID and flood themselves with heartbeats.");
        }

        // =========================================================================
        // Bug 3: HeartbeatLayer uninitialized replicaID causes dropped node-0 heartbeats
        // =========================================================================

        [TestMethod]
        [Timeout(5000)]
        public async Task HeartbeatLayer_NonZeroReplica_MustSendHeartbeatsToReplica0()
        {
            // EXPECTED BEHAVIOR:
            // A node with ID != 0 (e.g. replica 1) MUST send heartbeats to replica 0 when connected.
            //
            // CURRENT BUG:
            // In HeartbeatLayer.cs, `replicaID` is default 0.
            // When replica 1 runs:
            //   if (replica != replicaID) ...
            // When replica is 0, (0 != 0) is FALSE!
            // Replica 1 drops replica 0 and never sends it any heartbeats.
            // In star topologies where node 0 is the center node, node 0 times out on all nodes,
            // corrupting active connection sets and causing message storms/routing failures.
            //
            // HOW THE TEST DETECTS THE BUG:
            // Create HeartbeatLayer for replica 1 with peer replica 0.
            // Start the layer and assert that at least one heartbeat was sent to replica 0.

            var mockLower = new ControllableMockLayer();
            var scheduler = new ControllableTestScheduler();
            var r0 = new ReplicaID(0);
            var r1 = new ReplicaID(1);

            var tracker = new ActiveConnectionTracker(
                new HashSet<ReplicaID> { r0 },
                new HashSet<ReplicaID>(),
                r1);

            var hbLayer = new HeartbeatLayer(tracker, mockLower, scheduler, 0.5f, r1);
            await hbLayer.Launch();

            var sentToNode0 = mockLower.SentMessages
                .Count(m => m.Recipient == r0);

            Assert.IsTrue(sentToNode0 > 0,
                $"BUG: Node 1 NEVER sent a heartbeat to node 0! " +
                $"Because `replicaID` in HeartbeatLayer defaults to 0, node 1 mistakenly treats node 0 " +
                $"as self and drops all heartbeats to node 0. Node 0 will time out on all connections.");
        }

        

        // =========================================================================
        // Bug 5: TrackActiveConnectionsLayer orphaned in CreateBasicFloodRoutingStack
        // =========================================================================

        [TestMethod]
        [Timeout(5000)]
        public void FloodingRoutingStack_MustIncludeTrackerLayerInChainBetweenHeartbeatAndTimeout()
        {
            // EXPECTED BEHAVIOR:
            // In CreateBasicFloodRoutingStack, the layer hierarchy must be:
            //   EnsureAckNetworkLayer -> FloodingRedirectionLayer -> HeartbeatLayer ->
            //   TrackActiveConnectionsLayer -> TrackMessageReceptionTimeoutLayer -> BaseLayer
            // Specifically, HeartbeatLayer.LayerBelow must be TrackActiveConnectionsLayer
            // (consistent with CreateBasicLinkStateStack).
            //
            // CURRENT BUG:
            // In Experiment.CreateBasicFloodRoutingStack:
            //   TrackActiveConnectionsLayer trackerLayer = new TrackActiveConnectionsLayer(timeoutLayer, tracker, id);
            //   HeartbeatLayer heartbeat = new HeartbeatLayer(tracker, timeoutLayer, scheduler, 0.1f);
            // `heartbeat` is constructed with `timeoutLayer` instead of `trackerLayer`!
            // `trackerLayer` is orphaned: it never participates in the layer hierarchy, so
            // Stop() and Run() never traverse it.

            var asm = LoadRunSimulationsAssembly();
            Assert.IsNotNull(asm, "Could not load RunSimulations.dll");

            var expType = asm.GetType("RunSimulations.FullyConnectedExperiment");
            Assert.IsNotNull(expType);

            var exp = Activator.CreateInstance(expType, 10.0f, 0.0f, 1, 2, 0.1f, 0.1f);
            var stackMethod = expType.GetMethod("CreateBasicFloodRoutingStack", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(stackMethod, "Could not find CreateBasicFloodRoutingStack method");

            var map = new ConnectionMap();
            var sim = new DiscreteSimulator(new Simulation(), map);
            var id = new ReplicaID(1);

            var result = stackMethod.Invoke(exp, new object[] { sim, id });
            Assert.IsNotNull(result);

            // result is ValueTuple (NetworkLayer, IScheduler, List<ExtraMetric>)
            var topLayer = result.GetType().GetField("Item1")?.GetValue(result) as NetworkLayer;
            Assert.IsNotNull(topLayer, "Top layer (ackLayer) must not be null");

            // ackLayer.LayerBelow -> FloodingRedirectionLayer
            var floodLayer = topLayer.LayerBelow;
            Assert.IsNotNull(floodLayer, "FloodingRedirectionLayer must not be null");
            Assert.IsInstanceOfType(floodLayer, typeof(FloodingRedirectionLayer));

            // floodLayer.LayerBelow -> HeartbeatLayer
            var heartbeatLayer = floodLayer.LayerBelow;
            Assert.IsNotNull(heartbeatLayer, "HeartbeatLayer must not be null");
            Assert.IsInstanceOfType(heartbeatLayer, typeof(HeartbeatLayer));

            // heartbeatLayer.LayerBelow MUST be TrackActiveConnectionsLayer, NOT timeoutLayer!
            var layerBelowHeartbeat = heartbeatLayer.LayerBelow;
            Assert.IsInstanceOfType(layerBelowHeartbeat, typeof(TrackActiveConnectionsLayer),
                $"BUG: HeartbeatLayer.LayerBelow is {layerBelowHeartbeat?.GetType().Name ?? "null"}, " +
                $"skipping TrackActiveConnectionsLayer! trackerLayer was created as an orphaned object " +
                $"and excluded from the layer hierarchy in CreateBasicFloodRoutingStack.");
        }
    }

    /// <summary>
    /// Lightweight behaviour stub used to simulate task completion in tests.
    /// </summary>
    internal class StubTaskBehaviour : Behaviour
    {
        public bool IsRunning { get; private set; }

        public override Task Launch()
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public override void Stop()
        {
            IsRunning = false;
        }

        public void CompleteTask()
        {
            CallBehaviourEnded();
        }
    }
}
