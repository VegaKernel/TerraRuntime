# Передача TCP socket в Level 2

[Обзор](README.md) · [Level 2](level-2.md) · [English](../../en/sandbox/socket-handoff.md)

Локальный Level 2 сохраняет существующее Terraria TCP connection игрока, перемещая ownership connection между main process и sandbox worker.

## Разделение state и socket ownership

Во время transfer перемещаются две разные вещи:

1. **semantic player/runtime state** через bounded/versioned сообщения `TerraRuntime.Transport`;
2. **живой accepted socket/descriptor** через OS-specific socket-handoff mechanism.

Socket не сериализуется в Transport payload.

```mermaid
flowchart LR
    MainState["Main player state"] -->|"semantic transfer"| Transport["TerraRuntime.Transport"]
    Transport --> WorkerState["Worker player state"]
    MainSocket["accepted TCP socket"] -->|"OS handoff"| WorkerSocket["worker socket ownership"]
```

## Требование safe point

Kernel receive/send queues следуют механизму socket ownership, но process-local bytes не переезжают. Если process уже считал bytes в frame decoder, `PipeReader`, backing buffer `SequenceReader` или локальную queue, эти bytes не появятся в destination process сами собой.

Поэтому ownership commit разрешён только в safe point, где:

- в source decoder нет partial Terraria frame;
- нет непереданных process-local receive bytes;
- нет одновременно выполняющегося read callback;
- pending writes flushed/completed или явно retired согласно connection pipeline contract;
- подготовлен transferable player-state snapshot/command set;
- destination runtime готов принять connection.

## Вход в sandbox

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

После ownership commit source не должен возобновлять чтение.

## Возврат в main

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

## State machine ownership

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

Состояния `BothOwned` не существует.

## Platform paths

### Windows

Используются проверенные Winsock socket duplication/handoff semantics, например `WSADuplicateSocket` плюс поддерживаемый .NET reconstruction path. Конкретная реализация должна быть проверена на shipping .NET 11/Windows target до закрытия roadmap item.

### Unix/Linux

Используется file-descriptor passing через локальный Unix-domain control channel с `SCM_RIGHTS` или другим проверенным equivalent. Descriptor passing является ancillary-data operation, а не обычными serialized Transport payload data.

## Правило отказа

Если после crash process или неоднозначного platform failure ownership доказать нельзя, система fail closed. Отключить одного sandbox-player безопаснее, чем позволить двум process одновременно читать один TCP byte stream и ломать protocol state.
