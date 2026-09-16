using CRDTApp.Messages;
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

            manager2.SendMessage(id1,new Message("HI"));

            var topOfQueue = manager2.MessagesSent.Dequeue();

            manager1.ReceiveMessage(id2,topOfQueue.Item2);

            bool something = false;
            tracker1.AddOnMessageReceivedHandler((id,message) => {
                if (message.Payload == "HI")
                {
                    something = true;
                }
            });



            Assert.IsTrue(something);
        }
    }
}
