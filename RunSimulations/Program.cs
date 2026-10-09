using CRDTApp;
using CRDTApp.Behaviours;
using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using CRDTApp.Schedulers;
using Newtonsoft.Json;
using Shared;
using SimSharp;
using Simulator;

namespace RunSimulations
{
    internal class Program
    {
        static void Main(string[] args)
        {
            
            List<Experiment> experiments = new List<Experiment>()
            {
                new CompleteGraphExperiment(
                    numReplicas: 4,
                    communicationDelay: 0.01f,
                    maxExperimentDuration: 200.0f,
                    extraParams: CreateInsertionParams(1)),
                new CompleteGraphExperiment(
                    numReplicas: 6,
                    communicationDelay: 0.01f,
                    maxExperimentDuration: 2000.0f,
                    extraParams: CreateInsertionParams(2)),
                new RingTopologyExperiment(
                    numReplicas: 10,
                    delay: 0.01f,
                    maxExperimentDuration: 2000.0f,
                    extraParams: CreateInsertionParams(10)),
                new StarTopologyExperiment(
                    numReplicas: 10,
                    centerNode: 0,
                    delay: 0.01f,
                    maxExperimentDuration: 2000.0f,
                    extraParams: CreateInsertionParams(10)),
                new RandomFullyConnectedExperiment(
                    numReplicas: 10,
                    minDelay: 0.01f,
                    maxDelay: 0.2f,
                    maxExperimentDuration: 2000.0f,
                    seed: 42,
                    extraParams: CreateInsertionParams(10)),
                new RandomFullyConnectedExperiment(
                    numReplicas: 10,
                    minDelay: 0.01f,
                    maxDelay: 0.2f,
                    maxExperimentDuration: 2000.0f,
                    seed: 1,
                    extraParams: CreateInsertionParams(10)),
                new RandomFullyConnectedExperiment(
                    numReplicas: 10,
                    minDelay: 0.01f,
                    maxDelay: 0.2f,
                    maxExperimentDuration: 2000.0f,
                    seed: 2,
                    extraParams: CreateInsertionParams(10)),
                new ScaleFreeNetworkExperiment(
                    numReplicas: 10,
                    minDelay: 0.01f,
                    maxDelay: 0.2f,
                    maxExperimentDuration: 2000.0f,
                    seed: 42,
                    extraParams: CreateInsertionParams(10)),
            };

            

            List<ExperimentResults> results = new();

            foreach (var experiment in experiments) {
                results.AddRange(experiment.Run());
            }

            Console.WriteLine(JsonConvert.SerializeObject(results));

        }

        private static Experiment.InsertionExperimentParams CreateInsertionParams(int opsPerReplica)
        {
            return new Experiment.InsertionExperimentParams(
                opsPerReplica: opsPerReplica,
                delayBetweenSends: 0.4f,
                delayBetweenOps: 0.1f,
                heartbeatTime: 0.1f,
                ackTimeout: 5.0f);
        }
    }
}
