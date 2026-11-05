using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixDesertBastPriority1458Tests
{
    // Untouched TerrariaServer.exe 1.4.5.8 DesertHouseBuilder priority helper;
    // original three-seed JSON SHA4102E4F1659F65D5CB78E97BA8002D406EDC9EDE163E090B6B3B4C3FBC4A6644.
    [Theory]
    [InlineData(1, 237820880)]
    [InlineData(42, 302596119)]
    [InlineData(1458, 1335025742)]
    public void Remix_desert_room_below_rock_layer_returns_before_first_x_draw(int seed, int next)
    {
        var world = new Workspace(600, 500);
        Assert.True(world.TrySetLayers(50, 100));
        world.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.Remix,
            VanillaSecretWorldSeed1458.None));
        world.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458
        {
            CopperOre = 7, IronOre = 6, SilverOre = 9, GoldOre = 8,
            CopperBar = 20, IronBar = 704, SilverBar = 705, GoldBar = 706,
            ExtraBastStatueCountMax = 2,
            HellChestItems = [], TreeX = [], TreeStyle = [], CaveBackX = [],
            CaveBackStyle = [], ForestBackgroundStyles = []
        });
        world.SetVanillaTerrainState(new(50, 100, 50, 100, 200, 312, 400, 450));
        var random = new RandomAdapter(seed);
        random.NextDouble(); // DesertHouseBuilder constructor's PotentiallyConvertToSeedHouse.
        var state = new ChestPlacementState1458();
        state.EnsureInitialized(new Context(world, random, seed), world);
        int[] beforeSeeds = random.Seeds.ToArray();
        uint beforeIndex = random.Index;
        Assert.Equal((uint)1, beforeIndex);
        MethodInfo priority = typeof(CaveHousePlacement1458).GetMethod("PlacePriorityTool",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        priority.Invoke(null, [world, state, random,
            new CaveHouseRoom1458[] { new(50, 110, 16, 10) }, CaveHouseType1458.Desert]);
        Assert.Equal(beforeIndex, random.Index);
        Assert.Equal(beforeSeeds, random.Seeds);
        Assert.Equal(0, state.CaveHouseBastStatueCount);
        Assert.Equal(next, random.Next());
    }

    [Fact]
    public void Ordinary_desert_room_below_rock_layer_still_attempts_statue()
    {
        var world = new Workspace(600, 500);
        Assert.True(world.TrySetLayers(50, 100));
        world.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458
        {
            CopperOre = 7, IronOre = 6, SilverOre = 9, GoldOre = 8,
            CopperBar = 20, IronBar = 704, SilverBar = 705, GoldBar = 706,
            ExtraBastStatueCountMax = 2,
            HellChestItems = [], TreeX = [], TreeStyle = [], CaveBackX = [],
            CaveBackStyle = [], ForestBackgroundStyles = []
        });
        world.SetVanillaTerrainState(new(50, 100, 50, 100, 200, 312, 400, 450));
        var random = new RandomAdapter(42);
        random.NextDouble();
        var state = new ChestPlacementState1458();
        state.EnsureInitialized(new Context(world, random, 42), world);
        MethodInfo priority = typeof(CaveHousePlacement1458).GetMethod("PlacePriorityTool",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        priority.Invoke(null, [world, state, random,
            new CaveHouseRoom1458[] { new(50, 110, 16, 10) }, CaveHouseType1458.Desert]);
        Assert.Equal((uint)21, random.Index); // The two ten-attempt floor/roof loops still run.
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public uint Index => (uint)typeof(VanillaUnifiedRandom1458).GetField("inext",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!;
        public int[] Seeds => (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray",
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!;
        public int Next() => random.Next();
        public int Next(int maximum) => random.Next(maximum);
        public int Next(int minimum, int maximum) => random.Next(minimum, maximum);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }

    private sealed class Context(Workspace world, IWorldGenerationVanillaRandom random, int seed)
        : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => new(Provider1458.GeneratorId,
            "RemixBastPriority", (ulong)seed, 600, 500);
        public IWorldGenerationWorkspace Workspace => world;
        public IWorldGenerationMetadataWorkspace Metadata => world;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null) { }
    }
}
