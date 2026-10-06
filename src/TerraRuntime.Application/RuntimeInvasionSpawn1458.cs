using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Application;

internal readonly record struct RetainedInvasionTownSlot1458(bool Active, bool? TownNpc, float? CenterX);

/// <summary>TerrariaServer 1.4.5.8 selected invasion eligibility and Goblin SpawnAnNPC dispatch.</summary>
internal static class RuntimeInvasionSpawn1458
{
    private const int SourceNpcSlots = 200;
    private const int GoblinSummonerType = 471;
    private const int SourceScreenHeightPixels = 1200;
    private const int InvasionRangePixels = 3000;
    private const int MidpointToleranceTiles = 5;

    internal static bool TryShouldSpawn(in InvasionState1458 invasion, float x, float y,
        double surface, int spawnTileY, int width, ReadOnlySpan<RetainedInvasionTownSlot1458> slots,
        IVanillaNpcRandom random, out bool result)
    {
        result = false;
        if (!float.IsFinite(x) || !float.IsFinite(y) || !double.IsFinite(surface) ||
            !double.IsFinite(invasion.X) || width <= 0)
            return false;
        if (invasion.Type <= 0 || invasion.Delay != 0 || invasion.Size <= 0)
            return true;
        if (!(y < surface * 16d + SourceScreenHeightPixels || spawnTileY > surface))
            return true;
        if (x > invasion.X * 16d - InvasionRangePixels && x < invasion.X * 16d + InvasionRangePixels)
        {
            result = true;
            return true;
        }
        if (invasion.X < width / 2 - MidpointToleranceTiles || invasion.X > width / 2 + MidpointToleranceTiles)
            return true;
        // The fallback is a source physical-slot scan, not a compact active/resident roster.
        if (slots.Length != SourceNpcSlots)
            return false;
        foreach (var slot in slots)
        {
            // Source scans retained slots and does not predicate on Active.
            if (slot.TownNpc is not { } town)
                return false;
            if (!town)
                continue;
            if (slot.CenterX is not { } center || !float.IsFinite(center))
                return false;
            if (Math.Abs(x - center) >= InvasionRangePixels)
                continue;
            if (random.NextInt32(0, 3) == 0)
                break;
            result = true;
            return true;
        }
        return true;
    }
    // Caller owns invaders=true and known Skyblock.lowTiles=false. Preserve selected 471: behavioral
    // admission is a later owner check, never permission to replace an unsupported Summoner with another Goblin.
    internal static NpcTypeId SelectGoblin(bool hardmode, bool summonerActive, IVanillaNpcRandom random)
    {
        _ = random.NextInt32(0, 7); // Actual unconditional GetZombieSettings offer.
        if (hardmode && !summonerActive && random.NextInt32(0, 30) == 0)
            return new NpcTypeId(GoblinSummonerType);
        if (random.NextInt32(0, 9) == 0)
            return VanillaNpcIds.GoblinSorcerer;
        if (random.NextInt32(0, 5) == 0)
            return VanillaNpcIds.GoblinPeon;
        if (random.NextInt32(0, 3) == 0)
            return VanillaNpcIds.GoblinArcher;
        if (random.NextInt32(0, 3) == 0)
            return VanillaNpcIds.GoblinThief;
        return VanillaNpcIds.GoblinWarrior;
    }
}
