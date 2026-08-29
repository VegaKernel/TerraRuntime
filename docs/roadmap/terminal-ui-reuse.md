# Terminal UI roadmap

This document refines Phase 10 of the main TerraRuntime roadmap.

The standalone TerraRuntime server already has a local Terminal.Gui v2 operations UI. The UI is intentionally separated from mutable runtime internals so its read-model and command semantics can be reused later without introducing a speculative remote-management framework now.

## Current rule

**Keep the local TUI useful and runtime-owned; preserve a clean operations boundary for possible future reuse.**

Do not add a remote management protocol, remote adapters, a separate client executable, or client-side plugin loading merely to prepare for a client that does not exist yet.

Views consume immutable operations snapshots. Mutations cross explicit runtime command boundaries.

## Verified implementation checklist

> Checkbox policy: `[x]` means the item is verified on `main` by implementation plus tests/CI or an equivalent executable proof. Partial/foundation-only work remains `[ ]`.

- [x] Local Terminal.Gui v2 operations UI exists and is exercised.
- [x] Views consume immutable/bounded operations snapshots instead of mutable runtime stores.
- [x] Administrative interest-management mutation crosses the authoritative command boundary.
- [x] TUI runs independently from the authoritative loop and TUI failure/exit does not stop the server.
- [x] Dashboard, Players, NPCs, Projectiles, Network, World and Logs views are implemented.
- [x] CoreCLR and Linux/Windows NativeAOT CI exercise the TUI smoke path.
- [ ] Remote administration protocol/adapter.
- [ ] Separate reusable remote administration client.
- [ ] Dynamic external/plugin UI windows for a future CoreCLR administration client.

## Current implementation

The standalone server has seven exercised operational views:

- **Dashboard** consumes `IRuntimeDashboardOperations` and `RuntimeDashboardSnapshot`. It shows lifecycle/world identity, target and observed TPS, tick wall/CPU timings, slowest phase, missed deadlines, authoritative command backlog/budget telemetry, managed heap/lifetime allocation, working set, process CPU, GC pause/collection counters, connection admission counters, and interest-management state.
- **Players** consumes `IPlayerOperations` and `RuntimePlayersSnapshot`. The generation-safe read model is populated only from already validated authoritative player events and includes slot/generation/connection identity, name/team, position, velocity, selected inventory slot, mount type, health, and mana.
- **NPCs** consumes `INpcOperations` and `RuntimeNpcsSnapshot`. It observes only committed authoritative NPC snapshots and exposes generation/revision/content identity, position/velocity, target/AI state, and bounded simulation/collision flags without exposing `RuntimeNpcStore` to the UI thread.
- **Projectiles** consumes `IProjectileOperations` and `RuntimeProjectilesSnapshot`. It observes committed authoritative projectile lifecycle state without exposing `RuntimeProjectileStore`, collapses live entries into `(spawner, projectile type)` groups, sorts the largest groups first, and exposes count, representative aggregate motion, and bounded damage/knockback maxima instead of flooding the screen with hundreds of near-identical rows.
- **Network** consumes `INetworkOperations` and `RuntimeNetworkSnapshot`. It shows admission/registration state, player replication counters, packet-23 NPC and packet-27 projectile replication counters, bounded outbound-queue/backpressure telemetry, live inbound one-second frame/byte rates, lifetime inbound counters, rejected inbound frames, and bounded per-connection pressure/rate detail.
- **World** consumes `IWorldOperations` and `RuntimeWorldSnapshot`. It exposes validated world identity, format/worldgen version, dimensions, persisted object/NPC counts, runtime-cache/startup timings, and a live authoritative world-clock projection containing day/night state, world time, day rate, moon phase, and slime-rain timer without exposing the mutable `RuntimeWorldClock` to the UI thread.
- **Logs** consumes `ILogOperations` over a bounded `RuntimeLogBuffer`. It supports severity filtering, dynamic source filtering, and pause/resume while remaining independent of the plain-console sink.

The UI also has a first bounded administrative action surface:

- **Enable/disable interest management** is queued through the authoritative runtime command ingress. The UI does not receive `IInterestManagementControl` or another mutable runtime capability. Queue-full/stopping rejection is reported instead of pretending the action succeeded.

## Operational behavior

- `--tui` explicitly enables the UI; plain-console/headless startup remains the default.
- Terminal.Gui v2 runs on a dedicated UI thread and never owns the authoritative game-loop thread.
- Closing the TUI closes only the UI and leaves the server running.
- TUI initialization/runtime failure is non-fatal and returns the host to plain-console behavior.
- `Console.Out` and `Console.Error` are never globally replaced. `RuntimeHostLog` suppresses matching host-owned console writes only while the full-screen UI is active, while the bounded log read model continues receiving events.
- Observed TPS comes from authoritative tick progress over time, not from `LastTickMilliseconds`.
- Runtime/network telemetry is read from subsystem-owned counters rather than reconstructed in the view.
- NPC UI telemetry exists only when TUI mode is enabled. Authoritative NPC commit publication remains allocation-free and does not take a simulation lock for the UI.
- Projectile UI telemetry also exists only when TUI mode is enabled. Authoritative projectile commits publish into a fixed protocol-addressable slot projection without a UI lock or per-commit allocation; grouping/allocation happens only when the TUI captures a snapshot.
- Packet-23 and packet-27 replication remain the first authoritative commit consumers. TUI projections are secondary observers through explicit fan-out objects and never own replication.
- World-clock UI telemetry also exists only when TUI mode is enabled. `RuntimeWorldClock` publishes committed primitive state through a non-blocking internal observer; the UI never receives the mutable clock instance.
- `--tui-smoke` renders Dashboard, Players, NPCs, grouped Projectiles, Network, World, and Logs and exercises packet-23/packet-27/world-clock formatting plus authoritative admin actions through the Terminal.Gui ANSI test driver.
- CoreCLR CI and Linux/Windows NativeAOT jobs exercise the same TUI smoke path.
- Host-affecting changes are also covered by the official-world workflow, including real world verification, host startup, live join/movement relay, snapshot-only warm startup, and canonical `.wld` checkpoint restoration.

The foundational local-TUI slice of Phase 10 is complete. Remaining work is incremental and should follow concrete operational needs.

## Dependency shape

Current shape:

```text
Terminal UI
    |
    v
small operations/read-model interfaces
    |
    +--> immutable snapshots
    |
    `--> authoritative command ingress
             |
             v
         TerraRuntime
```

Possible future shape, only when a real remote client exists:

```text
same/extracted Terminal UI
    |
    v
same operations semantics
    |
    +--> local adapter  --> TerraRuntime
    |
    `--> remote adapter --> operations protocol --> TerraRuntime server
```

The dependency boundary matters more than the number of projects.

## Project layout

The current implementation may remain directly in the server project:

```text
src/TerraRuntime/
    Operations/
    TerminalUI/
```

A separate `TerraRuntime.TerminalUI` project should be introduced only when there is an actual second consumer or when the current project becomes materially harder to maintain without the split.

Shared toolkit-independent read models/contracts may move to `TerraRuntime.Contracts` when they become genuine cross-component contracts. Do not create `Operations.Local`, `Operations.Remote`, `Ui.Contracts`, and similar assemblies merely for architectural symmetry.

## UI-facing operations boundary

Views must not depend directly on `ServerRuntimeState`, mutable world/player/NPC/projectile collections, sockets, connection queues, or authoritative-thread-owned objects.

Current screen-facing interfaces are deliberately small:

```text
IRuntimeDashboardOperations
IPlayerOperations
INpcOperations
IProjectileOperations
INetworkOperations
IWorldOperations
ILogOperations
```

Rules:

- snapshots are immutable and bounded;
- the TUI may poll snapshots from its own event loop/thread;
- administrative mutations are marshalled through the authoritative command boundary;
- local UI receives no privileged mutable-state access merely because it is in-process;
- telemetry is calculated or exposed by the subsystem that owns the truth;
- observability should reuse existing thread-safe counters when possible instead of adding duplicate hot-path accounting;
- bounded/grouped detail is preferred over unbounded per-connection/per-entity/per-event materialization.

## Runtime telemetry ownership

TerraRuntime currently owns and publishes UI-facing measurements for:

- target and observed TPS;
- tick wall time and authoritative-thread CPU time;
- missed deadlines and phase timings;
- command backlog, rejection, deferral, age, and budget-exhaustion telemetry;
- managed heap, lifetime allocation, working set, process CPU, GC pause percentage, and Gen0/Gen1/Gen2 collection counts;
- player replication counters;
- packet-23 NPC relay, baseline, rejection, and unsupported-commit counters owned by `RuntimeNpcReplicationRegistry`;
- packet-27 projectile relay, baseline, rejection, and unsupported-commit counters owned by `RuntimeProjectileReplicationRegistry`;
- aggregate and bounded per-connection outbound queue/backpressure state;
- live and lifetime inbound frame/byte accounting plus rejected inbound frames;
- authoritative player identity/vitals/movement state used by the Players view;
- authoritative committed NPC state used by the NPCs view;
- authoritative committed projectile state grouped for the Projectiles view;
- authoritative world-clock state published after committed clock changes;
- world/cache startup state and timings;
- bounded runtime log events.

The TUI formats these values but does not invent them.

## Implemented screens

### Dashboard

- lifecycle/readiness;
- world identity;
- target/observed TPS;
- last/worst tick wall and CPU time;
- slowest phase;
- missed deadlines;
- command backlog/budget state;
- managed heap and lifetime allocated bytes;
- process working set and CPU percentage;
- GC pause percentage and Gen0/Gen1/Gen2 collection counts;
- interest-management state and authoritative action result.

### Players

- stable player/session identity;
- generation-safe connection identity;
- name/team;
- position;
- velocity;
- selected inventory slot;
- mount type;
- health/mana.

Player data is fed from validated authoritative events. The UI does not read `ServerRuntimeState` directly. Optional movement fields are normalized to the same resulting state used by the authoritative runtime, so a later movement without velocity/mount clears the corresponding read-model values rather than leaving stale UI data.

### NPCs

- active authoritative slot and generation;
- committed revision;
- gameplay type and network identity;
- position and velocity;
- target;
- four AI state values;
- direction and collision state;
- wet/no-gravity/no-tile-collide flags;
- committed spawn/update/despawn counters.

NPC data is projected only after `RuntimeNpcStore` commits authoritative state. Packet-23 replication remains the primary consumer and the TUI is an observer through a commit fan-out. In TUI mode the projection uses a fixed 256-slot, single-writer sequence publication so authoritative NPC updates do not acquire a UI lock or allocate per commit. Headless mode does not create the NPC operations projection.

### Projectiles

- total live authoritative projectile count;
- grouping by `(Spawner, ProjectileTypeId)` rather than one line per projectile;
- groups sorted by descending count so projectile floods are immediately visible;
- best-effort player display when the spawner byte matches a currently playing player slot;
- explicit `spawner #N` fallback when no live player can be resolved;
- average group position and velocity for compact operational context;
- maximum current/original damage and knockback in the group;
- committed spawn/update/despawn counters.

`ProjectileSnapshot.Spawner` remains protocol/runtime provenance in the operations model; it is not renamed into a stronger gameplay-ownership contract merely for UI wording. The view may resolve that byte to a currently playing player name when the slot matches, but an unresolved value remains visibly a spawner instead of inventing an owner. This also keeps the operations model aligned with the packed `ProjectileKey` terminology used by the protocol adapter.

Projectile data is projected only after `RuntimeProjectileStore` commits authoritative state. Packet-27 replication remains the first consumer and the TUI is a second observer through `RuntimeProjectileStateCommitFanout`. In TUI mode the projection uses the fixed protocol-addressable 1001-slot table with a single-writer sequence publication; authoritative spawn/update/despawn commits do not allocate for the UI or acquire a UI lock. Aggregation uses TUI-thread-local temporary state only during snapshot capture. Headless mode does not create the projectile operations projection.

### Network

- active/admitted/rejected/registered connections;
- player relay/baseline/AOI-resync counters;
- packet-23 NPC relayed frames;
- packet-23 NPC join baselines;
- packet-23 NPC rejected outbound frames;
- packet-23 unsupported NPC commits;
- packet-27 projectile relayed frames;
- packet-27 projectile join baselines;
- packet-27 projectile rejected outbound frames;
- packet-27 unsupported projectile commits;
- tracked bounded outbound queues;
- aggregate queued frames/bytes;
- rejected outbound frames;
- slow-client count;
- bounded top-two outbound queue-pressure detail;
- tracked inbound rate accountants;
- aggregate one-second inbound frames/bytes;
- lifetime inbound frames/bytes;
- rejected inbound frames;
- bounded top-two live inbound-rate detail.

Inbound telemetry reuses the `TerrariaConnectionRateAccountant` already used by the connection policy. NPC and projectile replication telemetry reads existing thread-safe counters from `RuntimeNpcReplicationRegistry` and `RuntimeProjectileReplicationRegistry`. These paths do not add duplicate packet/entity hot-path counters merely for the UI.

### Logs

- bounded retention;
- severity filtering;
- dynamic source filtering from currently retained sources;
- follow/pause independent of telemetry refresh;
- no global console redirection;
- automatic plain-console resume after TUI exit/failure.

### World

- name/ID/GUID/readiness;
- format/worldgen identity;
- dimensions/tile count;
- persisted world-object/NPC counts;
- runtime-cache hit/result/read parallelism;
- startup/cache/bootstrap/readiness timings;
- live authoritative day/night state and raw world time;
- current day rate and moon phase;
- slime-rain active/cooldown/inactive state and timer.

The mutable `RuntimeWorldClock` remains authoritative-thread-owned. In TUI mode it publishes primitive committed state through `IRuntimeWorldClockObserver` into a single-writer sequence-protected projection. `LocalRuntimeWorldOperations` merges that projection into the immutable `RuntimeWorldSnapshot`; headless mode does not create the projection.

## Administrative actions

Administrative controls must be added only when there is an explicit runtime-owned operation with stable semantics.

Implemented:

- interest-management enable/disable through the bounded authoritative command ingress.

Rules for future actions:

- no direct mutable runtime references in UI callbacks;
- no bypass around command queue/budget/lifecycle rules;
- report queue rejection/failure honestly;
- separate requesting an operation from observing its eventual state/result where an action is asynchronous;
- keep authorization concerns outside the local-only UI until a real remote boundary exists.

## Next local UI work

Future slices should be driven by operational need rather than by filling screens for appearance's sake. Useful candidates are:

- save/checkpoint/cache-rebuild status once persistence publishes a bounded runtime-owned snapshot;
- richer player/NPC/projectile navigation and sorting only when the compact/grouped views become operationally limiting;
- additional packet/category telemetry only where the network subsystem already owns trustworthy counters;
- more administrative actions only after explicit runtime operations exist.

Do not add controls that merely expose implementation internals with no stable operational meaning.

## Future remote client, deferred

A separate administration client is intentionally not part of the current Phase 10 implementation.

If it becomes a real project:

- reuse or extract the existing view/layout code rather than reimplementing screens;
- keep UI layout as compiled code, not network data;
- transmit operations state/events/commands/results/version information, not window coordinates or control trees;
- implement remote operations with the same semantics already consumed by local views;
- extract additional assemblies only when a real second consumer creates that pressure.

## Future external/plugin UI windows

TerraRuntime must not contain knowledge of a specific external host/platform.

A future CoreCLR administration client may load optional client-side UI modules. Those modules can provide their own Terminal.Gui windows, and absent modules simply do not contribute windows.

That dynamic model must not leak into the NativeAOT server architecture:

- shipping TerraRuntime NativeAOT does not scan folders for managed UI DLLs;
- it does not use reflection-driven extension discovery;
- it does not download or dynamically load arbitrary managed UI code;
- UI extensions compiled into a NativeAOT host must be explicit/static or source-generated.

An external CoreCLR host may define its own dynamic extension policy outside TerraRuntime.

## Non-goals

- remote administration protocol before there is a real remote client;
- `TerraRuntime.Client` executable merely for symmetry;
- custom declarative UI language;
- transmitting layout/control trees over the network;
- dynamic UI DLL loading in the NativeAOT server;
- provider-specific windows or identifiers inside TerraRuntime;
- splitting the UI into several assemblies without concrete implementation pressure.

## Current acceptance state

The local foundation now satisfies the intended shape:

```text
Terminal.Gui view
      |
      v
small operations/read-model interface
      |
      v
TerraRuntime snapshot/authoritative-command boundary
```

The standalone server exercises that shape without direct mutable-state access, without blocking the authoritative loop, and without making TUI availability a server-readiness requirement.

A future remote-client milestone can prove reuse by extracting the same views and substituting a remote operations implementation. That work should happen when the client is actually being built, not before.
