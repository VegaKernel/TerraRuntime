# Terminal UI roadmap

This document refines Phase 10 of the main TerraRuntime roadmap.

The immediate goal is deliberately small: build a useful local Terminal UI for the standalone TerraRuntime server without coupling views to mutable runtime internals. A remote administration client remains a future possibility, not part of the current implementation milestone.

## Current rule

**Build the local TUI now; preserve a clean data boundary so a remote client can reuse it later.**

Do not add a remote management protocol, remote adapters, client executable or client-side plugin loader merely to prepare for a client that does not exist yet.

The only forward-looking requirement now is that terminal views consume stable operations snapshots/interfaces instead of directly reading mutable runtime state.

## Current implementation status

The local TUI currently has three exercised operational views in the standalone server:

- start it explicitly with `--tui`; plain-console/headless startup remains the default;
- Terminal.Gui v2 is hosted on its own UI thread and never runs on the authoritative game-loop thread;
- Dashboard consumes `IRuntimeDashboardOperations` and an immutable `RuntimeDashboardSnapshot` with lifecycle, world identity, target/observed TPS, tick wall/CPU timings, slowest phase, missed deadlines, command backlog/budget telemetry and connection admission counters;
- Players consumes `IPlayerOperations` and immutable `RuntimePlayersSnapshot` values populated only from already validated authoritative player events; the read model carries stable slot/generation/connection identity plus name, team, position and current health/mana without reading `ServerRuntimeState` from the UI thread;
- Network consumes `INetworkOperations` and `RuntimeNetworkSnapshot`, publishing active/registered/admitted/rejected connections plus appearance, equipment, lifecycle, movement and AOI-resync replication counters owned by runtime/network subsystems;
- observed TPS is sampled from authoritative tick progress over time in the operations layer, not reconstructed in the view from tick execution duration;
- closing the TUI stops only the UI; it does not stop the server;
- TUI initialization/refresh failure falls back to the running plain-console server path;
- `--tui-smoke` renders Dashboard, Players and Network with the Terminal.Gui ANSI test driver;
- normal CI plus Linux and Windows NativeAOT jobs exercise the same `--tui-smoke` path.

Dashboard, Players and Network are implemented. Logs and World remain pending. Aggregate per-connection queue/backpressure totals are also not published yet; the Network view currently exposes the counters that have authoritative subsystem ownership instead of reconstructing synthetic values in the UI.

Existing direct `Console.WriteLine` server messages are not redirected into the full-screen UI; do not solve that by globally replacing `Console.Out`. The next logging/UI step should introduce a bounded log read model/sink so plain console and TUI can consume the same structured events independently.

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

Initial implementation may live directly in the server project:

```text
src/TerraRuntime/
    Operations/
    TerminalUI/
        DashboardView.cs
        PlayersView.cs
        NetworkView.cs
        LogsView.cs
        WorldView.cs
```

Shared toolkit-independent read models/contracts may live in `TerraRuntime.Contracts` when they are genuine runtime contracts.

A separate `TerraRuntime.TerminalUI` project is optional and should be introduced only when there is an actual second consumer, such as a remote client, or when the existing project becomes materially harder to maintain without the split.

Do not create `TerraRuntime.Operations.Local`, `TerraRuntime.Operations.Remote`, `TerraRuntime.Ui.Contracts` and similar assemblies in advance unless concrete implementation pressure justifies them.

The dependency boundary matters more than the number of projects.

## UI-facing operations boundary

Views must not depend directly on `ServerRuntimeState`, mutable world/player/NPC collections, sockets or authoritative-thread-owned objects.

Introduce small operations/read-model interfaces as real screens require them, for example:

```text
IRuntimeDashboardOperations
IPlayerOperations
IWorldOperations
INetworkOperations
ILogOperations
```

Avoid speculative mega-interfaces.

Rules:

- snapshots are immutable and bounded;
- the TUI may poll/read snapshots from its own event loop/thread;
- administrative mutations are marshalled through the authoritative command boundary;
- local UI receives no privileged mutable-state access merely because it runs in-process;
- runtime telemetry is calculated by the subsystem that owns the truth, not reconstructed by the view.

This small boundary is useful even if a remote client is never built.

## Runtime telemetry ownership

TerraRuntime owns and publishes the measurements used by its UI, including:

- target tick rate;
- observed/current TPS or equivalent tick-rate telemetry;
- tick wall time;
- authoritative-thread CPU time;
- missed deadlines;
- phase timings;
- command backlog/budget telemetry;
- network/queue counters owned by runtime/network subsystems;
- later save/cache and GC telemetry as those systems mature.

The TUI only formats and displays these values.

In particular, do not calculate TPS in the view from `LastTickMilliseconds`; execution duration and observed simulation rate are different measurements.

## Terminal.Gui implementation

Use Terminal.Gui v2 as the first renderer. Prefer its official application/template/dashboard patterns and UICatalog examples instead of building a private terminal framework from scratch.

The local TUI should provide:

- application shell;
- menu/status bars;
- navigation;
- dashboard panels;
- keyboard bindings;
- bounded log viewer;
- screen/view lifecycle;
- graceful fallback to plain console/headless operation.

TUI initialization or refresh failure must not make the game server unavailable.

Do not globally replace `Console.Out` to create the TUI. Structured logging should eventually fan out independently to file/plain-console/TUI sinks.

## Initial screens

First screens remain operational and runtime-focused:

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
   - join/connection state;
   - position/basic state only through safe snapshots.

3. Network
   - active/admitted/rejected connections;
   - bounded queue/backpressure state;
   - packet/byte counters as they become available.

4. Logs
   - bounded retention;
   - severity/source filtering;
   - follow/pause independent of telemetry refresh.

5. World
   - dimensions/name/readiness;
   - save/cache state as those subsystems mature.

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

## Current acceptance direction

The current UI milestone is complete when:

```text
Terminal.Gui view
      |
      v
small operations/read-model interface
      |
      v
TerraRuntime runtime snapshot/command boundary
```

works in the standalone server without direct mutable-state access, without blocking the authoritative loop, and without making TUI availability a server-readiness requirement.

A future remote-client milestone can then prove reuse by extracting the same views and substituting a remote operations implementation. That work should happen when the client is actually being built, not before.
