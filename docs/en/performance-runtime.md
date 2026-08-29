# Performance, tick scheduling and work budgets

[Русский](../ru/performance-runtime.md) · [Documentation](README.md) · [Architecture](architecture.md) · [Performance roadmap](../roadmap/performance-tick-stability.md)

## 1. Performance model

TerraRuntime treats performance as a correctness constraint around bounded work, not as permission to change observable vanilla behavior for a prettier benchmark.

The baseline simulation model is one authoritative owner running a fixed-rate tick schedule while networking and bounded background work proceed independently.

The priority order is:

```text
correctness and vanilla-visible behavior
        -> bounded work / failure isolation
        -> measurement
        -> optimization
```

An optimization without measurement is a hypothesis, not a completed performance change.

## 2. Default tick rate

`GameLoopOptions.DefaultTicksPerSecond` is **60**.

At 60 Hz the nominal interval is about **16.67 ms**.

The runtime allows a configured positive tick rate up to 1000 Hz at the generic loop level, but the Terraria runtime baseline is 60 Hz. Raising the generic loop option does not automatically make vanilla Terraria gameplay semantically correct at a higher simulation frequency.

Game logic constants, timers, networking cadence and vanilla reference behavior must be audited before any alternative tick-rate mode is presented as supported gameplay.

## 3. Dedicated authoritative thread

`AuthoritativeGameLoop<TState,TCommand>` runs on a dedicated non-background thread named:

```text
TerraRuntime Game Loop
```

The thread owns mutable simulation state. Producers submit commands; they do not receive the state reference.

This ownership model avoids using locks as the normal mechanism for gameplay mutation and makes per-tick work measurable as one controlled sequence.

## 4. Tick phases in the generic loop

The current generic authoritative loop measures three top-level phases:

```text
Ingress  -> stage bounded commands from the channel
Commands -> apply bounded/fair commands
Update   -> run the authoritative state update
```

The runtime snapshot exposes last/worst timings and the slowest phase for diagnosis.

Higher-level Terraria update code can further decompose work into subsystem phases. The architecture roadmap expects areas such as liquids, items, NPC AI, projectiles, combat, spawning, housing, progression and synchronization to remain separately observable as implementation grows.

## 5. Global command capacity

Default global command mailbox capacity:

```text
CommandCapacity = 8192
```

The channel is bounded. External producers cannot force an unbounded retained command list merely by submitting faster than the game loop can execute.

`TryPost` can reject when global or per-source pending capacity is exhausted.

A bounded mailbox is an invariant; the exact production sizing should continue to be validated against realistic load.

## 6. Bounded ingress

Default command staging limit:

```text
MaxCommandIngressPerTick = 2048
```

The game loop does not drain the entire producer channel into internal source queues every tick. This keeps the ingress phase bounded even when the global mailbox is full.

Staging and applying commands are separate budgets because merely reorganizing a large backlog also costs time.

## 7. Global apply budget

Default authoritative command execution limit:

```text
MaxCommandsPerTick = 1024
```

Once the operation budget is exhausted, remaining commands are deferred to later ticks.

The runtime reports deferred work and command-budget exhaustion rather than hiding the backlog behind an unbounded loop.

## 8. Per-source fairness

Default per-source apply quota:

```text
MaxCommandsPerSourcePerTick = 128
```

Non-system sources are throttled after reaching their quota for the tick and re-enter the ready rotation later.

This prevents one busy connection/source from consuming the entire global command budget while other sources wait indefinitely.

System-owned work is exempt from the per-source quota but remains subject to the broader authoritative design and should not become an unbounded bypass path.

## 9. Per-source pending limit

Default retained pending commands for one external source:

```text
MaxPendingCommandsPerSource = 1024
```

This limit is separate from the global `CommandCapacity`.

It prevents one connection/source from occupying the entire global mailbox even before per-tick fairness is applied.

## 10. Optional command CPU budget

`MaxCommandCpuMillisecondsPerTick` can impose an optional CPU-time ceiling for the command-application phase.

The hard operation-count budget remains active even when the platform cannot provide a thread CPU clock.

When CPU timing is available and the configured budget is reached, command processing stops for that tick and remaining work is deferred.

The generic option has no default CPU budget value. A production value should be chosen from measurement rather than guessed.

## 11. CPU time versus wall time

The game loop records both:

```text
wall duration
thread CPU duration when available
```

They answer different questions.

- High wall + high CPU suggests real work on the authoritative thread.
- High wall + low CPU can indicate scheduler/OS contention or blocking outside pure computation.

Do not diagnose a slow tick from wall time alone when CPU data is available.

## 12. Missed tick policy

TerraRuntime does **not** run burst catch-up ticks after missing a deadline.

When the current tick finishes after its next deadline, the loop counts the missed deadlines and resets the next schedule anchor to the current time.

Conceptually:

```text
late tick
   -> count missed deadlines
   -> skip burst catch-up
   -> continue from now
```

This avoids a spiral where one expensive tick causes multiple immediate catch-up ticks that create even more backlog and latency.

## 13. Pending age

The loop tracks the age of the oldest pending command.

Queue depth alone can hide starvation. A stable queue count with an increasing oldest age means work is waiting longer even if the number of queued commands is not exploding.

Useful scheduler diagnosis therefore considers both:

```text
pending/deferred count
oldest pending age
```

## 14. Command rejection

`TryPost` reserves per-source and global pending capacity before writing into the bounded channel.

If reservation or channel write fails, the command is rejected and rejection telemetry increases.

The producer must receive an explicit failure result instead of assuming all submitted work eventually enters authoritative state.

## 15. Source scheduling

Staged commands are grouped by `GameCommandSourceId` and rotated through ready source queues.

This provides deterministic bounded fairness without creating one operating-system thread per player/source.

The scheduling structure is an implementation detail; the semantic guarantees are bounded global work, per-source fairness and preserved ordering where required.

## 16. Networking stays asynchronous

Socket read/write work is not performed by the game-loop thread.

Networking can receive and encode asynchronously, but authoritative mutation still crosses the bounded command boundary.

Similarly, the game loop does not wait synchronously for a slow client's TCP receive window. Outbound work ends in a bounded per-connection queue and a separate writer.

## 17. Background workers

CPU-heavy or blocking work may run outside the game loop when ownership is clear.

Workers must consume immutable snapshots or isolated buffers and return explicit completion data through a controlled commit path.

Do not use unbounded `Task.Run` fan-out as a substitute for designing work ownership and capacity.

## 18. Disk I/O and saving

Persistence is structured so disk serialization/write happens outside the authoritative hot path.

The game loop performs bounded snapshot/shadow synchronization, then hands detached data to the background save coordinator.

The current tile save shadow synchronizes a bounded number of sections per tick rather than copying the complete tile array in one pause.

## 19. Join/bootstrap performance

Joining is a burst workload and must not be budgeted as if it were normal steady-state movement.

Initial world sections/entity state can generate many frames and expensive serialization/compression work.

Join work must use **global subsystem budgets**. Giving every joining player a complete section-generation budget would multiply the worst-case tick cost by the number of concurrent joins.

Bootstrap frame count is also hard-bounded below the production outbound queue capacity by live integration checks.

## 20. Synchronization scaling

Unconditional player-to-player movement broadcast trends toward O(players²) work.

TerraRuntime already has spatial/visibility tracking infrastructure for runtime-owned interest management, but actual default movement suppression remains passthrough until enter/leave/full-resync semantics are verified.

The performance rule is fail-open correctness first: do not reduce bandwidth by creating stale or permanently missing remote state.

## 21. Dirty/revision-driven work

The target runtime avoids full-world/full-entity scans when work can be driven by mutations/revisions.

Examples include:

- dirty world sections;
- replication registries for entities/objects;
- persistence dirty-section tracking;
- cached/prepared startup state.

Caches and dirty flags need explicit invalidation rules. A fast stale cache is a correctness regression.

## 22. Allocation discipline

Hot paths should avoid avoidable heap churn, but allocation removal is measured rather than ritualized.

Preferred tools include spans, pooled/owned buffers where justified, immutable frame sharing and compact value types.

Do not introduce `unsafe`, custom allocators or broad pooling without evidence that they materially improve the measured workload and do not inflate RSS/paging or complexity.

## 23. GC discipline

GC configuration is tuned only from compatible production-like measurements.

The NativeAOT standalone runtime cannot depend on JIT-specific performance assumptions such as tiered compilation or dynamic PGO.

The CoreCLR extensible host may use CoreCLR features, but runtime-core design still passes the NativeAOT production gate.

`GC.TryStartNoGCRegion` is not a baseline architecture assumption.

## 24. Performance telemetry

Current/target useful runtime telemetry includes:

- tick wall/CPU duration;
- worst/last phase duration;
- command processed/deferred/rejected counts;
- command budget exhaustion count;
- oldest pending command age;
- missed tick deadlines;
- inbound/outbound queue depths;
- slow-client events;
- entity counts;
- save snapshot/write state;
- startup/cache timings;
- allocation/GC metrics where safely available.

Telemetry should be aggregated/bounded and must not itself become a hot-path allocation problem.

## 25. Benchmark matrix

The performance roadmap treats connection/load sizes such as:

```text
1
8
24
64
128
255
```

as useful checkpoints.

`24` players is the first meaningful realistic optimization baseline; `255` connections is a stress/scalability target.

Idle, normal-play, join-burst, slow-reader and save workloads expose different bottlenecks and should not be collapsed into one benchmark score.

## 26. Before/after rule

A meaningful optimization records before/after results on the same hardware/environment, world and workload.

Record enough context to reproduce the result, including relevant runtime configuration and player/connection count.

If an optimization does not materially improve the intended metric or makes memory/latency/correctness worse, revert it rather than keeping complexity for theoretical value.

## 27. Current limitations

Performance work is intentionally ongoing. Important incomplete areas include:

- final measurement-derived queue limits for every workload;
- complete per-subsystem global budgets;
- actual production interest-management suppression/resync;
- complete packet allocation/throughput baselines;
- broad 24-player/255-connection soak and stress coverage;
- complete startup/save/GC profiling across large worlds;
- optimized section cache/dirty synchronization at final scale.

## 28. Change checklist

A performance/scheduler change is incomplete unless, where relevant:

- mutable state still has one authoritative owner;
- producer and per-source work remains bounded;
- fairness cannot be bypassed by one external source;
- missed-tick behavior is deliberate and tested;
- CPU and wall measurements are not conflated;
- before/after measurement supports the performance claim;
- NativeAOT constraints remain valid;
- any changed observable behavior has an explicit compatibility decision;
- this page and `docs/ru/performance-runtime.md` are updated together.
