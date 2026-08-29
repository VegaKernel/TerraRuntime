# Руководство по проекту TerraRuntime

[English](../en/project-guide.md) · [Документация](README.md) · [Архитектура](architecture.md) · [Host interfaces](host-interfaces.md) · [Roadmap](../roadmap.md)

## 1. Что такое TerraRuntime

TerraRuntime — clean-room серверный runtime Terraria на .NET 11, ориентированный на воспроизведение наблюдаемого поведения официального dedicated server 1.4.5.8 при другой внутренней архитектуре: явное владение состоянием, bounded work, тестируемые границы и независимость gameplay-кода от сетевого транспорта.

Основной принцип:

> Vanilla-visible behavior сохраняется, внутреннюю реализацию разрешено менять, если это не ломает наблюдаемый контракт.

TerraRuntime не является форком TerrariaServer и не использует его runtime-объекты. Официальный сервер применяется как поведенческий источник истины и differential reference. Protocol 326 / Terraria 1.4.5.8 моделируется через Multiplicity за внутренней protocol boundary.

## 2. Профили запуска

Проект поддерживает два разных shipping profile.

### Standalone NativeAOT

`TerraRuntime.Server` — самостоятельный серверный executable. Runtime core обязан оставаться совместимым с NativeAOT; Linux x64 и Windows x64 publish/smoke являются обязательными CI-gates.

NativeAOT-профиль не загружает произвольные managed DLL плагины.

### Extensible CoreCLR

`TerraRuntime.Extensible.Server` — self-contained CoreCLR host для доверенного host module, прежде всего Vega. Он сохраняет те же runtime ownership rules, но добавляет узкий привилегированный контракт `TerraRuntime.HostContracts`.

Обычные Vega plugins не получают внутренние объекты TerraRuntime. Граница выглядит так:

```text
TerraRuntime implementation
        |
        v
TerraRuntime.HostContracts
        |
        v
trusted host module (Vega)
        |
        v
Vega.PluginSdk / ordinary plugins
```

## 3. Структура репозитория

| Путь | Назначение |
|---|---|
| `src/TerraRuntime` | standalone composition root, startup, gameplay/network/world composition, TUI |
| `src/TerraRuntime.ExtensibleHost` | CoreCLR host, trusted host module loading и host environment |
| `src/TerraRuntime.HostContracts` | публичная узкая поверхность для trusted host modules |
| `src/TerraRuntime.Contracts` | стабильные runtime/gameplay snapshots, IDs и control contracts |
| `src/TerraRuntime.Core` | authoritative state, command execution, NPC/projectile/item/player systems, scheduling |
| `src/TerraRuntime.Network` | connection pipeline, frame ingress/egress, queues и network contracts |
| `src/TerraRuntime.Protocol` | protocol boundary и общие codec/framing concepts |
| `src/TerraRuntime.Protocol.Multiplicity` | адаптация Multiplicity к runtime protocol boundary |
| `src/TerraRuntime.Transport` | низкоуровневые transport primitives, где они отделены от network policy |
| `src/TerraRuntime.World` | `.wld`, tiles, sections, world cache, collision, liquids, persistence helpers |
| `tests/TerraRuntime.Tests` | unit/integration/contract tests |
| `tests/TerraRuntime.HostModuleFixture` | fixture для проверки extensible host/module boundary |
| `tools/` | reference probes, world verification и CI tooling |
| `docs/roadmap/` | подробные планы отдельных подсистем |

## 4. Сборка

SDK закреплён через `global.json`. Основная solution: `TerraRuntime.slnx`.

Типичный цикл разработки:

```bash
dotnet restore TerraRuntime.slnx
dotnet build TerraRuntime.slnx -c Release
dotnet test TerraRuntime.slnx -c Release --no-build
```

Для shipping-проверки одной обычной сборки недостаточно. Изменение runtime core должно сохранять Linux/Windows NativeAOT publication и exercised smoke paths.

## 5. Runtime layout

Standalone runtime использует каталог executable как свой root и создаёт/использует:

```text
TerraRuntime.Server[.exe]
Worlds/
config/
data/
logs/
```

`Worlds/` — канонический каталог миров для интерактивного выбора. Явный `--world <path.wld>` может указывать на файл вне этого каталога.

Extensible CoreCLR deployment имеет отдельные каталоги доверенных host modules и серверных plugins:

```text
TerraRuntime.Extensible.Server[.exe]
runtime/
HostModules/
ServerPlugins/
Worlds/
config/
data/
logs/
```

## 6. Как сервер обрабатывает клиентское соединение

Главный поток данных намеренно однонаправленный по ownership:

```text
TCP socket
  -> connection read loop
  -> bounded frame decoder
  -> protocol validation/decode
  -> owned typed command
  -> authoritative game-loop queue
  -> gameplay/state validation
  -> authoritative mutation
  -> immutable outbound event/snapshot
  -> recipient/sync planning
  -> encoded frame
  -> bounded per-client outbound queue
  -> socket writer
```

Сетевой callback не имеет права непосредственно менять world/player/NPC/projectile/item state.

Полученный packet рассматривается как недоверенный ввод. До authoritative mutation проходят как минимум framing/size checks, connection-state legality и subsystem-specific validation.

## 7. Authoritative game loop

Mutable simulation state принадлежит одному dedicated game-loop thread.

Базовая частота simulation schedule — 60 Hz. Loop не обязан обрабатывать бесконечный входной backlog за один tick: command path имеет глобальный cap, fairness по источникам и telemetry отложенной работы.

Упрощённо tick состоит из фаз вида:

```text
inbound commands
clock/events
world/liquids/growth
items
NPC AI
projectiles
combat
spawning
progression
visibility/sync planning
outbound snapshots
```

Точный набор фаз развивается вместе с gameplay parity. Важен invariant: mutable state изменяется владельцем, а blocking disk/network work не выполняется внутри simulation hot path.

## 8. Player join и начальная синхронизация

Join flow строится вокруг vanilla protocol ordering. TerraRuntime назначает серверный player slot, проводит connection state через допустимые handshake states, выдаёт world metadata и sections, после чего переводит игрока в joined/spawned состояние.

Live CI probes используют настоящий официальный TerrariaServer/официально сгенерированный `.wld` как независимую проверку и отдельно проверяют критический join/movement flow. Это важнее self-roundtrip тестов, где encoder и decoder могли бы одинаково ошибаться.

Section/bootstrap path остаётся bounded: один joining client не должен останавливать simulation для уже подключённых игроков.

## 9. Мир и `.wld`

Каноническим persistent representation остаётся Terraria `.wld`. Runtime не должен считать собственный cache источником истины.

World subsystem отвечает за:

- parsing/verification поддерживаемого `.wld` layout;
- runtime tile/world representation;
- sections и их кодирование;
- chests/signs/tile entities по мере реализации parity;
- collision/world queries;
- liquid work;
- save snapshots;
- производный `.runtime-world` cache.

Неизвестный или неподтверждённый file layout обрабатывается консервативно: способность прочитать часть файла не означает право безопасно перезаписать его.

## 10. Runtime world cache

`.runtime-world` — disposable derived image для ускорения старта.

```text
world.wld            canonical
world.runtime-world  derived cache
```

Cache может хранить подготовленное runtime state, чтобы не повторять дорогую реконструкцию при каждом старте. При validation failure, corruption или schema/source mismatch сервер обязан вернуться к `.wld`.

Cache никогда не должен превращать повреждение производного файла в повреждение канонического мира.

## 11. Сохранение

Save path разделяется на короткую authoritative snapshot/commit boundary и работу вне game loop.

Целевой порядок:

```text
authoritative state
  -> bounded snapshot capture
  -> background serialization/write
  -> flush/validation
  -> atomic replace canonical .wld
  -> derived runtime cache rebuild
```

Одновременно допускается только bounded/coalesced save work. Нельзя строить бесконечную очередь autosave.

TUI/operations отображают состояние сохранения, но UI не владеет save state и не мутирует world напрямую.

## 12. NPC, projectiles и gameplay

Gameplay постепенно переносится подсистема за подсистемой. Уже существуют отдельные runtime stores, snapshots, definition catalogs и state/AI steppers для части NPC/projectile поведения.

Правило декомпозиции:

- packet IDs остаются в protocol boundary;
- content IDs становятся version-pinned domain concepts;
- live entity identity отделяется от content type;
- AI/physics/combat не кодируют packets напрямую;
- network replication строится из authoritative state/events.

Полная vanilla parity ещё не достигнута. Особенно широкими остаются NPC AI coverage, bosses, events, housing, loot, wiring/liquids, progression и vanilla world generation. Roadmap является источником текущего статуса, а не существование класса с подходящим именем.

## 13. Interest management

Interest management принадлежит TerraRuntime. Внешний host получает только узкий control contract включения/выключения.

Spatial layout, hysteresis, resync policy и recipient selection остаются внутренней реализацией runtime. При выключении механизм должен fail-open к vanilla-like broad recipient selection.

Packet suppression нельзя включать только потому, что spatial index уже существует: сначала должны быть доказаны enter/leave semantics, full state on entry и forced resync.

## 14. TUI и operations boundary

Terminal UI не читает mutable collections напрямую. Она получает bounded immutable projections и отправляет административные mutations обратно через контролируемую command boundary.

Следствие: зависание/падение UI не должно менять ownership world state и не должно становиться обязательным условием network readiness.

Extensible host может регистрировать отдельные dashboard providers через host contract, но не получает права внедрять произвольные controls во внутренний system dashboard TerraRuntime.

## 15. Trusted host modules

Trusted host module используется только в CoreCLR profile. Его lifecycle разделён на bootstrap и runtime attach:

```text
load module
  -> StartAsync(environment)
  -> TerraRuntime starts authoritative runtime
  -> AttachRuntimeAsync(runtime contracts)
  -> normal operation
  -> DetachRuntimeAsync()
  -> StopAsync()
```

`ITerraRuntimeHostEnvironment` предоставляет deploy paths и registration surfaces, которым не нужен live world. `ITerraRuntimeHostRuntime` появляется позже и предоставляет snapshots/controlled operations без выдачи mutable implementation state.

Подробности и примеры: [Host interfaces](host-interfaces.md).

## 16. World generation

Worldgen framework уже отделён от конкретной генерации: registry → provider → validated plan → isolated workspace → final acceptance.

Built-in generator на текущем этапе является deterministic flat baseline и не претендует на vanilla WorldGen parity. Это специально: worldgen огромен и RNG-order-sensitive, поэтому архитектурный extension pipeline развивается отдельно от полного порта vanilla passes.

Trusted host может регистрировать custom generator через `ITerraRuntimeWorldGeneratorRegistry`; TerraRuntime остаётся владельцем validation, execution boundaries и принятия итогового мира.

## 17. Ошибки и безопасность

Нормальный network/gameplay failure должен быть локальным и bounded.

Запрещены архитектурные пути, где client-controlled input может:

- выделить неограниченную память;
- создать неограниченный queue backlog;
- заставить server process упасть decoder exception;
- выполнить blocking expensive work без бюджета;
- мутировать state до проверки connection/gameplay legality.

Malformed protocol, rate limit, invalid state и gameplay rejection должны оставаться различимыми категориями, а не превращаться в одно универсальное `catch`.

## 18. Как проверяется совместимость

TerraRuntime использует несколько независимых уровней доказательства:

1. unit/contract tests;
2. golden packet/file facts;
3. официально сгенерированные `.wld`;
4. official client/server captures;
5. live process probes;
6. differential checks против TerrariaServer 1.4.5.8;
7. Linux/Windows NativeAOT publish + smoke.

Green self-roundtrip без независимого источника не считается достаточным доказательством protocol/gameplay parity.

## 19. Правило изменения проекта

При изменении кода сразу обновляйте соответствующие RU/EN документы. Минимальная матрица:

| Изменение | Что обновить |
|---|---|
| публичный host/runtime contract | `host-interfaces.md` + при необходимости `architecture.md` |
| lifecycle/ownership/threading | `architecture.md` + `project-guide.md` |
| CLI/deployment/startup | `project-guide.md` |
| persistence/cache/world format | `project-guide.md` + `architecture.md` |
| новая gameplay subsystem boundary | `architecture.md` и соответствующий roadmap |
| новое ограничение/known divergence | user-facing guide + roadmap |

Документация должна фиксировать уже реализованное поведение и явно отделять его от target design.
