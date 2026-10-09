using CRDTApp.Messages;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{
    public class AckMessagePayload<T> : Parseable<AckMessagePayload<T>>
    {
        public T Tag { get; set; }
        public string Message { get; set; }
        public bool IsAck { get; set; }

        public AckMessagePayload()
        {

        }

        public AckMessagePayload(T tag, Message message, bool isAck = false)
        {
            Tag = tag;
            Message = message.Encode();
            IsAck = isAck;
        }
        public AckMessagePayload(T tag, string message, bool isAck = false)
        {
            Tag = tag;
            Message = message;
            IsAck = isAck;
        }

    }


}
