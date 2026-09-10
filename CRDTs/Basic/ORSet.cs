using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTs.Basic
{
    internal class ORSet<T> where T : notnull
    {

        public struct Tag
        {
            public readonly string tag;
            public Tag(string tag)
            {
                this.tag = tag;
            }
        }
        
        Dictionary<T, HashSet<Tag>> adds;
        Dictionary<T, HashSet<Tag>> removes;

        public ORSet(){
            adds = new Dictionary<T, HashSet<Tag>>();
            removes = new Dictionary<T, HashSet<Tag>>();
        }

        public ORSet(Dictionary<T, HashSet<Tag>> initialAdds, Dictionary<T, HashSet<Tag>> initialRemoves)
        {
            adds = initialAdds;
            removes = initialRemoves;
        }

        public List<T> GetValues()
        {
            // all values in adds that are not in removes
            var values = new List<T>();
            foreach (var kvp in adds)
            {
                bool seen_unique = false;
                foreach (var tag in kvp.Value)
                {
                    if (!removes.ContainsKey(kvp.Key) || !removes[kvp.Key].Contains(tag))
                    {
                        seen_unique = true;
                        break;
                    }
                }
                if (seen_unique)
                {
                    values.Add(kvp.Key);
                }
            }
            return values;
        }

        ulong counter = 0;
        Tag CreateNewTag(ReplicaID id, T value)
        {
            return new Tag($"{id}_{++counter}");
        }

        public ORSet<T> CreateAddOperation(ReplicaID id, T value) {
            return new ORSet<T>(
                new Dictionary<T, HashSet<Tag>>() { [value] = new HashSet<Tag>(new HashSet<Tag>()) { CreateNewTag(id, value) } }, 
                new Dictionary<T, HashSet<Tag>>());
        }
        public ORSet<T> CreateRemoveOperation(ReplicaID id, T value)
        {
            return new ORSet<T>(
                new Dictionary<T, HashSet<Tag>>(),
                new Dictionary<T, HashSet<Tag>>() { [value] = adds.GetValueOrDefault(value, new HashSet<Tag>()) });
        }

        public void Merge(ORSet<T> other)
        {
            foreach (var kvp in other.adds)
            {
                if (!adds.ContainsKey(kvp.Key))
                {
                    adds[kvp.Key] = new HashSet<Tag>();
                }
                adds[kvp.Key].UnionWith(kvp.Value);
            }
            foreach (var kvp in other.removes)
            {
                if (!removes.ContainsKey(kvp.Key))
                {
                    removes[kvp.Key] = new HashSet<Tag>();
                }
                removes[kvp.Key].UnionWith(kvp.Value);
            }
        }
    }
}
