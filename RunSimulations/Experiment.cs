using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using CRDTApp.NetworkLayers.Composite;
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
            HeartbeatLayer heartbeat = new HeartbeatLayer(tracker,baseLayer,scheduler,0.1f);

            int i = 0;
            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(id, heartbeat, scheduler, (_) => { i++; return i; });
            TrackActiveConnactionsLayer trackerLayer = new TrackActiveConnactionsLayer(ackLayer,tracker);
            
            FloodingRedirectionLayer flood = new FloodingRedirectionLayer(id, tracker,trackerLayer,5);

            CausalHistoryTrackerLayer historyTrackerLayer = new CausalHistoryTrackerLayer(id,flood);
            
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
