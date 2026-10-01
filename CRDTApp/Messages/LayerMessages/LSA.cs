using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.Messages.LayerMessages
{


    public class LSA : NetworkLayerMessage<LSA>
    {
        public ReplicaID Replica { get; set; }
        public int Number { get; set; }

        public List<(ReplicaID, float)> Neighbours { get; set; }
        public LSA(ReplicaID replica, int number, List<(ReplicaID, float)> neighbours)
        {
            Replica = replica;
            Number = number;
            Neighbours = neighbours;
        }
    }

}
