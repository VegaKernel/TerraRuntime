# Level 1: in-process sandbox

[Overview](README.md) · [Русский](../../ru/sandbox/level-1.md)

Level 1 runs a sandbox as another independent `WorldRuntime` inside the same TerraRuntime process.

## Ownership model

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

The process may own several worlds, but mutable simulation ownership remains per runtime. A runtime owns its players, NPCs, projectiles, items, world clock/progression, RNG streams, extension state, replication state and persistence policy.

No world may mutate another world through shared global state.

## Creation lifecycle

```mermaid
sequenceDiagram
    participant V as Vega
    participant H as TerraRuntime host
    participant W as World source
    participant R as WorldRuntime
    participant P as Vega world scope

    V->>H: CreateSandbox(InProcess, source, policy)
    H->>W: load/generate/clone and validate
    H->>R: create runtime + new runtime/session identity
    H->>R: start authoritative execution
    H-->>V: runtime available
    V->>P: attach selected world-local logic
    P-->>V: hooks/commands ready
```

A real implementation may attach host logic before admitting players, but it must not expose a sandbox as ready until the required world/plugin scope is usable.

## Plugin and command scope

Level 1 does not need another Vega process. The currently loaded Vega plugin can receive a separate scope for each world it participates in.

```mermaid
flowchart LR
    Plugin["CTF plugin instance"] --> Scope1["World scope: Arena 1"]
    Plugin --> Scope2["World scope: Arena 2"]
    Scope1 --> Hooks1["hooks / commands / match state"]
    Scope2 --> Hooks2["hooks / commands / match state"]
```

Registrations must be world-scoped and revocable. A plugin must not add a global `/team` command or global player-death handler merely because one arena needs it.

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

Ephemeral state must disappear with the runtime. Persistent worlds follow their explicit persistence policy.

## What Level 1 does not provide

Level 1 provides state/lifecycle isolation, not hostile-code security. A crash, `Environment.FailFast`, unsafe native failure or process-wide OOM in in-process code can still terminate the whole server process. Use Level 2 when a real process boundary is required.
