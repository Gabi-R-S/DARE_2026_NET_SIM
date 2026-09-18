using CRDTApp;
using CRDTApp.Behaviours;
using CRDTApp.NetworkLayers;
using Shared;
using SimSharp;
using Simulator;
using System;
using System.Collections.Generic;
using System.Text;

namespace RunSimulations
{
    internal class FullyConnectedExperiment :Experiment // assumes no broken connections, and no lost messages
    {
        public FullyConnectedExperiment(float maxExperimentDuration = 100.0f,float delay= 0.0f, int opsPerReplica = 1000) {

            this.delay = delay;
            this.maxExperimentDuration = maxExperimentDuration;
            this.opsPerReplica = opsPerReplica;
        }

        int opsPerReplica = 1000;
        float maxExperimentDuration = 100.0f;
        float delay = 0.0f;
        public override List<ExperimentResults> Run()
        {
            List<ExperimentResults> results = new();



            // Control case: no network stack.

            Simulation sim = new ThreadSafeSimulation(); //ERROR: THINGS ARE DEADLOCKING AND I THINK IT COMES FROM HERE!
            (ConnectionMap map, List<ReplicaID> replicas) = CreateFullyConectedNetwork(20,delay);


            int messagesSend = 0;
            NetworkSimulator simulator = new NetworkSimulator(sim,map);
            simulator.AddMessageSendScheduledHandler((_) => { messagesSend++; });

           
            List<Task> tasks = new List<Task>();
            int i = 0;
            foreach (var replica in replicas) {
                
                var networkManager = new SimulatedNetworkBaseLayer(simulator,replica);
                
                var app = new App();
                var otherReplicas = new List<ReplicaID>(replicas);
                otherReplicas.Remove(replica);
                Console.WriteLine("here! "+(opsPerReplica*i).ToString()+" " + (opsPerReplica * (i+1)-1).ToString());
                tasks.Add(app.Run(new InsertBehaviour(app,networkManager,replica,otherReplicas.ToArray(),opsPerReplica*i,opsPerReplica*(i+1)-1,opsPerReplica*replicas.Count)));
                i++;
                Console.WriteLine("completed loop!");
            }

            Console.WriteLine("starting simulation!");            
            sim.RunD(maxExperimentDuration);

            Console.WriteLine("simulation ended!");

            Task.WaitAll(tasks);

            results.Add(new ExperimentResults("fully_connected_1",true,messagesSend,0,new List<ExtraMetric>()));// TODO: set correct finished successfully and completion time

            
            // Simple Case: full stack, no routing





            // Flood routing case

            

            return results;
        }
    }
}
