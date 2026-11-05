using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationUndergroundDesertRemixDepth1458Tests
{
    [Fact]
    public void Direct_official_rock_layer_rule_limits_only_Remix_desert_chests()
    {
        // Direct TerrariaServer 1.4.5.8 WorldGen.IsUndergroundDesert on a
        // 300x300 wall187 scene: surface100, rock120, wall at (150,120).
        // Official results: Remix at rock=true; Remix below rock=false;
        // ordinary below rock=true. The same wall remains in the 31x31 box.
        BuriedChest1458 remix = Create(VanillaSpecialWorldSeed1458.Remix);
        Assert.True(IsUndergroundDesert(remix, 150, 120));
        Assert.False(IsUndergroundDesert(remix, 150, 121));

        BuriedChest1458 ordinary = Create(VanillaSpecialWorldSeed1458.None);
        Assert.True(IsUndergroundDesert(ordinary, 150, 121));
    }

    private static BuriedChest1458 Create(VanillaSpecialWorldSeed1458 seed)
    {
        var world = new Workspace(300, 300);
        world.SetVanillaSeedProfile(new(seed, VanillaSecretWorldSeed1458.None));
        Assert.True(world.TrySetLayers(100, 120));
        var wall = new WorldTile { Wall = 187 };
        world.TileStore.Set(150, 120, in wall);
        var context = new BuriedChestContext1458
        {
            Height = 300, WorldSurface = 100, RockLayer = 120, LavaLine = 250,
            CopperBar = 20, IronBar = 22, SilverBar = 21, GoldBar = 19,
            TungstenIsSilverTier = false, DesertHiveLow = 150, DesertHiveHigh = 100,
            HellChestItem = []
        };
        return new BuriedChest1458(world.TileStore, new RandomAdapter(1458), context,
            workspace: world);
    }

    private static bool IsUndergroundDesert(BuriedChest1458 owner, int x, int y)
    {
        MethodInfo method = typeof(BuriedChest1458).GetMethod("IsUndergroundDesert",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.NotNull(method);
        return (bool)method.Invoke(owner, [x, y])!;
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
