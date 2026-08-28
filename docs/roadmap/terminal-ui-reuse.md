# Terminal UI roadmap

This document refines Phase 10 of the main TerraRuntime roadmap.

The standalone TerraRuntime server already has a local Terminal.Gui v2 operations UI. The UI is intentionally separated from mutable runtime internals so its read-model and command semantics can be reused later without introducing a speculative remote-management framework now.

## Current rule

**Keep the local TUI useful and runtime-owned; preserve a clean operations boundary for possible future reuse.**

Do not add a remote management protocol, remote adapters, a separate client executable, or client-side plugin loading merely to prepare for a client that does not exist yet.

Views consume immutable operations snapshots. Mutations cross explicit runtime command boundaries.

## Current implementation

The standalone server has five exercised operational views:

- **Dashboard** consumes `IRuntimeDashboardOperations` and `RuntimeDashboardSnapshot`. It shows lifecycle/world identity, target and observed TPS, tick wall/CPU timings, slowest phase, missed deadlines, authoritative command backlog/budget telemetry, managed heap/lifetime allocation, GC collection counts, connection admission counters, and interest-management state.
- **Players** consumes `IPlayerOperations` and `RuntimePlayersSnapshot`. The generation-safe read model is populated only from already validated authoritative player events and includes slot/generation/connection identity, name/team, position, velocity, selected inventory slot, mount type, health, and mana.
- **Network** consumes `INetworkOperations` and `RuntimeNetworkSnapshot`. It shows admission/registration state, replication counters, bounded outbound-queue/backpressure telemetry, live inbound one-second frame/byte rates, lifetime inbound counters, rejected inbound frames, and bounded per-connection pressure/rate detail.
- **World** consumes `IWorldOperations` and `RuntimeWorldSnapshot`. It exposes validated world identity, format/worldgen version, dimensions, persisted object/NPC counts, runtime-cache result/read parallelism, and startup/cache/bootstrap/readiness timings without giving the UI mutable world access.
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
- `--tui-smoke` renders Dashboard, Players, Network, World, and Logs and exercises authoritative admin actions through the Terminal.Gui ANSI test driver.
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

Views must not depend directly on `ServerRuntimeState`, mutable world/player/NPC collections, sockets, connection queues, or authoritative-thread-owned objects.

Current screen-facing interfaces are deliberately small:

```text
IRuntimeDashboardOperations
IPlayerOperations
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
- bounded detail is preferred over unbounded per-connection/per-event materialization.

## Runtime telemetry ownership

TerraRuntime currently owns and publishes UI-facing measurements for:

- target and observed TPS;
- tick wall time and authoritative-thread CPU time;
- missed deadlines and phase timings;
- command backlog, rejection, deferral, age, and budget-exhaustion telemetry;
- managed heap, lifetime allocation, and Gen0/Gen1/Gen2 collection counts;
- network replication counters;
- aggregate and bounded per-connection outbound queue/backpressure state;
- live and lifetime inbound frame/byte accounting plus rejected inbound frames;
- authoritative player identity/vitals/movement state used by the Players view;
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
- Gen0/Gen1/Gen2 collection counts;
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

### Network

- active/admitted/rejected/registered connections;
- relay/baseline/AOI-resync counters;
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

Inbound telemetry reuses the `TerrariaConnectionRateAccountant` already used by the connection policy. It does not add a second frame/byte counter to the receive hot path.

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
- startup/cache/bootstrap/readiness timings.

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
- richer player navigation/detail only when the compact list becomes operationally limiting;
- additional packet/category telemetry only where the network subsystem already owns trustworthy counters;
- more administrative actions only after explicit runtime operations exist;
- table/list navigation and sorting when current bounded detail becomes too dense.

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
