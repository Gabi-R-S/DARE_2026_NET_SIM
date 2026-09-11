using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTs.Basic
{
    public class PNCounter
    {
        GCounter positive;
        GCounter negative;
        public PNCounter(Dictionary<ReplicaID, int> initialPositiveCounts, Dictionary<ReplicaID, int> initialNegativeCounts)
        {
            positive = new GCounter(initialPositiveCounts);
            negative = new GCounter(initialNegativeCounts);
        }
        public PNCounter()
        {
            positive = new GCounter();
            negative = new GCounter();
        }

        public int Value { get => positive.Value - negative.Value; }
        public PNCounter CreateIncrementOperation(ReplicaID replicaID) { 
            return new PNCounter(new Dictionary<ReplicaID, int> { { replicaID, 1 } }, new Dictionary<ReplicaID, int>());
        }
        public PNCounter CreateDecrementOperation(ReplicaID replicaID)
        {
            return new PNCounter(new Dictionary<ReplicaID, int>(), new Dictionary<ReplicaID, int> { { replicaID, 1 } });
        }
        public void Merge(PNCounter other) { 
            positive.Merge(other.positive);
            negative.Merge(other.negative);
        }
    }
}
