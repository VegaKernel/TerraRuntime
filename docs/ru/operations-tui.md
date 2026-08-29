# Operations, startup и Terminal UI

[English](../en/operations-tui.md) · [Документация](README.md) · [Архитектура](architecture.md) · [Host interfaces](host-interfaces.md)

## 1. Назначение

Operations layer предоставляет bounded read models и safe command surfaces для local administration, не позволяя UI code напрямую обходить или мутировать authoritative runtime collections.

Terminal.Gui v2 является первой local UI implementation, но architectural boundary toolkit-independent:

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

## 2. Startup без аргументов

Обычный server entry point без `--world` не завершает процесс только потому, что world не указан в command line.

`StartupProgram` создаёт runtime directory layout и сканирует canonical `Worlds/` через local world selector. Выбранный `.wld` затем добавляется как effective `--world` argument.

Если пользователь отменяет selection, startup завершается чисто и не делает вид, что world был загружен.

## 3. Runtime directories

Standalone runtime владеет небольшой directory layout относительно executable deployment directory:

```text
Worlds/
config/
data/
logs/
```

`Worlds/` является canonical directory для interactive local world selection. Explicit `--world <path>` по-прежнему может указывать в другое место.

Ошибка создания required runtime directories является startup error и сообщается до world loading.

## 4. Основные server options

Normal server startup сейчас поддерживает:

```text
--world <path.wld>
--port <1..65535>
--max-players <1..255>
--interest-management
--tui
--no-tui
```

Текущие defaults:

```text
port        = 7777
max players = 8
TUI         = enabled
interest management = disabled
```

`--tui` принимается явно, но TUI уже enabled по умолчанию. `--no-tui` выключает его.

No-argument path сначала выполняет interactive world selection, а уже потом `ServerHostOptions` validation, поэтому lower-level options record всё ещё требует resolved world path.

## 5. Другие startup modes

`StartupProgram` также распознаёт специальные paths:

- `--help` / `-h`;
- `--list-world-generators`;
- smoke modes CI, включая loop/protocol/network/world/TUI smoke paths;
- `--save-wld` checkpoint export/restore mode.

Special smoke/checkpoint modes идут через standalone program path, а не normal world-selection startup.

## 6. TUI lifecycle

TUI работает на собственном background thread с именем `TerraRuntime Terminal UI`.

Она не выполняется на authoritative game-loop thread и не блокирует server readiness обычной UI refresh работой.

`TerminalUiHost` владеет linked cancellation source и при dispose ждёт UI thread только bounded interval.

## 7. Refresh model

Dashboard refresh loop выполняется из Terminal.Gui application iteration callback.

Текущий refresh interval примерно **500 ms** (`Stopwatch.Frequency / 2`).

UI refresh читает operations snapshots. Он не должен обходить mutable player/NPC/projectile/world collections напрямую.

Так terminal rendering и toolkit callbacks остаются вне simulation ownership boundary.

## 8. Dashboard data

Runtime operations surface уже содержит read models/telemetry для областей вроде lifecycle/runtime status, tick/TPS и phase timing, players, NPCs, projectiles, world items, networking/queues, world state, world clock, save/persistence state и logs/warnings.

Exact window layout может меняться. Инвариант: dashboard потребляет bounded snapshots, а не implementation stores.

## 9. Save telemetry

World save status публикуется в operations/TUI через detached status capture.

Status содержит, среди прочего, принимает ли persistence requests, tile-shadow readiness, remaining bootstrap sections, pending dirty tile sections, save requested, active/pending background write state и accepted/started/completed/coalesced/failed write counters.

TUI поэтому не должен лазить напрямую в `WorldTileStore` или save coordinator.

## 10. Network telemetry

Operations может отдавать bounded network state: active connections, queue depth и другие runtime counters.

UI не становится владельцем connection lifetime. Disconnect или иная mutation проходит через explicit safe operation/command path.

High-frequency telemetry должна агрегироваться до display. Форматировать одну UI string на каждый packet в hot path — ровно та поганая хуйня, от которой архитектура и отделяет operations.

## 11. Logs

Logging развивается в сторону runtime-owned structured asynchronous pipeline. TUI/log operations boundary должна потреблять уже bounded log state, а не становиться logging backend.

Важные правила: UI failure не теряет authoritative state, log rendering не блокирует game loop, retained history bounded, а future structured events сохраняют category/event identity вместо одной готовой строки.

Незавершённая logging work описана в `docs/roadmap/runtime-logging-pipeline.md`.

## 12. Fallback при UI failure

TUI failure не является server failure.

Если Terminal.Gui initialization или dashboard session падает, `TerminalUiHost` сообщает проблему и переключается в plain console session.

Runtime предпочитает degraded local control surface вместо shutdown здорового game server из-за проблем terminal capabilities.

## 13. Plain console fallback

После выхода или failure TUI local fallback console сейчас поддерживает:

```text
tui | ui | dashboard   снова открыть dashboard
clear                  очистить console, если terminal это умеет
help                   показать fallback-console commands
```

Unknown commands сообщаются явно, а не превращаются в runtime mutations.

Closed/redirected stdin обрабатывается ожиданием, а не busy loop.

## 14. Trusted host dashboards

CoreCLR extensible host может добавлять complete dashboards через `ITerraRuntimeTerminalDashboardRegistry`.

Provider отдаёт stable `Id`, display `Title`, `CreateDashboard()` на Terminal.Gui UI thread и `Refresh(View rootView)` на UI thread.

Trusted provider предоставляет собственный root view. Он не может произвольно внедрять controls в built-in system dashboard TerraRuntime.

Registration metadata/factory-oriented и не выдаёт mutable runtime state.

## 15. UI-thread ownership

Terminal.Gui views являются UI-thread objects.

Dashboard provider может обновлять свой view из `Refresh`, но runtime/gameplay work всё равно запрашивается через safe contracts. Нельзя передавать `View` в game loop или использовать UI controls как synchronization primitive authoritative state.

## 16. Правило administrative mutation

Любая operation, меняющая runtime state, проходит ту же ownership boundary, что и другие control paths.

Примеры существующих/будущих safe operations: player actions, runtime world-item operations, interest-management toggle, save request и server-controlled actor commands.

TUI не получает direct-mutation shortcut только потому, что работает в одном process.

## 17. Headless/plain operation

Отключение TUI не отключает server runtime. UI является operations adapter, а не dependency simulation correctness.

NativeAOT и CoreCLR deployment profiles должны выполнять server functionality независимо от successful graphical terminal session.

CI имеет dedicated TUI smoke path, но normal network/world smoke tests не должны зависеть от terminal rendering.

## 18. Extensible host boundary

CoreCLR profile может загружать trusted host modules вроде Vega за `TerraRuntime.HostContracts`. Эти modules могут регистрировать complete terminal dashboards и после attach получать narrow runtime operations.

Обычные Vega plugins остаются за Vega Plugin SDK и автоматически не становятся TerraRuntime trusted host modules.

Standalone NativeAOT profile не выполняет arbitrary managed DLL loading.

## 19. Observability и control

Хороший operations API разделяет reads и mutations:

```text
read path
  immutable snapshot -> display/export

write path
  validated command -> authoritative owner -> result
```

Объединить это в mutable `ServerState` object означало бы развалить single-writer architecture и сделать future web/API/TUI adapters небезопасными по определению.

## 20. Текущие ограничения

Operations/TUI уже usable, но это не финальная administration platform.

Ещё развиваются complete structured logging/event IDs, более широкая packet/rejection/security telemetry, final configurable dashboard layout и long-term UX, future remote/web API adapters за тем же operations model, более богатые safe administrative actions и документация каждого dashboard panel после стабилизации layout.

## 21. Checklist изменения operations/TUI

Operations/TUI change не завершён, пока по необходимости UI work остаётся вне authoritative thread, read models immutable/bounded, mutations возвращаются через command/operations boundary, TUI failure деградирует без убийства server, redirected/closed input не создаёт busy-loop, host dashboard providers не получают mutable implementation state, а startup CLI/default behavior обновлены здесь и в `docs/en/operations-tui.md` тем же change.
