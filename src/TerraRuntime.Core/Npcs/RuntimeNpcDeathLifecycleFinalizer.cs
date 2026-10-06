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
            !VanillaNpcDefinitionCatalog.TryGet(type, snapshot.NetIdentity, out var selectedDefinition) ||
            selectedDefinition.DefinitionOnly ||
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
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(random);
        var simulation = parent.Simulation;
        if (parent.Type != VanillaNpcIds.MotherSlime.Value || simulation.Life != 0 ||
            !store.TryGet(parent.Handle, out var current) || current.Revision != parent.Revision ||
            !VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.MotherSlime, out var definition) ||
            !definition.TryResolveHitbox(in simulation, out var body))
            return;
        float bottomX = parent.PositionX + body.Width / 2;
        float bottomY = parent.PositionY + body.Height;
        if (!float.IsFinite(bottomX) || !float.IsFinite(bottomY) ||
            bottomX < int.MinValue || bottomX >= int.MaxValue || bottomY < int.MinValue || bottomY >= int.MaxValue)
            return;
        int count = random.NextInt32(0, 2) + 2;
        for (int index = 0; index < count; index++)
            store.TrySpawnMotherSlimeChild(in parent, (int)bottomX, (int)bottomY, index, random, out _);
    }
}
