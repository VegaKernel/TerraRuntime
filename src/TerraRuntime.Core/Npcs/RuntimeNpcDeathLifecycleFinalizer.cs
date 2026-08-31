using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

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
/// overload without a context keeps the historical normal-mode behavior. Imported normal King Slime loot cannot
/// be bypassed through this fallback; Expert/Master remain explicitly unsupported and may still complete lifecycle.
/// </summary>
public sealed class RuntimeNpcDeathLifecycleFinalizer
{
    private readonly RuntimeNpcStore _store;
    private readonly RuntimeVanillaNpcRoleBoundary _roles;

    public RuntimeNpcDeathLifecycleFinalizer(RuntimeNpcStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _roles = new RuntimeVanillaNpcRoleBoundary(store);
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
            !_roles.TryClassify(snapshot.Handle, out VanillaNpcRoleClassification classification) ||
            !_store.TryDespawn(snapshot.Handle))
        {
            return false;
        }

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
        return type == VanillaNpcIds.KingSlime && !lootContext.IsExpertMode;
    }
}
