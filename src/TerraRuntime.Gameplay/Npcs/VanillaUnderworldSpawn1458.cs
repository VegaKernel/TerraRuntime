using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Inputs to the ordinary NPC.Spawner.SpawnAnNPC underworld branch (1.4.5.8).</summary>
public readonly record struct VanillaUnderworldSpawnFacts1458(
    bool HardMode,
    bool DownedMechBossAny,
    bool SavedTaxCollector,
    bool TorturedSoulPresent,
    bool BoneSerpentPresent);

public static class VanillaUnderworldSpawn1458
{
    public static bool IsUnderworld(int floorY, int worldHeight) => floorY > worldHeight - 190;

    /// <summary>
    /// Preserves the source's short-circuit rolls. False means the multi-actor lava-bait branch,
    /// which is not yet admitted; callers must not reroll or substitute an ordinary cave NPC.
    /// Definitions and authoritative AI admission are checked separately by the world owner.
    /// Secret-seed and earlier biome/event branches are deliberately not evaluated here.
    /// </summary>
    public static bool TrySelect(in VanillaUnderworldSpawnFacts1458 facts, IVanillaNpcRandom random, out NpcTypeId type)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (facts.HardMode && !facts.SavedTaxCollector && random.NextInt32(0, 20) == 0 && !facts.TorturedSoulPresent)
            type = new(534);
        else if (random.NextInt32(0, 8) == 0)
        {
            type = default;
            return false;
        }
        else if (random.NextInt32(0, 40) == 0 && !facts.BoneSerpentPresent)
            type = new(39);
        else if (random.NextInt32(0, 14) == 0)
            type = new(24);
        else if (random.NextInt32(0, 7) == 0)
        {
            if (random.NextInt32(0, 10) == 0)
                type = new(66);
            else if (facts.HardMode && facts.DownedMechBossAny && random.NextInt32(0, 5) != 0)
                type = new(156);
            else
                type = new(62);
        }
        else if (random.NextInt32(0, 3) == 0)
            type = new(59);
        else if (facts.HardMode && facts.DownedMechBossAny && random.NextInt32(0, 5) != 0)
            type = new(151);
        else
            type = new(60);
        return true;
    }
}
