using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTs
{
    internal class GSet<T>
    {

        HashSet<T> values;
        public GSet(HashSet<T> initialValues)
        {
            values = initialValues;
        }
        public GSet()
        {
            values = new HashSet<T>();
        }

        public GSet<T> CreateAddOperation(T value)
        {
            var newSet = new GSet<T>();
            newSet.values.Add(value);
            
            return newSet;
        }

        public void Merge(GSet<T> other)
        {
            foreach (var value in other.values)
            {
                values.Add(value);
            }
        }

    }
}
