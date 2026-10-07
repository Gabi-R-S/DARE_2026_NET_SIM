using System;
using System.Collections.Generic;
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
        ORSet<int> ints = new();
        NetworkLayer network;
        ReplicaID[] otherReplicas;
        int minNumber, maxNumber, expectedCount;
        float delayBetweenSends, delayBetweenOps;
        ReplicaID replicaID;
        bool running = false;
        IScheduler scheduler;

        CausalHistoryTracker historyTracker;

        public InsertBehaviour(NetworkLayer network, IScheduler scheduler, ReplicaID ourID, ReplicaID[] otherIDs, int minNumber, int maxNumberInclusive, int expectedCount, float delayBetweenSends, float delayBetweenOps)
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
            network.AddOnMessageReceivedHandler(historyTracker.OnMessageReceived);
            historyTracker.AddMessageIssuedHandler(OnMessageReceivedInSequence);
        }

        public int CurrentCount => ints.GetValues().Count;
        public int ExpectedCount => expectedCount;
        public ReplicaID ReplicaID => replicaID;
        public event Action<ReplicaID, int, int>? OperationApplied;

        void OnMessageReceivedInSequence(ReplicaID replica, Message? message, VersionVector vector)
        { //TODO: retrieve CRDT and merge it
            var op = ORSet<int>.FromJSON(message.Payload, (str) => { return int.Parse(str); });
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
            if (!succeeded && running)
            {
                scheduler.ExecuteAfter(delayBetweenSends, () =>
                {
                    if (running)
                    {
                        SendUntilSuccess(replica, message);
                    }
                });
            }
        }



        async Task SendUpdateUntil(int currentNumber, int maxNumber)
        {
            if (currentNumber > maxNumber || !running)
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

            Message message = historyTracker.TagMessage(new Message(addOp.ToJson((i) => { return i.ToString(); }))).AsMessage();

            foreach (ReplicaID replica in otherReplicas)
            {
                SendUntilSuccess(replica, message);
            }

            scheduler.ExecuteAfter(delayBetweenOps, () => { SendUpdateUntil(currentNumber + 1, maxNumber); });

        }


        public override async Task Run()
        {
            running = true;
            SendUpdateUntil(minNumber, maxNumber);
        }

        public override void Stop()
        {
            running = false;
        }

    

    }
}
