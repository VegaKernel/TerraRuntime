# Level 1: in-process sandbox

[Обзор](README.md) · [English](../../en/sandbox/level-1.md)

Level 1 запускает sandbox как ещё один независимый `WorldRuntime` внутри того же процесса TerraRuntime.

## Модель владения

```mermaid
flowchart TD
    Host["TerraRuntime process"] --> Registry["World runtime host/registry"]
    Registry --> Primary["Primary WorldRuntime"]
    Registry --> Arena["Arena WorldRuntime"]
    Registry --> Tutorial["Tutorial WorldRuntime"]
    Primary --> Loop1["Authoritative owner"]
    Arena --> Loop2["Authoritative owner"]
    Tutorial --> Loop3["Authoritative owner"]
```

Процесс может содержать несколько миров, но mutable simulation ownership остаётся отдельным для каждого runtime. Runtime владеет своими players, NPCs, projectiles, items, world clock/progression, RNG streams, extension state, replication state и persistence policy.

Один мир не должен менять другой через shared global state.

## Lifecycle создания

```mermaid
sequenceDiagram
    participant V as Vega
    participant H as TerraRuntime host
    participant W as World source
    participant R as WorldRuntime
    participant P as Vega world scope

    V->>H: CreateSandbox(InProcess, source, policy)
    H->>W: load/generate/clone + validate
    H->>R: create runtime + new runtime/session identity
    H->>R: start authoritative execution
    H-->>V: runtime available
    V->>P: attach selected world-local logic
    P-->>V: hooks/commands ready
```

Реальная реализация может attach host logic до admission игроков, но sandbox нельзя считать ready, пока обязательный world/plugin scope не готов.

## Scope плагинов и команд

Level 1 не требует отдельного процесса Vega. Уже загруженный Vega plugin получает отдельный scope для каждого мира, в котором участвует.

```mermaid
flowchart LR
    Plugin["CTF plugin instance"] --> Scope1["World scope: Arena 1"]
    Plugin --> Scope2["World scope: Arena 2"]
    Scope1 --> Hooks1["hooks / commands / match state"]
    Scope2 --> Hooks2["hooks / commands / match state"]
```

Registrations должны быть world-scoped и revocable. Плагин не должен добавлять глобальную `/team` или глобальный player-death handler только потому, что они нужны одной arena.

## Teardown

```mermaid
sequenceDiagram
    participant V as Vega
    participant P as World plugin scope
    participant R as WorldRuntime
    participant H as TerraRuntime host

    V->>P: retire scope
    P->>P: revoke hooks/commands/timers
    V->>R: stop sandbox
    R->>R: finish authoritative teardown
    R-->>H: resources retired
    H-->>V: sandbox destroyed
```

Ephemeral state исчезает вместе с runtime. Persistent worlds следуют своей явной persistence policy.

## Чего Level 1 не даёт

Level 1 даёт state/lifecycle isolation, но не security isolation от враждебного кода. Crash, `Environment.FailFast`, unsafe native failure или process-wide OOM внутри in-process кода всё ещё способны уронить весь сервер. Для реальной process boundary используется Level 2.
