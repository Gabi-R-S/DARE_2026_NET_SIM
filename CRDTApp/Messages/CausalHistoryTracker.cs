using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTs
{
    public class CausalHistoryTracker
    {

        object messageIssuedLock = new object();
        event Action<ReplicaID, Message?, VersionVector>? MessageIssued;

        public void AddMessageIssuedHandler(Action<ReplicaID, Message?, VersionVector> handler)
        {
            lock (messageIssuedLock)
            {
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

        public HistoryTrackedMessage TagMessage(Message message) {
            var version = IncrementVersion();
            return new HistoryTrackedMessage(message,version);
        }

        public CausalHistoryTracker(ReplicaID replicaID, VersionVector? latest = null) 
        {
            this.replicaID = replicaID;
            this.Latest = latest?.Clone() ?? new VersionVector();
            pendingReceivedMessages = new();

        }


        public VersionVector Latest { get; private set; }
        List<(ReplicaID, HistoryTrackedMessage)> pendingReceivedMessages;


        public VersionVector IncrementVersion()
        {
            Latest.Increment(replicaID);
            return Latest.Clone();
        }

        public void OnMessageReceived(ReplicaID senderID, Message lowerLayerMessage)
        {
            var trackerMessage = HistoryTrackedMessage.FromJson(lowerLayerMessage.Payload);

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


      


    }
}
