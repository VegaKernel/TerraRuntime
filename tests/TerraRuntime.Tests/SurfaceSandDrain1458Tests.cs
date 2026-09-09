using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SurfaceSandDrain1458Tests
{
    [Fact]
    public void Production_pass_matches_official_surface_scan_and_preserves_other_liquid()
    {
        // Independent official 1.4.5.8 AddPasses / Remove Water From Sand invocation.
        // Same pinned Linux binary as TerrainReference1458Tests; isolated 1000x600 input,
        // worldSurface=200.5. Hash x-major [liquid amount, liquid kind] for every cell.
        var workspace = new Workspace(1000, 600);
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            ref WorldTile tile = ref At(workspace, x, y);
            tile.LiquidAmount = 200;
            tile.LiquidKind = (WorldLiquidKind)(y % 4);
        }

        // Source admits exactly these six types, not the evil/hallowed sand families.
        ushort[] types = [53, 396, 397, 404, 407, 151, 0, 112, 116, 234];
        for (int i = 0; i < types.Length; i++)
            SetActive(workspace, 400 + i, 150, types[i]);
        SetActive(workspace, 399, 150, 53);
        SetActive(workspace, 599, 150, 53);
        SetActive(workspace, 600, 150, 53);
        SetActive(workspace, 420, 100, 53);
        SetActive(workspace, 421, 199, 53);
        SetActive(workspace, 422, 200, 53);
        SetActive(workspace, 423, 120, 0);
        SetActive(workspace, 423, 150, 53);
        var random = new RandomAdapter(1458);
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1458), 4200, false));
        Assert.True(workspace.TrySetLayers(200.5, 300));
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "SurfaceDrainReference", 1458, 4200, 1200);
        var builder = new Builder();
        new SourceBackedFinal1458().BuildPlan(in request, builder);

        builder.Pass!.Execute(new Context(request, workspace, random));

        // No randomness is used by this stage.
        Assert.Equal(new RandomAdapter(1458).Next(), random.Next());
        for (int i = 0; i < types.Length; i++)
        {
            Assert.Equal(i < 6 ? (byte)0 : (byte)200, At(workspace, 400 + i, 149).LiquidAmount);
            Assert.Equal(200, At(workspace, 400 + i, 150).LiquidAmount); // solid is not drained
            Assert.Equal(200, At(workspace, 400 + i, 151).LiquidAmount); // below surface is untouched
        }
        Assert.Equal(200, At(workspace, 399, 149).LiquidAmount);
        Assert.Equal(0, At(workspace, 599, 149).LiquidAmount);
        Assert.Equal(200, At(workspace, 600, 149).LiquidAmount);
        Assert.Equal(200, At(workspace, 400, 99).LiquidAmount);
        Assert.Equal(0, At(workspace, 400, 100).LiquidAmount);
        Assert.Equal(200, At(workspace, 420, 100).LiquidAmount);
        Assert.Equal(0, At(workspace, 421, 198).LiquidAmount);
        Assert.Equal(200, At(workspace, 422, 199).LiquidAmount);
        Assert.Equal(200, At(workspace, 423, 119).LiquidAmount);
        Assert.Equal(200, At(workspace, 423, 149).LiquidAmount);

        byte[] cells = new byte[workspace.WidthTiles * workspace.HeightTiles * 2];
        long drained = 0;
        for (int x = 0; x < workspace.WidthTiles; x++)
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            WorldTile tile = At(workspace, x, y);
            Assert.Equal((WorldLiquidKind)(y % 4), tile.LiquidKind);
            drained += 200 - tile.LiquidAmount;
            int offset = (x * workspace.HeightTiles + y) * 2;
            cells[offset] = tile.LiquidAmount;
            cells[offset + 1] = (byte)tile.LiquidKind;
        }
        Assert.Equal(89800, drained);
        Assert.Equal("852DB98DB5764633074FD8ADB10301ADFD19C0A1105E6746F732C0AE893F1632",
            Convert.ToHexString(SHA256.HashData(cells)));
    }

    private static ref WorldTile At(Workspace workspace, int x, int y) =>
        ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x, y)];

    private static void SetActive(Workspace workspace, int x, int y, ushort type)
    {
        ref WorldTile tile = ref At(workspace, x, y);
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
    }

    private sealed class Builder : IWorldGenerationPlanBuilder
    {
        public IWorldGenerationPass? Pass { get; private set; }
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass)
        {
            if (descriptor.Id == SourceBackedPostSettle1458.RemoveWaterFromSandId)
                Pass = pass;
        }
    }

    private sealed class Context(WorldGenerationRequest request, Workspace workspace, IWorldGenerationVanillaRandom random)
        : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
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
