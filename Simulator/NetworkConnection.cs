using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace Simulator
{
    public class NetworkConnection
    {
        public float TransmissionTime { get; set; }
        public bool Active { get; set; }

       

        List<(float, NetworkMessage)> messageQueue = new ();

        public NetworkConnection(float transmissionTime = 0.0f, bool active=true) {
            TransmissionTime = transmissionTime;
            Active = active;
        }

    }
}
