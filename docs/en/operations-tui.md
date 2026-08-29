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

The production TUI also installs an explicit high-contrast TerraRuntime scheme after Terminal.Gui initialization. Base, Menu, Dialog, Accent and Error roles use opaque near-black backgrounds with green foreground/accent colors instead of inheriting `Color.None` terminal defaults. Besides the intended terminal/hacker visual identity, this guarantees that dashboard text cannot become foreground-equal-to-background merely because terminal default-color discovery behaved badly.

## 5. Refresh model

The dashboard refreshes from the Terminal.Gui application iteration callback at approximately

$$
T_{\mathrm{refresh}}\approx500\,\mathrm{ms}.
$$

The UI reads operations snapshots rather than walking mutable player/NPC/projectile/world collections.

## 6. Dashboard data

Current operations read models cover lifecycle/runtime status, tick/TPS and phase timing, players, NPCs, projectiles, world items, networking/queues, world state/clock, save/persistence state and bounded logs/warnings.

The System Dashboard is an operational health summary rather than a duplicate of the detail screens. It renders last/worst tick wall time, tick CPU time when available, the slowest game-loop phase, missed deadlines, process CPU, managed heap, working set, total allocation, GC collection/pause state, command queue pressure and connection/admission totals. Detailed replication and queue diagnostics remain on the Network screen.

The World screen also surfaces section-cache pipeline health from `RuntimeWorldSnapshot`: in-flight/submitted/rejected rebuilds, stale results, encode failures, publish rejections and accumulated encode time. Its on-demand row shows requests, unique/deduplicated requests, pending work versus bounded capacity, rejected requests and completed/timed-out waits. These values come from the runtime-owned rebuild pipeline snapshot; the TUI does not inspect cache workers directly.

The window layout may evolve; snapshot ownership is the invariant.

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

The ANSI TUI smoke exercises the actual menu path (`Alt+A`, then `S`) and verifies rendered pending-save state; unit tests cover accepted and rejected requests. Compatibility tests separately pin the Windows production-driver choice and explicit contrasting Base/Menu scheme attributes.

## 8. Network telemetry

`INetworkOperations` exposes bounded network state without transferring connection ownership to the UI. It includes active/registered connections, admission totals/rejections, inbound one-second and lifetime totals, per-connection inbound details, outbound current/capacity/high-water/rejection state, slow clients, player lifecycle and replication counters, unsupported replication commits, typed terminal stop categories and normalized frame-rejection categories.

The terminal stop projection distinguishes protocol, admission-rate, invalid-handshake, unsupported-protocol, slow-client, timeout/application-stop and frame-rejected outcomes. Frame-rejection categories remain a separate view of why frames were rejected, rather than being conflated with terminal connection-stop counts.

TUI projections consume subsystem-owned counters; they do not parse log text or add packet-hot-path counters of their own.

## 9. Logs

The TUI consumes bounded log state and is not a logging backend. UI failure must not lose authoritative state, log rendering must not block the game loop, retained history remains bounded, and the future structured pipeline should preserve event/category identity.

See [Observability and logging](observability-logging.md) and the logging roadmap.

## 10. Plain console fallback

Fallback commands remain literal commands:

```text
tui | ui | dashboard   reopen the dashboard
clear                  clear the console when supported
help                   show fallback-console commands
```

Unknown commands are reported rather than interpreted as runtime mutations. Closed/redirected stdin waits rather than busy-looping.

## 11. Trusted host dashboards

CoreCLR trusted hosts can register complete dashboards via `ITerraRuntimeTerminalDashboardRegistry`.

A provider supplies stable `Id`, display `Title`, `CreateDashboard()` and `Refresh(View rootView)` on the Terminal.Gui UI thread. It contributes its own root view and does not inject arbitrary controls into the built-in TerraRuntime dashboard.

Registration is metadata/factory-oriented and grants no mutable runtime state.

## 12. UI-thread ownership

Terminal.Gui views remain UI-thread objects. A dashboard provider may update its view from `Refresh`, while gameplay/runtime work is requested through safe contracts. A `View` must not become a synchronization primitive for authoritative state.

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

Still evolving are complete structured event IDs/logging, deeper subsystem-owned packet/security telemetry, final dashboard layout/UX, future remote/web adapters, richer safe administrative actions and panel-specific documentation.

## 16. Change checklist

An operations/TUI change is incomplete unless UI work stays off the authoritative thread, read models stay immutable/bounded, mutations return through safe operations, UI failure degrades without killing the server, terminal input cannot busy-loop, host dashboards receive no mutable implementation state, diagrams use Mermaid, dimensional timings use LaTeX, and this page changes together with `docs/ru/operations-tui.md`.