using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Source-pinned global drop inputs; default is a fresh pre-Hardmode world.</summary>
public readonly record struct VanillaMechBossSpawnersContext1458(
    float NpcValue,
    bool HardMode,
    bool DestroyerDowned,
    bool TwinsDowned,
    bool PrimeDowned,
    bool IsInSimulation = false)
{
    public bool CanDrop => NpcValue > 0f && HardMode && !IsInSimulation &&
        (!DestroyerDowned || !TwinsDowned || !PrimeDowned);
}

/// <summary>MechBossSpawnersDropRule: first successful unfinished boss offer wins, without a stack roll.</summary>
public static class VanillaMechBossSpawners1458
{
    public static bool TryEvaluate(in VanillaMechBossSpawnersContext1458 context,
        INpcLootRollSource random, out bool dropped, out NpcLootDrop drop)
    {
        ArgumentNullException.ThrowIfNull(random);
        dropped = false;
        drop = default;
        if (!float.IsFinite(context.NpcValue)) return false;
        if (!context.CanDrop) return true;
        if (!context.DestroyerDowned && random.RollLuck(2500) == 0)
            drop = new(VanillaMechanicalBossItemIds.MechanicalWorm, 1);
        else if (!context.TwinsDowned && random.RollLuck(2500) == 0)
            drop = new(VanillaMechanicalBossItemIds.MechanicalEye, 1);
        else if (!context.PrimeDowned && random.RollLuck(2500) == 0)
            drop = new(VanillaMechanicalBossItemIds.MechanicalSkull, 1);
        dropped = drop.IsValid;
        return true;
    }
}
