using CRDTApp.Messages;
using CRDTApp.NetworkManagers;
using CRDTs;
using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading.Channels;

namespace CRDTApp.NetworkManagers.Composite
{
    //TODO: for this to be fully correct, there should be a way to abstract the wait time into the scheduler, like "scheduler.await_task_or_timeout()"
    // Goal: ensuring causal delivery
    public class MessageHistoryTracker : NetworkManager
    {
        class TrackerMessage
        {
            public Message? Message { get; set; }

            public VersionVector CausalityTracker { get; set; }

            TrackerMessage()
            {
            }
            public TrackerMessage(Message? message, VersionVector causalityTracker)
            {

                Message = message;
                CausalityTracker = causalityTracker;


            }
            public string ToJson()
            {
                return JsonConvert.SerializeObject(this);
            }

            public static TrackerMessage? FromJson(string json)
            {
                return JsonConvert.DeserializeObject<TrackerMessage>(json);
            }
        }



        ReplicaID replicaID;
        NetworkManager networkManager;

        public MessageHistoryTracker(ReplicaID replicaID, NetworkManager networkManager, VersionVector? latest = null)
        {
            this.replicaID = replicaID;
            this.latest = latest ?? new VersionVector();
            pendingReceivedMessages = new();

            this.networkManager = networkManager;
            networkManager.AddOnMessageReceivedHandler(OnLowerLayerMessageReceived);
        }

VersionVector latest;
        List<(ReplicaID, TrackerMessage)> pendingReceivedMessages;


        public VersionVector IncrementVersion(ReplicaID id)
        {
            latest.Increment(id);
            return latest.Clone();
        }

        public void OnLowerLayerMessageReceived(ReplicaID senderID, Message lowerLayerMessage)
        {
            var trackerMessage = TrackerMessage.FromJson(lowerLayerMessage.Payload);

            if (trackerMessage == null)
            {
                throw new Exception("Error: unexpected message format!");
            }


            pendingReceivedMessages.Add((senderID, trackerMessage));



            bool shouldContinue = true;
            while (shouldContinue)
            {
                shouldContinue = false;
                for (int i = 0; i < pendingReceivedMessages.Count; i++)
                {
                    if (!latest.LacksDependencies(pendingReceivedMessages[i].Item2.CausalityTracker))
                    {
                        latest.Merge(pendingReceivedMessages[i].Item2.CausalityTracker);
                        var atPos = pendingReceivedMessages[i];
                        pendingReceivedMessages.RemoveAt(i);
                        CallMessageReceived(atPos.Item1, atPos.Item2.Message);
                        shouldContinue = true;
                        break;
                    }
                }
            }


        }





        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            var vector = IncrementVersion(replicaID);

            var trackerMessage = new TrackerMessage(message, vector);

            var networkMessage = new Message(trackerMessage.ToJson());

            return await networkManager.SendMessage(recipient, networkMessage);
        }


    }
}
