using CRDTApp.Messages;
using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkManagers.Composite
{
    // Goal: ensure at-least-once delivery (or return false if impossible)
    internal class EnsureAckNetworkManager<T>: NetworkManager
    {
        class EnsureAckMessage {
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
            public string ToJson()
            {
                return JsonConvert.SerializeObject(this);
            }

            public static EnsureAckMessage? FromJson(string json)
            {
                return JsonConvert.DeserializeObject<EnsureAckMessage>(json);
            }

        }
        ReplicaID replicaID;
        Func<ReplicaID,T> GenerateNextTag;
        Dictionary<(T, ReplicaID), SemaphoreSlim> pendingAcks = new();
        
        NetworkManager lowerLayerNetworkManager;


        public EnsureAckNetworkManager(ReplicaID replicaID, NetworkManager lowerLayerNetworkManager, Func<ReplicaID, T> generateNextTag)
        {
            this.replicaID = replicaID;
            GenerateNextTag = generateNextTag;
            
            this.lowerLayerNetworkManager = lowerLayerNetworkManager;
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
                var ack = new EnsureAckMessage( pendingAckMessage.Tag, pendingAckMessage.Message,true);
                lowerLayerNetworkManager.SendMessage(senderID, new Message(ack.ToJson()));
                
                CallMessageReceived(senderID,pendingAckMessage.Message);
            }
        }



        const int NUMBER_OF_ATTEMPTS_UNTIL_TIMEOUT = 3;
        const int WAIT_TIME_MILLISECONDS = 1000;



        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            var newTag = GenerateNextTag(replicaID);

            var trackerMessage = new EnsureAckMessage(newTag,message);

            var networkMessage = new Message(trackerMessage.ToJson());

            for (int i = 0; i < NUMBER_OF_ATTEMPTS_UNTIL_TIMEOUT; i++)
            {
                var semaphore = new SemaphoreSlim(0);
                pendingAcks[(newTag, recipient)] = semaphore;

                if (!await lowerLayerNetworkManager.SendMessage(recipient, networkMessage))
                {
                    return false;
                }

                if (await semaphore.WaitAsync(WAIT_TIME_MILLISECONDS))
                {
                    return true;
                }


            }

            //CallConnectionInterrupted(recipient);

            return false;
        }


    }
}
