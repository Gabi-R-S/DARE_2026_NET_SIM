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
        string tag;

        event Action<ReplicaID, string?, VersionVector>? MessageIssued;

        public void AddMessageIssuedHandler(Action<ReplicaID, string?, VersionVector> handler)
        {
                MessageIssued += handler;
        }
        public void RemoveMessageIssuedHandler(Action<ReplicaID, string?, VersionVector> handler)
        {
                MessageIssued -= handler;
        }


        ReplicaID replicaID;

        public HistoryTrackedMessage TagMessage(string message)
        {
            var version = IncrementVersion();
            return new HistoryTrackedMessage(message, version);
        }

        public CausalHistoryTracker(ReplicaID replicaID, VersionVector? latest = null, string tag="causal history")
        {
            this.replicaID = replicaID;
            this.Latest = latest?.Clone() ?? new VersionVector();
            pendingReceivedMessages = new();
            this.tag = tag;

        }


        public VersionVector Latest { get; private set; }
        List<(ReplicaID, HistoryTrackedMessage)> pendingReceivedMessages;


        public VersionVector IncrementVersion()
        {
            lock (Latest)
            {
                Latest.Increment(replicaID);
                return Latest.Clone();
            }
        }

        public void OnMessageReceived(ReplicaID senderID, Message lowerLayerMessage)
        {
            if (lowerLayerMessage.ProtocolTag != tag) {
                return;
            }

            var trackerMessage = HistoryTrackedMessage.Decode(lowerLayerMessage.Payload);

            if (trackerMessage == null)
            {
                throw new Exception("Error: unexpected message format!");
            }

            List<(ReplicaID, HistoryTrackedMessage)> toIssue= new();
            lock (Latest)
            {
                
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
                            toIssue.Add(atPos);
                            shouldContinue = true;
                            break;
                        }
                    }
                }
            }

            foreach (var (rep,messg) in toIssue)
            {
                MessageIssued?.Invoke(rep, messg.Message, messg.CausalityTracker);

            }

        }





    }
}
