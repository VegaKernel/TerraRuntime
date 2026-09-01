using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.HostContracts;

/// <summary>
/// Ordering lane for behavior registered against one vanilla presentation type. Archetype-addressed behavior is
/// always an exclusive replacement selected by NpcArchetypeDescriptor.BehaviorId.
/// </summary>
public enum NpcBehaviorStage : byte
{
    Pre = 0,
    Replacement = 1,
    Post = 2
}

public enum NpcBehaviorRegistrationStatus : byte
{
    Registered = 0,
    InvalidId = 1,
    InvalidTarget = 2,
    InvalidStage = 3,
    InvalidProvider = 4,
    DuplicateId = 5,
    ReplacementConflict = 6,
    QueueRejected = 7,
    RuntimeDetached = 8
}

/// <summary>Lifetime handle for one runtime NPC behavior registration.</summary>
public interface INpcBehaviorRegistration : IDisposable
{
    GameplayExtensionId Id { get; }
    bool IsRetirementPending { get; }
    bool IsRetired { get; }
}

public readonly record struct NpcBehaviorRegistrationResult(
    NpcBehaviorRegistrationStatus Status,
    INpcBehaviorRegistration? Registration)
{
    public bool IsRegistered =>
        Status == NpcBehaviorRegistrationStatus.Registered && Registration is not null;
}

/// <summary>Finite axis-aligned entity rectangle used by bounded runtime collision queries.</summary>
public readonly record struct NpcBehaviorBounds(
    float PositionX,
    float PositionY,
    int Width,
    int Height)
{
    public bool IsValid =>
        float.IsFinite(PositionX) &&
        float.IsFinite(PositionY) &&
        Width > 0 &&
        Height > 0;
}

/// <summary>
/// Read-only query boundary available while an NPC behavior callback runs on the authoritative thread. It exposes
/// generation-safe snapshots and bounded world queries, never mutable entity arrays, packet queues or tile storage.
/// </summary>
public interface INpcBehaviorQueries
{
    long Tick { get; }

    bool TryGetPlayer(PlayerHandle player, out PlayerStateSnapshot snapshot);

    bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot);

    bool TryGetNpc(NpcHandle npc, out NpcSnapshot snapshot);

    int CopyNpcs(Span<NpcSnapshot> destination);

    bool HasSolidCollision(in NpcBehaviorBounds bounds);

    bool HasLineOfSight(in NpcBehaviorBounds source, in NpcBehaviorBounds target);
}

/// <summary>
/// Mutable portion of one authoritative NPC state transition. Type and NetId are intentionally absent: behavior may
/// change server-owned simulation state, but it cannot change the vanilla presentation selected by TerraRuntime.
/// </summary>
public readonly record struct NpcBehaviorState(
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    ushort Target,
    NpcAiState Ai,
    NpcSimulationState Simulation)
{
    public static NpcBehaviorState FromSnapshot(in NpcSnapshot npc) =>
        new(
            npc.PositionX,
            npc.PositionY,
            npc.VelocityX,
            npc.VelocityY,
            npc.Target,
            npc.Ai,
            npc.Simulation);

    public bool IsValid =>
        float.IsFinite(PositionX) &&
        float.IsFinite(PositionY) &&
        float.IsFinite(VelocityX) &&
        float.IsFinite(VelocityY) &&
        Ai.IsFinite &&
        Simulation.IsValid;
}

/// <summary>
/// Stack-only callback context. ArchetypeId is assigned only when the exact live NPC generation is bound to a
/// server-defined archetype; ordinary vanilla NPCs expose the default/unassigned value.
/// </summary>
public readonly ref struct NpcBehaviorContext
{
    private readonly INpcBehaviorQueries queries;

    public NpcBehaviorContext(
        GameplayExtensionId behaviorId,
        GameplayArchetypeId archetypeId,
        in NpcSnapshot npc,
        INpcBehaviorQueries queries)
    {
        if (!behaviorId.IsAssigned)
            throw new ArgumentException("NPC behavior context requires an assigned behavior ID.", nameof(behaviorId));
        ArgumentNullException.ThrowIfNull(queries);

        BehaviorId = behaviorId;
        ArchetypeId = archetypeId;
        Npc = npc;
        this.queries = queries;
    }

    public GameplayExtensionId BehaviorId { get; }

    public GameplayArchetypeId ArchetypeId { get; }

    public NpcSnapshot Npc { get; }

    public long Tick => queries.Tick;

    public bool TryGetPlayer(PlayerHandle player, out PlayerStateSnapshot snapshot) =>
        queries.TryGetPlayer(player, out snapshot);

    public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot) =>
        queries.TryGetPlayer(slot, out snapshot);

    public bool TryGetNpc(NpcHandle npc, out NpcSnapshot snapshot) =>
        queries.TryGetNpc(npc, out snapshot);

    public int CopyNpcs(Span<NpcSnapshot> destination) =>
        queries.CopyNpcs(destination);

    public bool HasSolidCollision(in NpcBehaviorBounds bounds) =>
        queries.HasSolidCollision(in bounds);

    public bool HasLineOfSight(in NpcBehaviorBounds source, in NpcBehaviorBounds target) =>
        queries.HasLineOfSight(in source, in target);
}

/// <summary>
/// Synchronous trusted-host NPC behavior. Returning false means no state proposal for this stage. Implementations
/// must not block, perform I/O, sleep, wait on tasks or retain callback context. TerraRuntime validates the resulting
/// state and remains the only authority that commits it.
/// </summary>
public interface INpcBehaviorProvider
{
    bool TryStep(in NpcBehaviorContext context, out NpcBehaviorState next);
}
