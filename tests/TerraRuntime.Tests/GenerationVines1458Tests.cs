using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GenerationVines1458Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        using Stream compressed = typeof(GenerationVines1458Tests).Assembly
            .GetManifestResourceStream("GenerationVines1458")!;
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using JsonDocument source = JsonDocument.Parse(gzip);
        foreach (JsonElement row in source.RootElement.EnumerateArray())
            yield return [row.GetProperty("seed").GetInt32(), row.GetProperty("vine").GetUInt16(),
                row.GetProperty("support").GetUInt16(), row.GetProperty("half").GetBoolean(),
                row.GetProperty("slope").GetByte(), row.GetProperty("state").GetInt32(),
                row.GetProperty("gridHash").GetString()!, row.GetProperty("rngHash").GetString()!,
                row.GetProperty("next").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Vine_families_match_original_support_conversion_cascade_and_physical_rng(
        int seed, ushort vine, ushort support, bool half, byte slope, int state,
        string expectedGrid, string expectedRandom, int expectedNext)
    {
        // Actual untouched 1.4.5.8 TileFrame, two identical repetitions of every profile.
        // Includes inactive/actuated anchors and independent halfbrick/slope combinations.
        var workspace = new Workspace(48, 48);
        WorldTileStore store = workspace.TileStore;
        ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
        At(24, 24) = new WorldTile
        {
            Type = vine,
            FrameX = 18,
            FrameY = 36,
            Wall = 7,
            TileColor = 3,
            WallColor = 4,
            Flags = WorldTileFlags.Active | WorldTileFlags.WireRed
        };
        At(24, 25) = new WorldTile { Type = vine, FrameY = 18, Flags = WorldTileFlags.Active };
        At(24, 23) = new WorldTile
        {
            Type = support,
            Flags = (state == 1 ? WorldTileFlags.None : WorldTileFlags.Active) |
                    (state == 2 ? WorldTileFlags.Inactive : WorldTileFlags.None)
        };
        Assert.True(At(24, 23).TrySetSourceShape(half, slope));
        var random = new RandomAdapter(seed);
        new GenerationTileFraming1458(store, random).TileFrame(24, 24);

        Assert.Equal(expectedGrid, GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(expectedRandom, GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false));
        Assert.Equal(expectedNext, random.Next());
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int exclusiveMax) => random.Next(exclusiveMax);
        public int Next(int inclusiveMin, int exclusiveMax) => random.Next(inclusiveMin, exclusiveMax);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
