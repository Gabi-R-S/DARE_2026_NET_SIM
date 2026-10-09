using CRDTApp.Messages;
using CRDTApp.Messages.LayerMessages;
using CRDTApp.Schedulers;
using Shared;

class DistanceTable
{
    private readonly object lockObj = new object();
    private readonly ReplicaID self;

    // neighbor -> (link cost to neighbor, neighbor's advertised distances)
    private readonly Dictionary<ReplicaID, (float LinkCost, Dictionary<ReplicaID, float> Vector)> neighborVectors = new();

    // cache: destination -> routes sorted best-first (distance, next hop)
    private Dictionary<ReplicaID, List<(float Distance, ReplicaID NextHop)>> routes = new();

    public DistanceTable(ReplicaID id)
    {
        self = id;
        Recompute();
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

    public void Update(ReplicaID us, ReplicaID sender, DistanceTableUpdateMessagePayload update, ActiveConnectionTracker tracker)
    {
        lock (lockObj)
        {
            neighborVectors[sender] = (tracker.GetDistance(sender), ConvertIntsToReplicaIDs(update.Distances));
            Recompute();
        }
    }

    public void ResetConnection(ReplicaID replica)
    {
        lock (lockObj)
        {
            neighborVectors.Remove(replica);
            Recompute();   
        }
    }

    private void Recompute()
    {
        var result = new Dictionary<ReplicaID, List<(float, ReplicaID)>>();
        result[self] = new() { (0, self) };

        foreach (var (neighbor, (linkCost, vector)) in neighborVectors)
        {
            foreach (var (dest, advertised) in vector)
            {
                if (dest == self) continue;
                if (!result.TryGetValue(dest, out var list))
                    result[dest] = list = new();
                list.Add((Math.Max(advertised + linkCost, 0), neighbor));
            }
        }

        foreach (var list in result.Values)
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));

        routes = result;
    }

    public ReplicaID GetNextInPathTo(ReplicaID target)
    {
        lock (lockObj)
        {
            return routes.TryGetValue(target, out var list) && list.Count > 0
                ? list[0].NextHop
                : target;
        }
    }

    public DistanceTableUpdateMessagePayload GetUpdate(IScheduler scheduler, ReplicaID target)
    {
        var advertised = new Dictionary<int, float>();
        var timestamp = scheduler.GetCurrentTime();

        lock (lockObj)
        {
            foreach (var (dest, list) in routes)
            {
                foreach (var (distance, nextHop) in list)
                {
                    if (nextHop != target)
                    {
                        advertised[(int)dest.ID] = distance;
                        break;
                    }
                }
            }
        }
        return new DistanceTableUpdateMessagePayload(advertised, timestamp);
    }
}