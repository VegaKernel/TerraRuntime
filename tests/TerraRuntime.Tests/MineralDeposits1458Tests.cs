using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class MineralDeposits1458Tests
{
    // Official Linux TerrariaServer 1.4.5.8 4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034.
    // Independent TileRunner calls, three depth bands, full 16-byte normalized cells in x-major order.
    [Theory]
    [InlineData(51, 42, 1598527415, "8033BC0B6170B5B7D68CE66DCDEE4F845846CAAAAB47A118485224F81C1B0F35")]
    [InlineData(51, 1458, 897100550, "9BFC696FB4B4DA72B11C667A38847CBE450004EBEE631AF3950D01F051A4FCF0")]
    [InlineData(59, 42, 1931498481, "4B41ABB68B5E7CAEC86A3326198676D1D8CF0B825563A0219EF4FFE596276359")]
    [InlineData(59, 1458, 261501614, "09EEB83A89A885FD27D0071AF353D4D3628A29B84002C995CD3F9B6727D612F4")]
    [InlineData(123, 42, 908459501, "5437B0A8AE3DA0E3D897D5C0166BAFDC6124FD32B601E2AE1EABD53407C46547")]
    [InlineData(123, 1458, 679914814, "2CA4282B39AFF09405E367E10FF368A8416CF8341517078A2E42D17E7B165A36")]
    [InlineData(6, 42, 908459501, "179DD6438BD4F54396AD6909166F26ACE64BB1D3234F3DBBD0AD9AE32FC060DE")]
    [InlineData(6, 1458, 679914814, "183AB0D7FDE85F71DCE480607D56E8CD9688D96B2E346FB6AD9390A0028C2341")]
    [InlineData(7, 42, 908459501, "5AAAD944184BD4A8E6B120566DAAA76EAB2DECDFE4F641A66EA7FDCB4268E6B8")]
    [InlineData(7, 1458, 679914814, "512865C6E73C5FC2296D259E817D4DC2106B1A3625A5211B066652879348C415")]
    [InlineData(8, 42, 908459501, "8274FAD3D9052772F4E2BBD4559FBB19E1AF885024228D6D6143DEC9D47DF2C1")]
    [InlineData(8, 1458, 679914814, "70E0D959C1CBFCA1DD213EA8D61F84CF4111B39D6FD687903AABCB24544AEA6E")]
    [InlineData(9, 42, 908459501, "9F1F07A447FF908032DA2745BEA0F08E12894CF6024CF34C37C739CCDF6267A5")]
    [InlineData(9, 1458, 679914814, "9C751DAE15C88CED11725CA61A356FEB6D59A5BC69C78E1EC0B2ABB498B1472D")]
    [InlineData(166, 42, 908459501, "C002A1A5301C1052E250FE2FC28F9CD14B34C5E74908BEC33C8DCC2E6978B24E")]
    [InlineData(166, 1458, 679914814, "4E68355400E427BF4DFC2B3F125889FE1104AA88FB61C4254119BC593ECD0136")]
    [InlineData(167, 42, 908459501, "3419AB69DBAEFD9A0E71E731A993213193E5D064B33E011CD017B86BCA15E21D")]
    [InlineData(167, 1458, 679914814, "DA3FD5E1092C7E19C5776D6C6F2641C69AE881B7A857DF6D4CCB7E3C2E2B5664")]
    [InlineData(168, 42, 908459501, "E1F4465CA1E07A053E5D509A27B6312A546810528CC7298D2564C5C868DD2EBC")]
    [InlineData(168, 1458, 679914814, "4704036F833362497EB0C8FAD376C72B41205A4F3BCD10A668A0F5D9051A0D27")]
    [InlineData(169, 42, 908459501, "8D1826DF75C1202BA4F2F364BCC6358CCFE747B48461BC306B6764AC4A88260E")]
    [InlineData(169, 1458, 679914814, "1853505FFE9293C99F5ECBC5EBF71F047A30974F5089BE9625AE1C0B53F9F8A0")]
    [InlineData(22, 42, 908459501, "9E322A671C8DCA4C64D1B52C6F696A0F0F5740F90FBCF4281B79051F3B51D87C")]
    [InlineData(22, 1458, 679914814, "13017721836AE82910FAA30CF8D12224860772763AACEB8828EDFC9BCABF5CCA")]
    [InlineData(204, 42, 908459501, "D3F17D28EAC247A5505E30F7D0E8433DD80B5C4C9A5D88C7C7D3B4A98B5D78D6")]
    [InlineData(204, 1458, 679914814, "ECA3AD976C2576DEF967A35F0220E6A300183BDCF56EA6DDC0E4C6ED8A66BE83")]
    public void Deposit_runner_matches_official_mixed_material_cells_and_rng(int type, int seed, int next, string hash)
    {
        var store = new WorldTileStore(new(120, 600));
        ushort[] materials = [0, 1, 53, 59, 60, 147, 161, 367, 368, 396, 397, 7, 51, 165, 189, 45];
        for (int x = 0; x < 120; x++)
        for (int y = 0; y < 600; y++)
        {
            store.Tiles[store.GetUncheckedIndex(x,y)] = new WorldTile
            {
                Type = materials[(x / 3 + y / 5) % materials.Length], Wall = 187,
                Flags = WorldTileFlags.WireRed | ((x + y) % 5 != 0 ? WorldTileFlags.Active : 0),
                LiquidAmount = 123, LiquidKind = (WorldLiquidKind)(y % 4),
                FrameX = 18, FrameY = 36, Shape = 1, TileColor = 3, WallColor = 4
            };
        }
        var random = new RandomAdapter(seed);
        var runner = new SmallTerrainRunner1458(store, random, 100, new(300,450),
            TestContext.Current.CancellationToken, rockLayer: 200);
        foreach (int y in new[] { 250, 350, 550 })
            runner.Run(60, y, type == 51 ? 10 : type == 59 ? 5 : 11, type == 51 ? 3 : type == 59 ? 39 : 49, type,
                addTile: type == 51, speedX: type == 51 ? 1 : 0, speedY: type == 51 ? -1 : 0,
                ignoreTileType: type == 59 ? 53 : -1, overRide: type != 51);
        Assert.Equal(next, random.Next());
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(store.Tiles))));
    }

    [Theory]
    [InlineData(59, null, 53)]
    [InlineData(59, 200d, -1)]
    [InlineData(123, 200d, 53)]
    [InlineData(226, 200d, -1)]
    [InlineData(51, 200d, -1)]
    public void Unsupported_runner_context_rejects_before_random_or_mutation(int type, double? rock, int ignore)
    {
        var store = new WorldTileStore(new(120,600));
        var random = new RandomAdapter(42);
        var runner = new SmallTerrainRunner1458(store, random, 100, new(300,450), default, rockLayer: rock);
        Assert.Throws<ArgumentOutOfRangeException>(() => runner.Run(60,250,5,39,type,ignoreTileType:ignore));
        Assert.Equal(new RandomAdapter(42).Next(), random.Next());
        foreach (var tile in store.Tiles) Assert.Equal(default, tile);
    }

    [Fact]
    public void Island_anchors_are_detached_and_bounded()
    {
        var workspace = new Workspace(120,600);
        VanillaSkyIsland1458[] anchors = [new(60,100,0,false), new(90,110,0,true)];
        workspace.SetVanillaSkyIslands(anchors);
        anchors[0] = default;
        Assert.Equal(new(60,100,0,false), workspace.VanillaSkyIslands[0]);
        Assert.True(workspace.VanillaSkyIslands[1].IsLake);
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.SetVanillaSkyIslands(new VanillaSkyIsland1458[10]));
    }

    [Fact]
    public void Mountain_cave_anchors_are_detached_and_bounded()
    {
        var workspace = new Workspace(120,600);
        WorldGenerationPoint[] caves = [new(60,100)];
        workspace.SetVanillaMountainCaves(caves);
        caves[0] = default;
        Assert.Equal(new(60,100), workspace.VanillaMountainCaves[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.SetVanillaMountainCaves(new WorldGenerationPoint[9]));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min,max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
