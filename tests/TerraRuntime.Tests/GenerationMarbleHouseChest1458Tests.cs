using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationMarbleHouseChest1458Tests
{
    // Untouched TerrariaServer.exe 1.4.5.8 WorldGen.AddBuriedChest
    // (300,450,0,false,style,false,21), 600x800 stone floor500, plain Anniversary.
    // Independent source JSON SHA256 style51 A20E2F95..., style50 647FED29...
    [Theory]
    [InlineData(51, 1, "50x1p0;21x3p0;188x3p0;2351x2p0;2350x3p0", 21,
        "b9eda5641dd2ce2ab39461f8c4c2621101025aaf652d24a14f9c017656af54ac", 200550235)]
    [InlineData(51, 42, "5011x1p38;21x4p0;279x33p0;188x3p0;296x2p0;2350x2p0;5643x28p0;5508x1p0", 28,
        "a06e1a200519bb3ebfeaf0ff2b217c4c34a1d01e31f6f44465d5d3c9abe40757", 317314903)]
    [InlineData(51, 1458, "53x1p70;167x1p0;21x6p0", 19,
        "88db2929620e73ce283e39192f2001a5cf7b411df9e31b08220fd687792e2f66", 1845354897)]
    [InlineData(50, 1, "50x1p0;21x3p0;188x3p0;2351x2p0;2350x3p0", 21,
        "b9eda5641dd2ce2ab39461f8c4c2621101025aaf652d24a14f9c017656af54ac", 200550235)]
    [InlineData(50, 42, "5011x1p38;21x4p0;279x33p0;188x3p0;296x2p0;2350x2p0;5643x28p0;5508x1p0", 28,
        "a06e1a200519bb3ebfeaf0ff2b217c4c34a1d01e31f6f44465d5d3c9abe40757", 317314903)]
    [InlineData(50, 1458, "53x1p70;167x1p0;21x6p0", 19,
        "88db2929620e73ce283e39192f2001a5cf7b411df9e31b08220fd687792e2f66", 1845354897)]
    public void Stone_house_chest_styles50_51_placement_loot_and_full_rng_match_original(
        int style, int seed, string inventory, uint expectedIndex, string expectedSeeds, int next)
    {
        var world = new Workspace(600, 800);
        world.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.CelebrationMk10,
            VanillaSecretWorldSeed1458.None));
        Assert.True(world.TrySetLayers(150, 250));
        for (int x = 0; x < 600; x++)
            for (int y = 500; y < 800; y++)
            {
                var tile = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
                world.TileStore.Set(x, y, in tile);
            }
        var random = new RandomAdapter(seed);
        var context = new BuriedChestContext1458
        {
            Height = 800, WorldSurface = 150, RockLayer = 250, LavaLine = 0,
            CopperBar = 20, IronBar = 22, SilverBar = 21, GoldBar = 19,
            TungstenIsSilverTier = false, DesertHiveHigh = 100, DesertHiveLow = 400,
            HellChestItem = [274, 220, 112, 218, 3019]
        };
        var placer = new BuriedChest1458(world.TileStore, random, context, workspace: world);
        Assert.True(placer.TryAdd(300, 450, out int chestX, out int chestY,
            0, notNearOtherChests: false, chestStyle: style, trySlope: false, chestTileType: 21));
        Assert.Equal((299, 499), (chestX, chestY)); // Source out coordinate is the row above the floor.
        BuriedChestResult1458 chest = Assert.Single(placer.Chests);
        Assert.Equal((299, 498, (ushort)21, style),
            (chest.Left, chest.Top, chest.TileType, chest.Style));
        Assert.Equal(inventory, string.Join(';', chest.Items.Where(static item => item.Stack > 0)
            .Select(static item => $"{item.ItemType.Value}x{item.Stack}p{item.Prefix.Value}")));
        for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                WorldTile tile = world.TileStore.Get(299 + dx, 498 + dy);
                Assert.True(tile.IsActive);
                Assert.Equal((ushort)21, tile.Type);
                Assert.Equal((short)(style * 36 + dx * 18), tile.FrameX);
                Assert.Equal((short)(dy * 18), tile.FrameY);
            }
        Assert.Equal(expectedIndex, random.Index);
        Assert.Equal(expectedSeeds, random.SeedArrayHash());
        Assert.Equal(next, random.Next());
    }

    [Theory]
    [InlineData(49, 21)] // Neighbouring style has no direct source fixture.
    [InlineData(50, 467)] // Stone-house style is only qualified on tile 21.
    [InlineData(51, 467)]
    public void Neighbouring_unqualified_special_families_refuse_before_rng(
        int style, ushort chestTileType)
    {
        var world = new Workspace(80, 80);
        world.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.CelebrationMk10,
            VanillaSecretWorldSeed1458.None));
        var random = new RandomAdapter(1458);
        int expectedNext = new RandomAdapter(1458).Next();
        var context = new BuriedChestContext1458
        {
            Height = 80, WorldSurface = 30, RockLayer = 40, LavaLine = 60,
            CopperBar = 20, IronBar = 22, SilverBar = 21, GoldBar = 19,
            TungstenIsSilverTier = false, DesertHiveHigh = 0, DesertHiveLow = 0,
            HellChestItem = []
        };
        var placer = new BuriedChest1458(world.TileStore, random, context, workspace: world);
        Assert.Throws<NotSupportedException>(() =>
            placer.TryAdd(30, 30, out _, out _, chestStyle: style,
                chestTileType: chestTileType));
        Assert.Equal(expectedNext, random.Next());
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        public uint Index => (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", Private)!
            .GetValue(random)!;
        public string SeedArrayHash()
        {
            int[] values = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", Private)!
                .GetValue(random)!;
            byte[] bytes = new byte[values.Length * 4];
            for (int index = 0; index < values.Length; index++)
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), values[index]);
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        public int Next() => random.Next();
        public int Next(int maximum) => random.Next(maximum);
        public int Next(int minimum, int maximum) => random.Next(minimum, maximum);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
