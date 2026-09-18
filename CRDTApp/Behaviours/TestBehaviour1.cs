using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using CRDTApp.Schedulers;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Behaviours
{
    public class TestBehaviour1 :IBehaviour
    {
        IScheduler scheduler;
        NetworkLayer manager;

        ReplicaID otherReplica;
        public TestBehaviour1(IScheduler scheduler, NetworkLayer manager, ReplicaID otherReplica) { 
            this.scheduler = scheduler;
            this.manager = manager;
            this.otherReplica = otherReplica;
            
            manager.AddOnMessageReceivedHandler(ReceiveMessage);
        }

        void ReceiveMessage(ReplicaID id, Message message)
        {
                if (message != null)
                {
                    Console.WriteLine(message);
                }
             
        }

        void Send() {
            scheduler.ExecuteAfter(1, () => {
                var t = manager.SendMessage(otherReplica, new Message("HI"));
                t.Wait();
                if (!t.Result)
                {
                    Console.WriteLine("Couldn't send message!");
                    Send();
                }
            });
        }
        public async Task Run()
        {
            Send();
            
        }
    }
}
