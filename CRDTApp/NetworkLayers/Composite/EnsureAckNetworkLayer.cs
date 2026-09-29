using CRDTApp.Messages;
using CRDTApp.Schedulers;
using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkLayers.Composite
{
    // Goal: ensure at-least-once delivery (or return false if impossible)
    public class EnsureAckNetworkLayer<T>: NetworkLayer
    {
        public class EnsureAckMessage : NetworkLayerMessage<EnsureAckMessage>
        {
            public T Tag { get; set; }
            public Message? Message { get; set; }
            public bool IsAck { get; set; }

            public EnsureAckMessage() { 
            
            }
            
            public EnsureAckMessage(T tag,Message message, bool isAck=false) {
                Tag = tag;
                Message = message;
                IsAck = isAck;
            }

        }

        IScheduler scheduler;
        ReplicaID replicaID;
        Func<ReplicaID,T> GenerateNextTag;
        Dictionary<(T, ReplicaID), SemaphoreSlim> pendingAcks = new();
        

        public EnsureAckNetworkLayer(ReplicaID replicaID, NetworkLayer lowerLayerNetworkManager,IScheduler scheduler, Func<ReplicaID, T> generateNextTag,float waitTime=0.5f):base(lowerLayerNetworkManager,false,true,true)
        {
            this.replicaID = replicaID;
            GenerateNextTag = generateNextTag;
            this.scheduler = scheduler;
            this.waitTime = waitTime;

            lowerLayerNetworkManager.AddOnMessageReceivedHandler(OnLowerLayerMessageReceived);
        }


        void OnAckReceived(T tag, ReplicaID senderID)
        {
            if (pendingAcks.ContainsKey((tag, senderID)))
            {
                pendingAcks[(tag, senderID)].Release();
                pendingAcks.Remove((tag, senderID));
            }
        }


        public void OnLowerLayerMessageReceived(ReplicaID senderID, Message lowerLayerMessage)
        {
            if (lowerLayerMessage == null) {
                //throw new Exception("WHY? THIS SHOULDN'T HAPPEN!");
                Console.WriteLine("Strange null thing!");
                return;
            }
            //Console.WriteLine(lowerLayerMessage.Payload);

            var pendingAckMessage = EnsureAckMessage.FromJson(lowerLayerMessage.Payload);

            if (pendingAckMessage == null)
            {
                throw new Exception("Error: unexpected message format!");
            }

            if (pendingAckMessage.IsAck)
            {
                OnAckReceived(pendingAckMessage.Tag, senderID);
            }
            else
            {
                var ack = new EnsureAckMessage( pendingAckMessage.Tag, new Message(""),true);
                LayerBelow.SendMessage(senderID, new Message(ack.ToJson()));
                
                CallMessageReceived(senderID,pendingAckMessage.Message);
            }
        }



        const int NUMBER_OF_ATTEMPTS_UNTIL_TIMEOUT = 3;
        float waitTime = 0.5f;



        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            var newTag = GenerateNextTag(replicaID);

            var trackerMessage = new EnsureAckMessage(newTag,message);

            var networkMessage = new Message(trackerMessage.ToJson());

            for (int i = 0; i < NUMBER_OF_ATTEMPTS_UNTIL_TIMEOUT && Running; i++)
            {
                var semaphore = new SemaphoreSlim(0);
                pendingAcks[(newTag, recipient)] = semaphore;

                if (!await LayerBelow.SendMessage(recipient, networkMessage))
                {
                    CallConnectionInterrupted(recipient);
                    return false;
                }

                if (await scheduler.ExecuteTaskOrTimeout(semaphore.WaitAsync(), waitTime))
                {
                    Console.WriteLine("ACK succeed");
                    return true;
                }

            }

            if (Running)
            {
                CallConnectionInterrupted(recipient);
            }
            return false;
        }


    }
}
