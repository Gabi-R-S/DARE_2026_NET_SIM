using CRDTApp.Behaviours;
using CRDTApp.NetworkManagers;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp
{
    public class App
    {
        IBehaviour behaviour;
        NetworkManager networkManager;

        public App(IBehaviour behaviour, NetworkManager networkManager)
        {
            this.behaviour = behaviour;
            this.networkManager = networkManager;
        }
        
        public async Task Run()
        {
            var t1 = behaviour.Run();
            var t2 = networkManager.Run();

            await Task.WhenAll(t1, t2);
        }
    }
}
