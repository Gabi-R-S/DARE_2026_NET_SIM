using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using CRDTApp.Messages;
using CRDTApp.NetworkLayers;
using CRDTApp.Schedulers;
using CRDTs;
using CRDTs.Basic;
using Shared;
namespace CRDTApp.Behaviours
{
    public class InsertBehaviour : Behaviour
    {
        IsRunningTracker runningTracker;

        ORSet<int> ints = new();
        NetworkLayer network;
        ReplicaID[] otherReplicas;
        int minNumber, maxNumber, expectedCount;
        float delayBetweenSends, delayBetweenOps;
        ReplicaID replicaID;
        IScheduler scheduler;

        CausalHistoryTracker historyTracker;

        public InsertBehaviour(IsRunningTracker runningTracker,NetworkLayer network, IScheduler scheduler, ReplicaID ourID, ReplicaID[] otherIDs, int minNumber, int maxNumberInclusive, int expectedCount, float delayBetweenSends, float delayBetweenOps)
        {
            historyTracker = new CausalHistoryTracker(ourID);
            this.replicaID = ourID;
            this.otherReplicas = otherIDs;
            this.network = network;
            this.minNumber = minNumber;
            this.maxNumber = maxNumberInclusive;
            this.expectedCount = expectedCount;
            this.delayBetweenOps = delayBetweenOps;
            this.delayBetweenSends = delayBetweenSends;
            this.scheduler = scheduler;
            this.runningTracker = runningTracker;
            network.AddOnMessageReceivedHandler(historyTracker.OnMessageReceived);
            historyTracker.AddMessageIssuedHandler(OnMessageReceivedInSequence);
        }

        public int CurrentCount => ints.GetValues().Count;
        public int ExpectedCount => expectedCount;
        public ReplicaID ReplicaID => replicaID;
        public event Action<ReplicaID, int, int>? OperationApplied;

        void OnMessageReceivedInSequence(ReplicaID replica, string message, VersionVector vector)
        { //TODO: retrieve CRDT and merge it
            var op = ORSet<int>.FromJSON(message, (str) => { return int.Parse(str); });
            lock (ints)
            {
                ints.Merge(op);

                OperationApplied?.Invoke(replicaID, ints.GetValues().Count, expectedCount);

                if (ints.GetValues().Count == expectedCount)
                {
                    CallBehaviourEnded();
                }
            }
        }


        async Task SendUntilSuccess(ReplicaID replica, Message message)
        {
            bool succeeded;
            succeeded = await network.SendMessage(replica, message);
            if (!succeeded && runningTracker.IsRunning)
            {
                scheduler.ExecuteAfter(delayBetweenSends, () =>
                {
                    if (runningTracker.IsRunning)
                    {
                        SendUntilSuccess(replica, message);
                    }
                });
            }
        }



        async Task SendUpdateUntil(int currentNumber, int maxNumber)
        {
            if (currentNumber > maxNumber || !runningTracker.IsRunning)
            {
                return;
            }

            ORSet<int> addOp;
            lock (ints)
            {
                addOp = ints.CreateAddOperation(replicaID, currentNumber);

                ints.Merge(addOp);

                OperationApplied?.Invoke(replicaID, ints.GetValues().Count, expectedCount);

                if (ints.GetValues().Count == expectedCount)
                {
                    CallBehaviourEnded();
                }
            }

            Message message = new Message("app", historyTracker.TagMessage(addOp.ToJson((i) => i.ToString())).Encode());

            foreach (ReplicaID replica in otherReplicas)
            {
                SendUntilSuccess(replica, message);
            }

            scheduler.ExecuteAfter(delayBetweenOps, () => { SendUpdateUntil(currentNumber + 1, maxNumber); });

        }


        public override void Launch()
        {
            SendUpdateUntil(minNumber, maxNumber);
        }



        public void InsertManualValue(int val)
        {
            if (runningTracker.IsRunning) return;
            ORSet<int> addOp;
            lock (ints)
            {
                addOp = ints.CreateAddOperation(replicaID, val);
                ints.Merge(addOp);
                OperationApplied?.Invoke(replicaID, ints.GetValues().Count, expectedCount);

                if (ints.GetValues().Count == expectedCount)
                {
                    CallBehaviourEnded();
                }
            }
            Message message = new Message("app",historyTracker.TagMessage(addOp.ToJson((i)=>i.ToString())).Encode());
            foreach (ReplicaID replica in otherReplicas)
            {
                SendUntilSuccess(replica, message);
            }
        }
    }
}
