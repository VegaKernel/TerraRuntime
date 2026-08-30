using System.Runtime.CompilerServices;

namespace TerraRuntime.World;

/// <summary>
/// Immutable owner-thread save image of progression milestones completed after the canonical .wld was loaded.
/// The bit layout is keyed by <see cref="VanillaWorldProgressionId"/> and is deliberately independent from the
/// physical SaveWorldFlags byte order in Terraria's header.
/// </summary>
public readonly record struct RuntimeWorldProgressionMutationSnapshot(ulong CompletedMask)
{
    public bool HasAny => CompletedMask != 0;

    public bool IsCompleted(VanillaWorldProgressionId milestone)
    {
        int index = (int)milestone;
        return (uint)index < VanillaWorldProgressionState.MilestoneCount &&
               (CompletedMask & (1UL << index)) != 0;
    }
}

/// <summary>
/// Single-writer progression journal for mutations produced by authoritative gameplay after world load.
/// It records only newly completed milestones and therefore never needs to rewrite or clear unrelated persisted
/// SaveWorldFlags. Owner-thread snapshot capture detaches the packed value before background serialization begins.
/// </summary>
public sealed class RuntimeWorldProgressionMutations
{
    private ulong completedMask;

    public bool MarkCompleted(VanillaWorldProgressionId milestone)
    {
        int index = (int)milestone;
        if ((uint)index >= VanillaWorldProgressionState.MilestoneCount)
            throw new ArgumentOutOfRangeException(nameof(milestone));

        ulong bit = 1UL << index;
        bool changed = (completedMask & bit) == 0;
        completedMask |= bit;
        return changed;
    }

    public bool IsCompleted(VanillaWorldProgressionId milestone) =>
        CaptureSnapshot().IsCompleted(milestone);

    public RuntimeWorldProgressionMutationSnapshot CaptureSnapshot() => new(completedMask);
}

/// <summary>
/// Associates mutable progression with the exact <see cref="WorldTileStore"/> instance representing a loaded world.
/// Weak keys prevent process-global current-world state and allow sequential/multi-world hosts to release worlds
/// without an explicit registry teardown protocol.
/// </summary>
public static class RuntimeWorldProgressionRegistry
{
    private static readonly ConditionalWeakTable<WorldTileStore, RuntimeWorldProgressionMutations> Worlds = new();

    public static RuntimeWorldProgressionMutations GetOrCreate(WorldTileStore tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        return Worlds.GetValue(tiles, static _ => new RuntimeWorldProgressionMutations());
    }

    public static bool TryGet(
        WorldTileStore tiles,
        out RuntimeWorldProgressionMutations? mutations)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        return Worlds.TryGetValue(tiles, out mutations);
    }
}
