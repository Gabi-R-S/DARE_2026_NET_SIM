using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.Schedulers;
using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkLayers.Composite
{
    // Goal: ensure at-least-once delivery (or return false if impossible)
    public class EnsureAckNetworkLayer<T> : NetworkLayer
    {
        IScheduler scheduler;
        ReplicaID replicaID;
        Func<ReplicaID, T> GenerateNextTag;
        Dictionary<(T, ReplicaID), SemaphoreSlim> pendingAcks = new();


        public EnsureAckNetworkLayer(ReplicaID replicaID, NetworkLayer lowerLayerNetworkManager, IScheduler scheduler, Func<ReplicaID, T> generateNextTag, float waitTime = 0.5f) : base(lowerLayerNetworkManager, false, true, true)
        {
            this.replicaID = replicaID;
            GenerateNextTag = generateNextTag;
            this.scheduler = scheduler;
            this.waitTime = waitTime;

            lowerLayerNetworkManager.AddOnMessageReceivedHandler(OnLowerLayerMessageReceived);
        }


        void OnAckReceived(T tag, ReplicaID senderID)
        {
            lock (pendingAcks)
            {
                if (pendingAcks.ContainsKey((tag, senderID)))
                {
                    pendingAcks[(tag, senderID)].Release();
                    pendingAcks.Remove((tag, senderID));
                }
            }
        }


        public void OnLowerLayerMessageReceived(ReplicaID senderID, Message lowerLayerMessage)
        {
            lock (pendingAcks)
            {
                var pendingAckMessage = EnsureAckMessage<T>.FromJson(lowerLayerMessage.Payload);

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
                    var ack = new EnsureAckMessage<T>(pendingAckMessage.Tag, new Message(""), true);
                    LayerBelow.SendMessage(senderID, new Message(ack.ToJson()));

                    CallMessageReceived(senderID, pendingAckMessage.Message);
                }
            }
        }



        const int NUMBER_OF_ATTEMPTS_UNTIL_TIMEOUT = 3;
        float waitTime = 0.5f;



        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            var newTag = GenerateNextTag(replicaID);

            var trackerMessage = new EnsureAckMessage<T>(newTag, message);

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
