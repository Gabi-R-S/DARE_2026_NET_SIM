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
                new CompleteGraphExperiment(200.0f,0.01f,1,4),
                new CompleteGraphExperiment(2000.0f,0.01f,2,6),
                new RingTopologyExperiment(2000.0f, 0.01f, 10,10), 
                new StarTopologyExperiment(2000.0f,0.01f, 10,10),
                new RandomFullyConnectedExperiment(2000.0f, 0.01f,0.2f, 10,10,false,0.4f,0.1f,42),
                new RandomFullyConnectedExperiment(2000.0f, 0.01f,0.2f, 10,10,false,0.4f,0.1f,1),
                new RandomFullyConnectedExperiment(2000.0f, 0.01f,0.2f, 10,10,false,0.4f,0.1f,2),
            };

            

            List<ExperimentResults> results = new();

            foreach (var experiment in experiments) {
                var t = experiment.Run();
                t.Wait();
                results.AddRange(t.Result);
            }

            Console.WriteLine(JsonConvert.SerializeObject(results));

        }
    }
}
