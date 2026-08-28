# Reusable Terminal UI roadmap

This document refines Phase 10 of the main TerraRuntime roadmap. The goal is to implement the Terminal UI once and reuse the same view/layout code both inside the standalone server process and inside a future remote administration client.

## Core rule

**UI layout is code, not network data.**

Do not invent a server-driven JSON/XAML/Protobuf schema that describes windows, controls, coordinates or layout. Do not transmit `Terminal.Gui` view trees over the network. The server and remote client should reuse the same compiled UI implementation.

The network/control boundary carries only operational state and behavior:

- immutable snapshots/read models;
- bounded event streams;
- commands/actions;
- command results and errors;
- protocol/API version information required for compatibility.

## Target architecture

```text
                    shared UI code
             TerraRuntime.TerminalUI
              /                  \
             /                    \
            v                      v
 standalone server           remote client
      TUI                         TUI
       |                           |
       v                           v
 local operations           remote operations
     adapter                    adapter
       |                           |
       v                           v
 live TerraRuntime        operations protocol
                                  |
                                  v
                           TerraRuntime server
```

The same `DashboardView`, `PlayersView`, `WorldView`, `NetworkView`, `LogsView` and later views are instantiated in both processes. A view must not know whether its data source is local or remote.

## UI-facing contracts

Terminal views depend only on stable toolkit-independent operations interfaces/read models, for example:

```text
IRuntimeDashboardOperations
IPlayerOperations
IWorldOperations
INetworkOperations
ILogOperations
```

The exact contracts should be introduced only as real screens require them; avoid speculative mega-interfaces.

Rules:

- no UI view receives `ServerRuntimeState`, mutable world state, sockets or game-thread-owned collections;
- no UI view directly depends on the remote transport implementation;
- local and remote adapters implement the same UI-facing contracts;
- administrative mutations still cross the authoritative command boundary;
- snapshots remain immutable/bounded and safe to consume from the UI event loop.

## Local adapter

The standalone server composes the shared UI with local operations implementations:

```text
PlayersView
    |
    v
IPlayerOperations
    |
    v
LocalPlayerOperations
    |
    v
authoritative runtime command/snapshot boundaries
```

The local adapter may avoid serialization, but it must obey the same public semantics as the future remote adapter. Do not give local UI privileged direct access merely because it runs in the same process.

## Remote adapter

A future administration client composes the exact same UI classes with remote operations implementations:

```text
PlayersView
    |
    v
IPlayerOperations
    |
    v
RemotePlayerOperations
    |
    v
network/control protocol
    |
    v
TerraRuntime server
```

The remote protocol is therefore an operations protocol, not a UI-description protocol.

## External UI extensions

TerraRuntime must provide only generic extension points. It must not contain provider-specific window names, identifiers, capabilities or behavior for an external platform layer.

An external host may ship its own reusable UI library and compose it alongside `TerraRuntime.TerminalUI` in both its local server host and its matching remote client.

Conceptually:

```text
TerraRuntime.TerminalUI
    +
ExternalPlatform.TerminalUI
    |
    +--> local host
    `--> remote client
```

The external UI library owns the layout and behavior of its own windows. TerraRuntime only supplies generic registration/composition contracts.

Because production TerraRuntime is NativeAOT-first:

- extension registration must be explicit/static or source-generated;
- do not discover UI extensions by runtime assembly scanning;
- do not require arbitrary managed DLL loading in the shipping TerraRuntime process;
- a composition host decides at build/startup which UI extension assemblies are present.

If a remote client does not contain the matching UI extension, its base TerraRuntime screens continue to work; provider-specific screens are simply unavailable in that client build. Do not solve this by downloading executable UI code from the server.

## Terminal.Gui implementation

Use Terminal.Gui v2 as the first renderer. Prefer its established application/dashboard patterns instead of hand-building a private terminal framework.

The reusable project should own:

- application shell;
- menu/status bars;
- navigation;
- reusable dashboard panels;
- keyboard bindings;
- bounded log viewer;
- screen/view lifecycle;
- generic extension registration slots.

It must not own authoritative runtime state.

Suggested project direction:

```text
src/TerraRuntime.Ui.Contracts/
    operations/read-model contracts needed by UI
    generic UI extension registration contracts

src/TerraRuntime.TerminalUI/
    Terminal.Gui v2 shell and reusable views

src/TerraRuntime.Operations.Local/
    local adapters over runtime snapshots/commands

future client-side projects:
    TerraRuntime.Operations.Remote
    TerraRuntime.Client
```

Project names may be collapsed if the implementation remains simpler with fewer assemblies; the dependency direction is the requirement, not the exact folder count.

## Runtime telemetry ownership

Metrics shown by the UI are calculated by the subsystem that owns the truth, not reconstructed by the view.

In particular, TerraRuntime owns:

- target tick rate;
- observed/current TPS or equivalent tick-rate telemetry;
- tick wall time;
- authoritative-thread CPU time;
- missed deadlines;
- phase timings;
- command backlog/budget telemetry;
- network/queue counters owned by runtime/network subsystems.

The UI only formats and displays these values. A remote client receives the same measurements through the operations protocol rather than calculating a second independent TPS value.

## Initial screens

First reusable screens should remain operational rather than becoming a full administration suite prematurely:

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
   - position/basic state only when exposed through safe snapshots.

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

## Non-goals

- transmitting window coordinates/layout/control trees over the network;
- creating a custom declarative UI language;
- making remote transport types visible to views;
- giving in-process UI direct access to mutable runtime state;
- adding knowledge of any specific external host/platform to TerraRuntime;
- runtime downloading/loading of arbitrary UI assemblies in the NativeAOT server.

## Acceptance direction

The architecture is proven when one real screen can run unchanged in both modes:

```text
same view class + local adapter  -> standalone server TUI
same view class + remote adapter -> administration client TUI
```

The rendered screen and available actions should have the same semantics in both modes, modulo network latency and capabilities supported by the connected server version.
