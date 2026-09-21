using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

public readonly record struct NpcDeathLifecycleResult(
    NpcHandle Target,
    NpcRevision FinalRevision,
    NpcTypeId Type,
    NpcArchetypeRole Role,
    float PositionX,
    float PositionY)
{
    public bool IsValid =>
        Target.IsAssigned && FinalRevision.IsAssigned && Type.IsAssigned && Enum.IsDefined(Role) &&
        float.IsFinite(PositionX) && float.IsFinite(PositionY);

    public bool WasBoss => Role == NpcArchetypeRole.Boss;
}

/// <summary>
/// Generation-safe fallback for dead vanilla NPCs whose loot is not imported for the active difficulty. The
/// overload without a context keeps the historical normal-mode behavior. Imported normal King Slime loot and the
/// all-difficulties Deerclops vertical slice cannot be bypassed through this fallback.
/// </summary>
public sealed class RuntimeNpcDeathLifecycleFinalizer
{
    private readonly RuntimeNpcStore _store;
    private readonly RuntimeVanillaNpcRoleBoundary _roles;
    private readonly IVanillaNpcRandom _random;

    public RuntimeNpcDeathLifecycleFinalizer(RuntimeNpcStore store, IVanillaNpcRandom? random = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _roles = new RuntimeVanillaNpcRoleBoundary(store);
        _random = random ?? new SystemVanillaNpcRandom();
    }

    public bool TryFinalizeWhenLootUnsupported(NpcHandle target, out NpcDeathLifecycleResult result)
    {
        VanillaNpcLootContext normalMode = default;
        return TryFinalizeWhenLootUnsupported(target, in normalMode, out result);
    }

    public bool TryFinalizeWhenLootUnsupported(
        NpcHandle target,
        in VanillaNpcLootContext lootContext,
        out NpcDeathLifecycleResult result)
    {
        result = default;
        if (!_store.TryGet(target, out NpcSnapshot snapshot) ||
            snapshot.Simulation.LifeMax <= 0 ||
            snapshot.Simulation.Life != 0 ||
            !NpcTypeId.TryCreate(snapshot.Type, out NpcTypeId type) ||
            !VanillaNpcDefinitionCatalog.TryGet(type, out _) ||
            HasImportedLootForContext(type, in lootContext) ||
            !_roles.TryClassify(snapshot.Handle, out VanillaNpcRoleClassification classification))
        {
            return false;
        }

        if (type == VanillaNpcIds.MotherSlime)
            VanillaMotherSlimeDeathSplit1458.SpawnChildren(_store, in snapshot, _random);
        if (!_store.TryDespawn(snapshot.Handle))
            return false;

        result = new NpcDeathLifecycleResult(
            snapshot.Handle,
            snapshot.Revision,
            type,
            classification.Role,
            snapshot.PositionX,
            snapshot.PositionY);
        return result.IsValid;
    }

    private static bool HasImportedLootForContext(NpcTypeId type, in VanillaNpcLootContext lootContext)
    {
        if (VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(type, out _))
            return true;
        return type == VanillaNpcIds.Deerclops ||
               (type == VanillaNpcIds.KingSlime && !lootContext.IsExpertMode);
    }

}

/// <summary>Server-relevant Mother Slime death side effect from TerrariaServer 1.4.5.8 <c>NPC.HitEffect</c>.</summary>
public static class VanillaMotherSlimeDeathSplit1458
{
    public static void SpawnChildren(RuntimeNpcStore store, in NpcSnapshot parent, IVanillaNpcRandom random)
    {
        // NPC.HitEffect (1.4.5.8): a dead Mother Slime creates two or three Baby Slimes after hit effects,
        // preserving parent velocity and then applying each source-ordered random offset.
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(random);
        int count = random.NextInt32(0, 2) + 2;
        if (!VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.MotherSlime, out VanillaNpcDefinition definition))
            return;
        int bottomX = (int)(parent.PositionX + definition.Width * .5f);
        int bottomY = (int)(parent.PositionY + definition.Height);
        for (int index = 0; index < count; index++)
        {
            var intent = new NpcAiSpawnIntent(VanillaNpcIds.BlueSlime, bottomX, bottomY,
                parent.VelocityX * 2f + random.NextInt32(-20, 20) * .1f + index * parent.Simulation.DirectionX * .3f,
                parent.VelocityY - random.NextInt32(0, 10) * .1f - index,
                parent.Target)
            {
                NetIdOverride = VanillaNpcNetVariantCatalog.BabySlime,
                InitialAi = new NpcAiState(-1000f * random.NextInt32(0, 3), 0f, 0f, 0f)
            };
            store.TrySpawnIntent(in intent, out _);
        }
    }
}
