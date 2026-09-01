# Интеграция Vega с sandbox worlds

[Обзор](README.md) · [English](../../en/sandbox/vega-integration.md)

Vega должна давать plugins один semantic sandbox API. Plugin запрашивает world source, game mode и isolation requirement; Vega/operator policy определяет effective isolation с правилом: требуемая isolation никогда незаметно не ослабляется.

## Primary runtime не является отдельным типом мира

Vega/host выбирает один обычный `WorldRuntime` как primary target для обычного входа игроков и compatibility старых plugins. Сам `WorldRuntime` не должен иметь отдельную primary/sandbox simulation class.

```mermaid
flowchart TD
    Registry["Live WorldRuntime registry"] --> A["WorldRuntime A"]
    Registry --> B["WorldRuntime B"]
    Vega["Vega/host policy"] --> Primary["Primary selection"]
    Primary -.-> A
    Vega --> Sandbox["Sandbox lifecycle"]
    Sandbox -.-> B
```

## Flow создания

```mermaid
sequenceDiagram
    participant P as Vega plugin
    participant V as Vega sandbox service
    participant R as TerraRuntime
    participant W as Optional worker

    P->>V: CreateSandbox(source, gameMode, isolation)
    V->>V: apply permissions / quotas / operator policy
    V->>R: create sandbox
    alt InProcess
        R->>R: create ordinary WorldRuntime
        R-->>V: runtime scope ready
        V->>V: create SandboxContext + game-mode instance
    else DedicatedProcess
        R->>W: spawn + configure worker
        W-->>R: RuntimeReady
        R-->>V: sandbox ready
    end
    V-->>P: SandboxHandle(runtime identity, effective isolation)
```

Конкретные public types пока не зафиксированы. Важна semantic boundary: plugin не запускает process, не дублирует socket и не разговаривает с raw Transport самостоятельно.

## Isolation policy

```mermaid
flowchart TD
    Request{"Requested isolation"}
    Request -->|Auto| Policy["Vega/operator policy"]
    Request -->|InProcess| Policy
    Request -->|DedicatedProcess| Required["DedicatedProcess"]
    Policy -->|ordinary trusted workload| L1["InProcess"]
    Policy -->|forced isolation / strict limits / risk| L2["DedicatedProcess"]
```

`DedicatedProcess` является минимумом. Policy может усилить `InProcess` до `DedicatedProcess`, но не может незаметно понизить dedicated requirement.

## Level 1: все plugins загружены, но не все подключены к каждому миру

Level 1 не создаёт отдельный plugin host. Все текущие Vega plugin assemblies остаются загруженными один раз в main process.

Но это **не означает**, что каждый plugin автоматически получает hooks/events каждого `WorldRuntime`.

Compatibility policy:

- legacy/single-world plugin получает world-scoped callbacks только runtime, выбранного Vega как primary;
- process-global infrastructure может оставаться общей, если она не владеет mutable gameplay state мира;
- sandbox/multi-world-aware plugin или game mode явно получает отдельный `SandboxContext` конкретного runtime;
- sandbox-aware mutable state создаётся отдельно для каждой arena/session.

```mermaid
flowchart TD
    Loaded["All loaded Vega plugins"] --> Legacy["legacy"]
    Loaded --> Global["process-global service"]
    Loaded --> Aware["sandbox-aware"]
    Legacy --> Primary["host-selected primary WorldRuntime"]
    Aware --> SA["SandboxContext A"]
    Aware --> SB["SandboxContext B"]
    SA --> WA["WorldRuntime A"]
    SB --> WB["WorldRuntime B"]
```

Baseline Level 1 не требует `Modules = [...]`. Создание sandbox выбирает один game mode/owner logic. Нужные Teams/score/spawn helper-части остаются обычным кодом game mode либо host APIs. Произвольный dependency graph добавляется только при реальной необходимости.

## Hooks, commands и timers

Sandbox получает собственный runtime context. Hooks, commands, timers и match state привязываются к конкретному `WorldRuntimeIdentity` и удаляются вместе с `SandboxContext`.

```mermaid
flowchart LR
    Plugin["Loaded game-mode plugin"] --> ScopeA["SandboxContext A"]
    Plugin --> ScopeB["SandboxContext B"]
    ScopeA --> RuntimeA["WorldRuntime A"]
    ScopeB --> RuntimeB["WorldRuntime B"]
```

Один plugin assembly загружен один раз, но per-match mutable state не является global singleton.

## Shared chat в Level 1

Общий Vega chat router является допустимым process-global сервисом. Он может обслуживать игроков из разных runtimes, но сообщение должно сохранять `WorldRuntimeIdentity` источника, чтобы Vega могла реализовать global/world/team/private visibility.

```mermaid
flowchart LR
    A["WorldRuntime A player"] --> Chat["Vega chat router"]
    B["WorldRuntime B player"] --> Chat
    Chat --> Policy["visibility policy"]
```

Общий chat не делает NPC, bosses, progression, hooks или другие gameplay systems общими.

## Level 2

```mermaid
flowchart TD
    MainPlugin["Main Vega controller/plugin"] --> Create["sandbox descriptor"]
    Create --> Worker["Dedicated sandbox worker"]
    Worker --> LocalHost["sandbox-local Vega/host scope"]
    LocalHost --> Selected["selected game-mode/plugin package"]
    Selected --> Runtime["WorldRuntime"]
```

В Level 2 worker загружает только требуемую sandbox-side game mode/plugin package и её необходимые runtime dependencies, а не полный набор plugins main Vega.

Hot-path hooks и world-local commands выполняются внутри worker, который владеет world и client socket. Они не являются RPC callbacks в main Vega process.

Global/operator commands остаются в main Vega и используют semantic control operations, когда им нужно посмотреть или остановить sandbox.

## Выбор package для Level 2

Vega описывает выбранную worker-side logic стабильными package identity, version и integrity metadata, а не сериализует живые plugin objects.

Концептуально creation descriptor может содержать:

```text
plugin/game-mode package id
version
content hash
configuration
required capabilities
```

Локальный worker может загружать package из controlled plugin/package store. Постоянно копировать arbitrary DLL bytes через Transport для каждого матча не является baseline design.

## Lifetime registrations

World-scoped registrations должны быть revocable. Точное имя типа пока не фиксируется, но lifecycle должен вести себя как lease:

```mermaid
stateDiagram-v2
    [*] --> Attached
    Attached --> Active: hooks/commands registered
    Active --> Retiring: sandbox stop / plugin reload / detach
    Retiring --> Retired: registrations revoked + references released
    Retired --> [*]
```

Stale delegate или command registration не должны удерживать unloaded plugin context или dead `WorldRuntime`.

## Текущее ограничение TerraRuntime

Текущий trusted-host loader использует single-runtime attachment model. Multi-world support должен развить lifecycle до независимых runtime scopes по `WorldRuntimeIdentity`, а не одного global attached flag. Существующую per-world activation policy нужно переиспользовать, а не создавать вторую activation system.
