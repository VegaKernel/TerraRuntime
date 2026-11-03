using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationGlowTulipWaterCheck1458Tests
{
    private static readonly JsonDocument Original = Read();
    private static JsonDocument Read()
    {
        using var stream = typeof(GenerationGlowTulipWaterCheck1458Tests).Assembly
            .GetManifestResourceStream("GenerationGlowTulipWaterCheck1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    public static IEnumerable<object[]> Rows() =>
        Enumerable.Range(0, Original.RootElement.GetArrayLength()).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(Rows))]
    public void Original_WaterCheck_glow_tulip_death_uses_existing_generation_framer(int index)
    {
        // Actual untouched WorldGen.WaterCheck, server2/en-US/full Main initialization, repeated twice.
        var row = Original.RootElement[index];
        int profile = row.GetProperty("profile").GetInt32();
        var world = new Workspace(80, 80);
        for (int x = 38; x <= 42; x++)
            world.TileStore.Set(x, 41, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var target = new WorldTile
        {
            Type = 656, FrameX = (short)(profile * 18), Wall = 15, TileColor = 7, WallColor = 9,
            LiquidAmount = row.GetProperty("amount").GetByte(),
            LiquidKind = (WorldLiquidKind)row.GetProperty("kind").GetByte(),
            Flags = WorldTileFlags.Active | WorldTileFlags.WireRed | WorldTileFlags.WireGreen |
                WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightWall
        };
        world.TileStore.Set(40, 40, target);
        var support = world.TileStore.Get(40, 41);
        if (profile == 1) Assert.True(support.TrySetSourceShape(true, 3));
        if (profile == 2) support.Flags |= WorldTileFlags.Actuator | WorldTileFlags.Inactive;
        if (profile == 3)
        {
            support.Flags &= ~WorldTileFlags.Active;
            world.TileStore.Set(39, 40, new WorldTile { WallColor = 5, Flags = WorldTileFlags.InvisibleWall });
        }
        world.TileStore.Set(40, 41, support);
        var random = new RandomAdapter(row.GetProperty("seed").GetInt32());
        var framing = new GenerationTileFraming1458(world.TileStore, random, world);
        var outcome = framing.WaterCheckLiquidsDuringGeneration();
        Assert.True(outcome.IsApplied, outcome.ToString());
        Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(world));
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(row.GetProperty("cursor").GetInt32(),
            Convert.ToInt32(typeof(VanillaUnifiedRandom1458).GetField("inext", hidden)!.GetValue(random.Source)));
        Assert.Equal(row.GetProperty("slots").EnumerateArray().Select(v => v.GetInt32()).ToArray(),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", hidden)!.GetValue(random.Source)!);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
        var actual = world.TileStore.LiquidUpdates.CaptureActiveSnapshot()
            .Select(v => (X: v.TileIndex / 80, Y: v.TileIndex % 80, v.Kill, v.Delay)).ToArray();
        var expected = row.GetProperty("queues").EnumerateArray()
            .Select(v => (X: v.GetProperty("x").GetInt32(), Y: v.GetProperty("y").GetInt32(),
                Kill: v.GetProperty("kill").GetInt32(), Delay: v.GetProperty("delay").GetInt32())).ToArray();
        Assert.Equal(expected, actual);
        Assert.Equal(row.GetProperty("bufferCount").GetInt32(), world.TileStore.LiquidUpdates.BufferedCount);
        var flags = row.GetProperty("flags").EnumerateArray()
            .ToDictionary(v => (v.GetProperty("x").GetInt32(), v.GetProperty("y").GetInt32()),
                v => (v.GetProperty("checking").GetBoolean(), v.GetProperty("skip").GetBoolean()));
        for (int x = 0; x < 80; x++) for (int y = 0; y < 80; y++)
        {
            var value = (world.TileStore.LiquidUpdates.IsCheckingLiquid(x, y),
                world.TileStore.LiquidUpdates.IsSkipNextUpdate(x, y));
            Assert.Equal(flags.GetValueOrDefault((x, y)), value);
        }
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        internal readonly VanillaUnifiedRandom1458 Source = new(seed);
        public int Next() => Source.Next();
        public int Next(int maximum) => Source.Next(maximum);
        public int Next(int minimum, int maximum) => Source.Next(minimum, maximum);
        public double NextDouble() => Source.NextDouble();
        public void NextBytes(byte[] buffer) => Source.NextBytes(buffer);
    }
}
