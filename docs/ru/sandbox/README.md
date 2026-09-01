# Архитектура sandbox runtime

[English](../../en/sandbox/README.md) · [Roadmap](../../roadmap/sandbox/README.md)

Этот каталог является канонической архитектурной спецификацией sandbox-миров TerraRuntime.

Sandbox — не второй игровой движок и не замена Dimensions. Оба уровня изоляции используют одну модель `WorldRuntime`. Даже primary world является обычным `WorldRuntime`, который Vega/host просто выбрали как primary target. Отличается lifecycle/policy и, для Level 2, process boundary.

## Архитектура целиком

```mermaid
flowchart TD
    Plugin["Vega plugin / operator"] --> Vega["Vega sandbox policy"]
    Vega --> Host["TerraRuntime sandbox API"]
    Host --> Source{"World source"}
    Source --> File[".wld"]
    Source --> Generated["Generated"]
    Source --> Schematic[".trschem"]
    Source --> Clone["SnapshotClone"]
    File --> Choice{"Effective isolation"}
    Generated --> Choice
    Schematic --> Choice
    Clone --> Choice
    Choice -->|InProcess| L1["Level 1 WorldRuntime"]
    Choice -->|DedicatedProcess| Supervisor["SandboxSupervisor"]
    Supervisor --> Control["TerraRuntime.Transport"]
    Control --> Worker["Sandbox worker process"]
    Worker --> WorkerRuntime["WorldRuntime внутри worker"]
```

Vega запрашивает sandbox-семантику. TerraRuntime владеет authoritative world state и lifecycle процессов/socket. `TerraRuntime.Transport` остаётся общей control/server boundary, но локальный Level 2 после socket handoff ведёт gameplay traffic напрямую client-to-worker.

## Документы

- [Level 1: in-process sandbox](level-1.md) — полный независимый `WorldRuntime` в общем процессе, plugin compatibility и shared chat.
- [Level 2: dedicated-process sandbox](level-2.md) — lifecycle worker, размещение, загрузка выбранного game mode/plugin и fault isolation.
- [Источники мира и TerraRuntime Schematic](world-sources-schematics.md) — `.wld`, generated worlds, `.trschem`, chests, tile entities, NPC, markers и materialization.
- [Передача TCP socket](socket-handoff.md) — ownership main -> worker -> main без reconnect клиента.
- [Transport и control plane](transport.md) — что переносит Transport и что через него намеренно не идёт.
- [Интеграция Vega](vega-integration.md) — создание sandbox, выбор isolation, hooks, commands и sandbox-local logic.

## Основные инварианты

1. Один live `WorldRuntime` имеет ровно одного authoritative simulation owner.
2. Primary world не является специальной simulation class; это host-selected обычный `WorldRuntime`.
3. Полный mutable gameplay state runtime-local: players, NPC, bosses/AI, projectiles, items, tiles, chests/signs/tile entities, liquids, wiring, events, time/weather, progression, RNG, replication и persistence.
4. Client принадлежит максимум одному активному `WorldSessionId` одновременно.
5. Переданный в Level 2 client socket имеет ровно одного application-level process owner одновременно.
6. Identity `.wld`/`.trschem` не является identity live runtime. Lifetime определяют `WorldRuntimeId` и `WorldSessionId`.
7. Level 1 не гоняет обычный gameplay через IPC.
8. Level 2 использует Transport для lifecycle/state/control, затем передаёт accepted TCP socket worker для прямого gameplay.
9. Legacy Vega plugins по умолчанию остаются привязаны только к selected primary runtime; sandbox-aware logic получает отдельный `SandboxContext`.
10. Общий Vega chat router разрешён для Level 1, но сообщения сохраняют `WorldRuntimeIdentity` и visibility policy.
11. Vega policy может усилить запрошенную isolation, но не может незаметно ослабить требование dedicated process.

## Выбор isolation

Концептуально Vega может запросить:

```text
Auto
InProcess
DedicatedProcess
```

`Auto` отдаёт выбор policy. `InProcess` выражает предпочтение по производительности, но policy может усилить его до `DedicatedProcess`. `DedicatedProcess` является минимальным требованием isolation и не должен незаметно понижаться.

```mermaid
flowchart LR
    Request["Plugin request"] --> Policy["Vega/operator policy"]
    Policy -->|trusted, обычная миниигра| InProc["InProcess"]
    Policy -->|risk / strict limits / forced isolation| Dedicated["DedicatedProcess"]
```

## Источники мира

Level 1 и Level 2 используют один `SandboxWorldSource`:

- существующий `.wld`;
- `Generated` request через TerraRuntime world generators;
- нативную TerraRuntime schematic `.trschem`;
- snapshot/clone source после реализации соответствующего runtime snapshot contract.

`.trschem` является общим форматом TerraRuntime/Vega/WorldEdit, а не WorldEdit-зависимостью. Он рассчитан на reusable scenes/arenas и может содержать tiles, liquids/wiring, chests с contents, signs, typed tile entities, NPC placements, world items и named markers/regions.

Одна и та же карта должна запускаться Level 1 или Level 2 без смены asset format.
