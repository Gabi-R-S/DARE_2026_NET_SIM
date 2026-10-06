# Concurrency & Parallelism Bug Audit — v2

> **Date:** 2026-10-06 | **Auditor:** Antigravity AI
> **Scope:** All C# source files in `DistributedMeshSimulation` (re-analysed after user fixes)
> **Constraint:** Read-only — no source files were modified during this audit.

---

## What Changed Since v1

The commit (`git diff HEAD~1 HEAD`) touched **7 files**:

| File | Summary of Change |
|------|-------------------|
| `DistanceVectorRoutingLayer.cs` | Minor parameter threading (heartbeat wired in) |
| `CompleteGraphExperiment.cs` | `messageTimeout`/`heartbeatTime` fields extracted; factory lambdas for stack creation |
| `NetworkCreationFunctions.cs` | Stack-creation functions now accept explicit `messageTimeout`/`heartbeatTime`; static mutable statics `HEARTBEAT_TIME` / `MESSAGE_TIMEOUT` **retained but no longer written from UI** |
| `RandomFullyConnectedExperiment.cs` | Same factory-lambda refactor |
| `RingTopologyExperiment.cs` | Same |
| `StarTopologyExperiment.cs` | Same |
| `SimulationPage.xaml.cs` | Removed direct writes to `NetworkCreationFunctions.HEARTBEAT_TIME/MESSAGE_TIMEOUT`; stack creation delegated to `NetworkCreationFunctions.*Stack(…)` helpers |

---

## Severity Legend

| Severity | Meaning |
|----------|---------|
| 🔴 **Critical** | Will crash or corrupt data under concurrent use |
| 🟠 **High** | Race conditions / undefined behaviour very likely under normal load |
| 🟡 **Medium** | Possible data race or logic error; may surface under timing pressure |
| 🟢 **Low / Advisory** | Not immediately dangerous but fragile |

---

## Summary Heatmap

| # | File | Severity | Status vs v1 | Short Description |
|---|------|----------|--------------|-------------------|
| 1 | `DiscreteSimulator.cs` L162 | 🔴 | **STILL PRESENT** | `connectionsToMessageEvents[con].Add()` inside `foreach` same list |
| 2 | `EnsureAckNetworkLayer.cs` L88 | 🔴 | **STILL PRESENT** | Unsynchronised write `pendingAcks[key] = sem` before lock |
| 3 | `CausalHistoryTracker.cs` L87 | 🟠 | **STILL PRESENT** | `MessageIssued` invoked inside `lock(Latest)` — lock held across arbitrary delegate calls |
| 4 | `HeartbeatLayer.cs` L32 | 🟠 | **STILL PRESENT** | `sendingTo` HashSet written in `OnConnectionGained` (no lock) while read in `Run()` |
| 5 | `FloodingRedirectionLayer.cs` L86 | 🟠 | **STILL PRESENT** | `rememberedMessages.Contains()` called outside lock in `OnLowerLayerMessageReceived` |
| 6 | `LinkStateRoutingLayer.cs` L178 | 🟠 | **STILL PRESENT** | `mostRecentLSAs` iterated in `OnConnect` without lock |
| 7 | `InsertBehaviour.cs` | 🟠 | **STILL PRESENT** | `ints` (ORSet), `running`, and `OperationApplied` accessed from multiple threads unsafely |
| 8 | `NetworkCreationFunctions.cs` L18–19 | 🟠 | **PARTIALLY FIXED** | Static mutable `HEARTBEAT_TIME`/`MESSAGE_TIMEOUT` no longer written from UI, but still globally mutable fields — any concurrent experiment run races |
| 9 | `TCPNetworkBaseLayer.cs` L126 | 🟠 | **STILL PRESENT** | Concurrent writes to same `NetworkStream` from multiple `SendMessage` calls |
| 10 | `DistanceVectorRoutingLayer.cs` L145–155 | 🟠 | **STILL PRESENT** | New `HashSet` assigned then immediately `lock`-ed; lock acquired on the wrong object after reassignment |
| 11 | `ConnectionMap.cs` | 🟡 | **STILL PRESENT** | No synchronisation on nested dictionary; callers lock `ConnectionMap` object inconsistently |
| 12 | `ORSet.cs` L124 | 🟡 | **STILL PRESENT** | Non-atomic `++counter`; `adds`/`removes` dictionaries unprotected |
| 13 | `CausalHistoryTracker.cs` L17–21 | 🟡 | **STILL PRESENT** | `MessageIssued += handler` / `-= handler` not synchronised; C# event `+=` is safe for simple delegates but not for the custom AddHandler pattern |
| 14 | `Experiment.cs` L63 | 🟡 | **STILL PRESENT** | `sim.Wait()` synchronously blocks caller; deadlock risk if called from thread pool thread |
| 15 | `BasicMetricCreator` in `NetworkCreationFunctions.cs` L232–248 | 🟡 | **STILL PRESENT** | Non-atomic `count++` inside concurrent `BehaviourEnded` callbacks |
| 16 | `RealtimeSimulator.cs` L166 | 🟢 | **FIXED ✅** | `foreach` with `.ToArray()` snapshot now used in `ChangeTimeOfCommunicationsBetween` |
| 17 | `RealtimeScheduler.cs` | 🟢 | **FIXED ✅** | Timer `time * 1000` correct; `await Task.WhenAny` correct |
| 18 | `TCPNetworkBaseLayer.cs` | 🟢 | **FIXED ✅** | `RemoveAll` replaces `RemoveAt(-1)`; `Task.WhenAll` replaces `Task.WaitAll`; zero-byte break added |
| 19 | `TrackActiveConnectionsLayer.cs` L59 | 🟢 | **FIXED ✅** | Inverted `CallConnectionRegained`/`CallConnectionInterrupted` logic corrected |
| 20 | `DistanceTable.cs` | 🟢 | **FIXED ✅** | All methods now guarded by `lock(lockObj)` |
| 21 | `TrackMessageReceptionTimeoutLayer.cs` | 🟢 | **FIXED ✅** | Switched to `ConcurrentDictionary<ReplicaID, DateTime>` |
| 22 | `TestBehaviour1.cs` | 🟢 | **FIXED ✅** | `t.Wait()` replaced with `await t` |
| 23 | `NetworkSimulator.cs` | 🟢 | **FIXED ✅** | `messageDeliveredHandlers` and `connectionInterruptedHandlers` now protected by `lock` |

---

## Detailed Findings

---

### 🔴 BUG-1 — `DiscreteSimulator.cs`: Collection Mutation Inside `foreach` (STILL PRESENT)

**File:** `Simulator/DiscreteSimulator.cs` — Lines 152–177

```csharp
// ChangeTimeOfCommunicationsBetween
foreach (var ev in connectionsToMessageEvents[con])   // ← iterating
{
    ...
    connectionsToMessageEvents[con].Add(newEv);        // ← modifying same list!
```

The `DiscreteSimulator` version of `ChangeTimeOfCommunicationsBetween` still mutates the list while iterating over it (the `RealtimeSimulator` version was fixed using `.ToArray()`). This throws `InvalidOperationException` at runtime whenever a connection latency change occurs.

**Fix:** Snapshot the list first: `foreach (var ev in connectionsToMessageEvents[con].ToArray())`

---

### 🔴 BUG-2 — `EnsureAckNetworkLayer.cs`: Unsynchronised Write Then Lock, and Lock Held During Async I/O (STILL PRESENT)

**File:** `CRDTApp/NetworkLayers/Composite/EnsureAckNetworkLayer.cs` — Lines 85–100 and 47–67

```csharp
public override async Task<bool> SendMessage(...)
{
    for (int i = 0; i < NUMBER_OF_ATTEMPTS_UNTIL_TIMEOUT && Running; i++)
    {
        var semaphore = new SemaphoreSlim(0);
        pendingAcks[(newTag, recipient)] = semaphore;  // ← NO lock here!
        ...
    }
}

void OnLowerLayerMessageReceived(...)
{
    lock (pendingAcks)   // ← reader holds lock
    {
        ...
        LayerBelow.SendMessage(senderID, new Message(ack.ToJson()));  // ← async I/O inside lock!
        CallMessageReceived(senderID, pendingAckMessage.Message);     // ← arbitrary delegate inside lock!
    }
}
```

Two issues:

1. `SendMessage` writes to `pendingAcks` **without** `lock(pendingAcks)`, while both `OnAckReceived` and `OnLowerLayerMessageReceived` lock it. A concurrent ACK arriving between the write and next loop iteration can corrupt the dictionary state.
2. `OnLowerLayerMessageReceived` holds `lock(pendingAcks)` while calling `LayerBelow.SendMessage` (async network I/O) and `CallMessageReceived` (arbitrary subscriber code). This can hold the lock for an unbounded duration, blocking all ACK processing.

**Fix:** Wrap the write `pendingAcks[…] = semaphore` inside `lock(pendingAcks)`. Extract `LayerBelow.SendMessage` and `CallMessageReceived` outside the lock scope.

---

### 🟠 BUG-3 — `CausalHistoryTracker.cs`: Arbitrary Delegate Invoked Inside Lock (STILL PRESENT)

**File:** `CRDTApp/Messages/CausalHistoryTracker.cs` — Lines 65–93

```csharp
lock (Latest)
{
    ...
    MessageIssued?.Invoke(...);   // ← arbitrary subscriber called while holding lock
}
```

`MessageIssued` subscribers (e.g., `InsertBehaviour.OnMessageReceivedInSequence`) call `ints.Merge(...)`, `OperationApplied?.Invoke(...)`, etc. If any of those try to acquire a lock that another thread currently holds while that thread is waiting for `lock(Latest)`, a deadlock occurs.

**Fix:** Collect the messages that are ready to fire, release the lock, then invoke the delegates outside the lock.

---

### 🟠 BUG-4 — `HeartbeatLayer.cs`: Unsynchronised `sendingTo` HashSet (STILL PRESENT)

**File:** `CRDTApp/NetworkLayers/Composite/HeartbeatLayer.cs` — Lines 31–37 and 74–88

```csharp
// OnConnectionGained — runs on timer thread
void OnConnectionGained(ReplicaID replica) {
    if (replica != replicaID && !sendingTo.Contains(replica) && Running)
    {
        sendingTo.Add(replica);    // ← no lock
    }
}

// Run — runs on main/task thread
public override Task Run() {
    sendingTo = new HashSet<ReplicaID>();   // ← new reference assigned
    foreach (var replica in connectionTracker.AllConnections)
    {
        sendingTo.Add(replica);             // ← no lock
    }
}
```

`OnConnectionGained` fires on a timer thread while `Run()` creates a new `HashSet` and iterates it — classic TOCTOU on a mutable reference plus the `HashSet` itself. Note: `DistanceVectorRoutingLayer` has the same pattern (see BUG-10).

**Fix:** Use a dedicated `private readonly object _sendingToLock = new()` and lock it consistently. Never lock on the collection reference itself when it can be reassigned.

---

### 🟠 BUG-5 — `FloodingRedirectionLayer.cs`: `rememberedMessages.Contains()` Outside Lock (STILL PRESENT)

**File:** `CRDTApp/NetworkLayers/Composite/Routing/FloodingRedirectionLayer.cs` — Line 86

```csharp
if (... && !rememberedMessages.Contains((floodMessage.Tag, floodMessage.OriginalSender)))
//                                    ^^^^^^ no lock — but Remember() uses lock(rememberedMessages)
```

The `Remember()` helper correctly locks `rememberedMessages`, but the read on line 86 does not. A concurrent `Remember()` call can insert exactly the tuple being checked here, causing a duplicate flood to be forwarded.

**Fix:** Wrap the entire check-then-act block (check + `Remember` + forward) inside `lock(rememberedMessages)`.

---

### 🟠 BUG-6 — `LinkStateRoutingLayer.cs`: `mostRecentLSAs` Iterated Without Lock in `OnConnect` (STILL PRESENT)

**File:** `CRDTApp/NetworkLayers/Composite/Routing/LinkStateRoutingLayer.cs` — Lines 176–188

```csharp
public void OnConnect(ReplicaID replica)
{
    foreach (var oldLSA in mostRecentLSAs.Values)   // ← no lock
    {
        SendLSA(replica, oldLSA);
    }
    ...
}
```

`AddLSA()` and `RecalculateRoutingTable()` both lock `lsaHelperLock` before accessing `mostRecentLSAs`, but `OnConnect` iterates `mostRecentLSAs.Values` without any lock. A concurrent LSA receipt while a new node connects throws `InvalidOperationException`.

**Fix:** Wrap the `foreach` in `lock(lsaHelperLock)`, or take a snapshot: `var snapshot = mostRecentLSAs.Values.ToList()`.

---

### 🟠 BUG-7 — `InsertBehaviour.cs`: Unsynchronised ORSet and `running` Flag (STILL PRESENT)

**File:** `CRDTApp/Behaviours/InsertBehaviour.cs`

Three concurrent access patterns on shared state:

1. **`ints` (ORSet):** `CreateAddOperation` + `Merge` called from `SendUpdateUntil` (scheduler thread) AND `OnMessageReceivedInSequence` (network receive thread) AND `InsertManualValue` (UI thread, via button click). No locks anywhere around `ints`.
2. **`running` flag:** Written by `Stop()` (UI/app thread), read by lambdas on scheduler threads — not `volatile`.
3. **`OperationApplied` event:** Fired from scheduler thread; subscribed from UI thread — no synchronisation.

**Fix:** Serialise all access to `ints` (and the `CallBehaviourEnded` check) under a single lock. Mark `running` as `volatile`.

---

### 🟠 BUG-8 — `NetworkCreationFunctions.cs`: Static Mutable Fields Still Exist (PARTIALLY FIXED)

**File:** `RunSimulations/NetworkCreationFunctions.cs` — Lines 18–19

```csharp
public static float HEARTBEAT_TIME = 0.1f;
public static float MESSAGE_TIMEOUT = 5.0f;
```

The UI no longer writes to these fields, so the most acute race is gone. But they remain `public static` mutable — any future concurrent use (parallel test runs, etc.) would race. They are also not used anywhere now that the helper functions take explicit parameters.

**Fix:** Remove them entirely, or change to `private const`.

---

### 🟠 BUG-9 — `TCPNetworkBaseLayer.cs`: Concurrent Writes to Same `NetworkStream` (STILL PRESENT)

**File:** `CRDTApp/NetworkLayers/Base/TCPNetworkBaseLayer.cs` — Lines 123–134

```csharp
public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
{
    ...
    await client.GetStream().WriteAsync(bytes);   // ← multiple callers can hit this simultaneously
```

`outputSockets` is `ConcurrentDictionary` (safe), but multiple concurrent `SendMessage` calls for the **same** `recipient` will all obtain the same `TcpClient` and call `GetStream().WriteAsync(bytes)` at the same time. `NetworkStream.WriteAsync` is **not thread-safe** for concurrent writes — the resulting interleaved bytes corrupt message framing.

**Fix:** Add a `ConcurrentDictionary<ReplicaID, SemaphoreSlim> _writeLocks` and gate each write with `await writeLock.WaitAsync()` / `Release()`.

---

### 🟠 BUG-10 — `DistanceVectorRoutingLayer.cs`: Lock Taken on Newly-Assigned Object (STILL PRESENT)

**File:** `CRDTApp/NetworkLayers/Composite/Routing/DistanceVectorRoutingLayer.cs` — Lines 140–157

```csharp
public override Task Run()
{
    sendingTo = new HashSet<ReplicaID>();   // ← new object assigned
    lock (sendingTo)                         // ← locks NEW object...
    {
        foreach (var replica in ...)
            sendingTo.Add(replica);
    }
}

void OnConnectionGained(ReplicaID replica)
{
    lock (sendingTo)   // ← could be locking old OR new object depending on timing
    {
        if (!sendingTo.Contains(replica) && Running)
            sendingTo.Add(replica);
    }
}
```

The `sendingTo = new HashSet<>()` assignment and the `lock(sendingTo)` call in `Run()` are not atomic. An `OnConnectionGained` event firing between the assignment and the lock can acquire the lock on the brand-new (empty) object and add a replica, which `Run()` then overwrites with the loop. Additionally, if `OnConnectionGained` ran on the **old** reference before the reassignment, both threads are now locking entirely different monitor objects — no mutual exclusion at all.

**Fix:** Use a separate, permanent lock object: `private readonly object _sendingToLock = new object();` and always `lock(_sendingToLock)`.

---

### 🟡 BUG-11 — `ConnectionMap.cs`: Inconsistent External Locking (STILL PRESENT)

**File:** `Simulator/ConnectionMap.cs`

`ConnectionMap` has no internal synchronisation. `NetworkSimulator` wraps most calls in `lock(ConnectionMap)`, but several callers access `ConnectionMap` methods directly without that external lock (e.g., `SimulationPage.OnMessageSendScheduled` line 287 reads `ConnectionMap.GetConnection` from a background timer thread without any lock).

**Fix:** Add internal locking inside `ConnectionMap` (e.g., using a `ReaderWriterLockSlim` for read-heavy workloads), so callers don't need to know the locking discipline.

---

### 🟡 BUG-12 — `ORSet.cs`: Non-Atomic Counter and Unprotected State (STILL PRESENT)

**File:** `CRDTs/Basic/ORSet.cs` — Line 124–128

```csharp
ulong counter = 0;
Tag CreateNewTag(ReplicaID id, T value)
{
    return new Tag($"{id}_{++counter}");   // ← non-atomic pre-increment
}
```

Two threads calling `CreateAddOperation` concurrently can produce the same tag, breaking ORSet semantics — two logically distinct add operations would appear identical during merge, causing elements to be incorrectly de-duplicated or retained.

The `adds`/`removes` dictionaries are also unprotected (mutation in `Merge`, reads in `GetValues`).

**Fix:** `Interlocked.Increment(ref counter)`. Protect all `adds`/`removes` accesses under a lock, or (better) ensure `ORSet` is only ever accessed from a single thread at a time (enforce this at the `InsertBehaviour` layer, BUG-7).

---

### 🟡 BUG-13 — `CausalHistoryTracker.cs`: Event Subscription Not Thread-Safe (STILL PRESENT)

**File:** `CRDTApp/Messages/CausalHistoryTracker.cs` — Lines 15–22

```csharp
public void AddMessageIssuedHandler(Action<...> handler)
{
    MessageIssued += handler;   // ← no synchronisation
}
public void RemoveMessageIssuedHandler(Action<...> handler)
{
    MessageIssued -= handler;
}
```

C# delegate `+=`/`-=` on events is thread-safe for the assignment itself (it's an Interlocked exchange under the hood), but these public add/remove wrapper methods are not guarded. If one thread adds a handler while another thread is inside `MessageIssued?.Invoke(…)`, the C# delegate invocation list is immutable (so the in-flight invocation is safe), but the interplay with the `lock(Latest)` block (BUG-3) makes reasoning about ordering difficult. Low immediate risk, but fragile.

**Fix:** Either remove the custom add/remove methods (use the `event` directly) or add `lock` guards around both.

---

### 🟡 BUG-14 — `Experiment.cs`: `sim.Wait()` Synchronous Block (STILL PRESENT)

**File:** `RunSimulations/Experiment.cs` — Lines 62–63

```csharp
var sim = simulator.Run(maxExperimentDuration);
sim.Wait();   // ← synchronous block inside an async method
```

`RunIndividualFillArraySimulation` is `async Task<…>`. Calling `.Wait()` on a Task that may schedule continuations back to the calling thread can deadlock in certain hosting contexts (e.g., WPF `Dispatcher`, ASP.NET classic sync context). Even in a pure thread-pool context it wastes a thread while waiting.

**Fix:** `await simulator.Run(maxExperimentDuration);`

---

### 🟡 BUG-15 — `NetworkCreationFunctions.BasicMetricCreator`: Non-Atomic `count++` (STILL PRESENT)

**File:** `RunSimulations/NetworkCreationFunctions.cs` — Lines 232–248

```csharp
int count = 0;
foreach (var app in apps)
{
    app.Behaviour.AddBehaviourEndedHandler(() =>
    {
        count++;   // ← non-atomic, concurrent across multiple app callbacks
        if (count == apps.Count) { ... close all ... }
    });
}
```

Multiple apps complete concurrently. Two threads can both read `count = N`, both write `N+1`, and the `if (count == apps.Count)` check may never trigger or may trigger multiple times.

**Fix:** `if (Interlocked.Increment(ref count) == apps.Count) { ... }`

---

## Fixes Confirmed in This Version (v1 → v2)

| Original Bug | What was fixed | ✅ |
|---|---|---|
| `RealtimeScheduler.cs` | `time * 1000` ms fix; `await Task.WhenAny` fix | ✅ |
| `TCPNetworkBaseLayer.cs` | `RemoveAll` replaces `RemoveAt(-1)`; `Task.WhenAll` awaited; zero-byte disconnect break added | ✅ |
| `TrackActiveConnectionsLayer.cs` | Inverted `CallConnectionInterrupted` / `CallConnectionRegained` logic corrected | ✅ |
| `DistanceTable.cs` | All methods guarded by `lock(lockObj)` | ✅ |
| `TrackMessageReceptionTimeoutLayer.cs` | `ConcurrentDictionary<ReplicaID, DateTime>` used | ✅ |
| `TestBehaviour1.cs` | `await t` replaces `t.Wait()` | ✅ |
| `RealtimeSimulator.ChangeTimeOfCommunicationsBetween` | `.ToArray()` snapshot before mutation loop | ✅ |
| `NetworkSimulator.cs` | `messageDeliveredHandlers` and `connectionInterruptedHandlers` protected by `lock` | ✅ |

---

## Recommended Fix Priority

```
IMMEDIATE (crashes / data corruption under real usage)
  1. BUG-1  — DiscreteSimulator: foreach + Add on same list → InvalidOperationException crash
  2. BUG-2  — EnsureAckNetworkLayer: unsynchronised pendingAcks write; I/O inside lock

HIGH (races, wrong behaviour, hard to reproduce)
  3. BUG-10 — DistanceVectorRoutingLayer: lock taken on wrong (newly assigned) object
  4. BUG-4  — HeartbeatLayer: sendingTo HashSet without stable lock
  5. BUG-6  — LinkStateRoutingLayer: mostRecentLSAs read without lock in OnConnect
  6. BUG-5  — FloodingRedirectionLayer: Contains() outside lock
  7. BUG-9  — TCPNetworkBaseLayer: concurrent NetworkStream writes
  8. BUG-7  — InsertBehaviour: ORSet + running + event unsynchronised

MEDIUM (data integrity, logic errors)
  9. BUG-3  — CausalHistoryTracker: delegate invoked inside lock → deadlock risk
 10. BUG-12 — ORSet: non-atomic counter → duplicate tags
 11. BUG-15 — BasicMetricCreator: non-atomic count++
 12. BUG-11 — ConnectionMap: inconsistent external locking
 13. BUG-14 — Experiment: .Wait() on async

LOW / CLEANUP
 14. BUG-8  — Remove or make const HEARTBEAT_TIME / MESSAGE_TIMEOUT static fields
 15. BUG-13 — Refactor CausalHistoryTracker event subscription
```

---

*End of report. No source files were modified during this analysis.*
