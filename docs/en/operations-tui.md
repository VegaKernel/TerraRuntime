# Operations, startup and Terminal UI

[Русский](../ru/operations-tui.md) · [Documentation](README.md) · [Architecture](architecture.md) · [Host interfaces](host-interfaces.md)

## 1. Purpose

The operations layer exposes bounded read models and safe control surfaces without letting UI code traverse or mutate authoritative runtime collections directly.

```mermaid
flowchart LR
    Runtime["Authoritative runtime"] --> Snapshots["Immutable / bounded operations snapshots"]
    Snapshots --> TUI["Terminal UI"]
    Snapshots --> Console["Plain console"]
    Snapshots --> Host["Trusted host integration"]
    Snapshots --> Future["Future API adapters"]

    TUI --> Control["Validated operation / command"]
    Console --> Control
    Host --> Control
    Control --> Runtime
```

Terminal.Gui v2 is the current local UI implementation, but the boundary is toolkit-independent.

## 2. Startup without arguments

Normal startup without `--world` creates required runtime directories and scans canonical `Worlds/` through the local selector. The selected `.wld` becomes the effective `--world` value. Cancelling selection exits cleanly.

The interactive list intentionally renders only world display names. Absolute filesystem paths are not repeated beside every world; an explicit path can still be supplied through `P` or `--world`.

The standalone directory layout remains literal filesystem structure:

```text
Worlds/
config/
data/
logs/
```

## 3. Main server options

```text
--world <path.wld>
--port <1..65535>
--max-players <1..255>
--interest-management
--tui
--no-tui
```

Defaults are `port = 7777`, `max players = 8`, TUI enabled, interest management disabled. These are configuration values/identifiers rather than dimensional measurements, so they remain code literals.

`--tui` is accepted explicitly although the TUI is already default-on. `--no-tui` disables it.

Special startup paths include `--help`, `--list-world-generators`, CI smoke modes and `--save-wld`.

## 4. TUI lifecycle

```mermaid
stateDiagram-v2
    [*] --> Starting
    Starting --> Dashboard: Terminal.Gui starts successfully
    Starting --> PlainConsole: initialization failure
    Dashboard --> PlainConsole: dashboard exits / fails
    PlainConsole --> Dashboard: tui / ui / dashboard command
    Dashboard --> Stopping: runtime shutdown
    PlainConsole --> Stopping: runtime shutdown
    Stopping --> [*]
```

The UI runs on its own background thread `TerraRuntime Terminal UI`, not on the authoritative game-loop thread. `TerminalUiHost` owns linked cancellation and waits only a bounded interval during disposal.

On Windows, TerraRuntime deliberately selects Terminal.Gui's cross-platform `dotnet` driver instead of forcing the native `windows` driver. This is a compatibility policy for Windows 10/conhost-class rendering failures where Terminal.Gui 2.4.x can leave the content area blank/black while menu chrome remains visible. Linux and other platforms keep Terminal.Gui's normal platform selection.

The production TUI also installs an explicit high-contrast TerraRuntime scheme after Terminal.Gui initialization. Base, Menu, Dialog, Accent and Error roles use opaque near-black backgrounds with green foreground/accent colors instead of inheriting `Color.None` terminal defaults.

## 5. Refresh and input-responsiveness model

Runtime data still targets an approximately

$$
T_{\mathrm{snapshot}}\approx100\,\mathrm{ms}
$$

snapshot cadence, but snapshot capture no longer runs on the Terminal.Gui thread. `TerminalUiOperationsCache` captures detached operations state on a worker task and publishes the complete cache through one atomic reference swap. The UI thread only reads the already-published state and formats it into views.

A lightweight Terminal.Gui timer checks for a newly published cache version approximately every

$$
T_{\mathrm{ui\ pump}}\approx16\,\mathrm{ms}.
$$

This timer does not capture gameplay/network/world state. Therefore a slow operations snapshot cannot pause keyboard navigation, mouse focus, menu movement, or panel interaction. If a background capture is still running, the UI keeps rendering the previous complete snapshot instead of waiting for it.

```mermaid
sequenceDiagram
    participant R as Runtime operations
    participant B as Snapshot worker
    participant C as Atomic TUI cache
    participant U as Terminal.Gui thread

    B->>R: Capture detached snapshots
    R-->>B: bounded read models
    B->>C: publish complete cache version
    U->>C: read latest published version
    C-->>U: immediate cached values
    U->>U: render / process input
```

The overview always refreshes the dashboard/player/network/world/log state it needs. Detail-only NPC, projectile, dropped-item and full-debug-log snapshots are demand-driven: they are refreshed while their detail screen is actually being read, avoiding a permanent allocation/copy cost merely to make the UI responsive.

World-scoped detail inspection is a separate cache responsibility. `LocalRuntimeWorldInspectionOperations` resolves live worlds by stable `WorldRuntimeId`, while `TerminalUiWorldInspectionCache` remembers the operator-selected world and captures only the demanded Players/NPCs/Projectiles/Items/World snapshot for that world. The Terminal.Gui thread receives detached read models only; it never retains `WorldRuntime` references. Sandbox operations telemetry is enabled only when TUI is enabled.

The Worlds / Players tile exposes a `+ Sandbox` action using the same Base scheme as the tree rather than a separate highlighted button background. The creation window exposes isolation as a dropdown and no longer renders a separate `Selected:` status line. Generator identity, game mode, world evil and size preset are dropdown selections. Generator choices are captured from the actual runtime/host generator registry. Size presets include Primary, Small `4200x1200`, Medium `6400x1800`, Large `8400x2400` and Custom. A cryptographically generated unsigned seed is populated when the window opens, and `Random` replaces it without requiring the operator to type `random`.

Create admission is synchronous and non-blocking: the form closes only after `SandboxHost.TryCreate` accepts the typed request into its bounded materialization queue. Immediate rejection, including selecting Level 2 dedicated-process isolation while only Level 1 is implemented, remains visible inside the form. Actual generation/materialization continues on the existing bounded worker and reports terminal success/failure through the sandbox job feed.

Administrative operations are not cached writes. Interest-management changes and world-save requests still delegate directly to their authoritative bounded ingress.

## 6. Tiled System Dashboard

The default System Dashboard is a tiled operational workspace inspired by the Vega operations view while remaining runtime-owned. The left side is a large **Console** tile. The right column contains **Server**, **TPS / CPU**, **Memory / GC**, and **Chat** tiles.

```mermaid
flowchart LR
    subgraph Workspace["System Dashboard"]
        Console["Console\nrecent runtime events"]
        subgraph Right["Right column"]
            Server["Server"]
            Perf["TPS / CPU"]
            Memory["Memory / GC"]
            Chat["Chat"]
        end
    end
```

The Console tile includes current tick/process/command pressure followed by recent runtime events. The performance and memory tiles maintain short in-memory histories only for rendering local sparklines; those histories are UI-owned and are never authoritative telemetry.

Focusable tiles now have an explicit selection state. Keyboard focus or a mouse press applies the Accent scheme with a distinct dark-green selected-panel background and prefixes the focused tile title with `▶`. This textual marker deliberately remains useful even when a terminal reduces or remaps colors. Focus changes remove the marker and restore the Base scheme.

Double-clicking a tile first focuses it and then toggles it between the tiled layout and full-workspace view. This is a presentation-only operation. Existing Details screens for Players, NPCs, Projectiles, Items, Network, World and Logs remain available and keep their existing bounded read-model contracts. External trusted-host dashboards remain separate roots.

The System Dashboard shows lifecycle/world state, player and connection counts, interest-management state, current/target TPS, tick wall/CPU timing, slowest phase, missed deadlines, process CPU, managed heap, working set, allocation/GC state, command pressure, recent log events and public chat.

The dashboard also exposes a visible **Settings** button and a top-level **Settings → Runtime settings** menu entry. The runtime settings window is deliberately limited to operator controls with concrete runtime value: current bind address/IP and TCP port, listener lifecycle/generation/draining/rebind counters, active connections versus the player limit, target TPS, and the interest-management toggle. Applying a bind/port change calls the operations boundary and replaces the listener generation; the TUI never receives a `Socket` and never owns connection lifetime. Existing accepted clients remain connected while the previous listener moves `Active → Draining → Closed`.

The World detail screen also surfaces section-cache pipeline health from `RuntimeWorldSnapshot`: in-flight/submitted/rejected rebuilds, stale results, encode failures, publish rejections and accumulated encode time. Its on-demand row shows requests, unique/deduplicated requests, pending work versus bounded capacity, rejected requests and completed/timed-out waits.

## 7. Save telemetry and manual checkpoint

World-save status is exposed through detached operations state: persistence acceptance, tile-shadow readiness, remaining bootstrap/dirty sections, requested state, active/pending writes and accepted/started/completed/coalesced/failed counters.

**Actions → Save world checkpoint** calls `IWorldOperations.TryRequestSave()` and crosses persistence ingress rather than receiving the save service or mutable tile shadow.

```mermaid
sequenceDiagram
    participant U as Operator / TUI
    participant O as IWorldOperations
    participant G as Authoritative owner
    participant S as Persistence pipeline

    U->>O: TryRequestSave()
    O->>G: bounded save request
    alt accepted
        G->>S: capture when authoritative snapshot is ready
        O-->>U: accepted
    else rejected
        O-->>U: explicit administrative rejection
    end
```

The ANSI TUI smoke exercises the real menu path and verifies pending-save state; unit tests cover accepted and rejected requests.

## 8. Network telemetry

`INetworkOperations` exposes bounded network state without transferring connection ownership to the UI. It includes active/registered connections, admission totals/rejections, inbound one-second and lifetime totals, per-connection inbound details, outbound current/capacity/high-water/rejection state, slow clients, player lifecycle and replication counters, unsupported replication commits, typed terminal stop categories and normalized frame-rejection categories.

TUI projections consume subsystem-owned counters; they do not parse log text or add packet-hot-path counters of their own.

## 9. Logs

The TUI consumes bounded log state and is not a logging backend. UI failure must not lose authoritative state, log rendering must not block the game loop, and retained history remains bounded.

See [Observability and logging](observability-logging.md) and the logging roadmap.

## 10. Plain console fallback

Fallback commands remain literal commands:

```text
tui | ui | dashboard   reopen the dashboard
clear                  clear the console when supported
help                   show fallback-console commands
```

When TUI is disabled with `--no-tui`, fails to initialize, or is closed by the operator, public chat is projected to stdout as `[chat] #<slot>: <text>`. The projection uses a bounded queue and a background writer; authoritative chat relay never waits for terminal I/O.

Structured console events have an independent sink threshold. The default is `Error`, which means error/critical-class events plus chat are visible in a quiet plain console while detailed `Debug`/`Information` records continue to remain available to other enabled sinks. Set `TERRARUNTIME_LOG_CONSOLE_LEVEL` to `Trace`, `Debug`, `Information`, `Warning`, `Error`, or `Critical` to change only the console threshold. `TERRARUNTIME_LOG_LEVEL` remains the global pipeline acceptance threshold and therefore cannot be bypassed by a more verbose console setting.

`TERRARUNTIME_LOG_CONSOLE=off` disables structured stdout/stderr delivery; public-chat projection remains independent so a headless server does not become completely silent about player conversation.

Unknown commands are reported rather than interpreted as runtime mutations. Closed/redirected stdin waits rather than busy-looping.

## 11. Trusted host dashboards

CoreCLR trusted hosts can register complete dashboards via `ITerraRuntimeTerminalDashboardRegistry`.

A provider supplies stable `Id`, display `Title`, `CreateDashboard()` and `Refresh(View rootView)` on the Terminal.Gui UI thread. It contributes its own root view and does not inject arbitrary controls into the built-in TerraRuntime dashboard.

Registration is metadata/factory-oriented and grants no mutable runtime state.

## 12. UI-thread ownership

Terminal.Gui views remain UI-thread objects. A dashboard provider may update its view from `Refresh`, while gameplay/runtime work is requested through safe contracts. A `View` must not become a synchronization primitive for authoritative state.

Built-in TerraRuntime snapshot acquisition is explicitly excluded from the UI thread. Only formatting, view mutation and trusted host dashboard `Refresh(View)` callbacks run there. A trusted host that performs blocking work from its own `Refresh(View)` can still make its own dashboard unresponsive and must move that work behind its own detached snapshot/cache boundary.

## 13. Administrative mutation rule

```mermaid
flowchart LR
    UI["TUI / console / trusted host"] --> Validate["Safe operations boundary"]
    Validate --> Command["Validated authoritative command / ingress"]
    Command --> Owner["Runtime owner"]
    Owner --> Result["Explicit result / snapshot"]
    Result --> UI
```

Implemented examples include interest-management enable/disable through authoritative command ingress and manual checkpoint requests through `IWorldOperations.TryRequestSave()`.

Running in the same process does not grant the TUI a direct-mutation shortcut.

## 14. Headless and extensible profiles

Disabling TUI does not disable the server. NativeAOT and CoreCLR profiles remain functional without a successful graphical terminal session.

CoreCLR may load trusted host modules such as Vega behind `TerraRuntime.HostContracts`; ordinary Vega plugins remain behind Vega's own plugin SDK. NativeAOT does not perform arbitrary managed DLL loading.

## 15. Current limitations

Still evolving are final dashboard layout/UX, future remote/web adapters, richer safe administrative actions and panel-specific documentation. The tiled dashboard intentionally uses compact local histories rather than pretending that UI sparklines are a metrics time-series store.

## 16. Change checklist

An operations/TUI change is incomplete unless UI work stays off the authoritative thread, built-in snapshot acquisition stays off the Terminal.Gui thread, read models stay immutable/bounded, mutations return through safe operations, UI failure degrades without killing the server, terminal input cannot busy-loop, host dashboards receive no mutable implementation state, diagrams use Mermaid, dimensional timings use LaTeX, and this page changes together with `docs/ru/operations-tui.md`.
