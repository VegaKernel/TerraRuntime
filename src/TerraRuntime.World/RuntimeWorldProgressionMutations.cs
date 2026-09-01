using System.Runtime.CompilerServices;

namespace TerraRuntime.World;

[Flags]
public enum RuntimeTownRescueFacts1458 : ushort
{
    None = 0,
    Goblin = 1 << 0,
    Wizard = 1 << 1,
    Mechanic = 1 << 2,
    Stylist = 1 << 3,
    Angler = 1 << 4,
    Bartender = 1 << 5,
    Golfer = 1 << 6,
    TaxCollector = 1 << 7,
    All = Goblin | Wizard | Mechanic | Stylist | Angler | Bartender | Golfer | TaxCollector
}

/// <summary>
/// Immutable owner-thread save image of progression milestones and source-backed world unlocks produced after the
/// canonical .wld was loaded. Milestone bits remain independent from the physical SaveWorldFlags byte order.
/// </summary>
public readonly record struct RuntimeWorldProgressionMutationSnapshot(ulong CompletedMask)
{
    public bool UnlockSlimeBlueSpawn { get; init; }

    public bool UnlockTruffleSpawn { get; init; }

    public bool UnlockSlimeYellowSpawn { get; init; }

    public RuntimeTownRescueFacts1458 RescuedTownNpcs { get; init; }

    public bool HasAny =>
        CompletedMask != 0 ||
        UnlockSlimeBlueSpawn ||
        UnlockTruffleSpawn ||
        UnlockSlimeYellowSpawn ||
        RescuedTownNpcs != RuntimeTownRescueFacts1458.None;

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
    private bool baselineTruffleSpawnUnlocked;
    private bool unlockTruffleSpawn;
    private bool baselineSlimeYellowSpawnUnlocked;
    private bool unlockSlimeYellowSpawn;
    private RuntimeTownRescueFacts1458 baselineRescuedTownNpcs;
    private RuntimeTownRescueFacts1458 rescuedTownNpcs;

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

    public void SetTruffleSpawnBaseline(bool unlocked)
    {
        if (unlocked)
            baselineTruffleSpawnUnlocked = true;
    }

    public bool IsTruffleSpawnUnlocked => baselineTruffleSpawnUnlocked || unlockTruffleSpawn;

    public bool MarkTruffleSpawnUnlocked()
    {
        if (IsTruffleSpawnUnlocked)
            return false;

        unlockTruffleSpawn = true;
        return true;
    }

    public void SetSlimeYellowSpawnBaseline(bool unlocked)
    {
        if (unlocked)
            baselineSlimeYellowSpawnUnlocked = true;
    }

    public bool IsSlimeYellowSpawnUnlocked => baselineSlimeYellowSpawnUnlocked || unlockSlimeYellowSpawn;

    public bool MarkSlimeYellowSpawnUnlocked()
    {
        if (IsSlimeYellowSpawnUnlocked)
            return false;

        unlockSlimeYellowSpawn = true;
        return true;
    }

    public void SetTownRescueBaseline(RuntimeTownRescueFacts1458 facts)
    {
        if ((facts & ~RuntimeTownRescueFacts1458.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(facts));
        baselineRescuedTownNpcs |= facts;
    }

    public bool MarkTownNpcRescued(RuntimeTownRescueFacts1458 fact)
    {
        ushort raw = (ushort)fact;
        if (raw == 0 || (raw & (raw - 1)) != 0 || (fact & ~RuntimeTownRescueFacts1458.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(fact));
        if (((baselineRescuedTownNpcs | rescuedTownNpcs) & fact) != 0)
            return false;
        rescuedTownNpcs |= fact;
        return true;
    }

    public RuntimeWorldProgressionMutationSnapshot CaptureSnapshot() =>
        new(completedMask)
        {
            UnlockSlimeBlueSpawn = unlockSlimeBlueSpawn,
            UnlockTruffleSpawn = unlockTruffleSpawn,
            UnlockSlimeYellowSpawn = unlockSlimeYellowSpawn,
            RescuedTownNpcs = rescuedTownNpcs
        };
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
