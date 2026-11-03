using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GenerationSpeleothemContinuation1458Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        using Stream compressed = typeof(GenerationSpeleothemContinuation1458Tests).Assembly
            .GetManifestResourceStream("GenerationSpeleothemContinuation1458")!;
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using JsonDocument source = JsonDocument.Parse(gzip);
        foreach (JsonElement row in source.RootElement.EnumerateArray())
            yield return [row.GetProperty("seed").GetInt32(), row.GetProperty("support").GetInt32(),
                row.GetProperty("form").GetInt32(), row.GetProperty("halo").GetBoolean(),
                row.GetProperty("Grid").GetString()!, row.GetProperty("Random").GetString()!,
                row.GetProperty("Next").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Framing_preserves_source_boulder_rejection_and_ordered_kill_continuation(
        int seed, int support, int form, bool halo, string grid, string rng, int next)
    {
        var workspace = new Workspace(80, 120);
        ref WorldTile At(int x, int y) => ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, y)];
        At(40, 40) = new WorldTile
        {
            Type = 165,
            FrameX = 414,
            FrameY = (short)(form == 0 ? 0 : form == 1 ? 36 : form == 2 ? 72 : 90),
            Flags = WorldTileFlags.Active
        };
        if (form < 2)
            At(40, 41) = new WorldTile
            {
                Type = 165,
                FrameX = 414,
                FrameY = (short)(form == 0 ? 18 : 54),
                Flags = WorldTileFlags.Active
            };
        int supportY = form is 0 or 2 ? 39 : form == 1 ? 42 : 41;
        At(40, supportY) = new WorldTile
        {
            Type = (ushort)support,
            Flags = support == 0 ? WorldTileFlags.None : WorldTileFlags.Active
        };
        if (halo)
            At(41, 40) = new WorldTile { Type = 3, FrameX = 18, Flags = WorldTileFlags.Active };
        var random = new RandomAdapter(seed);
        new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(40, 40);
        Assert.Equal(grid, GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(rng, GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false));
        Assert.Equal(next, random.Next());
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
