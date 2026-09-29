using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using CRDTApp.Schedulers;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Behaviours
{
    public class TestBehaviour1 : Behaviour
    {
        IScheduler scheduler;
        NetworkLayer manager;

        ReplicaID otherReplica;
        bool keepSending = true;
        public TestBehaviour1(IScheduler scheduler, NetworkLayer manager, ReplicaID otherReplica)
        {
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

        void Send()
        {
            if (keepSending == true)
            {
                scheduler.ExecuteAfter(1, () =>
                {
                    if (keepSending)
                    {
                        var t = manager.SendMessage(otherReplica, new Message("HI"));
                        t.Wait();

                        if (!t.Result)
                        {
                            Console.WriteLine("Couldn't send message!");
                            Send();
                        }
                    }
                });
            }
        }
        public override async Task Run()
        {
            Send();

        }

        public override void Stop()
        {
            keepSending = false;
        }
    }
}
