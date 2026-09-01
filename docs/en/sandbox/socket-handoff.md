# Level 2 TCP socket handoff

[Overview](README.md) · [Level 2](level-2.md) · [Русский](../../ru/sandbox/socket-handoff.md)

The Level 2 local-host design preserves the player's existing Terraria TCP connection while moving connection ownership between the main process and the sandbox worker.

## Separation of state and socket ownership

Two things move during transfer:

1. **semantic player/runtime state** through bounded, versioned `TerraRuntime.Transport` messages;
2. **the live accepted socket/descriptor** through an OS-specific socket-handoff mechanism.

The socket is not serialized into a Transport payload.

```mermaid
flowchart LR
    MainState["Main player state"] -->|"semantic transfer"| Transport["TerraRuntime.Transport"]
    Transport --> WorkerState["Worker player state"]
    MainSocket["accepted TCP socket"] -->|"OS handoff"| WorkerSocket["worker socket ownership"]
```

## Safe-point requirement

Kernel receive/send queues move with the socket ownership mechanism, but process-local bytes do not. If a process has already consumed bytes into a frame decoder, `PipeReader`, `SequenceReader` backing buffer or another local queue, those bytes cannot magically appear in the destination process.

Therefore ownership may commit only at a safe point with:

- no partial Terraria frame in the source decoder;
- no untransferred process-local receive bytes;
- no concurrently executing read callback;
- pending writes flushed, completed or explicitly retired according to the connection pipeline contract;
- a prepared transferable player-state snapshot/command set;
- destination runtime ready to accept the connection.

## Entering the sandbox

```mermaid
sequenceDiagram
    participant C as Client
    participant M as Main connection owner
    participant T as Transport
    participant S as SandboxSupervisor
    participant W as Worker

    C->>M: normal Terraria traffic
    M->>M: request transfer + stop admission of new reads
    M->>M: finish current frame / reach safe point
    M->>T: transferable player state
    T->>W: validate/prepare destination membership
    W-->>T: destination prepared
    M->>S: begin socket handoff
    S->>W: platform socket descriptor/duplication data
    W-->>S: ownership accepted
    S-->>M: commit handoff
    M->>M: retire local socket ownership
    C->>W: same TCP connection continues
```

The source must not resume reads after the ownership commit.

## Returning to main

```mermaid
sequenceDiagram
    participant C as Client
    participant W as Worker connection owner
    participant T as Transport
    participant S as SandboxSupervisor
    participant M as Main

    C->>W: normal sandbox traffic
    W->>W: reach transfer safe point
    W->>T: transferable player state
    T->>M: prepare destination world membership
    M-->>T: destination prepared
    W->>S: begin socket handback
    S->>M: platform socket descriptor/duplication data
    M-->>S: ownership accepted
    S-->>W: commit handback
    W->>W: retire local ownership
    C->>M: same TCP connection continues
```

## Ownership state machine

```mermaid
stateDiagram-v2
    [*] --> MainOwned
    MainOwned --> ToWorkerPrepared: safe point + destination ready
    ToWorkerPrepared --> WorkerOwned: worker ACK + commit
    ToWorkerPrepared --> MainOwned: cancel/timeout before commit
    WorkerOwned --> ToMainPrepared: safe point + destination ready
    ToMainPrepared --> MainOwned: main ACK + commit
    ToMainPrepared --> WorkerOwned: cancel/timeout before commit
    MainOwned --> Disconnected: unrecoverable failure
    WorkerOwned --> Disconnected: worker/ownership failure
    Disconnected --> [*]
```

There is no `BothOwned` state.

## Platform paths

### Windows

Use verified Winsock socket duplication/handoff semantics, such as `WSADuplicateSocket` plus the supported .NET reconstruction path. The exact implementation must be validated on the shipping .NET 11/Windows target before the roadmap item can be checked.

### Unix/Linux

Use file-descriptor passing over a local Unix-domain control channel with `SCM_RIGHTS` or another verified equivalent. Descriptor passing is an ancillary-data operation, not normal serialized Transport payload data.

## Failure rule

If ownership cannot be proven after a process crash or ambiguous platform failure, fail closed. Disconnecting one sandbox player is preferable to two processes concurrently consuming one TCP byte stream or corrupting protocol state.
