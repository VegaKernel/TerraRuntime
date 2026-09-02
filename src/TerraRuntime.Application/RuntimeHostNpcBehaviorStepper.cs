using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime;

/// <summary>
/// Adapts the public trusted-host behavior contract to the internal state-only NPC AI primitive. Presentation
/// identity is deliberately copied from the current authoritative snapshot instead of being supplied by the host.
/// </summary>
internal sealed class RuntimeHostNpcBehaviorStepper : INpcAiStateStepper
{
    private readonly GameplayExtensionId behaviorId;
    private readonly INpcBehaviorProvider provider;
    private readonly INpcBehaviorQueries queries;
    private readonly RuntimeNpcArchetypeRegistry archetypes;
    private readonly RuntimeNpcArchetypeIdentityStore identities;

    public RuntimeHostNpcBehaviorStepper(
        GameplayExtensionId behaviorId,
        INpcBehaviorProvider provider,
        INpcBehaviorQueries queries,
        RuntimeNpcArchetypeRegistry archetypes,
        RuntimeNpcArchetypeIdentityStore identities)
    {
        if (!behaviorId.IsAssigned)
            throw new ArgumentException("NPC behavior requires an assigned ID.", nameof(behaviorId));
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(archetypes);
        ArgumentNullException.ThrowIfNull(identities);

        this.behaviorId = behaviorId;
        this.provider = provider;
        this.queries = queries;
        this.archetypes = archetypes;
        this.identities = identities;
    }

    public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
    {
        GameplayArchetypeId archetypeId = default;
        if (identities.TryGet(npc.Handle, out GameplayArchetypeId boundId) &&
            archetypes.Snapshot.TryGet(boundId, out _))
        {
            archetypeId = boundId;
        }

        var context = new NpcBehaviorContext(behaviorId, archetypeId, in npc, queries);
        if (!provider.TryStep(in context, out NpcBehaviorState proposed))
        {
            next = default;
            return false;
        }

        next = new NpcStateUpdate(
            npc.Type,
            npc.NetId,
            proposed.PositionX,
            proposed.PositionY,
            proposed.VelocityX,
            proposed.VelocityY,
            proposed.Target,
            proposed.Ai,
            proposed.Simulation);
        return true;
    }
}

/// <summary>
/// Allocation-free authoritative-thread query adapter for host behavior callbacks. The callback never receives this
/// object directly; NpcBehaviorContext exposes only the bounded operations defined by HostContracts.
/// </summary>
internal sealed class RuntimeNpcBehaviorQueries : INpcBehaviorQueries
{
    private readonly ServerRuntimeState runtime;
    private readonly RuntimeNpcStore npcs;
    private readonly WorldTileStore? tiles;

    public RuntimeNpcBehaviorQueries(
        ServerRuntimeState runtime,
        RuntimeNpcStore npcs,
        WorldTileStore? tiles)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(npcs);
        this.runtime = runtime;
        this.npcs = npcs;
        this.tiles = tiles;
    }

    public long Tick => runtime.Updates;

    public bool TryGetPlayer(PlayerHandle player, out PlayerStateSnapshot snapshot) =>
        ((IRuntimePlayerSnapshotLookup)runtime).TryGetPlayer(player, out snapshot);

    public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot) =>
        ((IRuntimePlayerSlotSnapshotLookup)runtime).TryGetPlayer(slot, out snapshot);

    public bool TryGetNpc(NpcHandle npc, out NpcSnapshot snapshot) =>
        npcs.TryGet(npc, out snapshot);

    public int CopyNpcs(Span<NpcSnapshot> destination) =>
        npcs.CopyActive(destination);

    public bool HasSolidCollision(in NpcBehaviorBounds bounds) =>
        tiles is not null &&
        bounds.IsValid &&
        VanillaWorldSolidCollision.Intersects(
            tiles,
            bounds.PositionX,
            bounds.PositionY,
            bounds.Width,
            bounds.Height);

    public bool HasLineOfSight(in NpcBehaviorBounds source, in NpcBehaviorBounds target) =>
        tiles is not null &&
        source.IsValid &&
        target.IsValid &&
        VanillaWorldCanHit.HasLineOfSight(
            tiles,
            source.PositionX,
            source.PositionY,
            source.Width,
            source.Height,
            target.PositionX,
            target.PositionY,
            target.Width,
            target.Height);
}
