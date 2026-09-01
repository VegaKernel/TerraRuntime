# Level 2: dedicated-process sandbox

[Обзор](README.md) · [Передача socket](socket-handoff.md) · [English](../../en/sandbox/level-2.md)

Level 2 запускает один sandbox-мир в отдельном worker process для fault и resource isolation.

## Состав worker

```mermaid
flowchart TD
    Main["Main TerraRuntime + Vega"] --> Supervisor["SandboxSupervisor"]
    Supervisor --> Transport["TerraRuntime.Transport"]
    Transport --> Worker["Sandbox worker process"]
    Worker --> Runtime["WorldRuntime"]
    Worker --> LocalHost["sandbox-local host logic"]
    LocalHost --> Plugins["selected modules/plugins"]
    Plugins --> Runtime
```

Первая реализация должна использовать один sandbox world на worker. Несколько worlds в одном worker являются только будущей optimization после измерений.

## Что передаёт Vega

Создание декларативное. Концептуально запрос содержит:

- isolation requirement;
- world source (`.wld`, validated generated state или clone/snapshot);
- selected sandbox-side modules/plugins;
- configuration plugin/module;
- player/resource limits;
- lifecycle policy, например ephemeral/persistent.

Worker нельзя помечать `RuntimeReady`, пока обязательный world data и обязательная local logic успешно не загружены.

## Profile для dynamic plugin loading

Worker, который динамически загружает выбранные managed Vega/plugin assemblies, требует CoreCLR extensible profile, потому что arbitrary managed DLL loading не входит в NativeAOT runtime-only contract.

Worker без dynamic managed modules может использовать NativeAOT runtime-only profile. Нельзя ослаблять NativeAOT constraints всего core graph только ради упрощения Level 2 plugin loading.

## Последовательность запуска

```mermaid
sequenceDiagram
    participant V as Vega
    participant S as SandboxSupervisor
    participant T as TerraRuntime.Transport
    participant W as Worker

    V->>S: create dedicated sandbox descriptor
    S->>W: start process
    S->>T: establish bounded/versioned session
    T->>W: world source + module identities + config + limits
    W->>W: materialize WorldRuntime
    W->>W: load/attach selected local logic
    W-->>T: RuntimeReady(runtime identity)
    T-->>S: ready
    S-->>V: sandbox ready for player transfer
```

## Решение по data plane

После передачи accepted TCP connection игрока worker обычный Terraria gameplay traffic идёт напрямую между client и worker.

```mermaid
flowchart LR
    Client["Terraria client"] <-->|"same TCP connection"| Worker["Sandbox worker"]
    Main["Main TerraRuntime"] <-->|"control/state"| Transport["TerraRuntime.Transport"]
    Transport <-->|"control/state"| Worker
```

Это убирает необходимость decode/encode или proxy каждого movement/combat packet через main process.

## Fault model

```mermaid
stateDiagram-v2
    [*] --> Starting
    Starting --> Ready: world + local logic attached
    Ready --> Running: player admitted
    Running --> Stopping: normal teardown
    Running --> Faulted: worker crash / liveness failure
    Faulted --> Cleanup
    Stopping --> Cleanup
    Cleanup --> [*]
```

Crash worker может уничтожить sandbox-local gameplay, но не должен напрямую завершать main TerraRuntime process. `SandboxSupervisor` отвечает за detection, cleanup и детерминированную обработку affected connections.

Если ownership переданного socket потеряна так, что безопасный handback доказать нельзя, disconnect безопаснее попытки угадать, какой процесс всё ещё владеет connection.
