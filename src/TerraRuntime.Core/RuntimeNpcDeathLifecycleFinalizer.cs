using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Semantic lifecycle result for a dead vanilla NPC generation that was removed without an imported loot table.
/// This result is deliberately separate from NpcDeathLootResult: it records that entity lifecycle completed while
/// loot parity for that type is still unsupported rather than pretending zero drops were source-verified.
/// </summary>
public readonly record struct NpcDeathLifecycleResult(
    NpcHandle Target,
    NpcRevision FinalRevision,
    NpcTypeId Type,
    NpcArchetypeRole Role,
    float PositionX,
    float PositionY)
{
    public bool IsValid =>
        Target.IsAssigned &&
        FinalRevision.IsAssigned &&
        Type.IsAssigned &&
        Enum.IsDefined(Role) &&
        float.IsFinite(PositionX) &&
        float.IsFinite(PositionY);

    public bool WasBoss => Role == NpcArchetypeRole.Boss;
}

/// <summary>
/// Generation-safe death lifecycle fallback for vanilla NPC types whose NPC-specific loot table has not yet been
/// imported. It exists so a source-backed boss/NPC can complete authoritative entity lifecycle instead of remaining
/// forever at Life=0 merely because loot parity is incomplete.
///
/// The fallback intentionally refuses NPCs that already have an imported loot table; those must use the loot-aware
/// death transaction so callers cannot accidentally bypass verified drops. Unsupported catalog types, live NPCs and
/// stale generations fail closed. This is an explicit parity boundary: successful fallback finalization means loot
/// for that type remains unresolved, not that vanilla drops are empty.
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

    public bool TryFinalizeWhenLootUnsupported(
        NpcHandle target,
        out NpcDeathLifecycleResult result)
    {
        if (!_store.TryGet(target, out NpcSnapshot snapshot) ||
            snapshot.Simulation.LifeMax <= 0 ||
            snapshot.Simulation.Life != 0 ||
            !NpcTypeId.TryCreate(snapshot.Type, out NpcTypeId type) ||
            !VanillaNpcDefinitionCatalog.TryGet(type, out _) ||
            VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(type, out _) ||
            !_roles.TryClassify(snapshot.Handle, out VanillaNpcRoleClassification classification))
        {
            result = default;
            return false;
        }

        if (!_store.TryDespawn(snapshot.Handle))
        {
            result = default;
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
}
