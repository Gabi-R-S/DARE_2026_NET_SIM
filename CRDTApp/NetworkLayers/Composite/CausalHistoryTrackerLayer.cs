using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using CRDTs;
using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading.Channels;

namespace CRDTApp.NetworkLayers.Composite
{
    // Goal: ensuring causal delivery

    /*
    public class CausalHistoryTrackerLayer : NetworkLayer
    {
        public class TrackerMessage :NetworkLayerMessage<TrackerMessage>
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
            
        }

        object messageIssuedLock = new object();
        event Action<ReplicaID, Message?, VersionVector>? MessageIssued;

        public void AddMessageIssuedHandler(Action<ReplicaID, Message?, VersionVector> handler) {
            lock (messageIssuedLock) {
                MessageIssued += handler;
            }
        }
        public void RemoveMessageIssuedHandler(Action<ReplicaID, Message?, VersionVector> handler)
        {
            lock (messageIssuedLock)
            {
                MessageIssued -= handler;
            }
        }


        ReplicaID replicaID;
        
        public CausalHistoryTrackerLayer(ReplicaID replicaID, NetworkLayer networkManager, VersionVector? latest = null): base(networkManager,false,true,true)
        {
            this.replicaID = replicaID;
            this.Latest = latest ?? new VersionVector();
            pendingReceivedMessages = new();

            networkManager.AddOnMessageReceivedHandler(OnLowerLayerMessageReceived);
        }


        public VersionVector Latest { get; private set; }
        List<(ReplicaID, TrackerMessage)> pendingReceivedMessages;


        public VersionVector IncrementVersion(ReplicaID id)
        {
            Latest.Increment(id);
            return Latest.Clone();
        }

        public void OnLowerLayerMessageReceived(ReplicaID senderID, Message lowerLayerMessage)
        {
            var trackerMessage = TrackerMessage.FromJson(lowerLayerMessage.Payload);

            if (trackerMessage == null)
            {
                throw new Exception("Error: unexpected message format!");
            }

            var comp = trackerMessage.CausalityTracker.Compare(Latest);
            if (comp != VersionVector.Comparison.ThisHappenedBeforeOther && comp != VersionVector.Comparison.Equal)
            {
                pendingReceivedMessages.Add((senderID, trackerMessage));
            }


            bool shouldContinue = true;
            while (shouldContinue)
            {     
                shouldContinue = false;
                for (int i = 0; i < pendingReceivedMessages.Count; i++)
                {
                    if (!Latest.LacksDependencies(pendingReceivedMessages[i].Item2.CausalityTracker))
                    {
                        Latest.Merge(pendingReceivedMessages[i].Item2.CausalityTracker);
                        var atPos = pendingReceivedMessages[i];
                        pendingReceivedMessages.RemoveAt(i);
                        CallMessageReceived(atPos.Item1, atPos.Item2.Message);
                        lock (messageIssuedLock)
                        {
                            MessageIssued?.Invoke(atPos.Item1, atPos.Item2.Message, atPos.Item2.CausalityTracker);
                        }
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

            return await LayerBelow.SendMessage(recipient, networkMessage);
        }


    }*/
}
