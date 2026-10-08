using CRDTApp;
using CRDTApp.Behaviours;
using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using CRDTApp.NetworkLayers.Base;
using CRDTApp.NetworkLayers.Composite;
using CRDTApp.NetworkLayers.Composite.Routing;
using CRDTApp.Schedulers;
using Microsoft.VisualStudio.CodeCoverage;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace RunSimulations
{
    internal abstract class Experiment
    {

        public enum RoutingAlgorithmType
        {
            NONE,
            NONE_WITH_HEARTBEAT,
            FLOODING,
            DISTANCE_VECTOR,
            LINK_STATE
        }
        public ExperimentResults RunIndividualFillArraySimulation(string name,
            Func<(ConnectionMap map, List<ReplicaID> replicas)> createNetwork,
            RoutingAlgorithmType algorithm,
            Func<ConnectionMap, NetworkSimulator> simulatorCreator,
            float maxExperimentDuration,
            Func<NetworkSimulator, List<App>, List<ExtraMetric>> generalMetricsCreator,
            Dictionary<string, object> extraParams)
        {
            return RunSimulation(name, createNetwork, (sim) => { return CreateSimulatedInsertApps(sim, algorithm, generalMetricsCreator, extraParams); }, simulatorCreator, maxExperimentDuration);
        }


        public (List<App>, List<ExtraMetric>) CreateSimulatedInsertApps(NetworkSimulator simulator,
            RoutingAlgorithmType algorithm,
            Func<NetworkSimulator, List<App>, List<ExtraMetric>> generalMetricsCreator,
            Dictionary<string, object> extraParams)
        {

            List<App> apps = new List<App>();


            int i = 0;
            var replicas = simulator.ConnectionMap.GetAllKnownReplicas();
            foreach (var replica in simulator.ConnectionMap.GetAllKnownReplicas())
            {
                IScheduler scheduler = new SimulationScheduler(simulator);
                IsRunningTracker runningTracker = new IsRunningTracker();
                ActiveConnectionTracker connectionTracker = new ActiveConnectionTracker(new HashSet<ReplicaID>(), new HashSet<ReplicaID>(replicas), replica);



                int opsPerReplica = extraParams.TryGetValue("opsPerReplica", out var value)
                                                                                            ? (int)value
                                                                                            : 100;

                float delayBetweenSends = extraParams.TryGetValue("delayBetweenSends", out var value2)
                                                                                            ? (float)value2
                                                                                            : 5.0f;

                float delayBetweenOps = extraParams.TryGetValue("delayBetweenOps", out value2)
                                                                                            ? (float)value2
                                                                                            : 5.0f;

                float heartbeatTime= extraParams.TryGetValue("heartbeatTime", out value2)
                                                                                            ? (float)value2
                                                                                            : 1.0f;

                float timeoutTime = extraParams.TryGetValue("timeoutTime", out value2)
                                                                                            ? (float)value2
                                                                                            : 10.0f;

                int numHops = extraParams.TryGetValue("numHops", out value)
                                                                                            ? (int)value
                                                                                            : 5;

                float forgetingTime = extraParams.TryGetValue("forgetingTime", out value2)
                                                                                            ? (float)value2
                                                                                            : 30.0f;

                float ackTimeout = extraParams.TryGetValue("ackTimeout", out value2)
                                                                                            ? (float)value2
                                                                                            : 10.0f;

                float dvPollTime = extraParams.TryGetValue("dvPollTime", out value2)
                                                                                            ? (float)value2
                                                                                            : 10.0f;

                List<Behaviour> behaviours = new();
                List<NetworkLayer> layers = new List<NetworkLayer>();

                switch (algorithm)
                {
                    case RoutingAlgorithmType.NONE:
                        {
                            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, replica);
                            var insertBehaviour = new InsertBehaviour(runningTracker,
                                                                    baseLayer,
                                                                    scheduler,
                                                                    replica,
                                                                    replicas.ToArray(),
                                                                    i * opsPerReplica,
                                                                    (i + 1) * opsPerReplica - 1,
                                                                    i * replicas.Count,
                                                                    delayBetweenSends,
                                                                    delayBetweenOps);

                            behaviours.Add(insertBehaviour);
                        }
                        break;

                    case RoutingAlgorithmType.NONE_WITH_HEARTBEAT:
                        {
                            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, replica);
                            var insertBehaviour = new InsertBehaviour(runningTracker,
                                                                    baseLayer,
                                                                    scheduler,
                                                                    replica,
                                                                    replicas.ToArray(),
                                                                    i * opsPerReplica,
                                                                    (i + 1) * opsPerReplica - 1,
                                                                    i * replicas.Count,
                                                                    delayBetweenSends,
                                                                    delayBetweenOps);


                            var hearbeatBehaviour = new HeartbeatBehaviour(replica,
                                baseLayer,
                                runningTracker,
                                scheduler,
                                connectionTracker,
                                heartbeatTime,
                                timeoutTime);

                            behaviours.Add(insertBehaviour);
                            behaviours.Add(hearbeatBehaviour);
                        }
                        break;

                    case RoutingAlgorithmType.FLOODING:
                        {
                            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, replica);
                            
                            FloodingRedirectionLayer flood = new FloodingRedirectionLayer(replica, 
                                scheduler, 
                                connectionTracker, 
                                baseLayer, 
                                numHops, 
                                forgetingTime,
                                "flood");

                            int num = 0;
                            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(replica, 
                                flood, 
                                scheduler, 
                                (_) => { num++; return num; }, 
                                ackTimeout,
                                "ack");

                            var insertBehaviour = new InsertBehaviour(runningTracker,
                                                                    ackLayer,
                                                                    scheduler,
                                                                    replica,
                                                                    replicas.ToArray(),
                                                                    i * opsPerReplica,
                                                                    (i + 1) * opsPerReplica - 1,
                                                                    i * replicas.Count,
                                                                    delayBetweenSends,
                                                                    delayBetweenOps);


                            var hearbeatBehaviour = new HeartbeatBehaviour(replica,
                                baseLayer,
                                runningTracker,
                                scheduler,
                                connectionTracker,
                                heartbeatTime,
                                timeoutTime);

                            behaviours.Add(insertBehaviour);
                            behaviours.Add(hearbeatBehaviour);
                        }
                        break;
                    case RoutingAlgorithmType.DISTANCE_VECTOR:
                        {
                            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, replica);


                            DistanceVectorRoutingLayer distanceVectorRoutingLayer = new DistanceVectorRoutingLayer(replica, 
                                connectionTracker, 
                                baseLayer, 
                                scheduler, 
                                numHops, 
                                dvPollTime,
                                "dv");

                            int num = 0;
                            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(replica,
                                distanceVectorRoutingLayer,
                                scheduler,
                                (_) => { num++; return num; },
                                ackTimeout,
                                "ack");

                            var insertBehaviour = new InsertBehaviour(runningTracker,
                                                                    ackLayer,
                                                                    scheduler,
                                                                    replica,
                                                                    replicas.ToArray(),
                                                                    i * opsPerReplica,
                                                                    (i + 1) * opsPerReplica - 1,
                                                                    i * replicas.Count,
                                                                    delayBetweenSends,
                                                                    delayBetweenOps);


                            var hearbeatBehaviour = new HeartbeatBehaviour(replica,
                                baseLayer,
                                runningTracker,
                                scheduler,
                                connectionTracker,
                                heartbeatTime,
                                timeoutTime);

                            behaviours.Add(insertBehaviour);
                            behaviours.Add(hearbeatBehaviour);
                        }
                        break;

                    case RoutingAlgorithmType.LINK_STATE:
                        {
                            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, replica);


                            LinkStateRoutingLayer lsLayer = new LinkStateRoutingLayer(replica, 
                                baseLayer, connectionTracker,"ls");


                            int num = 0;
                            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(replica,
                                lsLayer,
                                scheduler,
                                (_) => { num++; return num; },
                                ackTimeout,
                                "ack");

                            var insertBehaviour = new InsertBehaviour(runningTracker,
                                                                    ackLayer,
                                                                    scheduler,
                                                                    replica,
                                                                    replicas.ToArray(),
                                                                    i * opsPerReplica,
                                                                    (i + 1) * opsPerReplica - 1,
                                                                    i * replicas.Count,
                                                                    delayBetweenSends,
                                                                    delayBetweenOps);


                            var hearbeatBehaviour = new HeartbeatBehaviour(replica,
                                baseLayer,
                                runningTracker,
                                scheduler,
                                connectionTracker,
                                heartbeatTime,
                                timeoutTime);

                            behaviours.Add(insertBehaviour);
                            behaviours.Add(hearbeatBehaviour);
                        }
                        break;

                    default:
                        throw new NotImplementedException();
                        
                }





                apps.Add(new App(runningTracker, behaviours, scheduler, layers));

                i++;
            }


            var metrics = generalMetricsCreator(simulator, apps);

            //TODO: call metrics generation, which needs to be passed as a parameter

            return (apps, metrics);
        }


        public ExperimentResults RunSimulation(string name,
            Func<(ConnectionMap map, List<ReplicaID> replicas)> createNetwork,
            Func<NetworkSimulator, (List<App>, List<ExtraMetric>)> createApps,
            Func<ConnectionMap, NetworkSimulator> simulatorCreator,
            float maxExperimentDuration = 100.0f)
        {
            (ConnectionMap map, List<ReplicaID> replicas) = createNetwork();

            int messagesSend = 0;
            NetworkSimulator simulator = simulatorCreator(map);
            simulator.AddMessageSendScheduledHandler((_) => { messagesSend++; });


            (List<App> apps, List<ExtraMetric> metrics) = createApps(simulator);

            foreach (var app in apps)
            {

                var launchTask = () => { app.Run(); };
                simulator.AddRunEvent(launchTask);
                simulator.AddCloseEvent(app.Close);

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
