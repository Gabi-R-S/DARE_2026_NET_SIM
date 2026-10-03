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
