using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

/// <summary>
/// Complete-delegate comparison for the ordinary TerrariaServer 1.4.5.8 <c>Shimmer</c> pass: the candidate depth
/// band, the dungeon-opposite column band, the refusal retry loop and the retained pool position.
/// </summary>
/// <remarks>
/// Expectations were produced by running the registered official pass delegate itself — located by
/// <c>GenPassNameID.Shimmer</c> in <c>WorldGen.AddPasses()</c> and invoked through <c>GenPass.Apply</c> — on the
/// same synthetic input in both pinned dedicated-server builds. The hash covers every cell's type, wall, frames,
/// active/half-brick/slope, both liquid fields and both paint channels.
/// </remarks>
public sealed class ShimmerPass1458Tests
{
    private const int Width = 2000;
    private const int Height = 900;

    [Theory]
    [InlineData(0,-1,42,"31D10D97BB8FE863FD61E7995C0AFB02D5C53993F5728584DF6131614387872F",762676243,"1782,393")]
    [InlineData(0,-1,1458,"236CB2C6E00C05248A83557FA981F04335AF964FC1CAA4E8E63981CF8A35613A",1469327027,"1792,359")]
    [InlineData(0,-1,7,"0507DB73BC4CFFB1B18D635DAEDC53B619743062F047D76C681F8AD3E963724B",577151598,"1797,353")]
    [InlineData(0,0,42,"31D10D97BB8FE863FD61E7995C0AFB02D5C53993F5728584DF6131614387872F",762676243,"1782,393")]
    [InlineData(0,0,1458,"236CB2C6E00C05248A83557FA981F04335AF964FC1CAA4E8E63981CF8A35613A",1469327027,"1792,359")]
    [InlineData(0,0,7,"0507DB73BC4CFFB1B18D635DAEDC53B619743062F047D76C681F8AD3E963724B",577151598,"1797,353")]
    [InlineData(0,1,42,"D4C6803912B065A38526C047684A801F13875F0A62C211732B7324D6BB9E2A5F",273770267,"202,393")]
    [InlineData(0,1,1458,"546F2C6195FE663537DF44EA6DBBE3F553E2CF91B23C31EB0A27C8E411A112A1",1378309941,"212,359")]
    [InlineData(0,1,7,"2ABF6C57F5E1E1C1E21724F1ACFD0CD002A310AA5C2EE8D52CFBFDCF7154AE92",577151598,"217,353")]
    [InlineData(1,-1,42,"C12C3DE46555DE7FB6CEAB0F5FEF2096A8D230F4B431BAF906D2564D77378E04",1040995222,"1798,425")]
    [InlineData(1,-1,1458,"C098D6EB4DE4D9AD28E1CC464CF674D0839DD24B559DDA526344AB741D3D5554",1144936097,"1783,424")]
    [InlineData(1,-1,7,"EEBAE45E0D0CC85B468CC61715D5FF75964DD4BF63240398E387FA6BFE141F8B",943842353,"1795,421")]
    [InlineData(1,0,42,"C12C3DE46555DE7FB6CEAB0F5FEF2096A8D230F4B431BAF906D2564D77378E04",1040995222,"1798,425")]
    [InlineData(1,0,1458,"C098D6EB4DE4D9AD28E1CC464CF674D0839DD24B559DDA526344AB741D3D5554",1144936097,"1783,424")]
    [InlineData(1,0,7,"EEBAE45E0D0CC85B468CC61715D5FF75964DD4BF63240398E387FA6BFE141F8B",943842353,"1795,421")]
    [InlineData(1,1,42,"2D78A8B046CABF39D41DB4F11993CD2EFA7059DA8733A091C4C01C588ABDA7D2",1597571591,"218,425")]
    [InlineData(1,1,1458,"059EFF59A2513F2C8492C7D93F57DFD0DC1A6DA881339370657E18A2C84EEF7F",1668150695,"203,424")]
    [InlineData(1,1,7,"1C2275EA113E97F039B537073229F8852571F89E6977222536CB75DF979BED23",1013239802,"215,421")]
    public void Production_pass_matches_official(
        int fixture, int dungeonSide, int seed, string cellHash, int next, string position)
    {
        Workspace workspace = CreateWorkspace(fixture, dungeonSide);
        var random = new RandomAdapter(seed);
        Execute(workspace, random, TestContext.Current.CancellationToken);

        WorldGenerationPoint pool = workspace.VanillaShimmerPosition ??
            throw new InvalidOperationException("Shimmer pass did not retain a pool position.");
        string expected = $"{cellHash}|{next}|{position}";
        string actual = $"{HashCells(workspace)}|{random.Next()}|{pool.X},{pool.Y}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");

        WorldTileRegion structure = workspace.VanillaShimmerStructure ??
            throw new InvalidOperationException("Shimmer pass did not retain its protected structure.");
        Assert.Equal(new WorldTileRegion(pool.X - 100, pool.Y - 100, 200, 200), structure);
    }

    private static void Execute(Workspace workspace, IWorldGenerationVanillaRandom random, CancellationToken cancellation)
    {
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Shimmer fixture", 42,
            workspace.WidthTiles, workspace.HeightTiles) { SeedText = "42" };
        new DungeonPass1458(DungeonStage1458.Shimmer, new DungeonState1458())
            .Execute(new Context(request, workspace, random, cancellation));
    }

    private static Workspace CreateWorkspace(int fixture, int dungeonSide)
    {
        var workspace = new Workspace(Width, Height);
        workspace.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458
        {
            HellChestItems = [],
            TreeX = [],
            TreeStyle = [],
            CaveBackX = [],
            CaveBackStyle = [],
            ForestBackgroundStyles = [],
            DungeonSide = dungeonSide,
            DungeonLocation = Width / 2,
        });
        Assert.True(workspace.TrySetLayers(200, 300));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            workspace.TileStore.Set(x, y, Fixture(fixture, x, y));
        return workspace;
    }

    // Normalized 13-byte cell record matching the official probe.
    private static string HashCells(Workspace workspace)
    {
        var bytes = new byte[Width * Height * 13];
        int index = 0;
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            bytes[index++] = (byte)(tile.Type & 0xFF);
            bytes[index++] = (byte)(tile.Type >> 8);
            bytes[index++] = (byte)(tile.Wall & 0xFF);
            bytes[index++] = (byte)(tile.Wall >> 8);
            bytes[index++] = (byte)(tile.FrameX & 0xFF);
            bytes[index++] = (byte)((tile.FrameX >> 8) & 0xFF);
            bytes[index++] = (byte)(tile.FrameY & 0xFF);
            bytes[index++] = (byte)((tile.FrameY >> 8) & 0xFF);
            int slope = tile.Shape >= 2 ? tile.Shape - 1 : 0;
            bytes[index++] = (byte)((tile.IsActive ? 1 : 0) | (tile.Shape == 1 ? 2 : 0) | (slope << 2));
            bytes[index++] = tile.LiquidAmount;
            bytes[index++] = (byte)tile.LiquidKind;
            bytes[index++] = tile.TileColor;
            bytes[index++] = tile.WallColor;
        }

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTile Fixture(int fixture, int x, int y)
    {
        ReadOnlySpan<ushort> materials = [1, 1, 1, 0, 59, 147, 161, 396, 53, 60, 117, 179];
        ReadOnlySpan<ushort> walls = [1, 2, 3, 40, 61, 15, 196, 0];

        bool active = y >= 200;
        ushort type = materials[(x / 6 + y / 8) % materials.Length];
        ushort wall = walls[(x / 4 + y / 11) % walls.Length];
        byte liquid = (byte)((x + y) % 9 == 0 ? 90 : 0);
        var liquidKind = (WorldLiquidKind)((x + y) % 3);

        if (fixture == 1)
        {
            // An Ebonstone band that only the deepest candidates clear, so the retry loop really runs.
            if (y >= 300 && y <= 310) type = 25;
            if ((x / 23 + y / 19) % 7 == 0 && y > 320) active = false;
        }

        return new WorldTile
        {
            Type = type,
            Wall = wall,
            FrameX = active ? (short)0 : (short)-1,
            FrameY = active ? (short)0 : (short)-1,
            LiquidAmount = liquid,
            LiquidKind = liquidKind,
            Flags = active ? WorldTileFlags.Active : 0,
        };
    }

    private sealed class Context(WorldGenerationRequest request, Workspace workspace,
        IWorldGenerationVanillaRandom random, CancellationToken cancellation) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => cancellation;
        public void ReportProgress(double fraction, string? message = null) { }
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
