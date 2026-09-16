using CRDTApp.NetworkManagers;
using CRDTs;
using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading.Channels;

namespace CRDTApp.Messages
{
    // Goal: ensuring causal delivery and implementing at-least-once semantics 
    public class MessageHistoryTracker : NetworkManager
    {
        class TrackerMessage
        {
            public Message? Message { get; set; }

            public VersionVector CausalityTracker { get; set; }

            public bool IsAck { get; set; }
            TrackerMessage()
            {
            }
            public TrackerMessage(Message? message, VersionVector causalityTracker, bool isAck = false)
            {

                Message = message;
                CausalityTracker = causalityTracker;
                IsAck = isAck;

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
            this.pendingAcks = new();
            pendingReceivedMessages = new();

            this.networkManager = networkManager;
            networkManager.AddOnMessageReceivedHandler(OnLowerLayerMessageReceived);
        }


        const int NUMBER_OF_ATTEMPTS_UNTIL_TIMEOUT = 3;
        const int WAIT_TIME_MILLISECONDS = 1000;
        VersionVector latest;
        List<(ReplicaID, TrackerMessage)> pendingReceivedMessages;
        Dictionary<(VersionVector, ReplicaID), SemaphoreSlim> pendingAcks;


        public VersionVector IncrementVersion(ReplicaID id)
        {
            latest.Increment(id);
            return latest.Clone();
        }


        async Task ScheduleSend(ReplicaID recipient, TrackerMessage message, NetworkManager manager)
        {

            var networkMessage = new Message(message.ToJson());

            for (int i = 0; i < NUMBER_OF_ATTEMPTS_UNTIL_TIMEOUT; i++)
            {
                var semaphore = new SemaphoreSlim(0);
                pendingAcks[(message.CausalityTracker, recipient)] = semaphore;

                manager.SendMessage(recipient, networkMessage);

                if (await semaphore.WaitAsync(WAIT_TIME_MILLISECONDS))
                {
                    return;
                }


            }

            CallConnectionInterrupted(recipient);
        }

        void OnAckReceived(VersionVector version, ReplicaID senderID)
        {
            if (pendingAcks.ContainsKey((version, senderID)))
            {
                pendingAcks[(version, senderID)].Release();
                pendingAcks.Remove((version, senderID));
            }
        }

        public void OnLowerLayerMessageReceived(ReplicaID senderID, Message lowerLayerMessage)
        {
            var trackerMessage = TrackerMessage.FromJson(lowerLayerMessage.Payload);

            if (trackerMessage == null)
            {
                throw new Exception("Error: unexpected message format!");
            }

            if (trackerMessage.IsAck)
            {
                OnAckReceived(trackerMessage.CausalityTracker, senderID);
            }
            else
            {
                var ack = new TrackerMessage(null, trackerMessage.CausalityTracker, true);
                networkManager.SendMessage(senderID, new Message(ack.ToJson()));

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
                            pendingReceivedMessages.RemoveAt(i);
                            CallMessageReceived(pendingReceivedMessages[i].Item1, pendingReceivedMessages[i].Item2.Message);
                            shouldContinue = true;
                            break;
                        }
                    }
                }

            }
        }





        public override bool SendMessage(ReplicaID recipient, Message message)
        {
            var vector = IncrementVersion(replicaID);

            var _ = ScheduleSend(recipient, new TrackerMessage(message, vector), networkManager);


            return true;
        }


    }
}
