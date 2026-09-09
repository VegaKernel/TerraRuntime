using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GraniteBiome1458Tests
{
    // Official 1.4.5.8 GraniteBiome.Place on solid, cavern and mixed metadata
    // fixtures, with ice and lava-boundary variants; every normalized16-byte cell.
    [Theory]
    [InlineData(0,1458,1960233424,"072E38CCEBDFA5153E32328949FC38E135F9EC65B8A393AD88A8A30C5F2968E2")]
    [InlineData(0,42,1634773126,"072E38CCEBDFA5153E32328949FC38E135F9EC65B8A393AD88A8A30C5F2968E2")]
    [InlineData(0,8675309,2125665252,"072E38CCEBDFA5153E32328949FC38E135F9EC65B8A393AD88A8A30C5F2968E2")]
    [InlineData(1,1458,1960233424,"88E9E2E9E2EA4DC7C90F5A0A24E5446F2D36EA8889278F60C8F872B5906CE9A1")]
    [InlineData(1,42,1634773126,"88E9E2E9E2EA4DC7C90F5A0A24E5446F2D36EA8889278F60C8F872B5906CE9A1")]
    [InlineData(1,8675309,2125665252,"88E9E2E9E2EA4DC7C90F5A0A24E5446F2D36EA8889278F60C8F872B5906CE9A1")]
    [InlineData(2,1458,888791937,"D593D8B9280F4B3FA8B0ACEFD074C44E8B2612F76A3705F3C7473A4B085AEB5D")]
    [InlineData(2,42,1385359230,"6F8D7496394400FE543E07FDB4BC0F3BC2D819E6B5633BEDF0F3DF3262E99201")]
    [InlineData(2,8675309,1385707481,"773DD201153EAA438E159E3CAAD1F796966C4CD78056A6D0E7739BB421ACB68F")]
    [InlineData(3,1458,888791937,"217AEDC76D3211D6F3D33CF5144B7353841575ADE1E4ED2EE3C1535E04F2E74E")]
    [InlineData(3,42,1385359230,"15E5EA252F339E741D6C2424452AEF97B3D5C0B1222AEC046DF05305733650A5")]
    [InlineData(3,8675309,1385707481,"1D8128E0E14ACDD643FB2491EF8A44819B3E52BABB52966304CECC178B389361")]
    [InlineData(4,1458,888791937,"40E0EAA8F3EE48DB0DFB0A25A0ED9BB14753D8F31A53E0438C4298A89DDFFAB4")]
    [InlineData(4,42,1385359230,"7D04DB5BA30DD07631E665F693972928951FDB71812E21ADDA6B07B41090E0FB")]
    [InlineData(4,8675309,1385707481,"650E5561074B2FD97E34FB8FEACF3AED090D6DDA158C297A0BC6B1AD1480AC30")]
    public void Pressure_material_cleanup_and_decoration_match_official(int fixture, int seed, int next, string hash)
    {
        var store = new WorldTileStore(new(600,400));
        for (int x = 0; x < 600; x++)
        for (int y = 0; y < 400; y++)
        {
            bool active = (fixture == 0 || (x/11+y/7)%3 != 0) && (x != 300 || y != 200);
            WorldTile tile = new()
            {
                Type = fixture >= 2 ? (ushort)((x+y)%17 == 0 ? 7 : (x/13+y/9)%2 == 0 ? 59 : 1) : (ushort)1,
                Wall = (ushort)(fixture >= 2 ? (x+y)%2 == 0 ? 62 : 0 : 2),
                Flags = active ? WorldTileFlags.Active : 0
            };
            if (fixture == 3 && x == 340 && y == 200) tile.Type = 161;
            if (fixture >= 2)
            {
                tile.LiquidAmount = 123; tile.LiquidKind = (WorldLiquidKind)(y%4);
                tile.FrameX = 18; tile.FrameY = 36; tile.Shape = 1;
                tile.TileColor = 3; tile.WallColor = 4; tile.Flags |= WorldTileFlags.WireRed;
            }
            store.Tiles[store.GetUncheckedIndex(x,y)] = tile;
        }
        var random = new RandomAdapter(seed);
        Assert.True(new GraniteBiome1458(store,random,seed,fixture == 4 ? 230 : 175,TestContext.Current.CancellationToken)
            .Place(300,200,out var area));
        Assert.Equal(next,random.Next());
        Assert.Equal(hash,Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(store.Tiles))));
        Assert.Equal(fixture < 2 ? new(299,199,3,3) : new WorldTileRegion(265,165,71,71), area);
    }

    [Fact]
    public void Place_rechecks_occupied_origin_before_mutation_or_random()
    {
        var store = new WorldTileStore(new(600,400));
        store.Tiles[store.GetUncheckedIndex(300,200)] = new() { Type = 1, Flags = WorldTileFlags.Active };
        var random = new RandomAdapter(1458);
        Assert.False(new GraniteBiome1458(store,random,1458,175,TestContext.Current.CancellationToken).Place(300,200,out _));
        Assert.Equal(new RandomAdapter(1458).Next(),random.Next());
        Assert.Equal(1,store.Get(300,200).Type);
    }

    [Fact]
    public void Cancellation_is_checked_before_pressure_simulation_or_mutation()
    {
        var store = new WorldTileStore(new(600,400));
        var random = new RandomAdapter(1458);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new GraniteBiome1458(store,random,1458,175,cancellation.Token).Place(300,200,out _));
        Assert.Equal(new RandomAdapter(1458).Next(),random.Next());
        foreach (WorldTile tile in store.Tiles) Assert.Equal(default,tile);
    }

    [Fact]
    public void Structure_records_are_detached_and_bounded_by_width_scaled_count()
    {
        var workspace = new Workspace(600,400);
        WorldTileRegion[] areas = [new(200,100,100,100)];
        workspace.SetVanillaGraniteRegions(areas);
        areas[0] = default;
        Assert.Equal(new(200,100,100,100),workspace.VanillaGraniteRegions[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.SetVanillaGraniteRegions(new WorldTileRegion[17]));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min,int max) => random.Next(min,max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
