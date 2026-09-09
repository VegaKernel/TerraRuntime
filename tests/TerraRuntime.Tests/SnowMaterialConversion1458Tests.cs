using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SnowMaterialConversion1458Tests
{
    // Complete official Slush delegate, full normalized cells; no random draws.
    [Theory]
    [InlineData(0,"EE4F1A3743C1C3E392D64540F33682F8283D4D151F146236E61AA5EA50EEF385")]
    [InlineData(1,"9D26250A4E0C65F7D3EF6174A5AD90B2A3912B3D37D43D2A0DFBAFE37AC49F3C")]
    [InlineData(2,"C2EFA77D5E4C308D63CA25E46E3B24660B3AA8257AC63E1901792251D276AFD8")]
    public void Stored_material_scan_matches_official(int fixture, string expected)
    {
        var workspace = new Workspace(200,120);
        int[] left = Enumerable.Range(0,120).Select(y => 30+y%9).ToArray();
        int[] right = Enumerable.Range(0,120).Select(y => 150-y%11).ToArray();
        workspace.SetVanillaSnowBounds(20,100,left,right);
        ushort[] materials = fixture == 1 ? [1,123,59,60,70,71,72,0,25] : [1,123,59];
        ushort[] jungle = [60,70,71,72];
        for (int x = 0; x < 200; x++)
        for (int y = 0; y < 120; y++)
        {
            ushort type = materials[(x/7+y/5)%materials.Length];
            if (fixture == 2 && x%30 == 0 && y%20 == 0) type = jungle[(x/30+y/20)%4];
            workspace.TileStore.Set(x,y,new WorldTile { Type = type, FrameX = 18, FrameY = 36, Wall = 40,
                TileColor = 3, WallColor = 4, LiquidAmount = 17,
                Flags = (x+y)%3 != 0 ? WorldTileFlags.Active : WorldTileFlags.None });
        }
        SnowMaterialConversion1458.Apply(workspace,TestContext.Current.CancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var column = new WorldTile[120];
        for (int x = 0; x < 200; x++)
        {
            for (int y = 0; y < 120; y++) column[y] = workspace.TileStore.Get(x,y);
            hash.AppendData(MemoryMarshal.AsBytes(column.AsSpan()));
        }
        Assert.Equal(expected,Convert.ToHexString(hash.GetHashAndReset()));
    }

    [Theory]
    [InlineData(60,true,3)] [InlineData(60,false,3)] [InlineData(60,true,4)]
    [InlineData(70,true,3)] [InlineData(70,false,3)] [InlineData(70,true,4)]
    [InlineData(71,true,3)] [InlineData(71,false,3)] [InlineData(71,true,4)]
    [InlineData(72,true,3)] [InlineData(72,false,3)] [InlineData(72,true,4)]
    public void Mud_preservation_uses_active_jungle_neighbours_in_inclusive_radius(ushort neighbour, bool active, int distance)
    {
        var workspace = SingleCellBand();
        workspace.TileStore.Set(10,10,new WorldTile { Type = 59 }); // Inactive stored Mud is still converted.
        workspace.TileStore.Set(10+distance,10,new WorldTile { Type = neighbour,
            Flags = active ? WorldTileFlags.Active : WorldTileFlags.None });
        SnowMaterialConversion1458.Apply(workspace,TestContext.Current.CancellationToken);
        Assert.Equal((ushort)(active && distance == 3 ? 59 : 224),workspace.TileStore.Get(10,10).Type);
    }

    [Fact]
    public void Bounds_are_detached_right_and_bottom_exclusive_and_unrelated_fields_survive()
    {
        var workspace = SingleCellBand();
        var original = new WorldTile { Type = 123, Wall = 40, FrameX = 18, FrameY = 36, Shape = 3,
            Flags = WorldTileFlags.WireRed | WorldTileFlags.Inactive | WorldTileFlags.InvisibleBlock,
            TileColor = 3, WallColor = 4, LiquidAmount = 17, LiquidKind = WorldLiquidKind.Shimmer };
        workspace.TileStore.Set(10,10,original); workspace.TileStore.Set(11,10,original); workspace.TileStore.Set(10,11,original);
        SnowMaterialConversion1458.Apply(workspace,TestContext.Current.CancellationToken);
        Assert.Equal(original,workspace.TileStore.Get(11,10)); Assert.Equal(original,workspace.TileStore.Get(10,11));
        original.Type = 224; Assert.Equal(original,workspace.TileStore.Get(10,10));
    }

    [Fact]
    public void Missing_bounds_fail_closed_and_cancellation_precedes_access()
    {
        var workspace = new Workspace(30,30);
        Assert.Throws<InvalidOperationException>(() => SnowMaterialConversion1458.Apply(workspace,TestContext.Current.CancellationToken));
        Assert.Throws<OperationCanceledException>(() => SnowMaterialConversion1458.Apply(workspace,new CancellationToken(true)));
    }

    private static Workspace SingleCellBand()
    {
        var workspace = new Workspace(30,30);
        var left = new int[30]; var right = new int[30]; left[10] = 10; right[10] = 11;
        workspace.SetVanillaSnowBounds(10,11,left,right);
        left[10] = 0; right[10] = 30;
        return workspace;
    }
}
