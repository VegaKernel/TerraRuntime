using System.Buffers.Binary;
using System.Security.Cryptography;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class TerrainReference1458Tests
{
    // Independent TerrariaServer 1.4.5.8 Linux executable, SHA-256
    // 4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034.
    // Invoke TerrainPass.ApplyPass with ordinary flags, FlatBeachPadding=5 and Reset beach inputs.
    // Hash x-major cells: UInt16 type, Int16 frameX/frameY (little endian), Byte active.
    // RunPass reseeds before Terrain; the two liquid draws belong to Terrain, not TerrainLayers.
    [Theory]
    [InlineData(4200, 1200, 1458, 745, 801, 201896576, "C01700E853493D2040A536F300C45C71AC51A6E6CF530744C77753F90B7D99C5")]
    [InlineData(4200, 1200, 42, 759, 830, 2065422597, "4583AB801DC8635964F5777B604A6DA869313B56EE4E35B5B405D7A0F5F8420E")]
    [InlineData(4200, 1200, 8675309, 895, 945, 1973570068, "ADB588954EAB3B3E7F148B083DD03E05441B66506FB2FA69ACB0C2033A5D0FC1")]
    [InlineData(6400, 1800, 1458, 1237, 1301, 634235657, "77F8FC1FE4444568D59F177F04D74825ABC0F07F9F03B128776A9E84E547ED1C")]
    [InlineData(6400, 1800, 42, 1273, 1351, 527953614, "E8A6F0085D8B72BDC6224630CA00FFE20E6977A5B496B1BB836B86113F47CD48")]
    [InlineData(6400, 1800, 8675309, 1294, 1345, 74419241, "9138856158EF929AEE80C16AD8DA9E7BEBE17B02644393ACDEEA904D7485E454")]
    [InlineData(8400, 2400, 1458, 1601, 1656, 1397897340, "068E6AD1D0693F0F9EA31BEC3C6077FE9E3890AC3D986BAA4F9495FEA7CE41AE")]
    [InlineData(8400, 2400, 42, 1637, 1708, 630836701, "41C8037B9E38A2AE665CA8C3EDE946663E13CF54D03611965B118940FF8D908D")]
    [InlineData(8400, 2400, 8675309, 1657, 1727, 79562546, "2B72629036FC4F79F4968352122FFAC6EA42A950667B0F633437B16118296EF0")]
    public void Production_terrain_matches_official_cells_liquid_lines_and_next_rng(
        int width, int height, int seed, int waterLine, int lavaLine, int nextRandom, string tileHash)
    {
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "TerrainReference", (ulong)seed, width, height)
        {
            SeedText = seed.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        var builder = new Builder();
        new SourceBackedFinal1458().BuildPlan(in request, builder);
        var workspace = new Workspace(width, height);
        builder.Get("Reset").Execute(new Context(request, workspace, new RandomAdapter(seed)));
        var terrainRandom = new RandomAdapter(seed);

        builder.Get("Terrain").Execute(new Context(request, workspace, terrainRandom));

        Assert.Equal(new VanillaLiquidLines1458(waterLine, lavaLine), workspace.VanillaLiquidLines);
        Assert.Equal(nextRandom, terrainRandom.Next());
        Assert.Equal(tileHash, HashCells(workspace));

        // The artificial bridge transfers state only; it must not need or advance a new RNG.
        builder.Get("TerrainLayers").Execute(new Context(request, workspace, null));
        Assert.Equal(new VanillaLiquidLines1458(waterLine, lavaLine), workspace.VanillaLiquidLines);
    }

    [Fact]
    public void Terrain_bridge_rejects_missing_liquid_state_instead_of_rerolling_it()
    {
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "MissingTerrainState", 1458, 4200, 1200);
        var builder = new Builder();
        new SourceBackedFinal1458().BuildPlan(in request, builder);
        var workspace = new Workspace(4200, 1200);
        builder.Get("Reset").Execute(new Context(request, workspace, new RandomAdapter(1458)));
        Assert.True(workspace.TrySetLayers(300, 600));
        workspace.SetVanillaTerrainState(new TerrainGenerationState1458(300, 600, 275, 550, 250, 275, 500, 600));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            builder.Get("TerrainLayers").Execute(new Context(request, workspace, new RandomAdapter(1458))));

        Assert.Contains("did not publish its liquid lines", error.Message);
        Assert.Null(workspace.VanillaLiquidLines);
    }

    private static string HashCells(Workspace workspace)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] column = new byte[workspace.HeightTiles * 7];
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                var tile = workspace.TileStore.Get(x, y);
                Span<byte> cell = column.AsSpan(y * 7, 7);
                BinaryPrimitives.WriteUInt16LittleEndian(cell, checked((ushort)tile.TileType.Value));
                BinaryPrimitives.WriteInt16LittleEndian(cell[2..], tile.FrameX);
                BinaryPrimitives.WriteInt16LittleEndian(cell[4..], tile.FrameY);
                cell[6] = tile.IsActive ? (byte)1 : (byte)0;
            }
            hash.AppendData(column);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private sealed class Builder : IWorldGenerationPlanBuilder
    {
        private readonly Dictionary<string, IWorldGenerationPass> passes = [];
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) => passes.Add(descriptor.Id.Value, pass);
        public IWorldGenerationPass Get(string name) => passes["terraria:1.4.5.8/" + name];
    }

    private sealed class Context(
        WorldGenerationRequest request, Workspace workspace, IWorldGenerationVanillaRandom? random) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom? VanillaRandom => random;
        public CancellationToken CancellationToken => CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null) { }
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int maxValue) => random.Next(maxValue);
        public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
