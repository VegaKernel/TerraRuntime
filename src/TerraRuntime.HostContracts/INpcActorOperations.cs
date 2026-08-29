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

/// <summary>
/// Trusted-host control surface for runtime-owned NPC actors. Every operation is serialized through the authoritative
/// game loop. Controllers express intent only; final motion, gravity and world collision remain TerraRuntime-owned.
/// </summary>
public interface INpcActorOperations
{
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
