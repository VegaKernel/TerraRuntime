using TerraRuntime.Core;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using TerraRuntime.Contracts.Gameplay;
using System.IO.Compression;
using Xunit;
namespace TerraRuntime.Tests;

public sealed class GenerationSourceSmoothingSpecial1458Tests
{
    private static readonly Lazy<JsonDocument> Original = new(() =>
    {
        using var resource = typeof(GenerationSourceSmoothingSpecial1458Tests).Assembly.GetManifestResourceStream("GenerationSourceSmoothingSpecial1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    });
    public static IEnumerable<object[]> OriginalRows()
    {
        for (int id = 0; id < Original.Value.RootElement.GetProperty("rows").GetArrayLength(); id++) yield return [id];
    }
    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Existing_source_smoothing_matches_actual_original_composite_shapes_and_mutable_solidity(int id)
    {
        const int width = 32, height = 64;
        JsonElement row = Original.Value.RootElement.GetProperty("rows")[id];
        bool[] phase = Original.Value.RootElement.GetProperty("phase").EnumerateArray().Select(x => x.GetBoolean()).ToArray();
        int seed = row.GetProperty("seed").GetInt32(), type = row.GetProperty("type").GetInt32(), phaseMode = row.GetProperty("phaseMode").GetInt32(), mask = row.GetProperty("mask").GetInt32(), shape = row.GetProperty("shape").GetInt32();
        var w = new Workspace(width, height); w.InitializeVanillaGenerationSolidity();
        for (int i = 0; i < phase.Length; i++) w.SetVanillaGenerationSolid(new TileTypeId(i), phase[i]);
        w.SetVanillaGenerationSolid(new TileTypeId(484), false);
        var rng = new RandomAdapter(seed); var framer = new GenerationTileFraming1458(w.TileStore, rng, w, true);
        // Mutate after constructing the framer: dynamic owner, not an initial snapshot.
        w.SetVanillaGenerationSolid(new TileTypeId(225), phaseMode == 0);
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
        {
            ref var tile = ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(x, y)]; tile.Type = 1; tile.FrameX = -1; tile.FrameY = -1; tile.Wall = (ushort)((x + y) % 4); tile.TileColor = (byte)(x % 31); tile.WallColor = (byte)(y % 31);
            tile.Flags = (x % 2 == 0 ? WorldTileFlags.WireRed : 0) | (y % 2 == 0 ? WorldTileFlags.WireBlue : 0) | (x % 3 == 0 ? WorldTileFlags.WireGreen : 0) | (y % 3 == 0 ? WorldTileFlags.WireYellow : 0) | (x % 5 == 0 ? WorldTileFlags.Actuator : 0) | (x % 7 == 0 ? WorldTileFlags.InvisibleBlock : 0) | (y % 7 == 0 ? WorldTileFlags.FullbrightBlock : 0) | (x % 11 == 0 ? WorldTileFlags.InvisibleWall : 0) | (y % 11 == 0 ? WorldTileFlags.FullbrightWall : 0);
            tile.LiquidAmount = 123; tile.LiquidKind = (WorldLiquidKind)((x + y) % 4);
        }
        ref var center = ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(16, 32)]; center.Flags |= WorldTileFlags.Active; center.Type = (ushort)type; center.TrySetSourceShape(shape >= 5, (byte)(shape % 5));
        (int X, int Y, int Bit)[] neighbours = { (16, 31, 8), (16, 33, 4), (15, 32, 2), (17, 32, 1) };
        foreach (var n in neighbours) { ref var tile = ref w.TileStore.Tiles[w.TileStore.GetUncheckedIndex(n.X, n.Y)]; if ((mask & n.Bit) != 0) tile.Flags |= WorldTileFlags.Active; tile.Type = 225; }
        framer.SmoothSourceSlope(16, 32, false);
        Assert.Equal(row.GetProperty("grid").GetString(), GenerationNaturalPrefix49Tests.GridHash(w));
        Assert.Equal(row.GetProperty("cursor").GetUInt32() + ":" + string.Join(',', row.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32())), rng.State());
        Assert.Equal(row.GetProperty("next").GetInt32(), rng.Next());
    }
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public string State()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            return typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random) + ":" + string.Join(',', (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!);
        }
        public int Next() => random.Next(); public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
