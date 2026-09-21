using CRDTApp;
using CRDTApp.Behaviours;
using CRDTApp.NetworkLayers;
using CRDTApp.Schedulers;
using Shared;
using SimSharp;
using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace RunSimulations
{
    internal class FullyConnectedExperiment : Experiment // assumes no broken connections, and no lost messages
    {
        public FullyConnectedExperiment(float maxExperimentDuration = 100.0f, float delay = 0.0f, int opsPerReplica = 1000)
        {

            this.delay = delay;
            this.maxExperimentDuration = maxExperimentDuration;
            this.opsPerReplica = opsPerReplica;
        }

        int opsPerReplica;
        float maxExperimentDuration;
        float delay;



        ExperimentResults RunIndividualSimulation(string name, Func<NetworkSimulator, ReplicaID, (NetworkLayer, IScheduler, List<ExtraMetric>)> networkCreator, Func<ConnectionMap, NetworkSimulator> simulatorCreator)
        {
            (ConnectionMap map, List<ReplicaID> replicas) = CreateFullyConectedNetwork(5, delay);

            int messagesSend = 0;
            NetworkSimulator simulator = simulatorCreator(map);
            simulator.AddMessageSendScheduledHandler((_) => { messagesSend++; });


            List<ExtraMetric> metrics = new List<ExtraMetric>();
            List<Task> tasks = new List<Task>();
            int i = 0;
            foreach (var replica in replicas)
            {
                (NetworkLayer networkManager, IScheduler scheduler, List<ExtraMetric> extraMetrics) = networkCreator(simulator, replica);

                metrics.AddRange(extraMetrics);

                var app = new App();
                var otherReplicas = new List<ReplicaID>(replicas);
                otherReplicas.Remove(replica);
                tasks.Add(app.Run(new InsertBehaviour(app, networkManager, replica, otherReplicas.ToArray(), opsPerReplica * i, opsPerReplica * (i + 1) - 1, opsPerReplica * replicas.Count)));
                i++;
            }

            Console.WriteLine("starting simulation!");
            var sim = simulator.Run(maxExperimentDuration);
            sim.Wait();
            Console.WriteLine("simulation ended!");



            return new ExperimentResults(name, sim.Result.Item1, messagesSend, sim.Result.Item2, metrics);
        }

        public override List<ExperimentResults> Run()
        {
            List<ExperimentResults> results = new();


            var CreateDiscreteSim = (ConnectionMap map) =>
            {
                Simulation sim = new ThreadSafeSimulation(); 
                return new DiscreteSimulator(sim, map);
            };


            // Control case: no network stack.
            results.Add(RunIndividualSimulation("No network stack", CreateOnlyBaseLayer, CreateDiscreteSim));


            // Simple Case: full stack, no routing
            results.Add(RunIndividualSimulation("ack layer + no routing", CreateBasicNoRouteStack, CreateDiscreteSim));


            // Flood routing case
            results.Add(RunIndividualSimulation("ack layer + flood routing with max hop count", CreateBasicFloodRoutingStack, CreateDiscreteSim));

            // Distance Vector case
            results.Add(RunIndividualSimulation("ack + distance vector", CreateBasicDistanceVectorStack, CreateDiscreteSim));

            return results;
        }
    }
}
