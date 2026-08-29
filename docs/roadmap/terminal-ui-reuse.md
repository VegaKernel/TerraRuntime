# Terminal UI roadmap

This document refines Phase 10 of the main TerraRuntime roadmap.

TerraRuntime owns a local Terminal.Gui v2 operations UI. The UI is intentionally separated from mutable runtime internals so the same read-model and command semantics can be reused later without inventing a remote-management protocol before there is a real remote client.

## Current rule

**Keep the local TUI useful and runtime-owned; preserve a clean operations boundary for future reuse.**

Views consume immutable, bounded operations snapshots. Mutations cross explicit authoritative runtime command boundaries. Do not transmit UI layout, expose mutable stores to the UI thread, or split the UI into extra assemblies merely for architectural symmetry.

## Verified implementation checklist

> Checkbox policy: `[x]` means the item is verified on `main` by implementation plus tests/CI or an equivalent executable proof. Partial/foundation-only work remains `[ ]`.

- [x] Local Terminal.Gui v2 operations UI exists and is exercised.
- [x] TUI is enabled by default for normal server startup and can be disabled with `--no-tui`.
- [x] No-argument startup scans the runtime `Worlds` directory and lets the operator choose a `.wld` world.
- [x] Views consume immutable/bounded operations snapshots instead of mutable runtime stores.
- [x] Administrative interest-management mutation crosses the authoritative command boundary.
- [x] TUI runs independently from the authoritative loop and TUI failure/exit does not stop the server.
- [x] Dashboard, Players, NPCs, Projectiles, Items, Network, World and Logs views are implemented.
- [x] World view exposes bounded runtime-owned save/checkpoint status without exposing the persistence service or mutable tile shadow.
- [x] Trusted CoreCLR host modules may contribute independent local dashboard roots through host contracts.
- [x] CoreCLR and Linux/Windows NativeAOT CI exercise the TUI smoke path.
- [x] The smoke path exercises the real Details MenuBar hotkeys and framebuffer rendering for every built-in detail view.
- [ ] Remote administration protocol/adapter.
- [ ] Separate reusable remote administration client.
- [ ] Dynamic client-side/plugin UI windows for a future remote CoreCLR administration client.

## Current implementation

The standalone server has eight exercised runtime-owned operational views:

- **Dashboard** consumes `IRuntimeDashboardOperations` and `RuntimeDashboardSnapshot`. It shows lifecycle/world identity, target and observed TPS, tick wall/CPU timings, missed deadlines, command backlog/budget telemetry, process/GC telemetry, connection state and interest-management state.
- **Players** consumes `IPlayerOperations` and `RuntimePlayersSnapshot`. It exposes generation-safe connection/player identity, name/team, position, velocity, selected inventory slot, mount, health and mana.
- **NPCs** consumes `INpcOperations` and `RuntimeNpcsSnapshot`. It observes committed authoritative NPC state and exposes bounded identity, motion, target/AI and simulation/collision flags without exposing `RuntimeNpcStore`.
- **Projectiles** consumes `IProjectileOperations` and `RuntimeProjectilesSnapshot`. It groups committed authoritative projectiles by `(spawner, projectile type)`, sorts the largest groups first and exposes representative motion plus bounded damage/knockback maxima.
- **Items** consumes `IWorldItemOperations` and `RuntimeWorldItemsSnapshot`. It groups authoritative dropped items by item net ID and exposes drop count, aggregate stack, reservation/shimmer counts, maximum stack and representative position.
- **Network** consumes `INetworkOperations` and `RuntimeNetworkSnapshot`. It shows admission/registration state, player/NPC/projectile/item replication counters, inbound rates, bounded outbound queue/backpressure telemetry and rejected traffic.
- **World** consumes `IWorldOperations` and `RuntimeWorldSnapshot`. It exposes world identity, dimensions, persisted object/NPC counts, startup/cache timings, authoritative world-clock state, section-cache lookup/rebuild telemetry and bounded save/checkpoint lifecycle status.
- **Logs** consumes `ILogOperations` over a bounded `RuntimeLogBuffer`. It supports bounded runtime log observation independently of the plain-console sink.

Trusted host modules may also contribute **independent dashboard roots** through `ITerraRuntimeTerminalDashboardSource` / `ITerraRuntimeTerminalDashboardProvider`. TerraRuntime keeps its System Dashboard intact; host modules do not inject arbitrary controls into the built-in runtime-owned detail screens.

## Operational behavior

- Terminal UI is enabled by default. `--no-tui` explicitly disables it; `--tui` remains accepted as an explicit enable flag.
- Starting without `--world` uses the local world selector over the runtime `Worlds` directory instead of terminating the process merely because command-line arguments were omitted.
- Terminal.Gui v2 runs on a dedicated UI thread and never owns the authoritative game-loop thread.
- Closing the TUI closes only the UI and leaves the server running.
- TUI initialization/runtime failure is non-fatal and falls back to plain-console behavior.
- `Console.Out` and `Console.Error` are not globally replaced. Host-owned console output is suppressed only while the full-screen UI is active; the bounded log read model continues receiving events.
- Runtime/network telemetry comes from subsystem-owned counters rather than being reconstructed in the view.
- NPC/projectile/item UI projections are observers of authoritative state. They do not become primary replication owners merely because the operator can see them.
- World-clock, section-cache and persistence telemetry are exposed through bounded operations snapshots rather than direct mutable runtime references.
- Persistence status is published by the save subsystem: the TUI does not inspect `WorldSaveCoordinator`, mutable tile-shadow state or file-writer internals directly.
- `--tui-smoke` uses the Terminal.Gui ANSI test driver and validates actual framebuffer contents.
- The smoke enters `Details` through the real MenuBar and selects Overview, Players, NPCs, Projectiles, Items, Network, World and Logs through their mnemonics. This prevents duplicated/ambiguous hotkeys from silently surviving CI.
- The World smoke verifies rendered section-cache and world-save telemetry, not merely the World screen heading.
- CoreCLR CI and Linux/Windows NativeAOT jobs exercise the same TUI smoke path.

## Dependency shape

```text
Terminal.Gui views
      |
      v
small operations/read-model interfaces
      |
      +--> immutable bounded snapshots
      |
      `--> authoritative command ingress
                 |
                 v
             TerraRuntime
```

Possible future remote shape, only when a real client exists:

```text
same/extracted views
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

A separate `TerraRuntime.TerminalUI` project should be introduced only when there is an actual second consumer or when keeping the UI in the server project becomes materially harder to maintain.

Shared toolkit-independent read models/contracts may move to `TerraRuntime.Contracts` only when they become genuine cross-component contracts.

## UI-facing operations boundary

Views must not depend directly on `ServerRuntimeState`, mutable world/player/NPC/projectile/item collections, sockets, connection queues, persistence coordinators, mutable save shadows or authoritative-thread-owned objects.

Current screen-facing interfaces are deliberately small:

```text
IRuntimeDashboardOperations
IPlayerOperations
INpcOperations
IProjectileOperations
IWorldItemOperations
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
- existing thread-safe counters should be reused instead of adding duplicate hot-path accounting;
- bounded/grouped detail is preferred over unbounded per-connection/per-entity/per-event materialization.

## Runtime telemetry ownership

TerraRuntime currently owns and publishes UI-facing measurements for:

- target and observed TPS;
- tick wall time and authoritative-thread CPU time;
- missed deadlines and phase timings;
- authoritative command backlog/rejection/deferral/budget state;
- managed heap, allocation, working set, process CPU and GC counters;
- player replication counters;
- NPC replication and committed NPC state;
- projectile replication and grouped committed projectile state;
- world-item replication and grouped authoritative dropped-item state;
- aggregate and bounded per-connection inbound/outbound pressure/rate detail;
- authoritative player identity/vitals/movement state;
- authoritative world-clock state;
- world/cache startup state and timings;
- section-cache entries/bytes/dirty backlog, lookup hit/miss/stale/wait counters and rebuild queue/worker/publication telemetry;
- world-save tile-shadow synchronization, pending dirty sections, requested/active/pending writes and accepted/started/completed/coalesced/failed save counters;
- bounded runtime log events.

The TUI formats these values but does not invent them.

## Implemented screen notes

### Dashboard

The dashboard is deliberately compact. It provides lifecycle/world/TPS/process/network summary, a bounded world/player/chat/log overview and the result of the most recent administrative interest-management request.

### Players

Player data is fed from validated authoritative events. Optional movement fields are normalized to the same resulting state used by the runtime, so a later movement without velocity/mount does not leave stale values in the UI projection.

### NPCs

NPC data is projected only after authoritative store commits. Replication remains the primary consumer; TUI telemetry is a secondary observer and does not take ownership of simulation state.

### Projectiles

Projectiles are grouped rather than rendered one line per live projectile. The spawner byte remains protocol/runtime provenance; the view may resolve it to a currently playing player name when possible but does not invent stronger gameplay ownership semantics.

### Items

Dropped items are grouped by item net ID. The view intentionally shows operational pressure and lifecycle characteristics rather than creating hundreds of rows: drop count, aggregate/max stack, reservations, shimmer state and representative position.

### Network

Network detail reuses subsystem-owned accounting for inbound traffic, outbound queues and replication counters. It includes player appearance/equipment/movement, NPC, projectile and world-item replication telemetry.

### World

World detail includes identity, dimensions, persisted object counts, startup/cache timings and authoritative clock state. Section-cache status exposes entries/capacity/bytes/dirty backlog, lookup hit/miss/stale/waits and rebuild queued/active/published values when the cache telemetry source is available.

Persistence status is also projected through `RuntimeWorldPersistenceSnapshot`: tile-shadow synchronization/readiness, pending dirty tile sections, pending save request, active/pending background write, and accepted/started/completed/coalesced/failed save counts. `RuntimeWorldTileChestSaveService` publishes the thread-safe status; `LocalRuntimeWorldOperations` maps it into the UI-facing immutable snapshot, so the TUI never reaches into the coordinator or mutable persistence state.

### Logs

Logs remain bounded and independent of console ownership. Full-screen TUI operation must never require replacing global console streams.

## Administrative actions

Implemented:

- enable interest management through the bounded authoritative command ingress;
- disable interest management through the same boundary.

Rules for future actions:

- no direct mutable runtime references in UI callbacks;
- no bypass around command queue/budget/lifecycle rules;
- report queue rejection/failure honestly;
- separate requesting an operation from observing its eventual result where the operation is asynchronous;
- keep remote authorization concerns outside the local-only UI until a real remote boundary exists.

The current "Move player between worlds" entry remains explicitly unavailable because one server process still owns one runtime world and there is no authoritative multi-world transfer operation to call.

## Next local UI work

Future slices should be driven by operational need rather than by filling screens for appearance's sake.

Useful candidates now are:

- richer player/NPC/projectile/item navigation and sorting only when compact/grouped views become operationally limiting;
- additional packet/category telemetry only where the network subsystem already owns trustworthy counters;
- a manual save/checkpoint administrative action only after it is exposed through an explicit operations/command boundary rather than by handing the UI the persistence service;
- more administrative actions only after explicit runtime operations exist.

Section-cache rebuild status and save/checkpoint status are no longer future work: both are already exposed by the World operations snapshot/view and exercised by the framebuffer smoke.

## Future remote client, deferred

A separate administration client is intentionally not part of the current Phase 10 implementation.

If it becomes a real project:

- reuse or extract the existing view/layout code rather than reimplementing screens;
- keep UI layout as compiled code, not network data;
- transmit operations state/events/commands/results/version information, not window coordinates or control trees;
- implement remote operations with the same semantics already consumed by local views;
- extract additional assemblies only when a real second consumer creates that pressure.

## External dashboards and future client plugins

Current local trusted-host dashboard registration is not the same thing as a future remote administration client's dynamic plugin system.

The local extensible CoreCLR host may register trusted dashboard providers explicitly through host contracts. Shipping TerraRuntime NativeAOT does not scan arbitrary folders for managed UI DLLs, use reflection-driven extension discovery, download code, or dynamically load arbitrary assemblies.

A future CoreCLR administration client may define its own optional client-side UI module policy outside TerraRuntime when that client actually exists.

## Non-goals

- remote administration protocol before there is a real remote client;
- a `TerraRuntime.Client` executable merely for symmetry;
- custom declarative UI language;
- transmitting layout/control trees over the network;
- arbitrary dynamic UI DLL loading in the NativeAOT server;
- provider-specific runtime internals exposed through built-in views;
- splitting the UI into several assemblies without concrete implementation pressure.

## Current acceptance state

The local Phase 10 foundation now satisfies the intended shape:

```text
Terminal.Gui view
      |
      v
small operations/read-model interface
      |
      v
TerraRuntime snapshot/authoritative-command boundary
```

The standalone server exercises that shape without direct mutable-state access, without blocking the authoritative loop and without making TUI availability a server-readiness requirement. A future remote-client milestone should prove reuse by substituting a remote operations implementation only when that client is actually being built.