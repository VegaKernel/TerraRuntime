# Operations, startup and Terminal UI

[Русский](../ru/operations-tui.md) · [Documentation](README.md) · [Architecture](architecture.md) · [Host interfaces](host-interfaces.md)

## 1. Purpose

The operations layer exposes bounded read models and safe command surfaces for local administration without allowing UI code to traverse or mutate authoritative runtime collections directly.

Terminal.Gui v2 is the first local UI implementation, but the architectural boundary is toolkit-independent:

```text
authoritative runtime
       |
       v
immutable/bounded operations snapshots
       |
       +--> Terminal UI
       +--> plain console
       +--> trusted host integration
       +--> future API surfaces

administrative action
       -> operations/command boundary
       -> authoritative owner
```

## 2. Startup without arguments

Running the normal server entry point without `--world` does not terminate just because no command-line world was supplied.

`StartupProgram` creates the runtime directory layout and scans the canonical `Worlds/` directory through the local world selector. The selected `.wld` is then appended as the effective `--world` argument.

If the user cancels selection, startup exits cleanly without pretending that a server world was loaded.

## 3. Runtime directories

The standalone runtime owns a small directory layout rooted at the executable deployment directory:

```text
Worlds/
config/
data/
logs/
```

`Worlds/` is the canonical directory used by interactive local world selection. An explicit `--world <path>` may still point elsewhere.

Failure to create the required runtime directories is a startup error and is reported before world loading begins.

## 4. Main server options

Normal server startup supports the current options:

```text
--world <path.wld>
--port <1..65535>
--max-players <1..255>
--interest-management
--tui
--no-tui
```

Defaults currently are:

```text
port        = 7777
max players = 8
TUI         = enabled
interest management = disabled
```

`--tui` is accepted explicitly, but TUI is already enabled by default. `--no-tui` disables it.

The normal no-argument path performs interactive world selection before `ServerHostOptions` validation, so the lower-level options record may still require a resolved world path.

## 5. Other startup modes

`StartupProgram` also recognizes special paths such as:

- `--help` / `-h`;
- `--list-world-generators`;
- smoke modes used by CI, including loop/protocol/network/world/TUI smoke paths;
- `--save-wld` checkpoint export/restore mode.

Special smoke/checkpoint modes go through the standalone program path rather than normal world-selection startup.

## 6. TUI lifecycle

The TUI runs on its own background thread named `TerraRuntime Terminal UI`.

It does not run on the authoritative game-loop thread and does not block server readiness on normal UI refresh work.

`TerminalUiHost` owns a linked cancellation source and joins its UI thread for a bounded interval during disposal.

## 7. Refresh model

The dashboard refresh loop runs from the Terminal.Gui application iteration callback.

The current refresh interval is approximately **500 ms** (`Stopwatch.Frequency / 2`).

UI refresh reads operations snapshots. It must not walk mutable player/NPC/projectile/world collections directly.

This keeps expensive terminal rendering and toolkit callbacks outside the simulation ownership boundary.

## 8. Dashboard data

The runtime operations surface currently includes read models/telemetry for areas such as:

- lifecycle/runtime status;
- tick/TPS and phase timing;
- players;
- NPCs;
- projectiles;
- world items;
- networking/queues;
- world state;
- world clock;
- save/persistence state;
- logs/warnings.

The exact window layout can evolve. The invariant is that the dashboard consumes bounded snapshots rather than implementation stores.

## 9. Save telemetry

World save status is exposed to operations/TUI through a detached status capture.

The status includes information such as:

- whether persistence accepts requests;
- tile-shadow readiness;
- remaining bootstrap sections;
- pending dirty tile sections;
- whether a save is requested;
- active/pending background write state;
- accepted/started/completed/coalesced/failed write counters.

The TUI therefore does not need to inspect `WorldTileStore` or the save coordinator directly.

## 10. Network telemetry

Operations may expose bounded network state such as active connections, queue depth and other runtime counters.

The UI must never become the owner of connection lifetime. Disconnects or other mutations go through an explicit safe operation/command path.

High-frequency telemetry should be aggregated before display. Formatting one UI string per packet on the packet hot path is explicitly the wrong design.

## 11. Logs

Logging is evolving toward a runtime-owned structured asynchronous pipeline. The TUI/log operations boundary should consume already-bounded log state rather than becoming the logging backend itself.

Important rules:

- UI failure must not lose authoritative runtime state;
- log rendering must not block the game loop;
- retained log history must be bounded;
- future structured events should preserve category/event identity rather than only preformatted text.

See `docs/roadmap/runtime-logging-pipeline.md` for incomplete logging work.

## 12. UI failure fallback

TUI failure is not a server failure.

If Terminal.Gui initialization or a dashboard session throws, `TerminalUiHost` reports the problem and switches to a plain console session.

The runtime deliberately prefers a degraded local control surface over shutting down a healthy game server because terminal capabilities are broken.

## 13. Plain console fallback

After leaving or failing the TUI, the local fallback console currently supports a deliberately small UI-host command set:

```text
tui | ui | dashboard   reopen the dashboard
clear                  clear the console when supported
help                   show fallback-console commands
```

Unknown commands are reported rather than silently interpreted as runtime mutations.

Closed/redirected stdin is handled with a wait rather than a busy loop.

## 14. Trusted host dashboards

The CoreCLR extensible host may expose additional complete dashboards through `ITerraRuntimeTerminalDashboardRegistry`.

A provider supplies:

- stable `Id`;
- display `Title`;
- `CreateDashboard()` called on the Terminal.Gui UI thread;
- `Refresh(View rootView)` called on the UI thread.

A trusted provider contributes its own root view. It does not inject arbitrary controls into TerraRuntime's built-in system dashboard.

Registration is metadata/factory-oriented and does not grant access to mutable runtime state.

## 15. UI-thread ownership

Terminal.Gui views are UI-thread objects.

A dashboard provider may update its view from `Refresh`, but runtime/gameplay work must still be requested through safe contracts. Do not pass a `View` to the game loop or use UI controls as a synchronization primitive for authoritative state.

## 16. Administrative mutation rule

Any operation that changes runtime state must cross the same ownership boundary used by non-UI control paths.

Examples include future or existing safe operations for:

- player actions;
- runtime world-item operations;
- interest-management toggling;
- save requests;
- server-controlled actor commands.

The TUI must not gain a special direct-mutation shortcut merely because it runs in the same process.

## 17. Headless/plain operation

Disabling TUI does not disable the server runtime. UI is an operations adapter, not a dependency of simulation correctness.

NativeAOT and CoreCLR deployment profiles must both remain capable of exercising server functionality independently of a successful graphical terminal session.

CI includes a dedicated TUI smoke path, but normal network/world smoke tests must not rely on terminal rendering.

## 18. Extensible host boundary

The CoreCLR profile can load trusted host modules such as Vega behind `TerraRuntime.HostContracts`. Those modules may register complete terminal dashboards and receive narrow runtime operations after attachment.

Ordinary Vega plugins remain behind Vega's plugin SDK and do not automatically become TerraRuntime trusted host modules.

The standalone NativeAOT profile does not perform arbitrary managed DLL loading.

## 19. Observability versus control

A useful operations API separates reads from mutations:

```text
read path
  immutable snapshot -> display/export

write path
  validated command -> authoritative owner -> result
```

Combining these into a mutable `ServerState` object would undermine the single-writer architecture and make future web/API/TUI adapters unsafe by construction.

## 20. Current limitations

Operations/TUI is usable but not the final administration platform.

Still evolving:

- complete structured logging/event IDs;
- broader packet/rejection/security telemetry;
- final configurable dashboard layout and long-term UX;
- future remote/web API adapters behind the same operations model;
- richer safe administrative actions;
- documentation of every dashboard panel as the layout stabilizes.

## 21. Change checklist

An operations/TUI change is incomplete unless, where relevant:

- UI work stays off the authoritative thread;
- read models are immutable/bounded;
- mutations return through the command/operations boundary;
- TUI failure still degrades without killing the server;
- redirected/closed terminal input cannot busy-loop;
- host dashboard providers do not receive mutable implementation state;
- startup CLI/default behavior is updated here and in `docs/ru/operations-tui.md` in the same change.
