using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

/// <summary>
/// Result of finalizing one generation-safe dead NPC whose NPC-specific loot rules are source-backed.
/// The snapshot coordinates are captured before despawn so a later world-item stage can place drops without
/// re-reading a slot that may already have been reused by another NPC generation.
/// </summary>
public readonly record struct NpcDeathLootResult(
    NpcHandle Target,
    NpcRevision FinalRevision,
    NpcTypeId Type,
    float PositionX,
    float PositionY,
    int DropCount)
{
    public bool IsValid =>
        Target.IsAssigned &&
        FinalRevision.IsAssigned &&
        Type.IsAssigned &&
        float.IsFinite(PositionX) &&
        float.IsFinite(PositionY) &&
        DropCount >= 0;
}

/// <summary>
/// Finalizes the currently source-backed NPC death/loot boundary without owning world-item spawning.
/// A successful call evaluates NPC-specific loot in source order and then despawns the exact dead generation,
/// making a second successful finalization for the same NPC impossible. Unsupported loot families fail closed.
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

        if (!_store.TryGet(target, out NpcSnapshot snapshot) ||
            snapshot.Simulation.LifeMax <= 0 ||
            snapshot.Simulation.Life != 0 ||
            !NpcTypeId.TryCreate(snapshot.Type, out NpcTypeId type) ||
            !VanillaNpcLootEvaluator.TryEvaluateNpcSpecificRules(
                type,
                in lootContext,
                rolls,
                destination,
                out int dropCount))
        {
            result = default;
            return false;
        }

        // RuntimeNpcStore is single-writer gameplay state. Revalidate the exact generation through TryDespawn
        // after loot evaluation so a stale handle cannot finalize a replacement occupying the same byte slot.
        if (!_store.TryDespawn(snapshot.Handle))
        {
            result = default;
            return false;
        }

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
