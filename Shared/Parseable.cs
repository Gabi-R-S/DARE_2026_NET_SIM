using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace Shared
{
    public class Parseable<T>
    {
        public virtual string Encode()
        {

            return JsonConvert.SerializeObject(this);
        }
        static public T? Decode(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
}
