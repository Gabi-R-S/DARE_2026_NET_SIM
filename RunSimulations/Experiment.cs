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
        protected (NetworkLayer, IScheduler, List<ExtraMetric>) CreateOnlyBaseLayer(NetworkSimulator simulator, ReplicaID id) {
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, id);
            return (baseLayer, scheduler, new List<ExtraMetric>());

        }
        protected (NetworkLayer,IScheduler, List<ExtraMetric>) CreateBasicNoRouteStack(NetworkSimulator simulator,ReplicaID id) {
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator,id);
            int i = 0;
            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(id,baseLayer,scheduler, (_) => { i++; return i; });
            CausalHistoryTrackerLayer historyTrackerLayer = new CausalHistoryTrackerLayer(id,ackLayer);
            return (historyTrackerLayer,scheduler, new List<ExtraMetric>());
        }

        protected (NetworkLayer, IScheduler,List<ExtraMetric>) CreateBasicFloodRoutingStack(NetworkSimulator simulator, ReplicaID id)
        {
            var set = simulator.GetAllKnownReplicas();
            set.Remove(id);
            ActiveConnectionTracker tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>() { id}, set );
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, id);

            TrackMessageReceptionTimeoutLayer timeoutLayer = new TrackMessageReceptionTimeoutLayer(id,5.0f,scheduler,baseLayer,false);
            
            TrackActiveConnectionsLayer trackerLayer = new TrackActiveConnectionsLayer(timeoutLayer, tracker);
            
            HeartbeatLayer heartbeat = new HeartbeatLayer(tracker,timeoutLayer,scheduler,0.1f);

            FloodingRedirectionLayer flood = new FloodingRedirectionLayer(id, tracker,heartbeat,5);
            
            int i = 0;
            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(id, flood, scheduler, (_) => { i++; return i; });

            CausalHistoryTrackerLayer historyTrackerLayer = new CausalHistoryTrackerLayer(id,ackLayer);
            
            return (historyTrackerLayer, scheduler, new List<ExtraMetric>());

        }
        protected (NetworkLayer, IScheduler, List<ExtraMetric>) CreateBasicDistanceVectorStack(NetworkSimulator simulator, ReplicaID id)
        {
            var set = simulator.GetAllKnownReplicas();
            set.Remove(id);
            ActiveConnectionTracker tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>() { id }, set);
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, id);

            TrackMessageReceptionTimeoutLayer timeoutLayer = new TrackMessageReceptionTimeoutLayer(id, 5.0f, scheduler, baseLayer,false);

            TrackActiveConnectionsLayer trackerLayer = new TrackActiveConnectionsLayer(timeoutLayer, tracker);


            DistanceVectorRoutingLayer distanceVectorRoutingLayer = new DistanceVectorRoutingLayer(id,tracker,trackerLayer,scheduler);
            int i = 0;
            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(id, distanceVectorRoutingLayer, scheduler, (_) => { i++; return i; });

            CausalHistoryTrackerLayer historyTrackerLayer = new CausalHistoryTrackerLayer(id, ackLayer);

            return (historyTrackerLayer, scheduler, new List<ExtraMetric>());

        }
        protected (NetworkLayer, IScheduler, List<ExtraMetric>) CreateBasicLinkStateStack(NetworkSimulator simulator, ReplicaID id)
        {
            var set = simulator.GetAllKnownReplicas();
            set.Remove(id);
            ActiveConnectionTracker tracker = new ActiveConnectionTracker(new HashSet<ReplicaID>() { id }, set);
            IScheduler scheduler = new SimulationScheduler(simulator);
            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, id);

            TrackMessageReceptionTimeoutLayer timeoutLayer = new TrackMessageReceptionTimeoutLayer(id, 5.0f, scheduler, baseLayer, false);

            TrackActiveConnectionsLayer trackerLayer = new TrackActiveConnectionsLayer(timeoutLayer, tracker);
            HeartbeatLayer heartbeat = new HeartbeatLayer(tracker,trackerLayer,scheduler,0.5f);
            
            int i = 0;
            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(id, heartbeat, scheduler, (_) => { i++; return i; });
            LinkStateRoutingLayer lsLayer = new LinkStateRoutingLayer(id,ackLayer,tracker);
        
            
            CausalHistoryTrackerLayer historyTrackerLayer = new CausalHistoryTrackerLayer(id, lsLayer);

            return (historyTrackerLayer, scheduler, new List<ExtraMetric>());

        }


        protected static (ConnectionMap,List<ReplicaID>) CreateFullyConectedNetwork(int numReplicas,float delay=0.0f) {
            ConnectionMap map = new ConnectionMap();
            List<ReplicaID> replicaIDs= new List<ReplicaID>();

            for (int i = 0; i<numReplicas;i++) {
                replicaIDs.Add(new ReplicaID((ulong)i));
            }


            for (int i = 0; i < replicaIDs.Count; i++) {

                for (int j=i;j<replicaIDs.Count;j++) {
                    map.SetConnection(replicaIDs[i], replicaIDs[j], new NetworkConnection(delay));
                }
            }
        
            
            return (map,replicaIDs);
        }
        protected static (ConnectionMap, List<ReplicaID>) CreateStarNetwork(int numReplicas, int centerNode)
        {
            ConnectionMap map = new ConnectionMap();
            List<ReplicaID> replicaIDs = new List<ReplicaID>();

            for (int i = 0; i < numReplicas; i++)
            {
                replicaIDs.Add(new ReplicaID((ulong)i));
            }


            for (int i = 0; i < replicaIDs.Count; i++)
            {
                map.SetConnection(replicaIDs[i], replicaIDs[centerNode], new NetworkConnection());
            }


            return (map, replicaIDs);
        }

        public abstract List<ExperimentResults> Run();
    }
}
