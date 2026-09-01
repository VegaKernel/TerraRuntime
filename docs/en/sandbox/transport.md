# Transport and sandbox control plane

[Overview](README.md) · [Русский](../../ru/sandbox/transport.md)

`TerraRuntime.Transport` is retained as a first-class boundary. Sandbox support gives it two explicit uses rather than turning it into a generic gameplay bus.

## Two topologies

```mermaid
flowchart TD
    Vega["Vega"] --> T1["TerraRuntime.Transport"]
    T1 --> ServerA["TerraRuntime server A"]
    T1 --> ServerB["TerraRuntime server B"]

    Main["Main TerraRuntime"] --> Supervisor["SandboxSupervisor"]
    Supervisor --> T2["TerraRuntime.Transport"]
    T2 --> Worker["Level 2 sandbox worker"]
```

The first topology supports Vega communication with multiple TerraRuntime servers. The second supports local sandbox worker supervision and semantic transfer.

## What Transport carries for Level 2

```mermaid
flowchart LR
    Lifecycle["create/start/stop"] --> T["Transport"]
    Heartbeat["heartbeat/liveness"] --> T
    Config["world/module config"] --> T
    State["player/runtime semantic state"] --> T
    Admin["admin/status/faults/metrics"] --> T
    T --> Worker["Sandbox worker"]
```

Transport should carry bounded semantic messages such as:

- worker handshake/capabilities;
- sandbox creation descriptor;
- runtime ready/faulted/stopping status;
- heartbeat and liveness information;
- bounded player-state transfer payloads;
- administrative lifecycle operations;
- logs/metrics summaries where appropriate.

## What Transport does not carry

For a local Level 2 sandbox, after socket handoff it does **not** permanently carry every Terraria movement/combat/world packet between client and worker.

```mermaid
flowchart LR
    Client["Terraria client"] <-->|"gameplay TCP"| Worker["Sandbox worker"]
    Main["Main TerraRuntime"] <-->|"control/state only"| T["TerraRuntime.Transport"]
    T <-->|"control/state only"| Worker
```

It also does not own Vega plugin permissions, gameplay mutation semantics, world business logic or a universal RPC object model.

## Service layering

```mermaid
flowchart TD
    Plugin["Vega plugin"] --> SDK["Vega.PluginSdk semantic API"]
    SDK --> Policy["Vega policy/capabilities"]
    Policy --> Service["server/sandbox control service"]
    Service --> Transport["TerraRuntime.Transport"]
    Transport --> Runtime["TerraRuntime endpoint"]
```

Ordinary plugins should not receive unrestricted raw Transport sessions merely because they need a cross-server or sandbox operation.

## Bounds and versioning

Every message family must have explicit size/count limits. Pending request counts, timeouts, queue depths and heartbeat state are bounded. Service versions/capabilities are negotiated rather than inferred from process version strings or socket addresses.

Socket handoff metadata is platform-specific and may be coordinated through the Transport session, but the live kernel socket itself is transferred through the verified platform mechanism described in [socket-handoff.md](socket-handoff.md).
