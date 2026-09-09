using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary AddBuriedChest Dungeon loot and its generation-scoped first-key/rune state.</summary>
internal sealed class DungeonChestLoot1458(IWorldGenerationVanillaRandom random, VanillaWorldGenerationBootstrapState1458 bootstrap)
{
    private readonly PrefixRolls prefixRolls = new(random);
    internal bool GeneratedShadowKey { get; private set; }
    internal bool GeneratedRamRune { get; private set; }

    internal WorldGenerationChestItem[] Build(int primary, ushort tileType, int style, int floorY,
        double worldSurface, double rockLayer, int worldHeight, bool dungeonWall)
    {
        bool biome = tileType == 21 && style is >= 23 and <= 27 || tileType == 467 && style == 13;
        if (!(biome || tileType == 21 && style is 0 or 2))
            throw new InvalidOperationException("Unsupported Dungeon chest family.");
        bool dungeon = biome || tileType == 21 && style != 0 && floorY >= worldSurface && dungeonWall;
        bool surface = tileType == 21 && style == 0 && floorY < worldSurface + 25d;
        var items = new List<WorldGenerationChestItem>(20);
        AddPrefixed(items, primary);
        if (!surface && floorY < worldHeight - 250 && dungeon && !biome)
        {
            if (!GeneratedShadowKey || random.Next(3) == 0)
            {
                GeneratedShadowKey = true;
                ChestLoot1458.Add(items, 329, 1);
            }
            if (!GeneratedRamRune || random.Next(8) == 0)
            {
                GeneratedRamRune = true;
                AddPrefixed(items, 5465);
            }
            if (random.Next(4) == 0) AddPrefixed(items, 6156);
        }

        if (surface) ChestLoot1458.FillSurface(random, bootstrap, items);
        else if (floorY < rockLayer) ChestLoot1458.FillUnderground(random, bootstrap, items);
        else if (floorY < worldHeight - 250) ChestLoot1458.FillCavern(random, bootstrap, items);
        else ChestLoot1458.FillUnderworld(random, bootstrap, items);

        if (tileType == 21 && dungeon && random.Next(8) == 0) ChestLoot1458.Add(items, 2192, 1);
        if (biome && random.Next(2) == 0) ChestLoot1458.Add(items, 5234, 1);
        if (random.Next(12) == 0)
        {
            // Item.GetRandomVoiceItem: final three entries are not contiguous with the others.
            int choice = random.Next(14);
            AddPrefixed(items, choice switch { 11 => 5484, 12 => 5485, 13 => 5534, _ => 5499 + choice });
        }
        return items.ToArray();
    }

    private void AddPrefixed(List<WorldGenerationChestItem> items, int id)
    {
        var type = new ItemTypeId(id);
        if (!VanillaNaturalItemPrefixRoller.TryRoll(type, prefixRolls, out PrefixId prefix))
            throw new InvalidOperationException($"Unverified natural prefix for Dungeon chest item {id}.");
        ChestLoot1458.Add(items, id, 1);
        items[^1] = items[^1] with { Prefix = prefix };
    }

    private sealed class PrefixRolls(IWorldGenerationVanillaRandom source) : INpcLootRollSource
    {
        public int NextInt32(int inclusiveMin, int exclusiveMax) => source.Next(inclusiveMin, exclusiveMax);
        public int RollLuck(int chanceDenominator) => throw new InvalidOperationException("World-generation prefixes do not roll player luck.");
    }
}
