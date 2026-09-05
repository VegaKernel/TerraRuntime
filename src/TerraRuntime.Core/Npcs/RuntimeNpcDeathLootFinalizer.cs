using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Npcs;

public readonly record struct NpcDeathLootResult(
    NpcHandle Target,
    NpcRevision FinalRevision,
    NpcTypeId Type,
    float PositionX,
    float PositionY,
    int DropCount)
{
    public bool IsValid =>
        Target.IsAssigned && FinalRevision.IsAssigned && Type.IsAssigned &&
        float.IsFinite(PositionX) && float.IsFinite(PositionY) && DropCount >= 0;
}

/// <summary>
/// Generation-safe dead-NPC finalizer for source-backed NPC-specific loot. Normal King Slime uses its dedicated
/// rule family; Expert/Master remain unsupported until treasure-bag and per-player delivery are explicit runtime
/// concepts instead of being flattened into ordinary world items.
/// </summary>
public sealed class RuntimeNpcDeathLootFinalizer
{
    private readonly RuntimeNpcStore _store;

    public RuntimeNpcDeathLootFinalizer(RuntimeNpcStore store) =>
        _store = store ?? throw new ArgumentNullException(nameof(store));

    public bool TryFinalize(
        NpcHandle target,
        in VanillaNpcLootContext lootContext,
        INpcLootRollSource rolls,
        Span<NpcLootDrop> destination,
        out NpcDeathLootResult result)
    {
        ArgumentNullException.ThrowIfNull(rolls);
        result = default;

        if (!_store.TryGet(target, out NpcSnapshot snapshot) ||
            snapshot.Simulation.LifeMax <= 0 ||
            snapshot.Simulation.Life != 0 ||
            !NpcTypeId.TryCreate(snapshot.Type, out NpcTypeId type))
        {
            return false;
        }

        bool evaluated;
        int dropCount = 0;
        if (type == VanillaNpcIds.KingSlime)
        {
            evaluated = !lootContext.IsExpertMode &&
                        VanillaKingSlimeNormalLootEvaluator.TryEvaluateAll(rolls, destination, out dropCount);
        }
        else
        {
            evaluated = VanillaNpcLootEvaluator.TryEvaluateNpcSpecificRules(
                type, in lootContext, rolls, destination, out dropCount);
        }

        if (!evaluated || !_store.TryDespawn(snapshot.Handle))
            return false;

        result = new NpcDeathLootResult(
            snapshot.Handle,
            snapshot.Revision,
            type,
            snapshot.PositionX,
            snapshot.PositionY,
            dropCount);
        return true;
    }
}
