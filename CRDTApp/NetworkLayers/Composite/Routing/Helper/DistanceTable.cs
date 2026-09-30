using CRDTApp.Messages.LayerMessages;
using CRDTApp.Schedulers;
using Shared;
using System;
using System.Collections.Generic;
using System.Text;

namespace CRDTApp.NetworkLayers.Composite.Routing.Helper
{
     class DistanceTable
    {
        Dictionary<ReplicaID, (float, ReplicaID)> distances; // target -> (distance, next in sequence)

        public DistanceTable(ReplicaID id)
        {
            distances = new Dictionary<ReplicaID, (float, ReplicaID)>();
            distances[id] = (0, id);
        }

        public void ResetConnection(ReplicaID replica)
        {

            foreach (var (k, (v1, v2)) in distances)
            {
                if (v2 == replica)
                {
                    distances.Remove(k);
                }
            }
            distances.Remove(replica);
        }


        Dictionary<ReplicaID, T> ConvertIntsToReplicaIDs<T>(Dictionary<int, T> rep)
        {
            Dictionary<ReplicaID, T> reps = new Dictionary<ReplicaID, T>();

            foreach (var (k, v) in rep)
            {
                reps[new ReplicaID((ulong)k)] = v;
            }

            return reps;
        }

        public void Update(ReplicaID us, ReplicaID sender, DistanceTableUpdateMessage update, IScheduler scheduler)
        {
            var timeNow = scheduler.GetCurrentTime();
            var timeDifference = Math.Max((float)(timeNow - update.Timestamp).TotalSeconds,0);
            var updateDistances = ConvertIntsToReplicaIDs(update.Distances);
            foreach (var (targetDest, oldDistance) in updateDistances)
            {

                var newDistance = Math.Max(oldDistance + timeDifference, 0);
                if (!distances.ContainsKey(targetDest))
                {
                    Console.WriteLine("unknown dest " + sender + " seen at " + us);

                    distances[targetDest] = (newDistance, sender);
                }
                else
                {

                    if (sender == distances[targetDest].Item2)  // if the sender is the target
                    {
                        distances[targetDest] = (newDistance, sender);
                    }
                    else
                    {
                        //Console.WriteLine("known dest " + sender + " seen at " + us);
                        var currentValue = distances[targetDest].Item1;
                        if (newDistance < currentValue)
                        {
                            Console.WriteLine("overwritten!");
                            distances[targetDest] = (newDistance, sender);

                        }
                    }
                }

            }



            foreach (var (target, (distance, nextInRoute)) in distances)
            {
                if (nextInRoute == sender && !updateDistances.ContainsKey(target))
                {
                    distances.Remove(target);
                }

            }
        }

        public ReplicaID GetNextInPathTo(ReplicaID target)
        {
            if (distances.ContainsKey(target))
            {
                return distances[target].Item2;
            }
            return target;
        }

        public DistanceTableUpdateMessage GetUpdate(IScheduler scheduler,ReplicaID target)
        {
            Dictionary<int, float> messgDistances = new();
            var timestamp = scheduler.GetCurrentTime();

            foreach ((var dest, (float k, ReplicaID v)) in distances)
            {
                if (v != target)
                {
                    messgDistances[(int)dest.ID] = Math.Max(k,0);
                }
            }


            return new DistanceTableUpdateMessage(messgDistances, timestamp);
        }
    }
}
