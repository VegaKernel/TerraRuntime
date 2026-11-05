using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixHouseYDraw1458Tests
{
    // Untouched TerrariaServer.exe 1.4.5.8 UnifiedRandom/WorldGenRange on
    // raw59 Remix seed42 bounds, source JSON SHA139AE2ACC62908B3...
    [Fact]
    public void First_cave_chest_offer_pays_ordinary_then_remix_y_draw()
    {
        var store = new WorldTileStore(new WorldDimensions(4200, 1200));
        var random = new StopAtRemixYRandom(42);
        var context = new BuriedChestContext1458
        {
            Height = 1200, WorldSurface = 337, RockLayer = 757, LavaLine = 421,
            CopperBar = 20, IronBar = 704, SilverBar = 705, GoldBar = 706,
            TungstenIsSilverTier = false, DesertHiveHigh = 0, DesertHiveLow = 0,
            HellChestItem = []
        };
        var chest = new BuriedChest1458(store, random, context);
        var pass = new BuriedChestsPass1458(chest, store, random,
            worldSurfaceHigh: 312, rockLayer: 757,
            oceanLevel: (337 + 757) / 2d + 40d, beachDistance: 380,
            cancellation: CancellationToken.None, worldSurface: 337, remix: true);
        Assert.Throws<StopAtRemixYException>(() => pass.Apply());
        Assert.Equal("35:41=39;10:16=10;35:41=35;2:3=2;20:4180=720;544:970=655;337:800=672",
            random.CallsString());
        Assert.Equal((uint)7, random.Index);
        Assert.Equal("a6742649518f08cabfff1c253e3fda6d473f9968fb0fa8a48f2ea2d3c01d75aa",
            random.SeedArrayHash());
        Assert.Equal(1101493307, random.Next());
    }

    [Fact]
    public void First_house_offer_pays_ordinary_then_remix_y_draw()
    {
        var world = new Workspace(4200, 1200);
        Assert.True(world.TrySetLayers(337, 757));
        world.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.Remix,
            VanillaSecretWorldSeed1458.None));
        world.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458
        {
            CopperOre = 7, IronOre = 6, SilverOre = 9, GoldOre = 8,
            CopperBar = 20, IronBar = 704, SilverBar = 705, GoldBar = 706,
            HellChestItems = [], TreeX = [], TreeStyle = [], CaveBackX = [],
            CaveBackStyle = [], ForestBackgroundStyles = []
        });
        world.SetVanillaTerrainState(new(337, 757, 337, 757,
            200, 312, 700, 800));
        var random = new StopAtRemixYRandom(42);
        var state = new ChestPlacementState1458();
        state.EnsureInitialized(new Context(world, random), world);
        var pass = new ChestPlacementPass1458(ChestPlacementStage1458.BuriedChests, state);
        MethodInfo offers = typeof(ChestPlacementPass1458).GetMethod("PlaceHouseOffers",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        TargetInvocationException thrown = Assert.Throws<TargetInvocationException>(() =>
            offers.Invoke(pass, [new Context(world, random), world, random, 1, 0]));
        Assert.IsType<StopAtRemixYException>(thrown.InnerException);
        Assert.Equal("80:4120=2779;332:970=421;337:800=395", random.CallsString());
        Assert.Equal((uint)3, random.Index);
        Assert.Equal("e2dfdf924f9fee510d25da437c4d31f6c2bb473c605bc18295d21fb25811bb6e",
            random.SeedArrayHash());
        Assert.Equal(1122627734, random.Next());
    }

    private sealed class Context(Workspace world, IWorldGenerationVanillaRandom random)
        : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => new(Provider1458.GeneratorId,
            "RemixYDraw", 42, 4200, 1200);
        public IWorldGenerationWorkspace Workspace => world;
        public IWorldGenerationMetadataWorkspace Metadata => world;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null) { }
    }

    private sealed class StopAtRemixYException : Exception { }
    private sealed class UnexpectedDrawException : Exception { }

    private sealed class StopAtRemixYRandom(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private readonly List<(int Min, int Max, int Value)> calls = [];
        private bool awaitingRemixY;
        public uint Index => (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", Private)!
            .GetValue(random)!;
        public string CallsString() => string.Join(';', calls.Select(static call =>
            $"{call.Min}:{call.Max}={call.Value}"));
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
        public int Next(int minimum, int maximum)
        {
            if (awaitingRemixY && (minimum != 337 || maximum != 800))
                throw new UnexpectedDrawException();
            int value = random.Next(minimum, maximum);
            calls.Add((minimum, maximum, value));
            if (minimum == 337 && maximum == 800)
                throw new StopAtRemixYException();
            if ((minimum == 544 || minimum == 332) && maximum == 970)
                awaitingRemixY = true;
            return value;
        }
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
