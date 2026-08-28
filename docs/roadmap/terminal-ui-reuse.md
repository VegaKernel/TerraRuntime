# Terminal UI roadmap

This document refines Phase 10 of the main TerraRuntime roadmap.

The local Terminal UI foundation is now implemented for the standalone TerraRuntime server. The design deliberately keeps views separated from mutable runtime internals so the same operations semantics can be reused by a future administration client without turning the current server into a speculative remote-management framework.

## Current rule

**Keep the local TUI useful and runtime-owned; preserve a clean data boundary so a remote client can reuse it later.**

Do not add a remote management protocol, remote adapters, client executable or client-side plugin loader merely to prepare for a client that does not exist yet.

Terminal views consume stable operations snapshots/interfaces instead of directly reading mutable runtime state.

## Current implementation status

The standalone server now has five exercised operational views:

- **Dashboard** consumes `IRuntimeDashboardOperations` and immutable `RuntimeDashboardSnapshot` values with lifecycle, world identity, target/observed TPS, tick wall/CPU timings, slowest phase, missed deadlines, command backlog/budget telemetry and connection admission counters.
- **Players** consumes `IPlayerOperations` and immutable `RuntimePlayersSnapshot` values populated only from already validated authoritative player events. The read model carries stable slot/generation/connection identity plus name, team, position and current health/mana without reading `ServerRuntimeState` from the UI thread.
- **Network** consumes `INetworkOperations` and `RuntimeNetworkSnapshot`. It publishes active/registered/admitted/rejected connections, appearance/equipment/lifecycle/movement/AOI-resync counters and aggregate bounded outbound-queue telemetry: tracked queues, queued frames, queued bytes, rejected frames and currently slow clients. Queue pressure is sampled from the queue-owned thread-safe counters instead of adding a second accounting path to enqueue/dequeue hot paths.
- **World** consumes `IWorldOperations` and an immutable `RuntimeWorldSnapshot` created from already validated `WorldFileData` plus startup/cache measurements. It exposes world identity, format/worldgen version, dimensions/tile count, persisted object/NPC counts, runtime-cache hit/result/schema state and file/cache/bootstrap/readiness timings without giving the UI mutable world access.
- **Logs** consumes `ILogOperations` over a bounded `RuntimeLogBuffer`. The view supports severity filtering and pause/resume while selected server/runtime/network events are mirrored into the bounded read model independently of plain-console output.

Operational behavior:

- start the UI explicitly with `--tui`; plain-console/headless startup remains the default;
- Terminal.Gui v2 is hosted on its own UI thread and never runs on the authoritative game-loop thread;
- observed TPS is sampled from authoritative tick progress over time in the operations layer, not reconstructed in the view from tick execution duration;
- closing the TUI stops only the UI; it does not stop the server;
- TUI initialization/runtime failure is non-fatal and leaves the server available through the plain-console path;
- direct `Console.WriteLine` output is not globally intercepted or replaced;
- `--tui-smoke` renders Dashboard, Players, Network, World and Logs through the Terminal.Gui ANSI test driver;
- normal CoreCLR CI plus Linux and Windows NativeAOT jobs exercise the same `--tui-smoke` path;
- changes touching the standalone host are also exercised by the official-world workflow, including world verification, host startup, live join/movement relay and warm runtime-cache startup.

The foundational local-TUI slice of Phase 10 is complete. Remaining UI work is incremental: richer drill-downs, additional runtime-owned telemetry and carefully bounded administrative command surfaces as concrete operations APIs become necessary.

Current shape:

```text
Terminal UI
    |
    v
operations/read-model boundary
    |
    v
local TerraRuntime
```

Possible future shape, only when a real client is started:

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

## Keep the implementation simple now

Do not create a separate project graph merely for hypothetical reuse.

The current implementation may continue to live directly in the server project:

```text
src/TerraRuntime/
    Operations/
    TerminalUI/
```

Shared toolkit-independent read models/contracts may move to `TerraRuntime.Contracts` when they become genuine cross-component runtime contracts.

A separate `TerraRuntime.TerminalUI` project is optional and should be introduced only when there is an actual second consumer, such as a remote client, or when the existing project becomes materially harder to maintain without the split.

Do not create `TerraRuntime.Operations.Local`, `TerraRuntime.Operations.Remote`, `TerraRuntime.Ui.Contracts` and similar assemblies in advance unless concrete implementation pressure justifies them.

The dependency boundary matters more than the number of projects.

## UI-facing operations boundary

Views must not depend directly on `ServerRuntimeState`, mutable world/player/NPC collections, sockets or authoritative-thread-owned objects.

Current screen-facing interfaces are deliberately small:

```text
IRuntimeDashboardOperations
IPlayerOperations
INetworkOperations
IWorldOperations
ILogOperations
```

Avoid speculative mega-interfaces.

Rules:

- snapshots are immutable and bounded;
- the TUI may poll/read snapshots from its own event loop/thread;
- administrative mutations are marshalled through the authoritative command boundary;
- local UI receives no privileged mutable-state access merely because it runs in-process;
- runtime telemetry is calculated or exposed by the subsystem that owns the truth, not reconstructed by the view;
- expensive observability must not add unnecessary work to gameplay/network hot paths.

This small boundary is useful even if a remote client is never built.

## Runtime telemetry ownership

TerraRuntime owns and publishes the measurements used by its UI, including:

- target tick rate;
- observed/current TPS;
- tick wall time;
- authoritative-thread CPU time;
- missed deadlines;
- phase timings;
- command backlog/budget telemetry;
- network replication counters;
- bounded outbound-queue/backpressure state;
- world/cache startup state and timings;
- bounded runtime log events;
- later save, GC and additional cache telemetry as those systems mature.

The TUI only formats and displays these values.

In particular, do not calculate TPS in the view from `LastTickMilliseconds`; execution duration and observed simulation rate are different measurements.

## Terminal.Gui implementation

Use Terminal.Gui v2 as the renderer. Prefer its official application/template/dashboard patterns and UICatalog examples instead of building a private terminal framework from scratch.

The local TUI currently provides:

- application shell;
- menu/status bars;
- Dashboard / Players / Network / World / Logs navigation;
- keyboard quit behavior that closes only the UI;
- bounded log viewer with severity filtering and pause/resume;
- periodic snapshot refresh on the UI thread;
- graceful fallback to plain console/headless operation.

TUI initialization or refresh failure must not make the game server unavailable.

Do not globally replace `Console.Out` to create the TUI. As structured logging matures, file/plain-console/TUI sinks should receive the same structured events independently.

## Implemented screens

1. Runtime/dashboard
   - lifecycle/readiness;
   - world identity;
   - TPS/tick budget;
   - last/worst tick wall and CPU time;
   - slowest phase;
   - missed deadlines;
   - command backlog/budget state.

2. Players
   - stable player/session identity;
   - generation-safe connection identity;
   - name/team;
   - position;
   - health/mana through safe authoritative-event snapshots.

3. Network
   - active/admitted/rejected/registered connections;
   - relay/baseline/AOI-resync counters;
   - tracked bounded outbound queues;
   - queued frames/bytes;
   - rejected outbound frames;
   - slow-client count.

4. Logs
   - bounded retention;
   - severity filtering;
   - follow/pause independent of telemetry refresh;
   - no global console redirection.

5. World
   - name/ID/GUID/readiness;
   - format/worldgen identity;
   - dimensions/tile count;
   - persisted world-object/NPC counts;
   - runtime-cache hit/result/schema/read parallelism;
   - startup/cache/bootstrap/readiness timings.

## Next local UI work

Future local UI slices should be driven by operational need rather than by filling screens for appearance's sake. Likely additions include:

- deeper per-player detail when authoritative snapshots expose useful state;
- per-connection drill-down and packet/rate telemetry where network subsystems already own trustworthy counters;
- GC/allocation and save-pipeline telemetry once those systems publish bounded snapshots;
- runtime-cache rebuild/save status as the persistence pipeline matures;
- administrative actions only through explicit command/operations interfaces, never by mutating runtime objects from UI callbacks;
- better table/list navigation when the compact current views become too dense.

Do not add controls that merely expose implementation internals with no stable operational meaning.

## Future remote client, deferred

A separate administration client is intentionally **not** part of the current Phase 10 implementation.

If/when it becomes a real project, preserve this model:

- reuse or extract the existing view/layout code rather than reimplementing screens;
- keep UI layout as compiled code, not network data;
- do not transmit window coordinates, control trees or a home-grown JSON/XAML UI description;
- transport only operations state/events/commands/results/version information;
- implement remote operations with the same semantics already consumed by local views.

At that point it may become worthwhile to extract reusable UI code into a separate project/library.

## Future external/plugin UI windows

TerraRuntime must not contain knowledge of any specific external host/platform.

If a future CoreCLR administration client supports optional UI modules, those modules may provide additional windows as ordinary client-side libraries. If the matching module is absent, its windows simply do not appear.

Conceptually:

```text
TerraRuntime.Client
    |
    +-- built-in runtime windows
    |
    `-- optional UI modules
          +-- ExternalPlatform.TerminalUI.dll
          `-- OtherExtension.TerminalUI.dll
```

Those libraries contain their own Terminal.Gui layout code. Window/control layout is not downloaded from the server.

This dynamic client-side model must not leak into the NativeAOT server architecture:

- shipping TerraRuntime NativeAOT does not scan folders for managed UI DLLs;
- it does not use reflection-driven extension discovery;
- it does not download or dynamically load arbitrary managed UI code;
- any UI extensions compiled into a NativeAOT host are registered explicitly/staticly or source-generated.

An external CoreCLR host may define its own dynamic extension policy outside TerraRuntime.

## Non-goals for the current implementation

- remote administration protocol;
- `TerraRuntime.Client` executable;
- remote operations adapters;
- custom declarative UI language;
- transmitting layout/control trees over the network;
- dynamic UI DLL loading in the NativeAOT server;
- provider-specific windows or identifiers inside TerraRuntime;
- splitting the UI into several assemblies merely for architectural symmetry.

## Current acceptance state

The local foundation now satisfies the intended dependency shape:

```text
Terminal.Gui view
      |
      v
small operations/read-model interface
      |
      v
TerraRuntime runtime snapshot/command boundary
```

The standalone server exercises that shape without direct mutable-state access, without blocking the authoritative loop, and without making TUI availability a server-readiness requirement.

A future remote-client milestone can prove reuse by extracting the same views and substituting a remote operations implementation. That work should happen when the client is actually being built, not before.
