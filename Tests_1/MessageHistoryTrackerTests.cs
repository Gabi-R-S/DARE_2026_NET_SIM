using CRDTApp.Messages;
using CRDTApp.NetworkManagers.Composite;
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
            MessageHistoryTracker tracker1 = new MessageHistoryTracker(id1, manager1);

            MockNetworkManager manager2 = new MockNetworkManager();
            ReplicaID id2 = new ReplicaID();
            MessageHistoryTracker tracker2 = new MessageHistoryTracker(id2, manager2);

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
