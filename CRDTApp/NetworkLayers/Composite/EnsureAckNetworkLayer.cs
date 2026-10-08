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


        public EnsureAckNetworkLayer(ReplicaID replicaID,IsRunningTracker runningTracker, NetworkLayer lowerLayerNetworkManager, IScheduler scheduler, Func<ReplicaID, T> generateNextTag, float waitTime = 2.5f,string tag="") : base(tag,runningTracker,lowerLayerNetworkManager)
        {
            this.replicaID = replicaID;
            GenerateNextTag = generateNextTag;
            this.scheduler = scheduler;
            this.waitTime = waitTime;

            lowerLayerNetworkManager.AddOnMessageReceivedHandler(OnLowerLayerMessageReceived);
        }


        void OnAckReceived(T tag, ReplicaID senderID)
        {
            if (!RunningTracker.IsRunning)
            {
                return;
            }

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
            if (!RunningTracker.IsRunning)
            {
                return;
            }
            lock (pendingAcks)
            {
                var pendingAckMessage = AckMessagePayload<T>.Decode(lowerLayerMessage.Payload);

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
                    var ack = new AckMessagePayload<T>(pendingAckMessage.Tag, "", true);
                    LayerBelow?.SendMessage(senderID, new Message(Tag, ack.Encode()));

                    CallMessageReceived(senderID, Message.Decode(pendingAckMessage.Message));
                }
            }
        }



        const int NUMBER_OF_ATTEMPTS_UNTIL_TIMEOUT = 3;
        float waitTime = 0.5f;



        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {

            if (!RunningTracker.IsRunning)
            {
                return false;
            }

            var newTag = GenerateNextTag(replicaID);

            var trackerMessage = new AckMessagePayload<T>(newTag, message);

            var networkMessage = new Message(Tag,trackerMessage.Encode());

            for (int i = 0; i < NUMBER_OF_ATTEMPTS_UNTIL_TIMEOUT && !RunningTracker.IsRunning; i++)
            {
                var semaphore = new SemaphoreSlim(0);
                lock (pendingAcks)
                {
                    pendingAcks[(newTag, recipient)] = semaphore;
                }

                if (!await base.SendMessage(recipient, networkMessage))
                {
                    return false;
                }

                if (await scheduler.ExecuteTaskOrTimeout(async (_) => { await semaphore.WaitAsync(); }, waitTime))
                {
                    return true;
                }

            }

            return false;
        }


    }
}
