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
    // Goal: ensuring causal delivery within the same "channel" and implementing at-least-once semantics
    public class MessageHistoryTracker
    {
        ReplicaID replicaID;
        INetworkManager networkManager;

        public MessageHistoryTracker(ReplicaID replicaID, INetworkManager networkManager)
        {
            this.replicaID = replicaID;
            this.networkManager = networkManager;
        }

        class TrackerMessage
        {
            public Message? Message { get; set; }
            public string Tag { get; set; }

            public VersionVector CausalityTracker { get; set; }

            public bool IsAck { get; set; }
            TrackerMessage()
            {
                Tag = "";
            }
            public TrackerMessage(Message? message, string tag, VersionVector causalityTracker, bool isAck = false)
            {

                Message = message;
                Tag = tag;
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


        class TrackerChannel
        {
            const int NUMBER_OF_ATTEMPTS_UNTIL_TIMEOUT = 3;
            const int WAIT_TIME_MILLISECONDS = 1000;
            VersionVector latest;
            List<(ReplicaID, TrackerMessage)> pendingReceivedMessages;
            Dictionary<(VersionVector, ReplicaID), SemaphoreSlim> pendingAcks;

            public TrackerChannel(VersionVector latest)
            {
                this.latest = latest;
                this.pendingAcks = new();
                pendingReceivedMessages = new();

            }
            public VersionVector IncrementVersion(ReplicaID id)
            {
                latest.Increment(id);
                return latest.Clone();
            }


            public async Task<bool> ScheduleSend(ReplicaID recipient, TrackerMessage message, INetworkManager manager)
            {

                var networkMessage = new Message(message.ToJson());

                for (int i = 0; i < NUMBER_OF_ATTEMPTS_UNTIL_TIMEOUT; i++)
                {
                    var semaphore = new SemaphoreSlim(0);
                    pendingAcks[(message.CausalityTracker, recipient)] = semaphore;

                    manager.SendMessage(recipient, networkMessage);

                    if (await semaphore.WaitAsync(WAIT_TIME_MILLISECONDS))
                    {
                        return await Task.FromResult(true);
                    }


                }

                return await Task.FromResult(false);
            }

            void OnAckReceived(VersionVector version, ReplicaID senderID)
            {
                if (pendingAcks.ContainsKey((version, senderID)))
                {
                    pendingAcks[(version, senderID)].Release();
                    pendingAcks.Remove((version, senderID));
                }
            }

            public void OnMessageReceived(ReplicaID senderID, TrackerMessage message, INetworkManager networkManager)
            {
                if (message.IsAck)
                {
                    OnAckReceived(message.CausalityTracker, senderID);
                }
                else
                {
                    var ack = new TrackerMessage(null, message.Tag, message.CausalityTracker, true);
                    networkManager.SendMessage(senderID, new Message(ack.ToJson()));

                    pendingReceivedMessages.Add((senderID, message));



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
                                MessageIssued?.Invoke(pendingReceivedMessages[i].Item2.Tag, pendingReceivedMessages[i].Item1, pendingReceivedMessages[i].Item2.Message, latest);
                                shouldContinue = true;
                                break;
                            }
                        }
                    }

                }
            }




            public event Action<string, ReplicaID, Message?, VersionVector>? MessageIssued;

        }





        Dictionary<string, TrackerChannel> tagsToChannels = new();


        Dictionary<ReplicaID, List<Action<string, ReplicaID, Message?, VersionVector>>> messageIssuedHandlers = new();


        public void AddOnMessageIssuedHandler(ReplicaID senderID, Action<string, ReplicaID, Message?, VersionVector> handler)
        {
            lock (messageIssuedHandlers)
            {
                if (!messageIssuedHandlers.ContainsKey(senderID))
                {
                    messageIssuedHandlers.Add(senderID, new());
                }
                messageIssuedHandlers[senderID].Add(handler);
            }
        }
        public void RemoveOnMessageIssuedHandler(ReplicaID senderID, Action<string, ReplicaID, Message?, VersionVector> handler)
        {
            lock (messageIssuedHandlers)
            {
                if (messageIssuedHandlers.ContainsKey(senderID))
                {
                    messageIssuedHandlers[senderID].Remove(handler);
                }
            }
        }
        void CallMessageIssued(string tag, ReplicaID id, Message? message, VersionVector versionVector)
        {
            lock (messageIssuedHandlers)
            {
                if (messageIssuedHandlers.ContainsKey(id))
                {
                    foreach (var handler in messageIssuedHandlers[id])
                    {
                        handler?.Invoke(tag, id, message, versionVector);
                    }
                }
            }
        }

        public void AddChannel(string tag, VersionVector vector)
        {
            tagsToChannels[tag] = new TrackerChannel(vector);
            tagsToChannels[tag].MessageIssued += CallMessageIssued;
        }


        public VersionVector? GenerateNextTagInChannel(string tag)
        {

            if (tagsToChannels.ContainsKey(tag))
            {
                var channel = tagsToChannels[tag];
                return channel.IncrementVersion(replicaID);
            }
            return null;
        }

        public void OnMessageReceived(ReplicaID senderID, Message message)
        {
            var trackerMessage = TrackerMessage.FromJson(message.Payload);

            if (trackerMessage != null)
            {
                var tag = trackerMessage.Tag;

                if (tagsToChannels.ContainsKey(tag))
                {
                    var channel = tagsToChannels[tag];

                    lock (channel)
                    {
                        channel.OnMessageReceived(senderID, trackerMessage, networkManager);
                    }
                }
            }
        }


        public async Task<bool> SendMessage(string tag, ReplicaID recipient, Message message, VersionVector vector)
        {
            if (tagsToChannels.ContainsKey(tag))
            {

                var channel = tagsToChannels[tag];
                
                return await channel.ScheduleSend(recipient, new TrackerMessage(message, tag, vector), networkManager);
               
            }

            return await Task.FromResult(false);
        }

    }
}
