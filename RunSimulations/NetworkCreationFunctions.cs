using CRDTApp;
using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using CRDTApp.NetworkLayers.Base;
using CRDTApp.NetworkLayers.Composite;
using CRDTApp.NetworkLayers.Composite.Routing;
using CRDTApp.Schedulers;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace RunSimulations
{
    public static class NetworkCreationFunctions
    {
        public static float HEARTBEAT_TIME = 0.1f;
        public static float MESSAGE_TIMEOUT = 5.0f;
        public static (NetworkLayer, IScheduler, List<ExtraMetric>) CreateOnlyBaseLayer(NetworkSimulator simulator, ReplicaID id)
        {
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, id);
            return (baseLayer, scheduler, new List<ExtraMetric>());

        }
        public static (NetworkLayer, IScheduler, List<ExtraMetric>) CreateBasicNoRouteStack(NetworkSimulator simulator, ReplicaID id)
        {
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, id);
            int i = 0;
            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(id, baseLayer, scheduler, (_) => { i++; return i; });
            // CausalHistoryTrackerLayer historyTrackerLayer = new CausalHistoryTrackerLayer(id, ackLayer);
            return (ackLayer, scheduler, new List<ExtraMetric>());
        }

        public static (NetworkLayer, IScheduler, List<ExtraMetric>) CreateBasicFloodRoutingStack(NetworkSimulator simulator, ReplicaID id)
        {
            var set = simulator.GetAllKnownReplicas();
            set.Remove(id);
            ActiveConnectionTracker tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>() { id }, set, id);
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, id);

            TrackMessageReceptionTimeoutLayer timeoutLayer = new TrackMessageReceptionTimeoutLayer(id, MESSAGE_TIMEOUT, scheduler, baseLayer, false);

            TrackActiveConnectionsLayer trackerLayer = new TrackActiveConnectionsLayer(timeoutLayer, tracker, id);
            HeartbeatLayer heartbeat = new HeartbeatLayer(tracker, trackerLayer, scheduler, HEARTBEAT_TIME, id);

            FloodingRedirectionLayer flood = new FloodingRedirectionLayer(id, scheduler, tracker, heartbeat, 5, 60.0f);

            int i = 0;
            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(id, flood, scheduler, (_) => { i++; return i; });

            //    CausalHistoryTrackerLayer historyTrackerLayer = new CausalHistoryTrackerLayer(id, ackLayer);

            return (ackLayer, scheduler, new List<ExtraMetric>());

        }
        public static (NetworkLayer, IScheduler, List<ExtraMetric>) CreateBasicDistanceVectorStack(NetworkSimulator simulator, ReplicaID id)
        {
            var set = simulator.GetAllKnownReplicas();
            set.Remove(id);
            ActiveConnectionTracker tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>() { id }, set, id);
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, id);

            TrackMessageReceptionTimeoutLayer timeoutLayer = new TrackMessageReceptionTimeoutLayer(id, MESSAGE_TIMEOUT, scheduler, baseLayer, false);

            TrackActiveConnectionsLayer trackerLayer = new TrackActiveConnectionsLayer(timeoutLayer, tracker, id);

            DistanceTrackerLayer distanceTrackerLayer = new DistanceTrackerLayer(trackerLayer, tracker, id, scheduler);

            DistanceVectorRoutingLayer distanceVectorRoutingLayer = new DistanceVectorRoutingLayer(id, tracker, distanceTrackerLayer, scheduler, (uint)simulator.ConnectionMap.GetAllKnownReplicas().Count);
            int i = 0;
            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(id, distanceVectorRoutingLayer, scheduler, (_) => { i++; return i; });

            //CausalHistoryTrackerLayer historyTrackerLayer = new CausalHistoryTrackerLayer(id, ackLayer);

            return (ackLayer, scheduler, new List<ExtraMetric>());

        }
        public static (NetworkLayer, IScheduler, List<ExtraMetric>) CreateBasicLinkStateStack(NetworkSimulator simulator, ReplicaID id)
        {
            var set = simulator.GetAllKnownReplicas();
            set.Remove(id);
            ActiveConnectionTracker tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>() { id }, set, id);
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, id);

            TrackMessageReceptionTimeoutLayer timeoutLayer = new TrackMessageReceptionTimeoutLayer(id, MESSAGE_TIMEOUT, scheduler, baseLayer, false);

            TrackActiveConnectionsLayer trackerLayer = new TrackActiveConnectionsLayer(timeoutLayer, tracker, id);
            DistanceTrackerLayer distanceTrackerLayer = new DistanceTrackerLayer(trackerLayer, tracker, id, scheduler);
            HeartbeatLayer heartbeat = new HeartbeatLayer(tracker, distanceTrackerLayer, scheduler, HEARTBEAT_TIME, id);

            int i = 0;
            LinkStateRoutingLayer lsLayer = new LinkStateRoutingLayer(id, heartbeat, tracker);

            EnsureAckNetworkLayer<int> ack2Layer = new EnsureAckNetworkLayer<int>(id, lsLayer, scheduler, (_) => { i++; return i; }); // 

            //            CausalHistoryTrackerLayer historyTrackerLayer = new CausalHistoryTrackerLayer(id, ack2Layer);

            return (ack2Layer, scheduler, new List<ExtraMetric>());

        }


        public static (ConnectionMap, List<ReplicaID>) CreateFullyConectedNetwork(int numReplicas, float delay = 0.0f)
        {
            ConnectionMap map = new ConnectionMap();
            List<ReplicaID> replicaIDs = new List<ReplicaID>();

            for (int i = 0; i < numReplicas; i++)
            {
                replicaIDs.Add(new ReplicaID((ulong)i));
            }


            for (int i = 0; i < replicaIDs.Count; i++)
            {

                for (int j = i; j < replicaIDs.Count; j++)
                {
                    map.SetConnection(replicaIDs[i], replicaIDs[j], new NetworkConnection(delay));
                }
            }


            return (map, replicaIDs);
        }
        public static (ConnectionMap, List<ReplicaID>) CreateStarNetwork(int numReplicas, int centerNode, float delay)
        {
            ConnectionMap map = new ConnectionMap();
            List<ReplicaID> replicaIDs = new List<ReplicaID>();

            for (int i = 0; i < numReplicas; i++)
            {
                replicaIDs.Add(new ReplicaID((ulong)i));
            }


            for (int i = 0; i < replicaIDs.Count; i++)
            {
                map.SetConnection(replicaIDs[i], replicaIDs[centerNode], new NetworkConnection(delay));
            }


            return (map, replicaIDs);
        }

        public static (ConnectionMap, List<ReplicaID>) CreateRingNetwork(int numReplicas, float delay)
        {
            ConnectionMap map = new ConnectionMap();
            List<ReplicaID> replicaIDs = new List<ReplicaID>();

            for (int i = 0; i < numReplicas; i++)
            {
                replicaIDs.Add(new ReplicaID((ulong)i));
            }


            for (int i = 0; i < replicaIDs.Count; i++)
            {
                map.SetConnection(replicaIDs[i], replicaIDs[(i + 1) % replicaIDs.Count], new NetworkConnection(delay));
            }


            return (map, replicaIDs);
        }



        public static (ConnectionMap, List<ReplicaID>) CreateRandomFullyConnectedNetwork(int numReplicas, float minDelay, float maxDelay, int seed = 42, double extraEdgeProbability = 0.15)
        {
            ConnectionMap map = new ConnectionMap();
            List<ReplicaID> replicaIDs = new List<ReplicaID>();

            for (int i = 0; i < numReplicas; i++)
            {
                replicaIDs.Add(new ReplicaID((ulong)i));
            }

            var random = new Random(seed);

            for (int i = 1; i < numReplicas; i++)
            {
                int j = random.Next(i);
                map.SetConnection(replicaIDs[i], replicaIDs[j],
                    new NetworkConnection(minDelay + (float)(random.NextDouble() * (maxDelay - minDelay))));
            }

            for (int i = 0; i < numReplicas; i++)
            {
                for (int j = i + 1; j < numReplicas; j++)
                {
                    if (map.GetConnection(replicaIDs[i], replicaIDs[j]) != null) continue;

                    if (random.NextDouble() < extraEdgeProbability)
                    {
                        map.SetConnection(replicaIDs[i], replicaIDs[j],
                            new NetworkConnection(minDelay + (float)(random.NextDouble() * (maxDelay - minDelay))));
                    }
                }
            }

            return (map, replicaIDs);
        }

        public static List<ExtraMetric> BasicMetricCreator(NetworkSimulator sim, List<App> apps)
        {
            List<ExtraMetric> metrics = new List<ExtraMetric>();


            BooleanMetric endedSuccessfully = new BooleanMetric("ended_successfully", false);

            int count = 0;
            foreach (var app in apps)
            {
                app.Behaviour.AddBehaviourEndedHandler(() =>
                {
                    count++;
                    if (count == apps.Count)
                    {
                        Console.WriteLine("Task ended!");
                        endedSuccessfully.Value = true;
                        foreach (var a in apps)
                        {
                            a.Close();
                        }
                    }
                });
            }

            metrics.Add(endedSuccessfully);


            return metrics;
        }

    }
}
