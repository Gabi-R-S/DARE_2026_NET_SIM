using CRDTApp.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{
    public class EnsureAckMessage<T> : NetworkLayerMessage<EnsureAckMessage<T>>
    {
        public T Tag { get; set; }
        public Message? Message { get; set; }
        public bool IsAck { get; set; }

        public EnsureAckMessage()
        {

        }

        public EnsureAckMessage(T tag, Message message, bool isAck = false)
        {
            Tag = tag;
            Message = message;
            IsAck = isAck;
        }

    }


}
