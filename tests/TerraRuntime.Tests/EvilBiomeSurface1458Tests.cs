using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class EvilBiomeSurface1458Tests
{
    // Complete official evil branches on identical synthetic input/state (not a full-world prefix claim).
    // Width4200 generates two regions: placement, caves, surface, altar RNG and deferred hearts must compose.
    [Theory]
    [InlineData(0,42,"6B40161F97D32FEC91FE24995BCE323126A78FE3176B162970F90D0D1A6C627F",169770834)]
    [InlineData(0,1458,"CE93AFD494B88978A1F301597184C81477820D4C96BDF50ACAA74D0E9E75B62B",354724496)]
    [InlineData(0,8675309,"C21CD00D8A2A7241254F340487F2A933D10950B69D48A8B3A94DD6D1087778A9",72742426)]
    [InlineData(1,42,"E7D17A761430715DE8CF7B8762D0068464D7A96422D260E6DCB8C257837B9CAE",1184634706)]
    [InlineData(1,1458,"27DEF45EA150BE5E2CCDA15093E160E0B47184A950183A5569BBF50141500013",1163669732)]
    [InlineData(1,8675309,"147FE174D26A4FB4CD0C3E0929E571065656A5BF11FBEF65181E5E4765668213",131449814)]
    [InlineData(0,42,"717B948D2F38E00244F7FA238962EBDB01BF33A96BA0DB6DB531615FDB7E045F",879894591,false)]
    [InlineData(0,1458,"CDAC7EE848B97D56306EEAE11B580413EECEA6DC94A4C6537D17BD383181BFDB",1000693205,false)]
    [InlineData(0,8675309,"A02BB15DEF59A4FBA8B48E581BC53CAF4DBFFF960AA8580828B42C428AE15877",1836229873,false)]
    [InlineData(1,42,"0DF13EC2470716B38ACAFCE29D86DB3FDBB16DE55DE4BA97F438F827E92DFD25",978069721,false)]
    [InlineData(1,1458,"3682FD1E354463E9163401F1C4920E11D60A6C29BFE653FB10771F98055EDE11",385968403,false)]
    [InlineData(1,8675309,"C8A80996890B392E68C7E081E5E81B5DC312533C08714EA83BFB4ED359880626",383486812,false)]
    public void Complete_evil_branch_matches_official_cells_and_next_random(int fixture, int seed, string hash, int next, bool crimson = true)
    {
        var workspace = new Workspace(4200,600);
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(seed),4200,crimson));
        Assert.True(workspace.TrySetLayers(220,300));
        workspace.SetVanillaTerrainState(new(220,300,160,280,140,200,250,320));
        workspace.SetVanillaUndergroundDesertRegion(0,0,1,1);
        workspace.SetVanillaLiquidLines(400,500);
        ushort[] palette = [0,1,2,59,60,53,161,396,397];
        for (int x = 0; x < 4200; x++)
        for (int y = 0; y < 600; y++)
        {
            ushort material = fixture == 0 ? (ushort)(y < 200 ? 0 : 1) : palette[(x/31+y/17)%palette.Length];
            workspace.TileStore.Set(x,y,new WorldTile { Type = material, Wall = (ushort)(fixture == 1 ? (x%2 == 0 ? 216 : 187) : 0),
                FrameX = 18, FrameY = 36, Flags = y >= 160 ? WorldTileFlags.Active : WorldTileFlags.None });
        }
        var request = new WorldGenerationRequest(Provider1458.GeneratorId,"Evil kernel",(ulong)seed,4200,600)
        { SeedText = seed.ToString(), Options = new(WorldGenerationGameMode.Classic,crimson ? WorldGenerationEvil.Crimson : WorldGenerationEvil.Corruption) };
        var random = new RandomAdapter(seed);
        new MidPass1458(MidStage1458.Corruption,new MidState1458()).Execute(new Context(request,workspace,random));
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var column = new WorldTile[600];
        for (int x = 0; x < 4200; x++)
        {
            for (int y = 0; y < 600; y++) column[y] = workspace.TileStore.Get(x,y);
            digest.AppendData(MemoryMarshal.AsBytes(column.AsSpan()));
        }
        Assert.Equal(hash,Convert.ToHexString(digest.GetHashAndReset())); Assert.Equal(next,random.Next());
    }

    // Direct official SpreadGrass, generating/loading flags true and initialized empty server network arrays.
    // Includes depth-limited full strip, lava-interrupted surface and an exposed underground strip.
    [Theory]
    [InlineData(23,0,521,"12B4AC99A65428257CF79B98356FFB0A41159857AD8371FF0660198E2C86515F")]
    [InlineData(23,1,42,"68FC8B7FA633BAF60C6E30D779E368A0EFA87FEB1D281D7B127617141B666885")]
    [InlineData(23,2,451,"449835F1B100DCCA70690581C59E51B95AA578A99E4089840F26AFD3D0590588")]
    [InlineData(23,3,521,"C34901959358210C92F2420FCA32074D83DCBEE219BFF6B71025FC01D7BF0DB7")]
    [InlineData(199,0,521,"0346592C754CA5C376F79AD96CD9689D8C111295E5823EBB48CD9BD654EFE335")]
    [InlineData(199,1,42,"2F08DE1EFC2C89FD686A61B8837C6DD15B31A0CFD1CF74AF32BB2B30F5D4FCC7")]
    [InlineData(199,2,451,"9C0BC2DE0044191BF9034CD1475B80DC1D5E37DC9767F73949627094F36A7050")]
    [InlineData(199,3,521,"8E693040B65C30E7D25B604950AD8CDBA08C0765F46B6B7D9943B435E8969DBB")]
    [InlineData(661,0,1591,"3714D5D50CFF046FD249822F504008EBE0EE717208507A6A5F5BFB7FEE4C4328")]
    [InlineData(661,1,42,"89AD87C2C25332E6A70E6F977A8F40A6FC931DE6475E8142E632730F28E438C4")]
    [InlineData(661,2,451,"1A84E25B265FF98671E73ADC3A1041EB2EC1BFAB3562DD2540026BEBC5A73866")]
    [InlineData(661,3,1591,"99908BB79029D2D884B27CBB53F6254C63B2CA7F4C28043084E1B6AE14CE7ADE")]
    [InlineData(662,0,1591,"1651893715356BCBD4629A521523A36261E7776FA77CA6D45D5E5F4B11BBF673")]
    [InlineData(662,1,42,"78825BC562AB1823144AE7935B2E0DCB76AA8A57540EABF5325C5C8E1779510B")]
    [InlineData(662,2,451,"296BB8E092DC769B71022C95E33BE29676ADE2F7323BA93577EF92537834EDB1")]
    [InlineData(662,3,1591,"A79431BF568EEE06540FEA7CDC4342E0B6FD7ACA60E178DE829E7AA9A191CCC0")]
    public void Grass_matches_official_full_cells_and_depth_boundary(int grass, int fixture, int changed, string hash)
    {
        var store = new WorldTileStore(new WorldDimensions(2000,600));
        ushort dirt = (ushort)(grass >= 661 ? 59 : 0);
        for (int x = 0; x < 2000; x++)
        for (int y = 0; y < 600; y++)
        {
            bool active = y >= 160;
            if (fixture == 1 && x >= 500 && x < 850 && y >= 170 && y < 400 && (x+y)%13 < 3) active = false;
            if (fixture == 2) active = x >= 400 && x <= 850 && y == 260;
            bool web = fixture == 3 && y == 159 && x % 7 == 0;
            if (web) active = true;
            var tile = new WorldTile { Type = dirt, Wall = 83, FrameX = 18, FrameY = 36, TileColor = 3, WallColor = 4,
                Shape = (byte)(active ? 0 : 1), Flags = WorldTileFlags.WireRed | (active ? WorldTileFlags.Active : WorldTileFlags.None) };
            if (web) tile.Type = 51;
            if (fixture == 1 && x % 43 == 0 && y == 159) { tile.LiquidAmount = 123; tile.LiquidKind = WorldLiquidKind.Lava; }
            store.Set(x,y,tile);
        }
        var random = new RandomAdapter(1458);
        new EvilBiomeSurface1458(store,random,140,220,TestContext.Current.CancellationToken)
            .SpreadGrass(600,fixture == 2 ? 260 : 160,dirt,grass is 199 or 662);
        int actualChanged = 0;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var column = new WorldTile[600];
        for (int x = 0; x < 2000; x++)
        {
            for (int y = 0; y < 600; y++) { column[y] = store.Get(x,y); if (column[y].Type == grass) actualChanged++; }
            digest.AppendData(MemoryMarshal.AsBytes(column.AsSpan()));
        }
        Assert.Equal(changed,actualChanged);
        Assert.Equal(hash,Convert.ToHexString(digest.GetHashAndReset())); Assert.Equal(906992634,random.Next());
    }

    private sealed class Context(WorldGenerationRequest request, Workspace workspace, IWorldGenerationVanillaRandom random) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => request;
        public IWorldGenerationWorkspace Workspace => workspace;
        public IWorldGenerationMetadataWorkspace Metadata => workspace;
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom VanillaRandom => random;
        public CancellationToken CancellationToken => TestContext.Current.CancellationToken;
        public void ReportProgress(double fraction, string? message = null) { }
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
