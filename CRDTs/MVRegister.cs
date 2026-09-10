using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTs
{
    internal class MVRegister<T> 
    {
        List<(VersionVector,T)> values = new ();

        VersionVector Latest {
            get {
                VersionVector v = new VersionVector();
                foreach (var (k, pv) in values) {
                    v.Merge(k);
                }
                return v;
            }
        }
        public MVRegister() {  
        }

        public MVRegister(List<(VersionVector, T)> values) { 
            this.values = values;
        }
        
        public List<T> Read() { 
            List<T> list = new List<T>();
            foreach (var v in values) 
            {
                if (!list.Contains(v.Item2))
                {
                    list.Add(v.Item2);
                }
            }
            return list;
        }

        public MVRegister<T> CreateWriteOperation(ReplicaID id, T value) {
            var latest = Latest;
            latest.Increment(id);

            return new MVRegister<T>(new List<(VersionVector, T)>([(latest, value)]));
        }

        public void Merge(MVRegister<T> other) { 
            values.AddRange(other.values);

            for (var i = 0; i < values.Count; i++) {
                bool skip = false; 
                for (var j = i+1; j < other.values.Count && !skip; j++) {
                    
                    switch (values[i].Item1.Compare(values[j].Item1)){
                        case VersionVector.Comparison.ThisHappenedAfterOther:

                            values.RemoveAt(j);
                            j--;
                            break;

                        case VersionVector.Comparison.Equal:

                            values.RemoveAt(j);
                            j--;
                            break;

                        case VersionVector.Comparison.ThisHappenedBeforeOther:
                            values.RemoveAt(i);
                            skip = true;
                            i--;
                            break;
                        case VersionVector.Comparison.Concurrent:
                            break;
                    }
                }
            }
        }
    }
}
