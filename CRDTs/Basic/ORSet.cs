using Newtonsoft.Json;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTs.Basic
{
    public class ORSet<T> where T : notnull
    {
        static Dictionary<T, HashSet<Tag>> DictionaryFromJsonParseResult(Dictionary<string, List<string>> res, Func<string, T> converter)
        {
            Dictionary<T, HashSet<Tag>> dic = new Dictionary<T, HashSet<Tag>>();


            foreach (var (k, v) in res) {
                HashSet<Tag> tags = new();
                foreach (var tag in v) {
                    tags.Add(new Tag(tag));
                }
                dic[converter(k)] = tags;
            
            }

            return dic;
        }

        public static ORSet<T> FromJSON(string json,Func<string,T> converter) {
            var obj=JsonConvert.DeserializeObject<Dictionary<string,Dictionary<string,List<string>>>>(json);

            if (obj == null) {

                throw new Exception("ERROR IN ORSET JSON FORMAT");
            }
            return new ORSet<T>(DictionaryFromJsonParseResult(obj["adds"], converter), DictionaryFromJsonParseResult(obj["removes"], converter));
            
        }

        string MapToJson(Dictionary<T, HashSet<Tag>> map, Func<T, string> converter) {
            var mapStr = "{";
            foreach (var (t, set) in adds)
            {
                var setStr = "[";

                foreach (var tag in set)
                {
                    setStr += "\"" + tag.ToString() + "\",";
                }
                if (set.Count > 0) {
                    setStr.Remove(setStr.Length-1);
                }
                setStr += "]";
                mapStr += "\"" + converter(t) + "\":" + setStr + ",";
            }

            if (adds.Count > 0)
            {
                mapStr.Remove(mapStr.Length - 1);
            }
            return mapStr;
        }


        public string ToJson(Func<T,string> converter) {
            var addsStr = MapToJson(adds, converter);

            var removesString = MapToJson(removes, converter);

            return "{\"adds\":"+addsStr+",\"removes\":"+removesString+"}";
        }

        public struct Tag
        {
            public override string ToString()
            {
                return tag;
            }
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
