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
/// Trusted-host control surface for runtime-owned NPC actors. Every mutable operation is serialized through the
/// authoritative game loop. Controllers express intent only; behavior providers propose state transitions only;
/// final validation, motion, collision, lifecycle and replication remain TerraRuntime-owned.
/// </summary>
public interface INpcActorOperations
{
    NpcArchetypeRegistrationStatus TryRegisterArchetype(
        NpcArchetypeDescriptor descriptor,
        out INpcArchetypeRegistration? registration);

    /// <summary>
    /// Registers one stable behavior ID used by NpcArchetypeDescriptor.BehaviorId. This is the archetype-specific
    /// exclusive replacement lane: multiple custom archetypes may share one vanilla presentation while selecting
    /// different behavior IDs.
    /// </summary>
    ValueTask<NpcBehaviorRegistrationResult> RegisterBehaviorAsync(
        GameplayExtensionId id,
        INpcBehaviorProvider provider,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            new NpcBehaviorRegistrationResult(NpcBehaviorRegistrationStatus.RuntimeDetached, null));

    /// <summary>
    /// Registers a behavior stage against one source-backed vanilla presentation type. Pre and Post decorate the
    /// normal/archetype replacement lane; Replacement replaces vanilla behavior when no archetype-specific behavior
    /// is selected for the live NPC generation.
    /// </summary>
    ValueTask<NpcBehaviorRegistrationResult> RegisterPresentationBehaviorAsync(
        GameplayExtensionId id,
        NpcTypeId presentationType,
        NpcBehaviorStage stage,
        int order,
        INpcBehaviorProvider provider,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            new NpcBehaviorRegistrationResult(NpcBehaviorRegistrationStatus.RuntimeDetached, null));

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
