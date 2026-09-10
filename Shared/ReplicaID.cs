using System;
using System.Collections.Generic;
using System.Text;

namespace Shared
{
    public class ReplicaID
    {
        public static bool operator ==(ReplicaID obj1, ReplicaID obj2)
        {
            return obj1.ID == obj2.ID;
        }
        public static bool operator !=(ReplicaID obj1, ReplicaID obj2)
        {
            return obj1.ID != obj2.ID;
        }
        public override bool Equals(object obj)
        {
            if (obj is ReplicaID) { 
                return this==(ReplicaID)obj;
            }
            return false;
        }
        public ulong ID { get; private set; }
        public ReplicaID(ulong id)
        {
            ID = id;
        }
    }
}
