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
    public static class NetworkCreationFunctions
    {

   

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
            FloatMetric executionTime = new FloatMetric("execution_time", 0.0f);
            
            int count = 0;
            foreach (var app in apps)
            {
                foreach (var item in app.Behaviours)
                {
                    if (item is InsertBehaviour) {
                        var behaviour = item as InsertBehaviour;
                        
                        behaviour.AddBehaviourEndedHandler(() =>
                        {
                            count++;
                            if (count == apps.Count)
                            {
                                Console.WriteLine("Task ended!");
                                endedSuccessfully.Value = true;
                                executionTime.Value = (float)(sim.GetCurrentTime() - sim.GetRunStartTime()).TotalSeconds;
                                foreach (var a in apps)
                                {
                                    a.Close();
                                }
                            }
                        });
                    }
                }
                
            }

            metrics.Add(endedSuccessfully);
            metrics.Add(executionTime);


            return metrics;
        }

    }
}
