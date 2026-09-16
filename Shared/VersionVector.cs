using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Shared
{
    //TODO: I THINK THIS ISN'T SERIALIZING CORRECTLY!
    public struct VersionVector
    {
        public override int GetHashCode()
        {
            int hash = 0;

            foreach (var pair in versions)
            {
                hash ^= HashCode.Combine(pair.Key, pair.Value);
            }

            return hash;
        }
        public static bool operator ==(VersionVector obj1, VersionVector obj2)
        {
            return obj1.IsEqual(obj2);
        }
        public static bool operator !=(VersionVector obj1, VersionVector obj2)
        {
            return !obj1.IsEqual(obj2);
        }
        public override bool Equals(object? obj)
        {
            if (obj is VersionVector)
            {
                return this == (VersionVector)obj;
            }
            return false;
        }


        //TODO: Check if correct
        public bool LacksDependencies(VersionVector newState) {
            int numIncreases = 0;

            foreach (var (replica, version) in newState.versions) {
                if (versions.ContainsKey(replica))
                {
                    if (version > versions[replica] + 1) {
                        return true;
                    }
                    if (version == versions[replica] + 1) {

                        numIncreases += 1;
                    }
                }
                else {
                    if (version > 1) {
                        return true;
                    }
                    numIncreases++;
                }

                if (numIncreases > 1) {
                    return true;
                }
            }
            return false;
        }


        public enum Comparison { 
            Concurrent,
            ThisHappenedBeforeOther,
            ThisHappenedAfterOther,
            Equal
        }

        bool IsEqual(VersionVector other) {
            
            foreach (var (k,v) in versions)
            {
                if (!other.versions.ContainsKey(k)) {
                    return false;
                }
                if (other.versions[k] != v) {
                    return false;
                }
            }
            return other.versions.Count == versions.Count; // before it was Dominates(other) && other.Dominates(this);

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
        public VersionVector Clone() { 
            return new VersionVector(new Dictionary<ReplicaID, ulong>(versions));;
        }

      
        public VersionVector CreateIncremented(ReplicaID id) {
            VersionVector newVector = Clone();
            if (!newVector.versions.ContainsKey(id)) {
                newVector.versions[id] = 0;
            }
            newVector.versions[id] += 1;

            return newVector;
        }



    }
}
