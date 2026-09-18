using System;
using System.Collections.Generic;
using System.Text;
using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using CRDTs.Basic;
using Shared;
namespace CRDTApp.Behaviours
{
    public  class InsertBehaviour:IBehaviour
    {
        ORSet<int> ints = new ();
        App app;
        NetworkLayer network;
        ReplicaID[] otherReplicas;
        int minNumber,maxNumber,expectedCount;
        ReplicaID replicaID;
        public InsertBehaviour(App app,NetworkLayer network, ReplicaID ourID, ReplicaID[] otherIDs,int minNumber,int maxNumberInclusive, int expectedCount) {
            this.app = app;
            this.replicaID = ourID;
            this.otherReplicas = otherIDs;
            this.network = network;
            this.minNumber = minNumber;
            this.maxNumber = maxNumberInclusive;
            this.expectedCount = expectedCount;
            network.AddOnMessageReceivedHandler(OnMessageReceived);
        }

        void OnMessageReceived(ReplicaID replica, Message message) { //TODO: retrieve CRDT and merge it
            Console.WriteLine(message.Payload);
            var op = ORSet<int>.FromJSON(message.Payload, (str) => { return int.Parse(str); });
            ints.Merge(op);
        }


        async Task SendUntilSuccess(ReplicaID replica, Message message) {
            bool succeeded;
            do {
                succeeded = await network.SendMessage(replica,message); 
            }while (!succeeded);
        }

        public async Task Run() {

            List<Task> tasks = new List<Task>();
            for (int i =minNumber;i<=maxNumber;i++) {
                var addOp = ints.CreateAddOperation(replicaID,i);
                ints.Merge(addOp);
                Message message = new Message(addOp.ToJson((i) => { return i.ToString();})); 
                foreach (ReplicaID replica in otherReplicas) {
                    tasks.Add(SendUntilSuccess(replica,message));
                }
                Console.WriteLine("hi");
            }
            
            await Task.WhenAll(tasks);

            while (ints.GetValues().Count<expectedCount) {
                await Task.Yield();
            }
        }

    }
}
