using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTs
{
    public class LWWRegister<T> where T:IComparable
    {
        public LWWRegister(DateTime timestamp, T value) { 
            this.timestamp = timestamp;
            this.Value = value;
        }
        DateTime timestamp;
        public T Value { get; private set; }

        public LWWRegister<T> CreateWriteOperation(DateTime timestamp, T value) { 
            return new LWWRegister<T>(timestamp, value);
        }

        public void Merge(LWWRegister<T> other) { 
            if (other == null) return;
            if (other.timestamp > timestamp || (other.timestamp == timestamp && other.Value.CompareTo(Value) >0 ))
            {
                timestamp = other.timestamp;
                Value = other.Value;
            }
        }
    }
}
