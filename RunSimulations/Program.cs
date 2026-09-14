using CRDTApp;
using Shared;
using SimSharp;
using Simulator;

namespace RunSimulations
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Simulation env = new ThreadSafeSimulation();

            var connectionMap = new ConnectionMap();

            connectionMap.SetConnection(new ReplicaID(0), new ReplicaID(1), new NetworkConnection());
            connectionMap.SetConnection(new ReplicaID(1), new ReplicaID(0), new NetworkConnection());

            NetworkSimulator sim = new NetworkSimulator(env, connectionMap);
            
            var net1 = new SimulatedNetworkManager(sim, new ReplicaID(0));
            App app1 = new App(new TestBehaviour1(new SimulationScheduler(sim), net1, new ReplicaID(1)),net1);



            var net2 = new SimulatedNetworkManager(sim, new ReplicaID(1));
            App app2 = new App(new TestBehaviour1(new SimulationScheduler(sim), net2, new ReplicaID(0)), net2);

            var t1 = app1.Run();
            var t2 = app1.Run();
            env.Run();
            


            t1.Wait();
            t2.Wait();

        }
    }
}
