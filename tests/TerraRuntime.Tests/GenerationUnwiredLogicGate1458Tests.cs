using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GenerationUnwiredLogicGate1458Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        using Stream compressed = typeof(GenerationUnwiredLogicGate1458Tests).Assembly
            .GetManifestResourceStream("GenerationUnwiredLogicGate1458")!;
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using JsonDocument source = JsonDocument.Parse(gzip);
        foreach (JsonElement row in source.RootElement.EnumerateArray())
            yield return [row.GetProperty("seed").GetInt32(), row.GetProperty("style").GetInt32(),
                row.GetProperty("gap").GetInt32(), row.GetProperty("pattern").GetInt32(),
                row.GetProperty("state").GetInt32(), row.GetProperty("Grid").GetString()!,
                row.GetProperty("Random").GetString()!, row.GetProperty("Next").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Lamp_destruction_refreshes_unwired_gate_like_original_server(
        int seed, int style, int gap, int pattern, int state,
        string expectedGrid, string expectedRandom, int expectedNext)
    {
        // Actual untouched whole KillTile419 -> Wiring.PokeLogicGate, gen/loading true.
        // Mode0 and mode2, each repeated twice, agree on every cell and physical RNG slot.
        var workspace = Create(style, gap, pattern, state);
        var random = new RandomAdapter(seed);
        new GenerationTileFraming1458(workspace.TileStore, random).KillTile(40, 40);
        Assert.Equal(expectedGrid, GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(expectedRandom, GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false));
        Assert.Equal(expectedNext, random.Next());
    }

    [Theory]
    [InlineData(WorldTileFlags.WireRed)]
    [InlineData(WorldTileFlags.WireBlue)]
    [InlineData(WorldTileFlags.WireGreen)]
    [InlineData(WorldTileFlags.WireYellow)]
    public void Connected_gate_requires_wiring_owner_before_lamp_or_rng_mutation(WorldTileFlags wire)
    {
        var workspace = Create(0, 0, 0, 0);
        ref WorldTile gate = ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(40, 41)];
        gate.Flags |= wire;
        string before = GenerationNaturalPrefix49Tests.GridHash(workspace);
        var random = new RandomAdapter(1458);
        string beforeRandom = GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false);
        Assert.Throws<NotSupportedException>(() =>
            new GenerationTileFraming1458(workspace.TileStore, random).KillTile(40, 40));
        Assert.Equal(before, GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal(beforeRandom, GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false));
    }

    private static Workspace Create(int style, int gap, int pattern, int state)
    {
        var workspace = new Workspace(80, 120);
        ref WorldTile At(int x, int y) => ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, y)];
        At(40, 40) = new WorldTile { Type = 419, FrameX = 1, Flags = WorldTileFlags.Active };
        for (int y = 41; y < 41 + gap; y++)
            At(40, y) = new WorldTile
            {
                Type = 419,
                Flags = WorldTileFlags.Active,
                FrameX = (short)(pattern == 1 ? 18 : pattern == 2 ? y % 2 * 18 :
                    pattern == 3 && y == 41 ? 36 : pattern == 4 && y == 40 + gap ? 36 : 0)
            };
        At(40, 41 + gap) = new WorldTile
        {
            Type = 420,
            Flags = WorldTileFlags.Active,
            FrameX = (short)state,
            FrameY = (short)(style * 18)
        };
        return workspace;
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
