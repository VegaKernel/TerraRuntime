# TerraRuntime host interfaces

[Русский](../ru/host-interfaces.md) · [Documentation](README.md) · [Architecture](architecture.md) · [Project guide](project-guide.md)

This document describes the public integration surface for a trusted host module in the CoreCLR profile. It is not a catalog of every internal `public` TerraRuntime type. The canonical external boundary for Vega and other trusted hosts is `TerraRuntime.HostContracts` plus deliberately exposed contracts from `TerraRuntime.Contracts`.

## 1. Trust model

A trusted host module is more privileged than an ordinary Vega plugin, but it does not become a co-owner of internal runtime state.

The core rule is:

> A host receives snapshots, semantic operations, and registration surfaces. It does not receive mutable stores, the game-loop object, socket connection objects, or direct setters for authoritative fields.

Ordinary plugins should use Vega and its Plugin SDK. `TerraRuntime.HostContracts` is not intended to be handed to every plugin DLL.

## 2. Trusted host module lifecycle

Primary contract:

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
   |  register bootstrap-only resources
   |  a live world may not exist yet
   v
TerraRuntime starts world/game loop
   |
   v
AttachRuntimeAsync(runtime)
   |
   |  runtime snapshots/operations are available
   v
normal operation
   |
   v
DetachRuntimeAsync()
   |
   |  stop live-runtime work
   v
StopAsync()
```

### Lifecycle rules

- `StartAsync` must not assume a live world exists.
- Registration handles/resources owned by the module are retired before unload.
- After `DetachRuntimeAsync`, retained references must not be used as though the world were still attached.
- `StopAsync` releases host-owned resources.
- Cancellation tokens must be respected; lifecycle calls must not hang indefinitely.

## 3. `ITerraRuntimeHostEnvironment`

The bootstrap environment is available in `StartAsync`.

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

### Intended uses

- resolve host-owned config/data/log paths;
- register an independent TUI dashboard;
- register a selectable world generator.

### Not intended for

- scanning TerraRuntime internal assemblies and reflecting implementation types;
- treating deployment paths as a substitute for runtime API;
- directly rewriting a running world's `.wld` behind the runtime persistence boundary.

## 4. `ITerraRuntimeHostRuntime`

The live runtime surface is attached after the authoritative runtime starts.

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

This is a composition surface. Each child contract owns one semantic area.

## 5. Reading player state

`IPlayerStateSnapshotReader` returns an immutable snapshot for a generation-safe `PlayerHandle`.

```csharp
PlayerStateSnapshot? snapshot = await runtime.PlayerStates.CaptureAsync(
    playerHandle,
    cancellationToken);

if (snapshot is null)
{
    // The player is gone, the handle is stale, or state is unavailable.
    return;
}

// Read the snapshot. Do not mutate authoritative player state.
```

The API is asynchronous because the request may be serialized through a runtime-owned boundary. A host must not assume capture is a direct dictionary/array read on the calling thread.

## 6. Interest management

`IInterestManagementControl` is intentionally narrow:

```csharp
bool currentlyEnabled = runtime.InterestManagement.IsEnabled;
bool changed = runtime.InterestManagement.SetEnabled(true);
```

The host controls only whether the mechanism participates.

The host does **not** control:

- spatial cell/section size;
- enter/leave radii;
- hysteresis;
- entity visibility rules;
- forced resync;
- packet-specific routing.

Those policies belong to TerraRuntime so Vega does not become a second networking runtime layered on top of the first.

## 7. Runtime-owned NPC actors

`INpcActorOperations` allows a trusted host to acquire semantic control of a supported NPC actor.

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

The host supplies **intent**, not final velocity/position values. TerraRuntime retains ownership of:

- gravity;
- collision;
- final motion;
- lifecycle;
- replication.

Release one actor:

```csharp
await runtime.NpcActors.ReleaseAsync(npc, controllerId, cancellationToken);
```

On controller/module unload, release all leases:

```csharp
int released = await runtime.NpcActors.ReleaseControllerAsync(
    controllerId,
    cancellationToken);
```

### `NpcActorAcquireStatus`

- `Acquired` — lease acquired;
- `InvalidActor` — handle does not reference a valid live actor;
- `InvalidController` — controller identity is invalid;
- `UnsupportedNpcType` — actor control is not implemented for that NPC type;
- `AlreadyControlled` — another controller owns the actor;
- `QueueRejected` — the authoritative command boundary did not accept the operation.

`QueueRejected` must not be interpreted as "it probably applied anyway". The operation is not confirmed.

## 8. Connection-free runtime-owned players

`IServerPlayerOperations` creates runtime-owned player actors without a network connection while using the normal Terraria player-slot pool.

Creation:

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

The host does not receive direct position/velocity setters after creation. Control is expressed as semantic intent:

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

`ServerPlayerJumpIntent` is button-level semantic input, not a velocity command. TerraRuntime owns the ordinary vanilla
jump speed, jump-duration counter, release gate, gravity and collision. Holding jump through landing therefore does not
start another jump until a `Released` state has armed the vanilla release gate again. The current source-backed slice is
the dry, unmounted, normal-gravity path; liquid, mount, grapple and extra-jump families are separate gameplay work.

Despawn:

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

A created server player uses generation-safe runtime player identity. The host must not treat the raw slot index as a permanent identifier.

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

Registration:

```csharp
bool registered = environment.TerminalDashboards.TryRegister(provider);
```

Removal:

```csharp
environment.TerminalDashboards.TryUnregister(provider.Id);
```

### Threading contract

`CreateDashboard()` and `Refresh(...)` run on the Terminal.Gui UI thread.

A provider supplies a **complete independent dashboard root**. It does not inject controls into TerraRuntime's built-in system dashboard.

UI callbacks must not mutate gameplay state directly. Mutations go through runtime operations or host-layer commands.

## 10. World-generator registration

Bootstrap registry:

```csharp
TerraRuntimeWorldGeneratorRegistrationResult result =
    environment.WorldGenerators.TryRegister(provider, out var registration);
```

Possible results:

- `Registered`;
- `DuplicateId`;
- `InvalidProvider`.

A successful registration returns a lifetime handle:

```csharp
ITerraRuntimeWorldGeneratorRegistration registration
```

It exposes `Id` and `IsRetired`; `Dispose()` retires the registration. Before unloading the provider assembly/module, retire its registration.

### Worldgen ownership

The host owns:

- discovery of its provider;
- provider lifetime;
- registration of a unique `WorldGeneratorId`;
- pass logic implemented through worldgen contracts.

TerraRuntime owns:

- selection of the registered provider;
- plan validation;
- isolated workspace;
- execution boundary;
- final world acceptance;
- cancellation/error containment.

Do not scan assemblies from TerraRuntime. Explicit registration exists specifically to avoid reflection-driven discovery.

## 11. `ITerraRuntimeHostLifecycle`

This optional bridge lets an extensible host attach its loaded modules to a live runtime:

```csharp
public interface ITerraRuntimeHostLifecycle
{
    ValueTask AttachRuntimeAsync(
        ITerraRuntimeHostRuntime runtime,
        CancellationToken cancellationToken = default);

    ValueTask DetachRuntimeAsync(CancellationToken cancellationToken = default);
}
```

The standalone NativeAOT host normally does not provide this lifecycle implementation.

## 12. Host-module implementation pattern

Minimal skeleton:

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

A real module also retires dashboard/worldgen registrations and releases actor-controller leases before `StopAsync` completes.

## 13. Boundary violations

Host integration must not:

- use reflection to reach private/internal runtime state;
- write directly into NPC/player/world stores;
- perform gameplay mutations from the TUI thread around the authoritative command boundary;
- store slot indexes as permanent identity when generation-safe handles exist;
- block with `.Result`/`.Wait()` on runtime operations inside sensitive host callbacks;
- continue treating runtime contracts as a live world after detach;
- leave registrations/controller leases behind after unload.

## 14. Interface documentation versioning

Any change to a signature, status enum, lifecycle ordering, threading semantics, or ownership guarantee in these contracts requires the same change to update:

- XML documentation in source when the contract needs local explanation;
- this file in `docs/en/`;
- mirrored `docs/ru/host-interfaces.md`;
- `architecture.md` when a system boundary changed;
- the roadmap when readiness or planned behavior changed.
