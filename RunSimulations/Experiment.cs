using CRDTApp;
using CRDTApp.Behaviours;
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
    internal abstract class Experiment
    {
        protected (NetworkLayer, IScheduler, List<ExtraMetric>) CreateOnlyBaseLayer(NetworkSimulator simulator, ReplicaID id)
        {
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, id);
            return (baseLayer, scheduler, new List<ExtraMetric>());

        }
        protected (NetworkLayer, IScheduler, List<ExtraMetric>) CreateBasicNoRouteStack(NetworkSimulator simulator, ReplicaID id)
        {
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, id);
            int i = 0;
            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(id, baseLayer, scheduler, (_) => { i++; return i; });
           // CausalHistoryTrackerLayer historyTrackerLayer = new CausalHistoryTrackerLayer(id, ackLayer);
            return (ackLayer, scheduler, new List<ExtraMetric>());
        }

        protected (NetworkLayer, IScheduler, List<ExtraMetric>) CreateBasicFloodRoutingStack(NetworkSimulator simulator, ReplicaID id)
        {
            var set = simulator.GetAllKnownReplicas();
            set.Remove(id);
            ActiveConnectionTracker tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>() { id }, set);
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, id);

            TrackMessageReceptionTimeoutLayer timeoutLayer = new TrackMessageReceptionTimeoutLayer(id, 5.0f, scheduler, baseLayer, false);

            TrackActiveConnectionsLayer trackerLayer = new TrackActiveConnectionsLayer(timeoutLayer, tracker,id);

            HeartbeatLayer heartbeat = new HeartbeatLayer(tracker, timeoutLayer, scheduler, 0.1f);

            FloodingRedirectionLayer flood = new FloodingRedirectionLayer(id,scheduler, tracker, heartbeat, 5,60.0f);

            int i = 0;
            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(id, flood, scheduler, (_) => { i++; return i; });

        //    CausalHistoryTrackerLayer historyTrackerLayer = new CausalHistoryTrackerLayer(id, ackLayer);

            return (ackLayer, scheduler, new List<ExtraMetric>());

        }
        protected (NetworkLayer, IScheduler, List<ExtraMetric>) CreateBasicDistanceVectorStack(NetworkSimulator simulator, ReplicaID id)
        {
            var set = simulator.GetAllKnownReplicas();
            set.Remove(id);
            ActiveConnectionTracker tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>() { id }, set);
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, id);

            TrackMessageReceptionTimeoutLayer timeoutLayer = new TrackMessageReceptionTimeoutLayer(id, 5.0f, scheduler, baseLayer, false);

            TrackActiveConnectionsLayer trackerLayer = new TrackActiveConnectionsLayer(timeoutLayer, tracker,id);


            DistanceVectorRoutingLayer distanceVectorRoutingLayer = new DistanceVectorRoutingLayer(id, tracker, trackerLayer, scheduler,(uint)simulator.ConnectionMap.GetAllKnownReplicas().Count);
            int i = 0;
            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(id, distanceVectorRoutingLayer, scheduler, (_) => { i++; return i; });

            //CausalHistoryTrackerLayer historyTrackerLayer = new CausalHistoryTrackerLayer(id, ackLayer);
            
            return (ackLayer, scheduler, new List<ExtraMetric>());

        }
        protected (NetworkLayer, IScheduler, List<ExtraMetric>) CreateBasicLinkStateStack(NetworkSimulator simulator, ReplicaID id)
        {
            var set = simulator.GetAllKnownReplicas();
            set.Remove(id);
            ActiveConnectionTracker tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>() { id }, set);
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, id);

            TrackMessageReceptionTimeoutLayer timeoutLayer = new TrackMessageReceptionTimeoutLayer(id, 5.0f, scheduler, baseLayer, false);

            TrackActiveConnectionsLayer trackerLayer = new TrackActiveConnectionsLayer(timeoutLayer, tracker,id);
            HeartbeatLayer heartbeat = new HeartbeatLayer(tracker, trackerLayer, scheduler, 0.5f);

            int i = 0;
            LinkStateRoutingLayer lsLayer = new LinkStateRoutingLayer(id, heartbeat, tracker);

            EnsureAckNetworkLayer<int> ack2Layer = new EnsureAckNetworkLayer<int>(id, lsLayer, scheduler, (_) => { i++; return i; }); // 

//            CausalHistoryTrackerLayer historyTrackerLayer = new CausalHistoryTrackerLayer(id, ack2Layer);

            return (ack2Layer, scheduler, new List<ExtraMetric>());

        }


        protected static (ConnectionMap, List<ReplicaID>) CreateFullyConectedNetwork(int numReplicas, float delay = 0.0f)
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
        protected static (ConnectionMap, List<ReplicaID>) CreateStarNetwork(int numReplicas, int centerNode, float delay)
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

        protected static (ConnectionMap, List<ReplicaID>) CreateRingNetwork(int numReplicas, float delay)
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

        protected List<ExtraMetric> BasicMetricCreator(NetworkSimulator sim, List<App> apps)
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
                        app.Close();
                    }
                });
            }

            metrics.Add(endedSuccessfully);


            return metrics;
        }

        protected ExperimentResults RunIndividualFillArraySimulation(string name, Func<(ConnectionMap map, List<ReplicaID> replicas)> createNetwork, Func<NetworkSimulator, ReplicaID, (NetworkLayer, IScheduler, List<ExtraMetric>)> networkStackCreator, Func<ConnectionMap, NetworkSimulator> simulatorCreator, float maxExperimentDuration, int opsPerReplica,float delayBetweenSends,float delayBetweenOps, Func<NetworkSimulator, List<App>, List<ExtraMetric>>? generalMetricsCreator = null)
        {
            (ConnectionMap map, List<ReplicaID> replicas) = createNetwork();

            int messagesSend = 0;
            NetworkSimulator simulator = simulatorCreator(map);
            simulator.AddMessageSendScheduledHandler((_) => { messagesSend++; });


            List<ExtraMetric> metrics = new List<ExtraMetric>();
            List<App> apps = new List<App>();
            int i = 0;
            foreach (var replica in replicas)
            {
                (NetworkLayer networkManager, IScheduler scheduler, List<ExtraMetric> extraMetrics) = networkStackCreator(simulator, replica);

                metrics.AddRange(extraMetrics);

                var otherReplicas = new List<ReplicaID>(replicas);
                otherReplicas.Remove(replica);


                var app = new App(new InsertBehaviour(networkManager, scheduler, replica, otherReplicas.ToArray(), opsPerReplica * i, opsPerReplica * (i + 1) - 1, opsPerReplica * replicas.Count,delayBetweenSends,delayBetweenOps), scheduler, networkManager);

                apps.Add(app);

                var launchTask = () => { return app.Run(); };
                simulator.AddRunEvent(launchTask);
                simulator.AddCloseEvent(() => { return Task.Run(app.Close); });
                i++;
            }

            if (generalMetricsCreator != null)
            {
                var generalMetrics = generalMetricsCreator(simulator, apps);
                if (generalMetrics != null)
                {
                    metrics.AddRange(generalMetrics);
                }
            }

            Console.WriteLine("starting simulation " + name);
            var sim = simulator.Run(maxExperimentDuration);
            sim.Wait();
            Console.WriteLine("simulation ended!");



            return new ExperimentResults(name, messagesSend, metrics);
        }


        public abstract List<ExperimentResults> Run();
    }
}
