using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{
    public abstract class NetworkLayerMessage<T>
    {

        public virtual string ToJson()
        {

            return JsonConvert.SerializeObject(this);
        }
        static public T? FromJson(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

        public virtual Message AsMessage() {
            return new Message(ToJson()); 
        }

    }
}
