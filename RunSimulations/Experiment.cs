using CRDTApp;
using CRDTApp.Behaviours;
using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using CRDTApp.NetworkLayers.Base;
using CRDTApp.NetworkLayers.Composite;
using CRDTApp.NetworkLayers.Composite.Routing;
using CRDTApp.Schedulers;
using Microsoft.VisualStudio.CodeCoverage;
using Newtonsoft.Json.Linq;
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

        public class InsertionExperimentParams
        {

            public int opsPerReplica;
            public float delayBetweenSends;
            public float delayBetweenOps;
            public float heartbeatTime;
            public float timeoutTime;
            public int numHops;
            public float forgetingTime;
            public float ackTimeout;
            public float dvPollTime;

            public InsertionExperimentParams(
            int opsPerReplica = 100,
            float delayBetweenSends = 10.0f,
            float delayBetweenOps = 2.0f,
            float heartbeatTime = 1.0f,
            float timeoutTime = 15.0f,
            int numHops = 10,
            float forgetingTime = 60.0f,
            float ackTimeout = 5.0f,
            float dvPollTime = 10.0f)
            {
                this.opsPerReplica = opsPerReplica;
                this.delayBetweenSends = delayBetweenSends;
                this.delayBetweenOps = delayBetweenOps;
                this.heartbeatTime = heartbeatTime;
                this.timeoutTime = timeoutTime;
                this.numHops = numHops;
                this.forgetingTime = forgetingTime;
                this.ackTimeout = ackTimeout;
                this.dvPollTime = dvPollTime;

            }
        }

        public ExperimentResults RunIndividualFillArraySimulation(string name,
            Func<(ConnectionMap map, List<ReplicaID> replicas)> createNetwork,
            RoutingAlgorithmType algorithm,
            Func<ConnectionMap, NetworkSimulator> simulatorCreator,
            float maxExperimentDuration,
            Func<NetworkSimulator, List<App>, List<ExtraMetric>> generalMetricsCreator,
            InsertionExperimentParams extraParams)
        {
            return RunSimulation(name, createNetwork, (sim) => { return CreateSimulatedInsertApps(sim, algorithm, generalMetricsCreator, extraParams); }, simulatorCreator, maxExperimentDuration);
        }


        public (List<App>, List<ExtraMetric>) CreateSimulatedInsertApps(NetworkSimulator simulator,
            RoutingAlgorithmType algorithm,
            Func<NetworkSimulator, List<App>, List<ExtraMetric>> generalMetricsCreator,
            InsertionExperimentParams extraParams)
        {

            List<App> apps = new List<App>();


            int i = 0;
            var replicas = simulator.ConnectionMap.GetAllKnownReplicas();
            foreach (var replica in simulator.ConnectionMap.GetAllKnownReplicas())
            {
                IScheduler scheduler = new SimulationScheduler(simulator);
                IsRunningTracker runningTracker = new IsRunningTracker();
                ActiveConnectionTracker connectionTracker = new ActiveConnectionTracker(new HashSet<ReplicaID>(), new HashSet<ReplicaID>(replicas), replica);



                List<Behaviour> behaviours = new();
                List<NetworkLayer> layers = new List<NetworkLayer>();

                switch (algorithm)
                {
                    case RoutingAlgorithmType.NONE:
                        {
                            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator,runningTracker, replica);
                            var insertBehaviour = new InsertBehaviour(runningTracker,
                                                                    baseLayer,
                                                                    scheduler,
                                                                    replica,
                                                                    replicas.ToArray(),
                                                                    i * extraParams.opsPerReplica,
                                                                    (i + 1) * extraParams.opsPerReplica - 1,
                                                                    extraParams.opsPerReplica * replicas.Count,
                                                                    extraParams.delayBetweenSends,
                                                                    extraParams.delayBetweenOps);

                            behaviours.Add(insertBehaviour);
                            layers.Add(baseLayer);
                        }
                        break;

                    case RoutingAlgorithmType.NONE_WITH_HEARTBEAT:
                        {
                            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, runningTracker, replica);
                            var insertBehaviour = new InsertBehaviour(runningTracker,
                                                                    baseLayer,
                                                                    scheduler,
                                                                    replica,
                                                                    replicas.ToArray(),
                                                                    i * extraParams.opsPerReplica,
                                                                    (i + 1) * extraParams.opsPerReplica - 1,
                                                                    extraParams.opsPerReplica * replicas.Count,
                                                                    extraParams.delayBetweenSends,
                                                                    extraParams.delayBetweenOps);


                            var hearbeatBehaviour = new HeartbeatBehaviour(replica,
                                baseLayer,
                                runningTracker,
                                scheduler,
                                connectionTracker,
                                extraParams.heartbeatTime,
                                extraParams.timeoutTime);

                            behaviours.Add(insertBehaviour);
                            behaviours.Add(hearbeatBehaviour);
                            layers.Add(baseLayer);
                            
                        }
                        break;

                    case RoutingAlgorithmType.FLOODING:
                        {
                            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, runningTracker, replica);

                            FloodingRedirectionLayer flood = new FloodingRedirectionLayer(replica, runningTracker,
                                scheduler,
                                connectionTracker,
                                baseLayer,
                                extraParams.numHops,
                                extraParams.forgetingTime,
                                "flood");

                            int num = 0;
                            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(replica, runningTracker,
                                flood,
                                scheduler,
                                (_) => { num++; return num; },
                                extraParams.ackTimeout,
                                "ack");

                            var insertBehaviour = new InsertBehaviour(runningTracker,
                                                                    ackLayer,
                                                                    scheduler,
                                                                    replica,
                                                                    replicas.ToArray(),
                                                                    i * extraParams.opsPerReplica,
                                                                    (i + 1) * extraParams.opsPerReplica - 1,
                                                                    extraParams.opsPerReplica * replicas.Count,
                                                                    extraParams.delayBetweenSends,
                                                                    extraParams.delayBetweenOps);


                            var hearbeatBehaviour = new HeartbeatBehaviour(replica,
                                baseLayer,
                                runningTracker,
                                scheduler,
                                connectionTracker,
                                extraParams.heartbeatTime,
                                extraParams.timeoutTime);

                            behaviours.Add(insertBehaviour);
                            behaviours.Add(hearbeatBehaviour);
                            layers.Add(ackLayer);
                            layers.Add(baseLayer);
                            layers.Add(flood);
                        }
                        break;
                    case RoutingAlgorithmType.DISTANCE_VECTOR:
                        {
                            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, runningTracker, replica);


                            DistanceVectorRoutingLayer dvRoutingLayer = new DistanceVectorRoutingLayer(replica, runningTracker,
                                connectionTracker,
                                baseLayer,
                                scheduler,
                                extraParams.numHops,
                                extraParams.dvPollTime,
                                "dv");

                            int num = 0;
                            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(replica, runningTracker,
                                dvRoutingLayer,
                                scheduler,
                                (_) => { num++; return num; },
                                extraParams.ackTimeout,
                                "ack");

                            var insertBehaviour = new InsertBehaviour(runningTracker,
                                                                    ackLayer,
                                                                    scheduler,
                                                                    replica,
                                                                    replicas.ToArray(),
                                                                    i * extraParams.opsPerReplica,
                                                                    (i + 1) * extraParams.opsPerReplica - 1,
                                                                    extraParams.opsPerReplica * replicas.Count,
                                                                    extraParams.delayBetweenSends,
                                                                    extraParams.delayBetweenOps);


                            var hearbeatBehaviour = new HeartbeatBehaviour(replica,
                                baseLayer,
                                runningTracker,
                                scheduler,
                                connectionTracker,
                                extraParams.heartbeatTime,
                                extraParams.timeoutTime);

                            hearbeatBehaviour.AddConnectionRegainedHandler(dvRoutingLayer.OnConnectionGained);

                            behaviours.Add(insertBehaviour);
                            behaviours.Add(hearbeatBehaviour);

                            layers.Add(ackLayer);
                            layers.Add(baseLayer);
                            layers.Add(dvRoutingLayer);
                        }
                        break;

                    case RoutingAlgorithmType.LINK_STATE:
                        {
                            SimulatedNetworkBaseLayer baseLayer = new SimulatedNetworkBaseLayer(simulator, runningTracker, replica);


                            LinkStateRoutingLayer lsLayer = new LinkStateRoutingLayer(replica, runningTracker,
                                baseLayer, connectionTracker, "ls");


                            int num = 0;
                            EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(replica, runningTracker,
                                lsLayer,
                                scheduler,
                                (_) => { num++; return num; },
                                extraParams.ackTimeout,
                                "ack");

                            var insertBehaviour = new InsertBehaviour(runningTracker,
                                                                    ackLayer,
                                                                    scheduler,
                                                                    replica,
                                                                    replicas.ToArray(),
                                                                    i * extraParams.opsPerReplica,
                                                                    (i + 1) * extraParams.opsPerReplica - 1,
                                                                    extraParams.opsPerReplica * replicas.Count,
                                                                    extraParams.delayBetweenSends,
                                                                    extraParams.delayBetweenOps);


                            var hearbeatBehaviour = new HeartbeatBehaviour(replica,
                                baseLayer,
                                runningTracker,
                                scheduler,
                                connectionTracker,
                                extraParams.heartbeatTime,
                                extraParams.timeoutTime);

                            hearbeatBehaviour.AddConnectionInterruptedHandler(lsLayer.OnDisconnect);
                            hearbeatBehaviour.AddConnectionRegainedHandler(lsLayer.OnConnect);

                            behaviours.Add(insertBehaviour);
                            behaviours.Add(hearbeatBehaviour);

                            layers.Add(ackLayer);
                            layers.Add(baseLayer);
                            layers.Add(lsLayer);
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
