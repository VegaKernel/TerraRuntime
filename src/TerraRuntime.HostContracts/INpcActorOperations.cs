using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.HostContracts;

public enum NpcActorAcquireStatus : byte
{
    Acquired = 0,
    InvalidActor = 1,
    InvalidController = 2,
    UnsupportedNpcType = 3,
    AlreadyControlled = 4,
    QueueRejected = 5
}

public enum NpcArchetypeRegistrationStatus : byte
{
    Registered = 0,
    InvalidDescriptor = 1,
    DuplicateId = 2,
    RuntimeDetached = 3
}

public interface INpcArchetypeRegistration : IDisposable
{
    GameplayArchetypeId Id { get; }
}

public readonly record struct NpcActorSpawnRequest(
    GameplayArchetypeId ArchetypeId,
    float PositionX,
    float PositionY)
{
    public bool IsValid =>
        ArchetypeId.IsAssigned &&
        float.IsFinite(PositionX) &&
        float.IsFinite(PositionY);
}

public enum NpcActorSpawnStatus : byte
{
    Spawned = 0,
    InvalidRequest = 1,
    ArchetypeNotFound = 2,
    NoAvailableSlot = 3,
    QueueRejected = 4
}

public readonly record struct NpcActorSpawnResult(NpcActorSpawnStatus Status, NpcHandle Npc)
{
    public bool IsSpawned => Status == NpcActorSpawnStatus.Spawned && Npc.IsAssigned;
}

/// <summary>
/// Trusted-host control surface for runtime-owned NPC actors. Every operation is serialized through the authoritative
/// game loop. Controllers express intent only; final motion, gravity and world collision remain TerraRuntime-owned.
/// </summary>
public interface INpcActorOperations
{
    NpcArchetypeRegistrationStatus TryRegisterArchetype(
        NpcArchetypeDescriptor descriptor,
        out INpcArchetypeRegistration? registration);

    ValueTask<NpcActorSpawnResult> SpawnAsync(
        NpcActorSpawnRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<bool> DespawnAsync(
        NpcHandle npc,
        CancellationToken cancellationToken = default);

    ValueTask<NpcActorAcquireStatus> AcquireAsync(
        NpcHandle npc,
        ActorControllerId controllerId,
        CancellationToken cancellationToken = default);

    ValueTask<bool> SetIntentAsync(
        NpcHandle npc,
        ActorControllerId controllerId,
        NpcActorIntent intent,
        CancellationToken cancellationToken = default);

    ValueTask<bool> ReleaseAsync(
        NpcHandle npc,
        ActorControllerId controllerId,
        CancellationToken cancellationToken = default);

    /// <summary>Releases every live lease owned by one controller identity, intended for host/plugin unload.</summary>
    ValueTask<int> ReleaseControllerAsync(
        ActorControllerId controllerId,
        CancellationToken cancellationToken = default);
}
