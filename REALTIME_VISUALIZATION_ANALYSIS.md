# Realtime Simulator & Visualization Diagnostic Report

> **Date:** 2026-10-06  
> **Topic:** Why `RunSimulations` (Discrete) converges successfully while `VisualizationTest` (Realtime) sends only one wave of packets and never converges.  
> **Constraint:** Analysis only — no source files were modified.

---

## Executive Summary

The reason the visualization only sends one wave of packets and stalls indefinitely is **not** a rendering/WPF bug or a CRDT logic bug. It is caused by **two major timing & lifecycle defects**:

1. **🔴 Primary Root Cause — Premature Simulation Shutdown in `RealtimeSimulator.Run`:**  
   `RealtimeSimulator.Run(simulationTimeLimit)` monitors `RunEvents` using `Task.WhenAny(Task.WhenAll(tasks), delay)`. However, `app.Run()` returns an **already-completed task in < 1 millisecond** because `InsertBehaviour.Run()` does not await `SendUpdateUntil` (fire-and-forget). Consequently, `RealtimeSimulator` concludes the simulation has finished at $T \approx 2\text{ ms}$, immediately executes all `CloseEvents`, and sets `running = false` / `Running = false` across all nodes.  
   - The **first wave of packets** was already synchronously queued before shutdown occurred.
   - All subsequent operations ($T = 1.0\text{ s}, 2.0\text{ s}, \dots$) abort because `running == false`.
   - All packet routing, heartbeats, and retry loops stop forwarding because `Running == false`.

2. **🔴 Secondary Root Cause — `EnsureAckNetworkLayer` Timeout is Smaller Than Link Latency:**  
   `EnsureAckNetworkLayer` has a hardcoded default `waitTime = 0.5f` seconds per attempt. In `VisualizationTest`, link latency is configured to $[0.5\text{ s}, 1.5\text{ s}]$. A single round-trip (Message + ACK) takes at least $1.0\text{ s} - 3.0\text{ s}$. Because the ACK timer expires in $0.5\text{ s}$, **every single ACK times out**, causing the layer to report failure and drop transmissions.  
   *(In contrast, `RunSimulations` runs with `delay = 0.0f`, where RTT is 0 ms, so ACKs return instantly).*

---

## Detailed Root Cause Analysis

---

### Root Cause 1: Premature Shutdown in `RealtimeSimulator.Run`

#### What Happens in `SimulationPage.xaml.cs`
In `SimulationPage.StartSimulation`:
```csharp
// SimulationPage.xaml.cs lines 187-188
_simulator.AddRunEvent(() => app.Run());
_simulator.AddCloseEvent(() => Task.Run(app.Close));

// Line 203
Task.Run(async () =>
{
    await _simulator.Run(3600); // Intended to run for up to 1 hour
});
```

#### What `RealtimeSimulator.Run` Does
```csharp
// Simulator/RealtimeSimulator.cs lines 13-34
public override async Task Run(float simulationTimeLimit)
{
    start = DateTime.Now;

    List<Task> tasks = new List<Task>();
    foreach (var ev in RunEvents)
    {
        tasks.Add(ev());
    }
    var delay = simulationTimeLimit > 0
        ? Task.Delay(TimeSpan.FromSeconds(simulationTimeLimit))
        : Task.Delay(System.Threading.Timeout.InfiniteTimeSpan);

    // BUG IS HERE:
    var t = await Task.WhenAny(Task.WhenAll(tasks), delay);

    tasks.Clear();
    foreach (var ev in CloseEvents) {
        tasks.Add(ev());
    }

    await Task.WhenAll(tasks);
}
```

#### What `app.Run()` Actually Returns
```csharp
// CRDTApp/App.cs lines 28-33
public async Task Run(float executionTime =-1)
{
    var t1 = Behaviour.Run();
    var t2 = NetworkComponent.Run();
    await Task.WhenAll(t1, t2);
}
```
And inside `InsertBehaviour.cs`:
```csharp
// CRDTApp/Behaviours/InsertBehaviour.cs lines 102-105
public override async Task Run() {
    running = true;
    SendUpdateUntil(minNumber, maxNumber); // CS4014: Unawaited call!
}
```
- `SendUpdateUntil` is an `async Task` method, but it is called **without `await`**.
- Furthermore, `SendUpdateUntil` schedules subsequent operations via `scheduler.ExecuteAfter(delayBetweenOps, ...)` — it does not return a Task representing total completion.
- `NetworkComponent.Run()` returns `Task.CompletedTask`.
- **Result:** `app.Run()` finishes synchronously and returns a completed Task within milliseconds of starting!

#### The Domino Effect of the Early Exit
1. At $T = 0.000\text{ s}$: `StartSimulation()` starts.
2. At $T = 0.001\text{ s}$: `SendUpdateUntil(0, 2)` runs its first iteration:
   - Queues operation 0.
   - Dispatches the first wave of packets into `simulator.ScheduleMessageDelivery()`.
   - Schedules operation 1 to run after `delayBetweenOps` ($1.0\text{ s}$) via `scheduler.ExecuteAfter`.
   - Returns immediately.
3. At $T = 0.002\text{ s}$: Every `app.Run()` task has returned!
4. At $T = 0.003\text{ s}$: `Task.WhenAll(tasks)` completes.
5. At $T = 0.004\text{ s}$: `await Task.WhenAny(Task.WhenAll(tasks), delay)` finishes because `Task.WhenAll(tasks)` completed. The `delay` of 3600 seconds is ignored!
6. At $T = 0.005\text{ s}$: `RealtimeSimulator.Run` calls all `CloseEvents`:
   ```csharp
   app.Close();
   ```
   `app.Close()` calls:
   ```csharp
   Behaviour.Stop();        // sets running = false
   NetworkComponent.Stop(); // sets Running = false
   ```
7. At $T = 1.000\text{ s}$: The timer for the second operation fires:
   ```csharp
   async Task SendUpdateUntil(int currentNumber, int maxNumber) {
       if (currentNumber > maxNumber || !running) { // running is FALSE!
           return; // SILENTLY ABORTED!
       }
   ```
   Because `running == false`, **no further operations are ever sent**.
8. In `SendUntilSuccess`:
   ```csharp
   if (!succeeded && running) // running is FALSE! Retries are dead.
   ```
9. In `FloodingRedirectionLayer`:
   ```csharp
   if (floodMessage.NumberOfHops > 0 && Running && ...) // Running is FALSE! Packet dropped.
   ```
10. In `HeartbeatLayer` & `DistanceVectorRoutingLayer`:
    ```csharp
    if (Running) // Running is FALSE! Periodic heartbeats and routing updates cease.
    ```

**Outcome:** The user sees exactly the first wave of packets in flight. Once those finish or drop, the simulation is completely dead and never converges.

---

### Root Cause 2: Why `RunSimulations` Works with `DiscreteSimulator`

In `Simulator/DiscreteSimulator.cs`:
```csharp
public override async Task Run(float simulationTimeLimit)
{
    List<Task> tasks = new List<Task>();
    foreach (var runEvent in RunEvents)
    {
        tasks.Add(runEvent());
    }
    await Task.WhenAll(tasks); // Completes immediately, BUT...

    // DiscreteSimulator runs the discrete event loop to completion!
    await Task.Run(() => { env.RunD(simulationTimeLimit); });

    // CloseEvents are only executed AFTER the simulation time runs out!
    tasks.Clear();  
    foreach (var ev in CloseEvents)
    {
        tasks.Add(Task.Run(ev));
    }
    await Task.WhenAll(tasks);
}
```
In `DiscreteSimulator`:
1. `env.RunD(simulationTimeLimit)` blocks and runs the SimSharp event queue in virtual time.
2. `CloseEvents` are **not** invoked until `env.RunD` completes.
3. Therefore, `running` and `Running` remain `true` throughout the entire run.
4. Additionally, in `RunSimulations/CompleteGraphExperiment.cs`, the test is configured with:
   ```csharp
   float delay = 0.0f; // Zero transmission latency
   ```
   With zero latency, ACK packets return instantaneously, so timeouts never occur.

---

### Root Cause 3: `EnsureAckNetworkLayer` Timeout Mismatch with Link Delays

In `CRDTApp/NetworkLayers/Composite/EnsureAckNetworkLayer.cs`:
```csharp
const int NUMBER_OF_ATTEMPTS_UNTIL_TIMEOUT = 3;
float waitTime = 0.5f; // Default timeout per attempt
```
In `VisualizationTest/SimulationConfig.cs`:
```csharp
public float MinimumMessageDelay { get; set; } = 0.5f;
public float MaximumMessageDelay { get; set; } = 1.5f;
```
In `RunSimulations/NetworkCreationFunctions.cs`:
```csharp
// Lines 59, 86, 114: waitTime is never passed, defaulting to 0.5f!
EnsureAckNetworkLayer<int> ackLayer = new EnsureAckNetworkLayer<int>(id, flood, scheduler, (_) => { i++; return i; });
```

#### The Timing Disparity
- **One-way link delay:** $0.5\text{ s} \le T_{\text{link}} \le 1.5\text{ s}$.
- **Message travel time:** at least $0.5\text{ s}$.
- **ACK travel time:** at least $0.5\text{ s}$.
- **Round-Trip Time (RTT):** at least $1.0\text{ s}$, up to $3.0\text{ s}$ (or more if multi-hop routing is used).
- **`EnsureAckNetworkLayer.waitTime`:** **$0.5\text{ s}$**.

When Node A sends a message:
1. Node A starts timer for $0.5\text{ s}$.
2. The packet is still in flight on the wire at $0.5\text{ s}$.
3. Timer expires $\rightarrow$ Attempt 1 fails.
4. Node A retransmits, waits $0.5\text{ s} \rightarrow$ Attempt 2 fails.
5. Node A retransmits, waits $0.5\text{ s} \rightarrow$ Attempt 3 fails.
6. Total time elapsed: $1.5\text{ s}$. Node A declares `ConnectionInterrupted` and returns `false`.
7. Even if the ACK finally arrives at $T = 1.0\text{ s}$, Node A has already discarded attempt 1 or 2.

In multi-hop topologies (Ring, Star, Random Mesh), RTT is multiplied by the hop count $H$:
$$\text{RTT} \ge 2 \times H \times T_{\text{link}}$$
For a 2-hop route, RTT is $\ge 2.0\text{ s}$. A timeout of $0.5\text{ s}$ will fail 100% of the time.

---

### Root Cause 4: The Unused `closeSem` in `App.cs`

Notice this field at the top of `CRDTApp/App.cs`:
```csharp
public class App
{
    SemaphoreSlim closeSem = new SemaphoreSlim(0); // Declared but NEVER USED
```
The original architectural intent was likely:
1. `App.Run()` should wait until either the app converges (`BehaviourEnded`), or `App.Close()` releases `closeSem`.
2. Because `closeSem` was never hooked into `App.Run()`, `App.Run()` returns immediately.

---

## Comparison Matrix

| Property | `RunSimulations` (Discrete) | `VisualizationTest` (Realtime) |
|---|---|---|
| **Simulator Class** | `DiscreteSimulator` | `RealtimeSimulator` |
| **Scheduler Class** | `SimulationScheduler` | `SimulationScheduler` wrapping `RealtimeSimulator` |
| **Simulation Loop** | `env.RunD(limit)` runs all queued virtual events | `Task.WhenAny(Task.WhenAll(tasks), delay)` |
| **`tasks` in `RunEvents`** | Complete in <1ms, but ignored because `env.RunD` blocks | Complete in <1ms $\rightarrow$ **Triggers immediate shutdown!** |
| **When `CloseEvents` Run** | After `env.RunD` finishes | **Immediately at $T \approx 2\text{ ms}$** |
| **Link Latency** | `delay = 0.0f` | `0.5f` to `1.5f` seconds |
| **ACK RTT vs `waitTime`** | RTT = 0ms $\le$ `0.5s` $\rightarrow$ **Success** | RTT = 1.0s-3.0s $>$ `0.5s` $\rightarrow$ **Always times out** |
| **Convergence Result** | All nodes converge | Stalls on wave 1; never converges |

---

## Recommended Remediation Steps

When you are ready to fix this in code, here are the exact adjustments needed:

### 1. Fix `RealtimeSimulator.Run` Lifecycle
In `Simulator/RealtimeSimulator.cs`:
Do not rely on `Task.WhenAll(tasks)` to keep the simulation alive if `RunEvents` are fire-and-forget. Either:
- Await the `delay` task directly instead of `Task.WhenAny(Task.WhenAll(tasks), delay)`:
  ```csharp
  // Wait for the full simulation duration or until cancellation
  await delay;
  ```
- Or track completion via a `TaskCompletionSource` / `CountdownEvent` that completes only when all apps have called `CallBehaviourEnded()`.

### 2. Connect `App.Run` to Completion or Cancellation
In `CRDTApp/App.cs`:
Use the declared `closeSem`:
```csharp
public void Close() {
    Behaviour.Stop();
    NetworkComponent.Stop();
    closeSem.Release();
}

public async Task Run(float executionTime = -1)
{
    var t1 = Behaviour.Run();
    var t2 = NetworkComponent.Run();
    await closeSem.WaitAsync(); // Keep alive until Close() is called
}
```

### 3. Make `EnsureAckNetworkLayer.waitTime` Configurable and Dynamic
In `CRDTApp/NetworkLayers/Composite/EnsureAckNetworkLayer.cs`:
Pass `waitTime` from `NetworkCreationFunctions` based on the network configuration:
$$\text{waitTime} \ge 2 \times \text{MaxHops} \times \text{MaximumMessageDelay} + \text{margin}$$
For the default visualization settings ($1.5\text{ s}$ max delay, 5 hops):
$$\text{waitTime} \approx 2 \times 5 \times 1.5\text{ s} \approx 15.0\text{ s}$$
At minimum, for direct/1-hop connections, `waitTime` must be $\ge 3.5\text{ s}$ (to cover $2 \times 1.5\text{ s} + 0.5\text{ s}$ safety margin).

---

*Report generated by Antigravity AI. No codebase files were edited.*
