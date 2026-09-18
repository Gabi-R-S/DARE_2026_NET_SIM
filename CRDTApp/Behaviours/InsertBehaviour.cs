using System;
using System.Collections.Generic;
using System.Text;
using CRDTApp.Messages;
using CRDTApp.NetworkManagers;
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
        }

        void OnMessageReceived(ReplicaID replica, Message message) { //TODO: retrieve CRDT and merge it    
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

                Message message = new Message(""); //TODO: CRDT INTO JSON!
                foreach (ReplicaID replica in otherReplicas) {
                    tasks.Add(network.SendMessage(replica,message));
                }
            
            }
            
            await Task.WhenAll(tasks);

            while (ints.GetValues().Count<expectedCount) {
                await Task.Yield();
            }
        }

    }
}
