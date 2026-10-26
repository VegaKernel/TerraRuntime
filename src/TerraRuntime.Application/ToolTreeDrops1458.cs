using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal readonly record struct ToolTreeDropContext1458(
    ToolTreeBiome1458 Biome, ushort PalmGroundType, int ClosestPlayerAxe,
    bool TenthAnniversary, bool Drunk, bool Remix, bool NotTheBees);

internal readonly record struct ToolTreeDrops1458(
    ItemTypeId Primary, ushort PrimaryStack, ItemTypeId Secondary, ushort SecondaryStack);

/// <summary>
/// Server-role source tree GetItemDrops offers, before Item.NewItem allocations.
/// The caller supplies captured bottom/closest-actor/world facts and a detached
/// preview of the one world Main.rand/genRand alias stream.
/// </summary>
internal static class VanillaToolTreeDrops1458
{
    private const ushort CommonTree = 5, MushroomTree = 72, Cactus = 80, PalmTree = 323;
    private const ushort FirstGemTree = 583, LastGemTree = 589;
    private const ushort Sakura = 596, Willow = 616, AshTree = 634;
    private const int Wood = 9, Stone = 3, Acorn = 27, GlowingMushroom = 183, MushroomGrassSeed = 194;
    private const int PalmWood = 2504, Ebonwood = 619, Shadewood = 911, Pearlwood = 621;
    private const int Mahogany = 620, BorealWood = 2503, AshWood = 5215, CactusBlock = 276;

    public static bool TryResolve(in WorldTile tile, in ToolTreeDropContext1458 context,
        IWorldItemSpawnRandom random, out ToolTreeDrops1458 drops)
    {
        drops = default;
        ArgumentNullException.ThrowIfNull(random);
        int primary = 0, secondary = 0, count = 1;
        bool extraWood = context.TenthAnniversary && context.Drunk && !context.Remix && !context.NotTheBees;
        if (tile.Type is >= FirstGemTree and <= LastGemTree)
        {
            ReadOnlySpan<int> gems = [180, 181, 177, 179, 178, 182, 999];
            ReadOnlySpan<int> seeds = [4851, 4852, 4853, 4854, 4855, 4856, 4857];
            int index = tile.Type - FirstGemTree;
            primary = random.NextInt32(0, 10) == 0 ? gems[index] : Stone;
            if (tile.FrameX >= 22 && tile.FrameY >= 198 && random.NextInt32(0, 2) == 0)
                secondary = seeds[index];
            // Source chooses seed before the fallback stone stack draw.
            if (primary == Stone) count = random.NextInt32(1, 3);
        }
        else if (tile.Type == PalmTree)
        {
            primary = PalmWood;
            if (context.TenthAnniversary) count += random.NextInt32(2, 5);
            if (tile.FrameX is >= 88 and <= 132) secondary = Acorn;
            const ushort CrimsonSand = 234, HallowedSand = 116, CorruptSand = 112;
            primary = context.PalmGroundType switch
            {
                CrimsonSand => Shadewood,
                HallowedSand => Pearlwood,
                CorruptSand => Ebonwood,
                _ => primary
            };
            if (extraWood) count *= 3;
        }
        else if (tile.Type is CommonTree or Sakura or Willow or AshTree)
        {
            if (context.ClosestPlayerAxe < 0) return false;
            primary = Wood;
            if (tile.FrameX >= 22 && tile.FrameY >= 198 && random.NextInt32(0, 2) == 0 &&
                context.Biome is not (ToolTreeBiome1458.None or ToolTreeBiome1458.Mushroom or ToolTreeBiome1458.Jungle))
                secondary = Acorn;
            primary = context.Biome switch
            {
                ToolTreeBiome1458.Ash => AshWood,
                ToolTreeBiome1458.Corrupt => Ebonwood,
                ToolTreeBiome1458.Crimson => Shadewood,
                ToolTreeBiome1458.Jungle => Mahogany,
                ToolTreeBiome1458.Hallowed => Pearlwood,
                ToolTreeBiome1458.Mushroom => random.NextInt32(0, 2) == 0 ? GlowingMushroom : 0,
                ToolTreeBiome1458.Snow => BorealWood,
                _ => primary
            };
            // The second offer is short-circuited even for mushroom/no-primary outcomes.
            if (random.NextInt32(0, 35) <= context.ClosestPlayerAxe || random.NextInt32(0, 3) == 0)
                count++;
            if (extraWood) count *= 2;
        }
        else if (tile.Type == MushroomTree)
        {
            if (random.NextInt32(0, 40) == 0) primary = MushroomGrassSeed;
            else if (random.NextInt32(0, 2) == 0) primary = GlowingMushroom;
        }
        else if (tile.Type == Cactus) primary = CactusBlock;
        else return false;
        drops = new(new ItemTypeId(primary), checked((ushort)count), new ItemTypeId(secondary), 1);
        return true;
    }
}
