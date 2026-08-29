# Roadmap: Performance & Tick Stability

This document is the detailed performance/scalability contract for TerraRuntime. It expands the main roadmap with concrete constraints, metrics, stress profiles and acceptance criteria.

The governing rule is:

> Не пытаться сделать каждый code path максимально быстрым. Сначала сделать его стоимость ограниченной и предсказуемой.

## Goal

TerraRuntime must preserve a stable authoritative simulation rate under gameplay, networking and background load.

Baseline direction:

- simulation rate: **60 TPS**;
- tick budget: **16.67 ms**;
- no subsystem may consume an unbounded share of one tick;
- burst-heavy work must be bounded, incremental or moved off the authoritative game thread;
- slow clients, mass join, autosave, section generation, NPC/projectile sync and administrative work must not create long tick stalls;
- performance changes require reproducible measurements before they are accepted.

Current foundation already present:

- dedicated authoritative loop with wall/CPU timing;
- bounded worker pool and explicit completion handoff;
- bounded per-connection outbound queues and slow-client signaling;
- server-authoritative player identity and movement relay;
- real two-client TCP movement relay smoke;
- runtime-owned interest-management control/routing boundary, initially passthrough until spatial correctness is implemented.

The existence of the routing boundary does **not** mean spatial culling is complete. Actual update suppression is blocked on enter/leave, hysteresis and resync correctness.

---

## Verified implementation checklist

> Checkbox policy: `[x]` means the item is verified on `main` by implementation plus tests/CI or an equivalent executable proof. Partial/foundation-only work remains `[ ]`.

- [x] Dedicated authoritative loop with wall/CPU timing.
- [x] Bounded worker pool with explicit completion handoff.
- [x] Bounded per-connection outbound queues with slow-client signaling.
- [x] Server-authoritative player identity/movement relay with a real two-client TCP movement smoke.
- [x] Runtime-owned interest-management control/routing boundary.
- [x] Single-active-save coalescing scheduler.
- [x] Atomic save-file writer.
- [ ] End-to-end staged/fair join work budget under mass join.
- [ ] Encoded section cache with section-local invalidation and bounded memory.
- [ ] Full dirty-section-driven sync/save architecture.
- [ ] Actual AOI packet suppression with enter/leave hysteresis and forced resync.
- [ ] Complete 24/64/128/255-connection stress acceptance matrix.

## 1. Global per-tick work budgets

Introduce explicit global budgets for burst-heavy subsystems:

- inbound commands;
- section generation/encoding;
- initial player synchronization;
- dirty entity synchronization;
- liquid processing;
- world scans;
- expensive gameplay maintenance;
- background-result application.

Each budget must expose:

- hard operation limit;
- optional CPU-time limit;
- backlog size;
- oldest backlog age;
- processed/deferred/dropped counters.

Unused work is deferred to later ticks when observable vanilla behavior allows it.

The budget is global **per subsystem**, never multiplied by player count. Example: if section streaming is budgeted at 4 ms/tick, twenty simultaneous joins still share 4 ms total rather than receiving 4 ms each.

Initial section-streaming direction: **2-4 ms/tick total**, to be corrected by Small/Medium/Large-world benchmarks.

## 2. Staged join pipeline

Replace synchronous initial bootstrap with a staged state machine:

```text
Connected
    -> Handshake
    -> WorldInfo
    -> MinimumSections
    -> InitialEntities
    -> SpawnReady
    -> Playing
    -> RemainingInterestSync
```

Requirements:

- first-time section encoding is spread across ticks;
- minimum spawn-critical data has priority;
- additional interest data may continue after playable state only where protocol semantics permit it;
- all joining players share one section-work budget;
- scheduling between joining clients is fair enough that one client cannot monopolize work;
- existing playing clients retain tick stability during mass join.

Metrics:

- currently joining players;
- pending sections;
- oldest pending join;
- sections encoded per tick;
- section CPU time;
- join completion average/p95/p99.

## 3. Encoded section cache

Each runtime section should eventually track at least:

```text
WorldSection
    revision
    dirty
    encodedFrame
    encodedRevision
```

When `encodedRevision == revision`, the immutable encoded frame can be reused for multiple recipients.

Mutation invalidates only affected sections:

```text
tile/world mutation
    -> section revision++
    -> cached encoded frame becomes stale
```

Requirements:

- one encoded immutable frame can be shared by many outbound queues;
- no recipient-specific `byte[]` clone when bytes are identical;
- invalidation is section-local;
- a stale frame is never sent after a committed mutation;
- failed encoding never marks a section cached/sent;
- cache memory is bounded and observable.

Metrics: hit/miss, encode count, invalidations, encode CPU, cache bytes, average encoded/compressed section size.

## 4. Section revision and dirty tracking

Dirty tracking is part of world runtime architecture, not a later networking patch.

World mutations that must mark affected sections include:

- tile placement/destruction;
- wall changes;
- liquid changes;
- wiring modifications;
- multi-tile object changes;
- other mutations that change section-visible state.

Use a deduplicated dirty set/queue. One section modified hundreds of times in a tick must not become hundreds of queue entries.

Avoid full-world change discovery scans.

## 5. Incremental save snapshots

Autosave must not serialize live mutable world state on the authoritative thread.

Direction:

```text
first save:
authoritative world -> bounded snapshot handoff -> background serialization

later saves:
previous reusable snapshot + dirty sections/state -> refreshed snapshot
```

The reusable snapshot strategy must cover all canonical mutable persistent state, not only tiles:

- chests;
- signs;
- tile entities;
- persistent NPC state;
- progression;
- persistent liquid state;
- `.wld` runtime metadata;
- any other canonical save data.

Measure snapshot copy time separately from serialization, compression/encoding, disk write, fsync and atomic replace.

Goals: reduce allocations, page faults and game-thread memcpy without risking save correctness.

## 6. Background save pipeline

```text
Game Thread
    -> immutable/atomic snapshot
    -> bounded Save Worker
        -> serialize
        -> validate
        -> write temp
        -> flush/fsync
        -> atomic replace
```

Rules:

- at most one active save;
- autosave requests arriving during an active save are coalesced rather than queued without bound;
- shutdown waits for active save work;
- final shutdown save represents newer state and is committed last;
- save failure is isolated from simulation failure;
- snapshot memory becomes immutable after worker handoff.

## 7. Fine-grained tick phases

Keep phases coarse enough to be useful and cheap, but detailed enough to identify the subsystem causing a slow tick.

Initial direction:

```text
Ingress
Commands
Snapshot
Liquids
Growth
Spread
Weather/World
Sections
Items
NPC
Projectiles
Damage
Spawning
Housing
Sync
```

Per tick record:

- wall duration;
- CPU duration;
- phase CPU/wall duration where available;
- worst phase;
- processed work counts;
- missed deadline state.

Reporting windows expose average, p50, p95, p99 and worst.

`wall time != CPU time`. A 40 ms wall tick with 1 ms thread CPU is primarily scheduler/contention evidence, not automatically a gameplay-hot-path regression.

## 8. Incremental O(world) systems

Large full-world scans in one tick are prohibited unless verified vanilla semantics require atomic completion.

Candidates include:

- biome/evil census;
- housing searches;
- liquid maintenance;
- growth/spread;
- world maintenance;
- inactive entity cleanup;
- visibility/spatial-index rebuild work.

Incremental systems must define:

- cursor/state;
- operation/CPU budget;
- maximum completion latency;
- invalidation/restart behavior;
- whether stale intermediate results are safe.

## 9. Runtime-owned interest management / spatial visibility

Interest management belongs entirely to TerraRuntime.

Vega, TUI, future web/API and other hosts may **only enable or disable it**. They do not own or configure recipient selection algorithms.

Public control plane:

```csharp
IInterestManagementControl control;
control.SetEnabled(true);
control.SetEnabled(false);
bool enabled = control.IsEnabled;
```

Standalone startup:

```text
TerraRuntime.Server --world world.wld --interest-management
```

Programmatic hosting foundation:

```csharp
var interest = new InterestManagementControl();
var options = new ServerHostOptions(worldPath, 7777, 255);
Task<int> server = TerrariaServerHost.RunAsync(options, interest);

interest.SetEnabled(true);
```

Ownership rules:

- state is world-scoped, not a global process switch;
- a future multi-world process has one independent control/index per world;
- external code receives no spatial buckets, radii, hysteresis knobs, observer queries or entity-routing callbacks;
- `Multiplicity` owns packet representation/encoding, never visibility decisions;
- TerraRuntime owns spatial policy, enter/leave, hysteresis, resync and recipient selection;
- disabling falls back to conservative global/vanilla-like fan-out;
- unknown spatial state fails open;
- live enable/disable does not require world restart.

Foundation implementation is intentionally passthrough while enabled. Real culling must not be activated until the following are correct and tested:

- initial observer/subject position state;
- enter-interest full state;
- leave-interest removal/deactivation semantics;
- teleport and respawn visibility rebuild;
- hysteresis around boundaries;
- forced resync/eventual consistency.

Target index direction:

```text
Section / InterestCell
    players
    NPCs
    items
    projectiles
```

Actual cell size/radii remain internal implementation details selected from correctness evidence and benchmarks.

## 10. Remove unconditional O(players²) movement relay

The current all-playing-peers movement fan-out is a temporary correctness baseline, not the scalability architecture.

Target:

```text
player moved
    -> authoritative position update
    -> spatial/interest lookup
    -> interested Playing recipients only
    -> one encoded immutable movement frame shared by recipient queues
```

Requirements:

- full state when a new observer enters range;
- correct leave/out-of-range semantics;
- teleport/respawn forces immediate recalculation;
- authoritative player slot remains server-owned;
- forced resync prevents permanent stale remote state.

Benchmark 255-player movement in clustered, evenly distributed, two-group and all-moving profiles.

## 11. Dirty-state entity synchronization

NPC, projectile and item updates use entity revisions/dirty flags rather than unconditional full broadcast.

Example state dimensions for an NPC:

- simulation revision;
- transform dirty;
- life dirty;
- AI dirty;
- full-sync-required flag.

Sync planner decides whether to send, full vs delta, recipients and last-recipient state.

## 12. Proximity-based sync frequency

Where protocol/gameplay correctness permits, sync cadence may vary by interest tier:

```text
very near   -> high frequency
near        -> normal frequency
far         -> reduced frequency
not visible -> skip
```

Candidates: NPCs, safe projectile classes, items and player movement.

Distance alone is not sufficient when section/visibility state gives a more correct answer.

## 13. Forced periodic resync

Interest throttling must provide eventual consistency.

Define a bounded maximum skipped-update count and/or maximum elapsed time since last sync per entity/recipient class. Crossing the bound forces a full or delta resync even if normal throttling would skip it.

## 14. Sync frame sharing

Encode identical outbound state once:

```text
encode once -> immutable frame -> A / B / C recipient queues
```

Requirements:

- asynchronous writers safely retain the frame lifetime;
- no recipient can mutate shared bytes;
- queue byte accounting still counts logical queued bytes per connection;
- slow-recipient behavior cannot corrupt or block other recipients.

## 15. Outbound batching

Batch only frames already queued. Never intentionally wait for a batch to fill.

Measure:

- frames/write;
- bytes/write;
- syscalls/sec;
- queue drain rate;
- packet latency.

Batching must have a strict latency ceiling for latency-sensitive traffic.

## 16. Slow-client isolation

Every connection owns a bounded outbound queue by frame count and byte count.

Slow-client policy may, where protocol semantics permit:

- drop/coalesce low-priority updates and schedule resync;
- disconnect pathological clients.

It must never block the simulation thread on socket output.

Telemetry records connection, queue depth/bytes, packet class that exhausted the budget and action taken.

## 17. Inbound flood isolation

One source cannot monopolize the global command budget.

Maintain per-source quotas and category budgets for at least movement, tile edits, chat, inventory and other traffic, with vanilla-compatible rate ceilings.

Track rejected/deferred packets, throttled source and oldest command age.

## 18. Dedicated worker isolation

CPU-heavy work without mutable-world ownership runs through bounded dedicated workers, not unlimited `Task.Run` fan-out.

Examples: compression, save serialization, expensive validation, password hashing, runtime-cache generation and diagnostics.

Worker pools require fixed/controlled worker count, bounded input/completion queues, backpressure, cancellation and authoritative completion application.

## 19. Allocation budget on hot paths

Frequently executed paths do not create heap garbage without measured justification.

Measure at least:

- bytes allocated/tick;
- allocations/tick;
- bytes allocated by packet/event class;
- Gen0 collections/minute.

Priority paths: movement decode/encode, NPC/projectile/item sync, frame routing, command staging and visibility calculations.

Movement/control target: zero or near-zero allocations per packet where this can be achieved without unsafe complexity.

## 20. GC observability and tuning

TerraRuntime is .NET 11 **NativeAOT-first**, but it still has a managed heap and GC behavior that must be measured.

Collect:

- allocation rate;
- Gen0/1/2 collections;
- GC pause duration;
- LOH allocation rate;
- heap size;
- fragmentation where available.

GC configuration changes are benchmark decisions, not folklore. Compare supported Workstation/Server GC and heap settings only on compatible NativeAOT builds. `GC.TryStartNoGCRegion` is experimental, never baseline architecture.

## 21. Avoid async machinery inside simulation hot path

The authoritative simulation tick remains synchronous.

Do not put normal tick work behind `await`, uncontrolled continuations, ThreadPool scheduling or arbitrary external callbacks.

Boundary:

```text
Async I/O
    -> bounded command queue
    -> synchronous authoritative tick
    -> bounded outbound queues
    -> Async I/O
```

## 22. Spatial indexes

As NPC/items/projectiles arrive, add measured incremental spatial indexes for:

- nearby players/NPCs;
- visibility;
- pickup candidates;
- damage/contact candidates;
- section subscribers.

Do not retain `for every entity -> for every player` global scans when section/grid buckets can preserve semantics more cheaply.

## 23. Performance benchmark suite

Create a dedicated benchmark/stress project with population profiles:

```text
1
8
24
64
128
255
```

Required scenarios:

- idle;
- all-player movement;
- 255-player clustered movement worst-case fan-out;
- distributed Large-world movement;
- mass join;
- NPC load;
- projectile load;
- combat;
- tile edits;
- liquids;
- autosave under load;
- join during combat/NPC/autosave load.

## 24. Tick performance gates

Record for each scenario:

- average CPU tick;
- p50/p95/p99/max;
- missed deadlines;
- wall stalls;
- worst phase;
- allocations/tick;
- network throughput;
- outbound queue peak;
- packet drops;
- slow-client disconnects.

Do not accept average-only results.

Initial directional gates, to be recalibrated when gameplay is complete:

```text
normal load: p99 CPU tick < 8 ms
stress load: p99 CPU tick < 12 ms
hard rule: no sustained simulation work > 16.67 ms
```

## 25. Performance regression CI

Split performance validation by cost.

Fast PR gate:

- microbenchmarks;
- allocations;
- queue behavior;
- section encoding;
- scheduler/game-loop checks;
- selected 8/24-player simulations.

Scheduled/nightly stress:

- 64/128/255 players;
- Large world;
- mass join;
- movement;
- autosave;
- NPC/projectile load.

Release gate runs the full matrix. Regressions fail when compatible-environment metrics exceed defined tolerances.

## 26. Real subprocess load tests

Critical network/crowd tests prefer a real TerraRuntime process plus a separate load-generator process and real TCP connections.

This captures scheduler behavior, OS socket buffering, real backpressure and contention that in-process virtual clients can hide.

## 27. Machine-readable performance baselines

Each release baseline records:

- CPU model;
- OS;
- .NET/runtime version;
- NativeAOT RID/build identity;
- world size;
- player count;
- scenario;
- commit SHA;
- tick metrics;
- allocations;
- network metrics;
- memory.

Compare only compatible environments or explicitly label the environment difference.

## 28. Measure before optimizing

Every performance change records:

```text
hypothesis
baseline
change
measurement
result
decision
```

Revert changes that do not provide measurable value or that damage memory, readability, correctness or latency. Document attractive failed experiments so they are not repeatedly rediscovered.

## 29. Memory scalability targets

Measure total and categorized memory at 0, 24, 128 and 255 players.

Track at least:

- world state;
- section cache;
- outbound queues;
- entity state;
- save snapshots;
- save workers;
- runtime-world cache;
- protocol buffers;
- spatial/interest indexes.

Do not buy network resilience with `255 players × huge outbound queue` memory growth.

## 30. Acceptance criteria

Performance architecture is not complete until real stress evidence shows:

- stable 60 TPS authoritative loop;
- mass join does not cause multi-tick stalls;
- autosave serialization is off the simulation thread;
- slow client cannot stall others;
- one abusive source cannot monopolize the loop;
- section generation/streaming is globally time/operation bounded;
- no unconditional O(players²) movement broadcast remains as baseline architecture;
- interest management is runtime-owned, world-scoped and externally controllable only through `IInterestManagementControl`/startup enablement;
- NPC/projectile/item synchronization uses interest management and dirty state;
- full-world maintenance is incremental where semantics permit;
- hot movement path has a measured allocation budget;
- 255 real TCP clients pass sustained movement tests;
- Large world passes sustained load tests;
- performance regression validation is automated;
- observability identifies the subsystem responsible for slow ticks.
