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
    internal class CompleteGraphExperiment : Experiment // assumes no broken connections, and no lost messages
    {
        public CompleteGraphExperiment(string prefix = "", int numReplicas = 10, float communicationDelay = 0.0f, float maxExperimentDuration = 100.0f, InsertionExperimentParams extraParams=null)
        {

            this.delay = communicationDelay;
            this.maxExperimentDuration = maxExperimentDuration;
            this.numReplicas = numReplicas;
            this.prefix = prefix;
            this.extraParams = extraParams ?? new InsertionExperimentParams();
        }
        string prefix;
        float maxExperimentDuration;
        float delay;
        int numReplicas = 10;
        InsertionExperimentParams extraParams;

        public override List<ExperimentResults> Run()
        {
            List<ExperimentResults> results = new();


            var CreateDiscreteSim = (ConnectionMap map) =>
            {
                Simulation sim = new ThreadSafeSimulation();
                return new DiscreteSimulator(sim, map);
            };



            var CreateMap = () => { return NetworkCreationFunctions.CreateFullyConectedNetwork(numReplicas, delay); };
            // Control case: no network stack.
            results.Add(RunIndividualFillArraySimulation(
                prefix + "fully-connected-no-stack",
                CreateMap,
                RoutingAlgorithmType.NONE,
                CreateDiscreteSim,
                maxExperimentDuration,
                NetworkCreationFunctions.BasicMetricCreator,
                extraParams
                ));


            // Simple Case: full stack, no routing
            results.Add(RunIndividualFillArraySimulation(prefix + "fully-connected-ack-no-routing",
                CreateMap,
                RoutingAlgorithmType.NONE_WITH_HEARTBEAT,
                CreateDiscreteSim,
                maxExperimentDuration,
                NetworkCreationFunctions.BasicMetricCreator,
                extraParams));


            // Flood routing case
            results.Add(RunIndividualFillArraySimulation(prefix + "fully-connected-basic-hop-flood",
                CreateMap,
                RoutingAlgorithmType.FLOODING,
                CreateDiscreteSim,
                maxExperimentDuration,
                NetworkCreationFunctions.BasicMetricCreator,
                extraParams));

            // Distance Vector case
            results.Add(RunIndividualFillArraySimulation(prefix + "fully-connected-basic-distance-vector",
                CreateMap,
                RoutingAlgorithmType.DISTANCE_VECTOR,
                CreateDiscreteSim,
                maxExperimentDuration,
                NetworkCreationFunctions.BasicMetricCreator,
                extraParams));


            // Link State case
            results.Add(RunIndividualFillArraySimulation(prefix + "fully-connected-basic-link-state",
                CreateMap,
                RoutingAlgorithmType.LINK_STATE,
                CreateDiscreteSim,
                maxExperimentDuration,
                NetworkCreationFunctions.BasicMetricCreator,
                extraParams));


            return results;
        }
    }
}
