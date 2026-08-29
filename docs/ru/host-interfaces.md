# Host-интерфейсы TerraRuntime

[English](../en/host-interfaces.md) · [Документация](README.md) · [Архитектура](architecture.md) · [Руководство](project-guide.md)

Этот документ описывает публичную интеграционную поверхность trusted host module в CoreCLR-профиле. Он не является каталогом всех внутренних `public` типов TerraRuntime. Каноническая внешняя граница для Vega и других доверенных hosts — `TerraRuntime.HostContracts` плюс специально вынесенные contracts из `TerraRuntime.Contracts`.

## 1. Модель доверия

Trusted host module привилегированнее обычного Vega plugin, но не становится совладельцем внутреннего runtime state.

Главное правило:

> Host получает snapshots, semantic operations и registration surfaces. Он не получает mutable stores, game-loop object, socket connection objects или прямую запись в authoritative fields.

Обычные плагины должны работать через Vega/его Plugin SDK. `TerraRuntime.HostContracts` не предназначен для массовой выдачи каждому plugin DLL.

## 2. Lifecycle trusted host module

Основной контракт:

```csharp
public interface ITerraRuntimeHostModule
{
    string Name { get; }

    ValueTask StartAsync(
        ITerraRuntimeHostEnvironment environment,
        CancellationToken cancellationToken = default);

    ValueTask AttachRuntimeAsync(
        ITerraRuntimeHostRuntime runtime,
        CancellationToken cancellationToken = default);

    ValueTask DetachRuntimeAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}
```

Lifecycle:

```text
module load
   |
   v
StartAsync(environment)
   |
   |  регистрация bootstrap-only ресурсов
   |  live world ещё может отсутствовать
   v
TerraRuntime starts world/game loop
   |
   v
AttachRuntimeAsync(runtime)
   |
   |  runtime snapshots/operations доступны
   v
normal operation
   |
   v
DetachRuntimeAsync()
   |
   |  прекратить live-runtime работу
   v
StopAsync()
```

### Правила lifecycle

- `StartAsync` не должен предполагать наличие live world.
- Registration handles/resources, принадлежащие модулю, снимаются до unload.
- После `DetachRuntimeAsync` нельзя продолжать отправлять runtime operations через сохранённые ссылки как будто world всё ещё attached.
- `StopAsync` освобождает host-owned resources.
- Cancellation token нужно уважать; lifecycle не должен зависать навечно.

## 3. `ITerraRuntimeHostEnvironment`

Bootstrap environment доступен в `StartAsync`.

```csharp
public interface ITerraRuntimeHostEnvironment
{
    string RootDirectory { get; }
    string HostModulesDirectory { get; }
    string ServerPluginsDirectory { get; }
    string WorldsDirectory { get; }
    string ConfigDirectory { get; }
    string DataDirectory { get; }
    string LogsDirectory { get; }

    ITerraRuntimeTerminalDashboardRegistry TerminalDashboards { get; }
    ITerraRuntimeWorldGeneratorRegistry WorldGenerators { get; }
}
```

### Для чего использовать

- вычислять пути к host-owned config/data/log files;
- регистрировать независимый TUI dashboard;
- регистрировать selectable world generator.

### Для чего не использовать

- искать TerraRuntime internal assemblies и рефлексией доставать implementation types;
- считать deploy path заменой runtime API;
- напрямую читать/писать `.wld` в обход runtime persistence boundary, если действие относится к запущенному миру.

## 4. `ITerraRuntimeHostRuntime`

Live runtime surface прикрепляется после запуска authoritative runtime.

```csharp
public interface ITerraRuntimeHostRuntime
{
    TerraRuntimeHostRuntimeInfo Info { get; }
    IInterestManagementControl InterestManagement { get; }
    IPlayerStateSnapshotReader PlayerStates { get; }
    INpcActorOperations NpcActors { get; }
    IServerPlayerOperations ServerPlayers { get; }
}
```

Это composition surface. Каждый дочерний contract отвечает за конкретную семантику.

## 5. Чтение player state

`IPlayerStateSnapshotReader` возвращает immutable snapshot по generation-safe `PlayerHandle`.

```csharp
PlayerStateSnapshot? snapshot = await runtime.PlayerStates.CaptureAsync(
    playerHandle,
    cancellationToken);

if (snapshot is null)
{
    // Игрок уже отсутствует, handle устарел или state недоступен.
    return;
}

// Читаем snapshot. Не пытаемся мутировать authoritative player state.
```

Почему API async: запрос может быть сериализован через runtime-owned boundary. Host не должен рассчитывать, что чтение означает прямой доступ к dictionary/array на текущем thread.

## 6. Interest management

`IInterestManagementControl` специально узкий:

```csharp
bool currentlyEnabled = runtime.InterestManagement.IsEnabled;
bool changed = runtime.InterestManagement.SetEnabled(true);
```

Host управляет только включением механизма.

Host **не управляет**:

- размером spatial cells/sections;
- enter/leave radii;
- hysteresis;
- entity visibility rules;
- forced resync;
- packet-specific routing.

Эти политики принадлежат TerraRuntime, чтобы Vega не становилась вторым сетевым runtime поверх первого.

## 7. Runtime-owned NPC actors

`INpcActorOperations` позволяет trusted host взять semantic control над поддерживаемым NPC actor.

```csharp
NpcActorAcquireStatus status = await runtime.NpcActors.AcquireAsync(
    npc,
    controllerId,
    cancellationToken);

if (status != NpcActorAcquireStatus.Acquired)
    return;

await runtime.NpcActors.SetIntentAsync(
    npc,
    controllerId,
    intent,
    cancellationToken);
```

Host задаёт **intent**, а не финальные velocity/position values. TerraRuntime сохраняет ownership:

- gravity;
- collision;
- final motion;
- lifecycle;
- replication.

Освобождение:

```csharp
await runtime.NpcActors.ReleaseAsync(npc, controllerId, cancellationToken);
```

При unload controller/module обязательно освобождает все leases:

```csharp
int released = await runtime.NpcActors.ReleaseControllerAsync(
    controllerId,
    cancellationToken);
```

### `NpcActorAcquireStatus`

- `Acquired` — lease получен;
- `InvalidActor` — handle не относится к valid live actor;
- `InvalidController` — controller identity некорректен;
- `UnsupportedNpcType` — этот NPC type пока не поддерживает actor control;
- `AlreadyControlled` — actor уже контролируется;
- `QueueRejected` — authoritative command boundary не приняла работу.

`QueueRejected` нельзя трактовать как «ну всё равно применилось». Операция не подтверждена.

## 8. Connection-free runtime-owned players

`IServerPlayerOperations` создаёт runtime-owned player actor без network connection, используя обычный Terraria player slot pool.

Создание:

```csharp
ServerPlayerCreateResult result = await runtime.ServerPlayers.CreateAsync(
    serverPlayerId,
    positionX,
    positionY,
    cancellationToken);

if (!result.IsCreated)
    return;

PlayerHandle handle = result.Player;
```

Host не получает прямой setter позиции/скорости после создания. Управление выражается semantic intent:

```csharp
bool accepted = await runtime.ServerPlayers.SetHorizontalIntentAsync(
    serverPlayerId,
    ServerPlayerHorizontalIntent.Right,
    cancellationToken);

bool jumping = await runtime.ServerPlayers.SetJumpIntentAsync(
    serverPlayerId,
    ServerPlayerJumpIntent.Held,
    cancellationToken);

await runtime.ServerPlayers.SetJumpIntentAsync(
    serverPlayerId,
    ServerPlayerJumpIntent.Released,
    cancellationToken);
```

`ServerPlayerJumpIntent` — это semantic состояние кнопки, а не команда записи скорости. TerraRuntime сам владеет
vanilla jump speed, счётчиком длительности прыжка, release gate, gravity и collision. Поэтому удерживание jump после
приземления не запускает новый прыжок, пока `Released` снова не взведёт vanilla release gate. Текущий source-backed
slice покрывает dry/unmounted/normal-gravity path; liquids, mounts, grapples и extra-jump families идут отдельно.

Удаление:

```csharp
await runtime.ServerPlayers.DespawnAsync(serverPlayerId, cancellationToken);
```

### `ServerPlayerCreateStatus`

- `Created`;
- `InvalidId`;
- `InvalidPosition`;
- `AlreadyExists`;
- `NoAvailableSlot`;
- `QueueRejected`.

Созданный server player использует generation-safe runtime player identity. Host не должен хранить slot number как вечный идентификатор.

## 9. Terminal dashboard registration

Bootstrap surface:

```csharp
public interface ITerraRuntimeTerminalDashboardProvider
{
    string Id { get; }
    string Title { get; }
    View CreateDashboard();
    void Refresh(View rootView);
}
```

Регистрация:

```csharp
bool registered = environment.TerminalDashboards.TryRegister(provider);
```

Удаление:

```csharp
environment.TerminalDashboards.TryUnregister(provider.Id);
```

### Threading contract

`CreateDashboard()` и `Refresh(...)` вызываются на Terminal.Gui UI thread.

Provider создаёт **целый независимый dashboard root**. Он не внедряет controls во внутренний built-in dashboard TerraRuntime.

Provider не должен использовать UI callbacks для прямой мутации gameplay state. Для mutations применяются runtime operations/host-layer commands.

## 10. World generator registration

Bootstrap registry:

```csharp
TerraRuntimeWorldGeneratorRegistrationResult result =
    environment.WorldGenerators.TryRegister(provider, out var registration);
```

Возможные результаты:

- `Registered`;
- `DuplicateId`;
- `InvalidProvider`.

Успешная регистрация возвращает lifetime handle:

```csharp
ITerraRuntimeWorldGeneratorRegistration registration
```

У него есть `Id` и `IsRetired`; `Dispose()` снимает registration. Перед unload assembly/provider registration должна быть retired.

### Ownership worldgen

Host отвечает за:

- discovery собственного provider;
- lifetime provider;
- регистрацию уникального `WorldGeneratorId`;
- реализацию pass logic через worldgen contracts.

TerraRuntime отвечает за:

- выбор зарегистрированного provider;
- validation plan;
- isolated workspace;
- execution boundary;
- final world acceptance;
- cancellation/error containment.

Не нужно сканировать assemblies из TerraRuntime: explicit registration сделана специально вместо reflection discovery.

## 11. `ITerraRuntimeHostLifecycle`

Этот optional bridge позволяет extensible host прикрепить свои loaded modules к live runtime:

```csharp
public interface ITerraRuntimeHostLifecycle
{
    ValueTask AttachRuntimeAsync(
        ITerraRuntimeHostRuntime runtime,
        CancellationToken cancellationToken = default);

    ValueTask DetachRuntimeAsync(CancellationToken cancellationToken = default);
}
```

Standalone NativeAOT host обычно не предоставляет такой lifecycle implementation.

## 12. Pattern реализации host module

Минимальный skeleton:

```csharp
public sealed class ExampleHostModule : ITerraRuntimeHostModule
{
    private ITerraRuntimeHostEnvironment? environment;
    private ITerraRuntimeHostRuntime? runtime;

    public string Name => "Example";

    public ValueTask StartAsync(
        ITerraRuntimeHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        this.environment = environment;
        return ValueTask.CompletedTask;
    }

    public ValueTask AttachRuntimeAsync(
        ITerraRuntimeHostRuntime runtime,
        CancellationToken cancellationToken = default)
    {
        this.runtime = runtime;
        return ValueTask.CompletedTask;
    }

    public ValueTask DetachRuntimeAsync(CancellationToken cancellationToken = default)
    {
        runtime = null;
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        environment = null;
        return ValueTask.CompletedTask;
    }
}
```

В реальном module перед `StopAsync` также снимаются dashboard/worldgen registrations и освобождаются actor controller leases.

## 13. Что считается нарушением boundary

Не допускается host integration, которая:

- использует reflection для поиска private/internal runtime state;
- пишет непосредственно в NPC/player/world stores;
- вызывает gameplay mutations с TUI thread в обход authoritative command boundary;
- хранит slot index как permanent identity при наличии generation-safe handle;
- делает blocking wait (`.Result`, `.Wait()`) на runtime operation внутри чувствительного host callback;
- продолжает использовать runtime contracts после detach как live world;
- оставляет registration/controller leases после unload.

## 14. Версионирование документации интерфейсов

Любое изменение signature, status enum, lifecycle ordering, threading semantics или ownership guarantee этих contracts требует одновременного изменения:

- XML documentation в исходнике, если contract требует локального пояснения;
- этого файла в `docs/ru/`;
- зеркального `docs/en/host-interfaces.md`;
- `architecture.md`, если изменилась системная граница;
- roadmap, если изменение меняет фактическую готовность/план.
