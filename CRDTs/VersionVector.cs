using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTs
{
    internal class VersionVector
    {
        public enum Comparison { 
            Concurrent,
            ThisHappenedBeforeOther,
            ThisHappenedAfterOther,
            Equal
        }

        bool IsEqual(VersionVector other) {
            return Dominates(other) && other.Dominates(this);
        }

        public bool Dominates(VersionVector other) {
            foreach (var (k,v) in versions)
            {
                if (other.versions.ContainsKey(k)) {
                    if (other.versions[k]>v) {
                        return false;
                    }
                }
            }

            foreach (var (k,v) in other.versions) {
                if (!versions.ContainsKey(k)) {
                    return false;
                }
            }

            return true;
        }

        public Comparison Compare(VersionVector other) {
            if (IsEqual(other)) {
                return Comparison.Equal;
            }
            if (Dominates(other)) {
                return Comparison.ThisHappenedAfterOther;
            }
            if (other.Dominates(this)) {
                return Comparison.ThisHappenedBeforeOther;
            }
            return Comparison.Concurrent;
        }

        public void Merge(VersionVector other) {
            foreach (var pair in other.versions) {
                if (this.versions.ContainsKey(pair.Key))
                {
                    versions[pair.Key] = pair.Value > versions[pair.Key] ? pair.Value : versions[pair.Key];
                }
                else
                {
                    versions[pair.Key] = pair.Value;
                }
            }
        }
        public VersionVector CreateMerged(VersionVector other) {
            var merged = Clone();
            merged.Merge(other);
            return merged;
        }

        Dictionary<ReplicaID, ulong> versions = new Dictionary<ReplicaID, ulong>();
        
        public VersionVector() { 
        
        }
        public VersionVector(Dictionary<ReplicaID, ulong> versions) { 
            this.versions = versions;
        }

        public void Increment(ReplicaID id)
        {
            if (!versions.ContainsKey(id))
            {
                versions[id] = 0;
            }
            versions[id] += 1;
        }
        VersionVector Clone() { 
            return new VersionVector(new Dictionary<ReplicaID, ulong>(versions));;
        }
        public VersionVector CreateIncremented(ReplicaID id) {
            VersionVector newVector = new VersionVector(new Dictionary<ReplicaID, ulong>(versions));
            if (!newVector.versions.ContainsKey(id)) {
                newVector.versions[id] = 0;
            }
            newVector.versions[id] += 1;

            return newVector;
        }



    }
}
