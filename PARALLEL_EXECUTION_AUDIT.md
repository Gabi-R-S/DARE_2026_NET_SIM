# Comprehensive Parallel Execution & Concurrency Audit Report

> **Project:** Distributed Mesh Simulation  
> **Audited Modules:** `CRDTApp`, `CRDTs`, `Simulator`, `Shared`, `RunSimulations`, `VisualizationTest`  
> **Scope:** Concurrency errors, deadlocks, race conditions, undefined behavior, and memory/resource corruption.  
> **Note:** As requested, no existing source files have been modified. This document serves as your implementation roadmap to fix these issues.

---

## 1. Executive Summary & Concurrency Risk Heatmap

Across the solution, several architectural concurrency patterns and implementation bugs leave the system susceptible to:
1. **Deadlocks & Thread Starvation:** Synchronous blocking (`.Wait()`, `.Result`, `Task.WaitAll()`) on asynchronous operations and timer threads, plus lock-order inversions and invoking external delegate callbacks inside `lock` blocks.
2. **Silent Livelock / Distributed Deadlocks:** Early shutdown of intermediate forwarding nodes causing distributed partitioning (the "A-B-C problem" noted in `THE_BIG_PROBLEM_SAD_FACE.md`).
3. **Data Races & Memory Corruption:** Standard non-thread-safe collections (`Dictionary`, `List`, `HashSet`) concurrently read and mutated across threads without synchronization, resulting in corrupted hash chains and infinite loops (`Dictionary.FindEntry`).
4. **Immediate Runtime Exceptions in Parallel Paths:** Modifying collections while iterating over them with `foreach` loops, and negative-index exceptions (`tasks.RemoveAt(-1)`).
5. **Logic Flaws in Concurrency Helpers:** Missing `await` on `Task.WhenAny`, mixing up seconds vs. milliseconds in timers, and non-atomic increments on sequence numbers.

### Severity Summary Table

| Severity | Count | Primary Areas |
| :--- | :---: | :--- |
| 🔴 **Critical** | 8 | Schedulers, TCP Network Layer, Routing Tables, Discrete Simulation Threading |
| 🟠 **High** | 11 | Distributed Termination Deadlock, Causal History Tracker, EnsureAck Layer, Version Vector, ORSet |
| 🟡 **Medium** | 9 | ConnectionMap, ActiveConnectionTracker, Metrics Counters, Lock Encapsulation |
| 🟢 **Low** | 4 | Non-volatile flags, timer resource cleanup |

---

## 2. Critical Parallel Bugs (High Likelihood of Deadlock, Undefined Behavior, or Crash)

---

### 🔴 Bug 1: Missing `await` in `RealtimeScheduler.ExecuteTaskOrTimeout` (Immediate Bypass)
- **File:** [`CRDTApp/Schedulers/RealtimeScheduler.cs`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/Schedulers/RealtimeScheduler.cs#L34-L48)
- **Lines:** 34–48
- **Code:**
  ```csharp
  public async Task<bool> ExecuteTaskOrTimeout(Task task, float time)
  {
      var t = Task.Delay((int)time * 1000);
      var res = Task.WhenAny(t, task); // BUG: Missing await! res is Task<Task>

      if (res == t) // Task<Task> == Task is ALWAYS false
      {
          return false;
      }
      return true; // ALWAYS returns true immediately without waiting!
  }
  ```
- **Why it happens:** `Task.WhenAny` returns a `Task<Task>`. Because it is not awaited, `res` is the wrapper task, which can never equal `t` (`Task`). The method exits synchronously and returns `true` at $t = 0$.
- **Impact:**
  1. [`EnsureAckNetworkLayer<T>.SendMessage`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/EnsureAckNetworkLayer.cs#L97) relies on this method to wait for acknowledgment. Because it immediately returns `true`, the layer assumes an ACK was received instantly without ever waiting for the remote replica!
  2. [`App.Run`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/App.cs#L37) finishes immediately on startup instead of waiting for the allocated execution duration.
- **Suggested Fix:** Change to `var completed = await Task.WhenAny(t, task); if (completed == t) return false; return true;`. Also fix `(int)time * 1000` to `(int)(time * 1000)` so fractional seconds (e.g., $0.5\text{s}$) don't truncate to $0$.

---

### 🔴 Bug 2: Listener Crash via Negative Index (`tasks.RemoveAt(-1)`) in TCP Layer
- **File:** [`CRDTApp/NetworkLayers/Base/TCPNetworkBaseLayer.cs`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Base/TCPNetworkBaseLayer.cs#L67-L72)
- **Lines:** 67–72
- **Code:**
  ```csharp
  for (int i = 0; i < tasks.Count; i++) {
      if (tasks[i].IsCompleted) {
          i--;
          tasks.RemoveAt(i); // CRASH when i == 0: i becomes -1 -> tasks.RemoveAt(-1)!
      }
  }
  ```
- **Why it happens:** If the completed task is at index `0`, `i--` decrements `i` to `-1`. Calling `tasks.RemoveAt(-1)` throws `ArgumentOutOfRangeException`.
- **Impact:** The background TCP connection acceptor loop throws an unhandled exception and immediately crashes, terminating incoming connection processing for the node.
- **Suggested Fix:** Use `tasks.RemoveAll(t => t.IsCompleted);` or standard backwards iteration `for (int i = tasks.Count - 1; i >= 0; i--) if (tasks[i].IsCompleted) tasks.RemoveAt(i);`.

---

### 🔴 Bug 3: Collection Mutation During `foreach` Enumeration in Distance Table
- **File:** [`CRDTApp/NetworkLayers/Composite/Routing/Helper/DistanceTable.cs`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/Routing/Helper/DistanceTable.cs#L24-L31) and [Lines 82–89](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/Routing/Helper/DistanceTable.cs#L82-L89)
- **Code:**
  ```csharp
  // ResetConnection (Lines 24-31)
  foreach (var (k, (v1, v2)) in distances)
  {
      if (v2 == replica)
      {
          distances.Remove(k); // Throws InvalidOperationException!
      }
  }

  // Update (Lines 82-89)
  foreach (var (target, (distance, nextInRoute)) in distances)
  {
      if (nextInRoute == sender && !updateDistances.ContainsKey(target))
      {
          distances.Remove(target); // Throws InvalidOperationException!
      }
  }
  ```
- **Why it happens:** Modifying a .NET `Dictionary` during `foreach` invalidates the enumerator version.
- **Impact:** The thread handling network messages or connection interruption crashes with `InvalidOperationException: Collection was modified; enumeration operation may not execute.`
- **Suggested Fix:** Collect keys to remove in a temporary list, e.g. `var toRemove = distances.Where(...).Select(...).ToList(); foreach(var k in toRemove) distances.Remove(k);` under a synchronization lock.

---

### 🔴 Bug 4: SimSharp Discrete Simulation Thread Safety & Enumeration Crash
- **Files:**
  - [`Simulator/DiscreteSimulator.cs`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/Simulator/DiscreteSimulator.cs#L23)
  - [`Simulator/DiscreteSimulator.cs`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/Simulator/DiscreteSimulator.cs#L143-L167)
- **Lines:** 23 & 143–167
- **Code:**
  ```csharp
  // Line 23:
  await Task.Run(() => { env.RunD(simulationTimeLimit); });

  // Lines 143-167 in ChangeTimeOfCommunicationsBetween:
  foreach (var ev in connectionsToMessageEvents[con])
  {
      ...
      connectionsToMessageEvents[con].Add(newEv); // Modifying list being enumerated!
  }
  ```
- **Why it happens:**
  1. `connectionsToMessageEvents[con].Add(newEv)` is called inside `foreach (var ev in connectionsToMessageEvents[con])` without `.ToArray()`. Throws `InvalidOperationException`.
  2. `env.RunD(...)` runs on a ThreadPool worker while other threads schedule discrete events into `Simulation env` concurrently. SimSharp's internal event queue heap and state are not thread-safe.
- **Impact:** Crashes during dynamic latency changes; non-deterministic simulation corruption or heap corruption in SimSharp.
- **Suggested Fix:** Iterate over `.ToArray()` and synchronize all accesses to `env` and `connectionsToMessageEvents`.

---

### 🔴 Bug 5: Synchronous Blocking on Async Tasks (Deadlock Traps)
- **Instances:**
  1. **[`CRDTApp/Behaviours/TestBehaviour1.cs`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/Behaviours/TestBehaviour1.cs#L44-L47):**
     ```csharp
     var t = manager.SendMessage(otherReplica, new Message("HI"));
     t.Wait(); // Synchronous block on timer callback!
     ```
  2. **[`CRDTApp/NetworkLayers/Base/TCPNetworkBaseLayer.cs`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Base/TCPNetworkBaseLayer.cs#L80):**
     ```csharp
     Task.WaitAll(tasks); // Inside async Task Run()
     ```
  3. **[`RunSimulations/Experiment.cs`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/RunSimulations/Experiment.cs#L63):**
     ```csharp
     var sim = simulator.Run(maxExperimentDuration);
     sim.Wait();
     ```
- **Why it happens:** Synchronously blocking (`.Wait()`, `.Result`) locks the executing thread. If the awaited task needs the same thread or synchronization context to deliver packets, complete socket IO, or execute continuation callbacks, the application deadlocks permanently.
- **Suggested Fix:** Refactor to asynchronous `await` calls (`await t;`, `await Task.WhenAll(tasks);`, `await sim;`).

---

### 🔴 Bug 6: Concurrent NetworkStream Writes & Socket Overwrite in TCP Layer
- **File:** [`CRDTApp/NetworkLayers/Base/TCPNetworkBaseLayer.cs`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Base/TCPNetworkBaseLayer.cs#L84-L121)
- **Lines:** 84–121
- **Code:**
  ```csharp
  if (!outputSockets.ContainsKey(recipient))
  {
      ...
      await client.ConnectAsync(ip, port);
      outputSockets[recipient] = client;
  }
  ...
  await client.GetStream().WriteAsync(bytes);
  ```
- **Why it happens:**
  1. `outputSockets` is a non-thread-safe dictionary modified and checked without synchronization. Concurrent sends cause race conditions, duplicate connections, and socket leaks.
  2. .NET's `NetworkStream` does **not** support concurrent write operations from multiple threads. Concurrent calls to `WriteAsync` interleave payload bytes or throw an `InvalidOperationException`.
- **Impact:** Payload corruption on the wire, broken serialization, and lost connections.
- **Suggested Fix:** Guard `outputSockets` with a lock or `ConcurrentDictionary`, and protect each connected client's stream with a dedicated per-connection `SemaphoreSlim` or write queue.

---

### 🔴 Bug 7: Infinite Zero-Byte Loop in `RunInputSocket`
- **File:** [`CRDTApp/NetworkLayers/Base/TCPNetworkBaseLayer.cs`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Base/TCPNetworkBaseLayer.cs#L29-L49)
- **Lines:** 34–40
- **Code:**
  ```csharp
  var buffer = new byte[MESSAGE_SIZE_LIMIT];
  int numBytes = await socket.ReceiveAsync(buffer);
  var message = Encoding.UTF8.GetString(buffer, 0, numBytes);
  var messageObj = new Message(message);
  CallMessageReceived(id, messageObj);
  ```
- **Why it happens:** In TCP sockets, when the remote peer gracefully shuts down the connection, `ReceiveAsync` returns `0`. The loop does not check `if (numBytes == 0) break;`.
- **Impact:** 100% CPU thread-spinning in an infinite loop, allocating empty buffers and flooding `CallMessageReceived` with empty messages.
- **Suggested Fix:** Check `if (numBytes <= 0) { CallConnectionInterrupted(id); break; }`.

---

### 🔴 Bug 8: Timer Milliseconds vs. Seconds Mismatch
- **File:** [`CRDTApp/Schedulers/RealtimeScheduler.cs`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/Schedulers/RealtimeScheduler.cs#L12-L17)
- **Lines:** 12–17
- **Code:**
  ```csharp
  public void ExecuteAfter(float time, Action action) {
      System.Timers.Timer timer = new System.Timers.Timer(time); // BUG!
      timer.AutoReset = false;
      timer.Elapsed += (_,_) => action();
      timer.Start();
  }
  ```
- **Why it happens:** In the simulation, `time` is expressed in seconds (e.g., `0.5f` seconds). However, `System.Timers.Timer(double interval)` expects milliseconds!
- **Impact:** An intended $0.5\text{s}$ delay fires after $0.5\text{ms}$ (essentially immediately). In contrast, `RealtimeSimulator.cs` line 115 properly did `timer.Interval = time * 1000;`.
- **Suggested Fix:** Pass `time * 1000` to the timer constructor, and ensure `timer.Dispose()` is called upon elapsed.

---

## 3. Concurrency Hotspots by Project & Module

### 3.1 `CRDTApp`

#### A. Causal History Tracker (`CRDTApp/Messages/CausalHistoryTracker.cs`)
1. **Unsynchronized `Latest` VersionVector ([Lines 52–56](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/Messages/CausalHistoryTracker.cs#L52-L56) vs [Lines 80–84](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/Messages/CausalHistoryTracker.cs#L80-L84)):**
   - `IncrementVersion()` (called when sending on producer threads) reads/increments `Latest`.
   - `OnMessageReceived()` (called on message arrival threads) reads/merges into `Latest`.
   - Neither path locks `Latest`. Concurrent read/write will corrupt `VersionVector` dictionary buckets or cause `InvalidOperationException`.
2. **Data Race on `pendingReceivedMessages` ([Lines 68–72](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/Messages/CausalHistoryTracker.cs#L68-L72) & [Lines 78–85](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/Messages/CausalHistoryTracker.cs#L78-L85)):**
   - Plain `List<(ReplicaID, HistoryTrackedMessage)>` is concurrently modified (`.Add(...)` vs `.RemoveAt(i)` vs `.Count`).
   - If two messages arrive concurrently from different peers, list state will corrupt and drop pending messages.
3. **Alien Callback Under Lock ([Lines 86–89](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/Messages/CausalHistoryTracker.cs#L86-L89)):**
   - `MessageIssued?.Invoke(...)` is executed while holding `lock (messageIssuedLock)`. If the subscriber calls back into the tracker or waits for another lock held by an incoming message thread, a classic deadlock occurs.

#### B. Network Layer Base (`CRDTApp/NetworkLayers/NetworkLayer.cs`)
1. **Event Invocation Under Lock ([Lines 53–72](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/NetworkLayer.cs#L53-L72)):**
   - `CallMessageReceived`, `CallConnectionInterrupted`, and `CallConnectionRegained` lock internal objects (`receiveLock`, `interruptedLock`, `regainedLock`) and invoke user event delegates inside the lock blocks.
   - Any subscriber performing synchronous network actions or cross-layer operations risks lock-order inversion deadlocks.
2. **Non-Volatile `Running` Property ([Line 17](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/NetworkLayer.cs#L17)):**
   - `public bool Running { get; protected set; }` is read and written by multiple threads without memory barriers or `volatile`. Background threads may observe stale cached values in registers.

#### C. EnsureAck Layer (`CRDTApp/NetworkLayers/Composite/EnsureAckNetworkLayer.cs`)
1. **Unsynchronized `pendingAcks` & Memory Leaks ([Lines 34–38](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/EnsureAckNetworkLayer.cs#L34-L38) & [Lines 88–90](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/EnsureAckNetworkLayer.cs#L88-L90)):**
   - `pendingAcks` (`Dictionary<(T, ReplicaID), SemaphoreSlim>`) is accessed concurrently by `SendMessage` and `OnAckReceived` with zero locking.
   - On retry timeout (lines 97–102), the old `SemaphoreSlim` is never removed from `pendingAcks`, leaking semaphores and causing tag collision issues.
2. **Unobserved Task in Fire-and-Forget ACK Send ([Line 65](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/EnsureAckNetworkLayer.cs#L65)):**
   - `LayerBelow.SendMessage(senderID, new Message(ack.ToJson()));` is invoked without `await`. Any socket or simulation exceptions will be unobserved.

#### D. Active Connections & Timeout Tracking
1. **Inverted Connection Logic in `TrackActiveConnectionsLayer.cs` ([Line 59](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/TrackActiveConnectionsLayer.cs#L59)):**
   - When a send fails:
     ```csharp
     if (tracker.SetInnactive(recipient))
     {
         CallConnectionRegained(recipient); // BUG: Calls Regained instead of Interrupted!
     }
     ```
     This triggers `OnConnectionRegained` handlers instead of `OnConnectionInterrupted`, keeping broken routes alive and causing infinite retry loops.
2. **Unsynchronized `lastCommunication` in `TrackMessageReceptionTimeoutLayer.cs` ([Lines 27–53](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/TrackMessageReceptionTimeoutLayer.cs#L27-L53)):**
   - `lastCommunication` is a raw `Dictionary<ReplicaID, DateTime>` updated by both receive and send threads simultaneously without locks.

#### E. Routing Layers
1. **Flooding Layer Data Races (`FloodingRedirectionLayer.cs` [Lines 23–29](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/Routing/FloodingRedirectionLayer.cs#L23-L29), [47–50](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/Routing/FloodingRedirectionLayer.cs#L47-L50)):**
   - `tag++` in `GetNextTag()` is non-atomic. Two parallel outgoing messages can receive identical tags.
   - `rememberedMessages` (`HashSet<(uint, ReplicaID)>`) is modified and queried without locks from packet-handling threads and timeout callbacks. Corrupted hash sets cause message deduplication to fail, triggering infinite packet storms.
2. **Null Reference & Data Race in Distance Vector (`DistanceVectorRoutingLayer.cs` [Lines 28](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/Routing/DistanceVectorRoutingLayer.cs#L28), [44](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/Routing/DistanceVectorRoutingLayer.cs#L44), [137](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/Routing/DistanceVectorRoutingLayer.cs#L137)):**
   - `sendingTo` is only initialized inside `Run()`. If `OnConnectionGained` fires before `Run()` completes, a `NullReferenceException` is thrown.
   - `sendingTo` is a raw `HashSet<ReplicaID>` modified by timer and event threads concurrently.
3. **Link State Routing (`LinkStateRoutingLayer.cs` [Lines 15–39](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/Routing/LinkStateRoutingLayer.cs#L15-L39), [121–150](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTApp/NetworkLayers/Composite/Routing/LinkStateRoutingLayer.cs#L121-L150)):**
   - `mostRecentLSAs` is updated by `AddLSA` without locking `lsaHelperLock`.
   - `RecalculateRoutingTable()` calls `nextNodeGivenTarget.Clear()` and rebuilds it. Meanwhile, `SendMessage` calls `GetNextNodeInPathTo(target)` concurrently on the same dictionary without locks, returning corrupted paths or throwing `KeyNotFoundException`.

---

### 3.2 `Simulator`

1. **Unsynchronized `ConnectionMap` ([Lines 10–54](file:///d:/DARE_PROJECT/DistributedMeshSimulation/Simulator/ConnectionMap.cs#L10-L54)):**
   - `connectionMap` uses nested dictionaries `Dictionary<ReplicaID, Dictionary<ReplicaID, NetworkConnection>>`.
   - Read concurrently by `IsConnectionAvailable` and `GetConnection` from timers and worker tasks while written by `SetConnection` or tests. Can cause infinite loops inside `Dictionary.FindEntry`.
2. **Unsynchronized Delegate Handlers in `NetworkSimulator.cs` ([Lines 87–140](file:///d:/DARE_PROJECT/DistributedMeshSimulation/Simulator/NetworkSimulator.cs#L87-L140)):**
   - `messageDeliveredHandlers` (`Dictionary<ReplicaID, List<Action<NetworkMessage>>>`) has no synchronization.
   - `DeliverMessage` loops through `handlers` on worker threads while `Add...` or `Remove...` modifies the list.
3. **Unawaited CloseEvents in `RealtimeSimulator.cs` ([Lines 27–30](file:///d:/DARE_PROJECT/DistributedMeshSimulation/Simulator/RealtimeSimulator.cs#L27-L30)):**
   - `foreach (var ev in CloseEvents) { ev(); }` invokes async delegates returning `Task`, but completely discards the tasks without `await`. Cleanup is non-deterministic.
4. **Data Race on `MessageEvent.Active` ([Lines 56–77](file:///d:/DARE_PROJECT/DistributedMeshSimulation/Simulator/RealtimeSimulator.cs#L56-L77), [136–139](file:///d:/DARE_PROJECT/DistributedMeshSimulation/Simulator/RealtimeSimulator.cs#L136-L139)):**
   - `MessageEvent.Active` is a non-volatile boolean. `DropCommunicationsBetween` sets `ev.Active = false;` without thread synchronization. The timer thread may deliver the dropped packet due to CPU register caching.

---

### 3.3 `CRDTs` & `Shared`

1. **Non-Thread-Safe CRDT Data Structures:**
   - **`ORSet<T>` ([Lines 87–155](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTs/Basic/ORSet.cs#L87-L155)):** `adds` and `removes` dictionaries are mutated in `Merge` and enumerated in `GetValues()`. In `InsertBehaviour`, `ints.Merge` and `ints.GetValues()` are called simultaneously from the receive thread, timer thread, and UI thread (`InsertManualValue`).
   - **`ORSet.CreateNewTag` ([Line 127](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTs/Basic/ORSet.cs#L127)):** `++counter` is non-atomic.
   - **`MVRegister<T>` ([Lines 48–77](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTs/MVRegister.cs#L48-L77)):** Indexing bug and collection mutation in nested loops; unsynchronized `values` list.
   - **`LWWRegister<T>` ([Lines 22–27](file:///d:/DARE_PROJECT/DistributedMeshSimulation/CRDTs/LWWRegister.cs#L22-L27)):** Updating `timestamp` and `Value` is non-atomic, allowing torn reads.
2. **`VersionVector` Thread Safety ([Lines 43–69](file:///d:/DARE_PROJECT/DistributedMeshSimulation/Shared/VersionVector.cs#L43-L69), [126–142](file:///d:/DARE_PROJECT/DistributedMeshSimulation/Shared/VersionVector.cs#L126-L142), [181–200](file:///d:/DARE_PROJECT/DistributedMeshSimulation/Shared/VersionVector.cs#L181-L200)):**
   - `Increment`, `Merge`, `Clone`, and `LacksDependencies` all iterate or modify `Dictionary<ReplicaID, ulong> versions`. Sharing a `VersionVector` across parallel operations leads to `Collection was modified` exceptions.

---

### 3.4 `RunSimulations` (Distributed Deadlock / Livelock)

1. **The "Sad Face" Intermediate Forwarder Deadlock ([`THE_BIG_PROBLEM_SAD_FACE.md`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/RunSimulations/THE_BIG_PROBLEM_SAD_FACE.md#L1-L2) & [`NetworkCreationFunctions.cs`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/RunSimulations/NetworkCreationFunctions.cs#L220-L233)):**
   - **Scenario:** Topology $A \longleftrightarrow B \longleftrightarrow C$.
   - When node $B$ completes its expected operation count, `BasicMetricCreator` or `InsertBehaviour` calls `app.Close()`.
   - `app.Close()` stops the network layer (`Running = false`).
   - Node $B$'s routing layer drops all forwarding packets because `Running == false`.
   - Result: Nodes $A$ and $C$ are now isolated. They never receive each other's CRDT operations, never reach `expectedCount`, and the simulation hangs forever until timeout.
   - **Suggested Fix:** Separate application-level completion (stopping CRDT generation) from routing-level forwarding (the routing layer must continue routing until all nodes or the global simulation ends).
2. **Closure Capture of Local Loop Variable in Factory ([`NetworkCreationFunctions.cs`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/RunSimulations/NetworkCreationFunctions.cs#L31-L32)):**
   ```csharp
   int i = 0;
   EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(..., (_) => { i++; return i; });
   ```
   - `i++` is a non-atomic data race across concurrent calls to `SendMessage`.

---

### 3.5 `VisualizationTest`

1. **WPF UI Thread vs. Background Simulation Threads:**
   - In [`SimulationPage.xaml.cs`](file:///d:/DARE_PROJECT/DistributedMeshSimulation/VisualizationTest/SimulationPage.xaml.cs#L413), `InjectAppOp_Click` calls `targetBehaviour.InsertManualValue(opVal)` directly on the UI thread, which calls `ints.Merge` and network sends while background threads are executing `SendUntilSuccess` and `OnMessageReceived`.
2. **Lock Re-entrancy / Alien Call in `GraphModels.cs` ([Line 302](file:///d:/DARE_PROJECT/DistributedMeshSimulation/VisualizationTest/Graph/GraphModels.cs#L302)):**
   - In `AdvanceParticles`: `onArrived?.Invoke(p)` is executed while holding `_syncLock`. If any subscriber calls `PulseNode`, `TryGetNode`, or accesses graph state, lock recursion or deadlock could occur.

---

## 4. Comprehensive Vulnerability & Remediation Matrix

| # | File | Line(s) | Category | Root Cause | Consequence | Recommended Fix |
| :-: | :--- | :---: | :--- | :--- | :--- | :--- |
| **1** | `RealtimeScheduler.cs` | 39–41 | Logic Bug / Race | Missing `await` on `Task.WhenAny` | `ExecuteTaskOrTimeout` returns `true` immediately; ACK waits bypassed | Await `Task.WhenAny` and check returned completed task |
| **2** | `TCPNetworkBaseLayer.cs` | 67–72 | Crash | `i--` becomes `-1` on index 0 | `RemoveAt(-1)` throws `ArgumentOutOfRangeException` crashing listener | Use `tasks.RemoveAll(t => t.IsCompleted)` |
| **3** | `DistanceTable.cs` | 24–31, 82–89 | Crash | `distances.Remove` inside `foreach` on `distances` | `InvalidOperationException: Collection was modified` | Snapshot keys with `.ToList()` before removing |
| **4** | `DiscreteSimulator.cs` | 143–167 | Crash | `connectionsToMessageEvents[con].Add` inside `foreach` on same list | `InvalidOperationException: Collection was modified` | Enumerate over `.ToArray()` |
| **5** | `TestBehaviour1.cs` | 44–47 | Deadlock | `t.Wait()` synchronously blocking async network send | Deadlocks scheduler/timer threadpool | Replace `.Wait()` with `await` |
| **6** | `TCPNetworkBaseLayer.cs` | 80 | Deadlock | `Task.WaitAll(tasks)` inside async method | Blocks thread, risks deadlocking context | Replace with `await Task.WhenAll(tasks)` |
| **7** | `TCPNetworkBaseLayer.cs` | 86–121 | Undefined Behavior | Concurrent writes to single `NetworkStream` | Corrupted bytes on socket, protocol deserialization failure | Synchronize socket writes with `SemaphoreSlim` |
| **8** | `TCPNetworkBaseLayer.cs` | 34–47 | CPU Starvation | No check for `numBytes == 0` on socket disconnect | 100% CPU infinite loop generating empty messages | Check `numBytes <= 0` and break connection |
| **9** | `RealtimeScheduler.cs` | 13 | Timing Bug | `new Timer(time)` passes seconds instead of ms | Timers fire ~1000x faster than intended | Multiply by 1000: `new Timer(time * 1000)` |
| **10** | `TrackActiveConnectionsLayer.cs` | 59 | Logic Bug | Calls `CallConnectionRegained` on failure | Inverts connection state, triggers wrong recovery | Call `CallConnectionInterrupted(recipient)` |
| **11** | `FloodingRedirectionLayer.cs` | 26–29 | Data Race | Non-atomic `tag++` across threads | Duplicate message tags; duplicate deduplication collisions | Use `Interlocked.Increment` |
| **12** | `FloodingRedirectionLayer.cs` | 47–50, 71 | Data Race | Unsynchronized `HashSet` `rememberedMessages` | Corrupted bucket chains, missed deduplication, packet storms | Wrap `rememberedMessages` access in `lock` |
| **13** | `LinkStateRoutingLayer.cs` | 20–39, 121–150 | Data Race | `mostRecentLSAs` modified without lock; `nextNodeGivenTarget.Clear()` read concurrently | Crashing Dijkstra loop, routing to invalid default replicas | Add lock around LSA updates and routing table reads |
| **14** | `DistanceVectorRoutingLayer.cs` | 28, 44 | Null Ref / Race | `sendingTo` uninitialized in constructor; raw `HashSet` accessed across threads | `NullReferenceException` if event arrives before `Run` | Initialize in field/constructor; synchronize access |
| **15** | `CausalHistoryTracker.cs` | 52–95 | Data Race | Unsynchronized `Latest` vector and `pendingReceivedMessages` list | Lost messages, version vector corruption, out-of-order CRDT merge | Protect tracker operations with a dedicated lock object |
| **16** | `NetworkLayer.cs` | 53–72 | Deadlock | Invoking subscriber delegates inside lock blocks | Lock-order inversion deadlocks with subscriber code | Copy delegate / invoke outside lock scope |
| **17** | `ConnectionMap.cs` | 24–53 | Data Race | Unsynchronized nested `Dictionary` | Infinite loop in `FindEntry` or missing connection states | Synchronize reads/writes with `ReaderWriterLockSlim` or `lock` |
| **18** | `EnsureAckNetworkLayer.cs` | 34–38, 88–90 | Data Race / Leak | Unsynchronized `pendingAcks`; timed-out entries never removed | Memory leak; race on ack semaphore release | Use `ConcurrentDictionary` or lock; clean up on timeout |
| **19** | `RealtimeSimulator.cs` | 27–30 | Resource Leak | `ev()` close tasks unawaited | Non-deterministic application shutdown | `await Task.WhenAll(CloseEvents.Select(ev => ev()))` |
| **20** | `BasicMetricCreator` | 220–232 | Livelock / Deadlock | Premature `a.Close()` stops intermediate routing nodes | Partitioned mesh nodes cannot converge | Keep network layers running until all nodes converge |

---

## 5. Actionable Hardening Roadmap (For Your Implementation)

When you begin applying fixes, address them in this logical sequence to avoid compounding bugs:

1. **Step 1: Fix Core Primitives & Schedulers**
   - Correct the missing `await` and millisecond conversion in `RealtimeScheduler.cs`.
   - Make all tag/sequence counters use `Interlocked.Increment`.
2. **Step 2: Eliminate Synchronous Blocking (`.Wait()`)**
   - Convert all `.Wait()` and `Task.WaitAll()` to `await` to unblock threadpool and timer threads.
3. **Step 3: Fix Collection Enumeration & Exception Hotspots**
   - Fix dictionary deletions in `DistanceTable.cs` and list modifications in `DiscreteSimulator.cs`.
   - Fix `tasks.RemoveAt(-1)` in `TCPNetworkBaseLayer.cs`.
4. **Step 4: Synchronize State in Network & Routing Layers**
   - Protect `pendingReceivedMessages` and `Latest` in `CausalHistoryTracker.cs`.
   - Add locks around `rememberedMessages` (Flooding) and `mostRecentLSAs` / `nextNodeGivenTarget` (Link State).
   - Invert line 59 in `TrackActiveConnectionsLayer.cs` to call `CallConnectionInterrupted`.
5. **Step 5: Resolve the Forwarder Livelock ("Sad Face" Problem)**
   - Decouple `Behaviour.Stop()` from `NetworkLayer.Stop()`. An app that has converged should stop sending new operations, but its lower routing layer must continue forwarding packets for other peers until the entire simulation concludes.
6. **Step 6: Thread-Safe TCP Layer & CRDTs**
   - Guard concurrent `NetworkStream.WriteAsync` calls with a write lock.
   - Protect CRDT `Merge` and `GetValues` methods when invoked across multiple threads.
