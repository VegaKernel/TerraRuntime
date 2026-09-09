using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed ordinary chest loot for the default TerrariaServer 1.4.5.8 world profile. This helper is called at
/// chest placement time, while the source family and depth branch are still known. It intentionally owns no geometry.
/// Prefix coverage is incomplete: generation-time Item.Prefix(-1) uses the shared genRand/Main.rand stream in 1.4.5.8.
/// Omission changes both the prefix and subsequent generation RNG; this is parity debt, not a separate RNG contract.
/// </summary>
internal static class ChestLoot1458
{
    private static readonly int[] SurfacePrimary = [280, 281, 284, 285, 953, 946, 3068, 3069, 3084, 4341, 6165];
    private static readonly int[] UndergroundPrimary = [49, 50, 53, 54, 5011, 975];
    private static readonly int[] CavernPrimary = [49, 50, 53, 54, 5011, 975];
    private static readonly int[] SurfacePotions = [292, 298, 299, 290, 2322, 2325];
    private static readonly int[] UndergroundPotions = [289, 298, 299, 290, 303, 291, 304, 2322, 2329];
    private static readonly int[] CavernPotionsA = [296, 295, 299, 302, 303, 305];
    private static readonly int[] CavernPotionsB = [301, 297, 304, 2329, 2351, 2326];
    private static readonly int[] UnderworldPotionsA = [296, 295, 293, 288, 294, 297, 304, 2323];
    private static readonly int[] UnderworldPotionsB = [305, 301, 302, 288, 300, 2351, 2348, 2345];

    internal static WorldGenerationChestItem[] BuildSurface(
        IWorldGenerationVanillaRandom random,
        VanillaWorldGenerationBootstrapState1458 bootstrap)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(bootstrap);
        var items = new List<WorldGenerationChestItem>(16);

        Add(items, SurfacePrimary[random.Next(SurfacePrimary.Length)], 1);
        FillSurface(random, bootstrap, items);
        return items.ToArray();
    }

    internal static void FillSurface(IWorldGenerationVanillaRandom random,
        VanillaWorldGenerationBootstrapState1458 bootstrap, List<WorldGenerationChestItem> items)
    {
        if (random.Next(6) == 0)
            Add(items, 282, random.Next(40, 76));
        if (random.Next(6) == 0)
            Add(items, 279, random.Next(150, 301));
        if (random.Next(6) == 0)
        {
            int stack = 1;
            if (random.Next(5) == 0)
                stack += random.Next(2);
            if (random.Next(10) == 0)
                stack += random.Next(3);
            Add(items, 3093, stack);
        }
        if (random.Next(6) == 0)
        {
            int stack = 1;
            if (random.Next(5) == 0)
                stack += random.Next(2);
            if (random.Next(10) == 0)
                stack += random.Next(3);
            Add(items, 4345, stack);
        }
        if (random.Next(3) == 0)
            Add(items, 168, random.Next(3, 6));
        if (random.Next(2) == 0)
            Add(items, random.Next(2) == 0 ? bootstrap.CopperBar : bootstrap.IronBar, random.Next(3, 11));
        if (random.Next(2) == 0)
            Add(items, 965, random.Next(50, 101));
        if (random.Next(3) != 0)
            Add(items, random.Next(2) == 0 ? 40 : 42, random.Next(25, 51));
        if (random.Next(2) == 0)
            Add(items, 28, random.Next(3, 6));
        if (random.Next(3) != 0)
            Add(items, 2350, random.Next(3, 6));
        if (random.Next(3) > 0)
            Add(items, SurfacePotions[random.Next(SurfacePotions.Length)], random.Next(1, 3));
        if (random.Next(2) == 0)
            Add(items, random.Next(2) == 0 ? 8 : 31, random.Next(10, 21));
        if (random.Next(2) == 0)
            Add(items, 72, random.Next(10, 30));
        if (random.Next(2) == 0)
            Add(items, 9, random.Next(50, 100));
    }

    internal static WorldGenerationChestItem[] BuildBuried(
        IWorldGenerationVanillaRandom random,
        VanillaWorldGenerationBootstrapState1458 bootstrap,
        int floorY,
        double rockLayer,
        int lavaLine,
        int worldHeight)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(bootstrap);
        if (floorY < rockLayer)
            return BuildUnderground(random, bootstrap, primary: 0, water: false, jungle: false, state: null);
        if (floorY < worldHeight - 250)
            return BuildCavern(random, bootstrap, primary: 0, floorY, lavaLine, water: false, jungle: false, state: null);
        return BuildUnderworld(random, bootstrap, primary: 0, shadow: false);
    }

    internal static WorldGenerationChestItem[] BuildShadow(
        IWorldGenerationVanillaRandom random,
        VanillaWorldGenerationBootstrapState1458 bootstrap,
        int primary) =>
        BuildUnderworld(random, bootstrap, primary, shadow: true);

    internal static WorldGenerationChestItem[] BuildJungle(
        IWorldGenerationVanillaRandom random,
        VanillaWorldGenerationBootstrapState1458 bootstrap,
        ChestPlacementState1458 state,
        int primary,
        int floorY,
        double rockLayer,
        int lavaLine,
        int worldHeight)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (floorY < rockLayer)
            return BuildUnderground(random, bootstrap, primary, water: false, jungle: true, state);
        if (floorY < worldHeight - 250)
            return BuildCavern(random, bootstrap, primary, floorY, lavaLine, water: false, jungle: true, state);
        return BuildUnderworld(random, bootstrap, primary, shadow: false, jungle: true, state);
    }

    internal static WorldGenerationChestItem[] BuildWater(
        IWorldGenerationVanillaRandom random,
        VanillaWorldGenerationBootstrapState1458 bootstrap,
        int primary,
        int floorY,
        double rockLayer,
        int lavaLine,
        int worldHeight)
    {
        if (floorY < rockLayer)
            return BuildUnderground(random, bootstrap, primary, water: true, jungle: false, state: null);
        if (floorY < worldHeight - 250)
            return BuildCavern(random, bootstrap, primary, floorY, lavaLine, water: true, jungle: false, state: null);
        return BuildUnderworld(random, bootstrap, primary, shadow: false);
    }

    private static WorldGenerationChestItem[] BuildUnderground(
        IWorldGenerationVanillaRandom random,
        VanillaWorldGenerationBootstrapState1458 bootstrap,
        int primary,
        bool water,
        bool jungle,
        ChestPlacementState1458? state)
    {
        var items = new List<WorldGenerationChestItem>(18);
        if (primary > 0)
        {
            Add(items, primary, 1);
            if (water && random.Next(2) == 0)
                Add(items, 4425, 1);
            if (water && random.Next(2) == 0)
                Add(items, 4460, 1);
        }
        else
        {
            Add(items, UndergroundPrimary[random.Next(UndergroundPrimary.Length)], 1);
            if (random.Next(20) == 0)
                Add(items, 997, 1);
            else if (random.Next(20) == 0)
            {
                Add(items, 930, 1);
                Add(items, 931, random.Next(25, 51));
            }
        }

        FillUnderground(random, bootstrap, items);
        AddJungleTail(random, items, jungle, state);
        return items.ToArray();
    }

    internal static void FillUnderground(IWorldGenerationVanillaRandom random,
        VanillaWorldGenerationBootstrapState1458 bootstrap, List<WorldGenerationChestItem> items)
    {
        if (random.Next(3) == 0)
            Add(items, 166, random.Next(10, 20));
        if (random.Next(5) == 0)
            Add(items, 52, 1);
        if (random.Next(3) == 0)
            Add(items, 965, random.Next(50, 101));
        if (random.Next(2) == 0)
            Add(items, random.Next(2) == 0 ? bootstrap.IronBar : bootstrap.SilverBar, random.Next(5, 15));
        if (random.Next(2) == 0)
            Add(items, random.Next(2) == 0 ? 40 : 42, random.Next(25, 50));
        if (random.Next(2) == 0)
            Add(items, 28, random.Next(3, 6));
        if (random.Next(3) > 0)
            Add(items, UndergroundPotions[random.Next(UndergroundPotions.Length)], random.Next(1, 3));
        if (random.Next(3) != 0)
            Add(items, 2350, random.Next(2, 5));
        if (random.Next(2) == 0)
            Add(items, 8, random.Next(10, 21));
        if (random.Next(2) == 0)
            Add(items, 72, random.Next(50, 90));
    }

    private static WorldGenerationChestItem[] BuildCavern(
        IWorldGenerationVanillaRandom random,
        VanillaWorldGenerationBootstrapState1458 bootstrap,
        int primary,
        int floorY,
        int lavaLine,
        bool water,
        bool jungle,
        ChestPlacementState1458? state)
    {
        var items = new List<WorldGenerationChestItem>(20);
        if (primary > 0)
        {
            Add(items, primary, 1);
            if (jungle && state is not null)
            {
                if (!state.LivingMahoganyWandsGenerated || random.Next(5) == 0)
                {
                    state.LivingMahoganyWandsGenerated = true;
                    Add(items, 3360, 1);
                    Add(items, 3361, 1);
                }
                if (random.Next(10) == 0)
                    Add(items, 4426, 1);
                if (random.Next(10) == 0)
                    Add(items, 5525, 1);
            }
            if (water && random.Next(2) == 0)
                Add(items, 4425, 1);
            if (water && random.Next(2) == 0)
                Add(items, 4460, 1);
        }
        else
        {
            int candidate = random.Next(7);
            if (floorY > lavaLine && random.Next(20) == 0)
            {
                Add(items, 906, 1);
            }
            else if (random.Next(15) == 0)
            {
                Add(items, 997, 1);
            }
            else if (candidate == 6)
            {
                Add(items, 930, 1);
                Add(items, 931, random.Next(25, 51));
            }
            else
            {
                Add(items, CavernPrimary[candidate], 1);
            }
        }

        FillCavern(random, bootstrap, items);
        AddJungleTail(random, items, jungle, state);
        return items.ToArray();
    }

    internal static void FillCavern(IWorldGenerationVanillaRandom random,
        VanillaWorldGenerationBootstrapState1458 bootstrap, List<WorldGenerationChestItem> items)
    {
        if (random.Next(5) == 0)
            Add(items, 43, 1);
        if (random.Next(3) == 0)
            Add(items, 167, 1);
        if (random.Next(4) == 0)
            Add(items, 51, random.Next(25, 51));
        if (random.Next(2) == 0)
            Add(items, random.Next(2) == 0 ? bootstrap.GoldBar : bootstrap.SilverBar, random.Next(3, 11));
        if (random.Next(2) == 0)
            Add(items, random.Next(2) == 0 ? 41 : 279, random.Next(25, 51));
        if (random.Next(2) == 0)
            Add(items, 188, random.Next(3, 6));
        if (random.Next(3) > 0)
            Add(items, CavernPotionsA[random.Next(CavernPotionsA.Length)], random.Next(1, 3));
        if (random.Next(3) > 1)
            Add(items, CavernPotionsB[random.Next(CavernPotionsB.Length)], random.Next(1, 3));
        if (random.Next(2) == 0)
            Add(items, 2350, random.Next(2, 5));
        if (random.Next(2) == 0)
            Add(items, random.Next(2) == 0 ? 8 : 282, random.Next(15, 31));
        if (random.Next(2) == 0)
            Add(items, 73, random.Next(1, 3));
    }

    private static WorldGenerationChestItem[] BuildUnderworld(
        IWorldGenerationVanillaRandom random,
        VanillaWorldGenerationBootstrapState1458 bootstrap,
        int primary,
        bool shadow,
        bool jungle = false,
        ChestPlacementState1458? state = null)
    {
        var items = new List<WorldGenerationChestItem>(20);
        if (primary > 0)
        {
            Add(items, primary, 1);
            if (shadow && random.Next(5) == 0)
                Add(items, 5010, 1);
            if (shadow && random.Next(10) == 0)
                Add(items, 4443, 1);
            if (shadow && random.Next(10) == 0)
                Add(items, 4737, 1);
            if (shadow && random.Next(10) == 0)
                Add(items, 4551, 1);
        }
        else
        {
            Add(items, UndergroundPrimary[random.Next(4)], 1);
        }

        FillUnderworld(random, bootstrap, items);
        AddJungleTail(random, items, jungle, state);
        return items.ToArray();
    }

    internal static void FillUnderworld(IWorldGenerationVanillaRandom random,
        VanillaWorldGenerationBootstrapState1458 bootstrap, List<WorldGenerationChestItem> items)
    {
        if (random.Next(3) == 0)
            Add(items, 167, 1);
        if (random.Next(2) == 0)
            Add(items, random.Next(2) == 0 ? 117 : bootstrap.GoldBar, random.Next(15, 30));
        if (random.Next(2) == 0)
            Add(items, random.Next(2) == 0 ? 265 : (bootstrap.SilverOre == 168 ? 4915 : 278), random.Next(50, 75));
        if (random.Next(2) == 0)
            Add(items, 227, random.Next(15, 21));
        if (random.Next(4) > 0)
            Add(items, UnderworldPotionsA[random.Next(UnderworldPotionsA.Length)], random.Next(1, 3));
        if (random.Next(3) > 0)
            Add(items, UnderworldPotionsB[random.Next(UnderworldPotionsB.Length)], random.Next(1, 3));
        if (random.Next(3) == 0)
        {
            // AddBuriedChest draws the stack before choosing Recall/Return potion.
            int stack = random.Next(1, 3);
            Add(items, random.Next(2) == 0 ? 2350 : 4870, stack);
        }
        if (random.Next(2) == 0)
            Add(items, random.Next(2) == 0 ? 8 : 282, random.Next(15, 30));
        if (random.Next(2) == 0)
            Add(items, 73, random.Next(2, 5));
    }

    private static void AddJungleTail(
        IWorldGenerationVanillaRandom random,
        List<WorldGenerationChestItem> items,
        bool jungle,
        ChestPlacementState1458? state)
    {
        if (!jungle)
            return;
        ArgumentNullException.ThrowIfNull(state);
        if (random.Next(4) == 0)
            Add(items, 2204, 1);
        if (random.Next(50) == 0)
            Add(items, 753, 1);
    }

    internal static void Add(List<WorldGenerationChestItem> items, int itemType, int stack)
    {
        if (items.Count >= WorldGenerationChestRules.VanillaItemSlotCount)
            throw new InvalidOperationException("Pinned Terraria AddBuriedChest branch exceeded the vanilla 40-slot chest capacity.");
        if (!VanillaItemIds.TryCreate(itemType, out ItemTypeId type) || type.IsNone)
            throw new InvalidOperationException($"Pinned Terraria chest item id {itemType} is outside the runtime vanilla catalog.");
        items.Add(new WorldGenerationChestItem(stack, type));
    }
}
