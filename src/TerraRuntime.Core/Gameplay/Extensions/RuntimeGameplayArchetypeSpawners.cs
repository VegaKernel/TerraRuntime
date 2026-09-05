using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Extensions;

public readonly record struct NpcArchetypeSpawnRequest(
    GameplayArchetypeId ArchetypeId,
    byte Slot,
    float PositionX,
    float PositionY,
    float VelocityX = 0f,
    float VelocityY = 0f,
    ushort Target = VanillaNpcDefinitionCatalog.DefaultTarget);

public readonly record struct NpcArchetypeAllocateRequest(
    GameplayArchetypeId ArchetypeId,
    float PositionX,
    float PositionY,
    float VelocityX = 0f,
    float VelocityY = 0f,
    ushort Target = VanillaNpcDefinitionCatalog.DefaultTarget);

public readonly record struct ProjectileArchetypeSpawnRequest(
    GameplayArchetypeId ArchetypeId,
    byte Spawner,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    short Damage,
    float KnockBack,
    short OriginalDamage,
    ProjectileAiState Ai = default,
    ushort BannerIdToRespondTo = 0);

/// <summary>
/// Resolves a published server-defined NPC archetype to its validated vanilla presentation and commits a normal
/// authoritative spawn. The caller still chooses the NPC slot until TerraRuntime has a source-backed NPC allocation
/// primitive. The identity store must be wired into the same RuntimeNpcStore commit chain; a failed bind rolls the
/// spawned generation back instead of leaving an apparently-custom vanilla NPC behind.
/// </summary>
public sealed class RuntimeNpcArchetypeSpawner
{
    private readonly RuntimeNpcStore store;
    private readonly RuntimeNpcArchetypeRegistry archetypes;
    private readonly RuntimeNpcArchetypeIdentityStore identities;

    public RuntimeNpcArchetypeSpawner(
        RuntimeNpcStore store,
        RuntimeNpcArchetypeRegistry archetypes,
        RuntimeNpcArchetypeIdentityStore identities)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.archetypes = archetypes ?? throw new ArgumentNullException(nameof(archetypes));
        this.identities = identities ?? throw new ArgumentNullException(nameof(identities));
    }

    public bool TrySpawn(in NpcArchetypeSpawnRequest request, out NpcSnapshot snapshot)
    {
        if (!request.ArchetypeId.IsAssigned ||
            !archetypes.Snapshot.TryGet(request.ArchetypeId, out NpcArchetypeDescriptor descriptor) ||
            descriptor.VanillaPresentationType.Value > short.MaxValue)
        {
            snapshot = default;
            return false;
        }

        int presentation = descriptor.VanillaPresentationType.Value;
        var update = new NpcStateUpdate(
            Type: presentation,
            NetId: (short)presentation,
            PositionX: request.PositionX,
            PositionY: request.PositionY,
            VelocityX: request.VelocityX,
            VelocityY: request.VelocityY,
            Target: request.Target,
            Ai: new NpcAiState(0f, 0f, 0f, 0f),
            Simulation: NpcSimulationState.Initial);

        if (!store.TrySpawn(request.Slot, in update, out snapshot))
            return false;

        if (identities.TryBind(snapshot.Handle, request.ArchetypeId))
            return true;

        store.TryDespawn(snapshot.Handle);
        snapshot = default;
        return false;
    }

    public bool TrySpawnAllocated(in NpcArchetypeAllocateRequest request, out NpcSnapshot snapshot)
    {
        if (!request.ArchetypeId.IsAssigned ||
            !archetypes.Snapshot.TryGet(request.ArchetypeId, out NpcArchetypeDescriptor descriptor) ||
            descriptor.VanillaPresentationType.Value > short.MaxValue)
        {
            snapshot = default;
            return false;
        }

        int presentation = descriptor.VanillaPresentationType.Value;
        var update = new NpcStateUpdate(
            Type: presentation,
            NetId: (short)presentation,
            PositionX: request.PositionX,
            PositionY: request.PositionY,
            VelocityX: request.VelocityX,
            VelocityY: request.VelocityY,
            Target: request.Target,
            Ai: new NpcAiState(0f, 0f, 0f, 0f),
            Simulation: NpcSimulationState.Initial);

        if (!store.TrySpawnVanilla(in update, out snapshot))
            return false;

        if (identities.TryBind(snapshot.Handle, request.ArchetypeId))
            return true;

        store.TryDespawn(snapshot.Handle);
        snapshot = default;
        return false;
    }
}

/// <summary>
/// Resolves a published custom projectile archetype and delegates physical allocation/replacement entirely to
/// RuntimeProjectileStore.TrySpawnVanilla. Thus custom projectiles inherit the same source-backed slot selection,
/// overflow, generation and replication semantics as vanilla projectiles while retaining separate server identity.
/// </summary>
public sealed class RuntimeProjectileArchetypeSpawner
{
    private readonly RuntimeProjectileStore store;
    private readonly RuntimeProjectileArchetypeRegistry archetypes;
    private readonly RuntimeProjectileArchetypeIdentityStore identities;

    public RuntimeProjectileArchetypeSpawner(
        RuntimeProjectileStore store,
        RuntimeProjectileArchetypeRegistry archetypes,
        RuntimeProjectileArchetypeIdentityStore identities)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.archetypes = archetypes ?? throw new ArgumentNullException(nameof(archetypes));
        this.identities = identities ?? throw new ArgumentNullException(nameof(identities));
    }

    public bool TrySpawn(in ProjectileArchetypeSpawnRequest request, out ProjectileSnapshot snapshot)
    {
        if (!request.ArchetypeId.IsAssigned ||
            !archetypes.Snapshot.TryGet(request.ArchetypeId, out ProjectileArchetypeDescriptor descriptor))
        {
            snapshot = default;
            return false;
        }

        var update = new ProjectileStateUpdate(
            descriptor.VanillaPresentationType,
            request.Spawner,
            request.PositionX,
            request.PositionY,
            request.VelocityX,
            request.VelocityY,
            request.Ai,
            request.BannerIdToRespondTo,
            request.Damage,
            request.KnockBack,
            request.OriginalDamage);

        if (!store.TrySpawnVanilla(in update, out snapshot))
            return false;

        if (identities.TryBind(snapshot.Handle, request.ArchetypeId))
            return true;

        store.TryRemove(snapshot.Handle, out _);
        snapshot = default;
        return false;
    }
}
