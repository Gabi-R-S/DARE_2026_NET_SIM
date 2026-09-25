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
            
            List<Experiment> experiments = new List<Experiment>() { new FullyConnectedExperiment(100.0f,0.1f,1)};

            

            List<ExperimentResults> results = new();

            foreach (var experiment in experiments) {
                results.AddRange(experiment.Run());
            }

            Console.WriteLine(JsonConvert.SerializeObject(results));

        }
    }
}
