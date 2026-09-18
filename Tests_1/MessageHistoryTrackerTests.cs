using CRDTApp.Messages;
using CRDTApp.NetworkLayers.Composite;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Tests_1
{
    [TestClass]
    public sealed class MessageHistoryTrackerTests
    {


        [TestMethod]
        public void MessageReception()
        {
            MockNetworkManager manager1 = new MockNetworkManager();
            ReplicaID id1 = new ReplicaID();
            CausalHistoryTrackerLayer tracker1 = new CausalHistoryTrackerLayer(id1, manager1);

            MockNetworkManager manager2 = new MockNetworkManager();
            ReplicaID id2 = new ReplicaID();
            CausalHistoryTrackerLayer tracker2 = new CausalHistoryTrackerLayer(id2, manager2);

            bool something = false;
            tracker1.AddOnMessageReceivedHandler((id, message) => {
                Console.WriteLine(message.Payload);
                if (message.Payload == "HI")
                {
                    something = true;
                }
            });


            var t =tracker2.SendMessage(id1,new Message("HI"));

            t.Wait();

            var res=t.Result;

            Assert.IsTrue(res);
            
            var topOfQueue = manager2.MessagesSent.Dequeue();

            Console.WriteLine(topOfQueue.Item2);


            manager1.ReceiveMessage(id2, topOfQueue.Item2);


            Assert.IsTrue(something);
        }
    }
}
