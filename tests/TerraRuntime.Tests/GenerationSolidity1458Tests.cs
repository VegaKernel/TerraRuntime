using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GenerationSolidity1458Tests
{
    [Theory]
    [InlineData(1458, 50)]
    [InlineData(42, 50)]
    [InlineData(1458, 99)]
    [InlineData(42, 99)]
    public void Framing_borrows_live_candidate_solidity_without_changing_configured_collision(int seed, int phase)
    {
        // Actual registered first/later liquid pass observations, with all Main.tileSolid entries.
        // This is an explicit boundary fixture, separate from genuine executor prefix tests.
        using Stream compressed = typeof(GenerationSolidity1458Tests).Assembly
            .GetManifestResourceStream("GenerationSolidity1458")!;
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using JsonDocument source = JsonDocument.Parse(gzip);
        JsonElement row = source.RootElement.EnumerateArray().Single(value =>
            value.GetProperty("seed").GetInt32() == seed && value.GetProperty("phase").GetInt32() == phase);

        var workspace = new Workspace(32, 32);
        workspace.InitializeVanillaGenerationSolidity();
        var random = new NoRandom();
        var generation = new GenerationTileFraming1458(workspace.TileStore, random, workspace,
            useWorldGenerationSolidity: true);
        var configured = new GenerationTileFraming1458(workspace.TileStore, random, workspace,
            useWorldGenerationSolidity: false);
        foreach (string boundary in new[] { "entry", "exit" })
        {
            bool[] expected = row.GetProperty(boundary).EnumerateArray().Select(value => value.GetBoolean()).ToArray();
            Assert.Equal(754, expected.Length);
            for (int type = 0; type < expected.Length; type++)
                workspace.SetVanillaGenerationSolid(new TileTypeId(type), expected[type]);
            for (int type = 0; type < expected.Length; type++)
            {
                Assert.Equal(expected[type], workspace.IsVanillaGenerationSolid(new TileTypeId(type)));
                Assert.Equal(expected[type], generation.GenerationSolid(checked((ushort)type)));
                Assert.Equal(VanillaTileCollisionCatalog.IsSolid(new TileTypeId(type)),
                    configured.GenerationSolid(checked((ushort)type)));
            }
        }
    }

    private sealed class NoRandom : IWorldGenerationVanillaRandom
    {
        public int Next() => throw new InvalidOperationException("Solidity reads must not consume RNG.");
        public int Next(int exclusiveMax) => Next();
        public int Next(int inclusiveMin, int exclusiveMax) => Next();
        public double NextDouble() => Next();
        public void NextBytes(byte[] bytes) => throw new InvalidOperationException("Unexpected RNG.");
    }
}
