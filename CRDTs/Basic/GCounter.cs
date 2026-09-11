using Shared;
using System.Diagnostics.Metrics;

namespace CRDTs.Basic
{
    public class GCounter
    {
        public GCounter(Dictionary<ReplicaID, int> initialCounts)
        {
            counts = initialCounts;
        }
        public GCounter()
        {
            counts = new Dictionary<ReplicaID, int>();
        }

        public int Value
        {
            get
            {
                var sum = 0;
                foreach (var (k, v) in counts)
                {
                    sum += v;
                }
                return sum;
            }
        }
        Dictionary<ReplicaID, int> counts;
        public GCounter CreateAddOperation(ReplicaID id)
        {
            return new GCounter(new Dictionary<ReplicaID, int>() { [id] = counts.GetValueOrDefault(id, 0) + 1 });
        }

        public void Merge(GCounter other)
        {
            foreach (var kvp in other.counts)
            {
                counts[kvp.Key] = Math.Max(counts.GetValueOrDefault(kvp.Key, 0), kvp.Value);
            }
        }
    }
}
