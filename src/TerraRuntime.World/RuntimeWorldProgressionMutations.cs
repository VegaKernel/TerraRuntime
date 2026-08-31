using System.Runtime.CompilerServices;

namespace TerraRuntime.World;

/// <summary>
/// Immutable owner-thread save image of progression milestones and source-backed world unlocks produced after the
/// canonical .wld was loaded. Milestone bits remain independent from the physical SaveWorldFlags byte order.
/// </summary>
public readonly record struct RuntimeWorldProgressionMutationSnapshot(ulong CompletedMask)
{
    public bool UnlockSlimeBlueSpawn { get; init; }

    public bool HasAny => CompletedMask != 0 || UnlockSlimeBlueSpawn;

    public bool IsCompleted(VanillaWorldProgressionId milestone)
    {
        int index = (int)milestone;
        return (uint)index < VanillaWorldProgressionState.MilestoneCount &&
               (CompletedMask & (1UL << index)) != 0;
    }
}

/// <summary>
/// Single-writer progression journal for mutations produced by authoritative gameplay after world load. Persisted
/// baseline facts are tracked separately from newly produced mutations so save patching changes only facts that this
/// runtime actually made true.
/// </summary>
public sealed class RuntimeWorldProgressionMutations
{
    private ulong completedMask;
    private bool baselineSlimeBlueSpawnUnlocked;
    private bool unlockSlimeBlueSpawn;

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

    public void SetSlimeBlueSpawnBaseline(bool unlocked)
    {
        if (unlocked)
            baselineSlimeBlueSpawnUnlocked = true;
    }

    public bool IsSlimeBlueSpawnUnlocked => baselineSlimeBlueSpawnUnlocked || unlockSlimeBlueSpawn;

    public bool MarkSlimeBlueSpawnUnlocked()
    {
        if (IsSlimeBlueSpawnUnlocked)
            return false;

        unlockSlimeBlueSpawn = true;
        return true;
    }

    public RuntimeWorldProgressionMutationSnapshot CaptureSnapshot() =>
        new(completedMask) { UnlockSlimeBlueSpawn = unlockSlimeBlueSpawn };
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
