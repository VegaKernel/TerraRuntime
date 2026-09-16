using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

/// <summary>
/// Complete-delegate comparison for the ordinary TerrariaServer 1.4.5.8 <c>OceanCaves</c> pass.
/// </summary>
/// <remarks>
/// Expectations were produced by running the registered official pass delegate itself — located by
/// <c>GenPassNameID.OceanCaves</c> in <c>WorldGen.AddPasses()</c> and invoked through
/// <c>GenPass.Apply</c> — on the same synthetic input, in both pinned dedicated-server builds (Windows
/// <c>d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e</c> and Linux
/// <c>4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034</c>), which agreed on all 72 cases. Seeds 1 and 14 carve caves on both sides; 42 and 1458 often fail the
/// one-in-three roll, so they also cover the case where the pass legitimately changes nothing.
/// The hash covers type, wall, frames, active/half-brick/slope and both liquid fields for every cell. This closes
/// the pass on synthetic input; it is not a claim about the real generated prefix or special seeds.
/// </remarks>
public sealed class CreateOceanCaves1458Tests
{
    private const int Height = 600;

    [Theory]
    [InlineData(1200,0,-1,42,"9F4C5D5752417AA627B6250C68A352CF61D09356B439F9C7EC9EE00BB68E60EE",302596119,"0;0,0;0,0")]
    [InlineData(1200,0,-1,1458,"9F4C5D5752417AA627B6250C68A352CF61D09356B439F9C7EC9EE00BB68E60EE",1335025742,"0;0,0;0,0")]
    [InlineData(1200,0,-1,1,"3E098AC7AE073A264F58A441C89C8B444F55BF9BD0C36CE5C18A4EEEEC33734E",476723227,"1;339,156;0,0")]
    [InlineData(1200,0,-1,14,"64C0275BF93CCFC8A6D9F3E1D0A8048016ED04A8403EBE9E21FA9B8229EFDFF1",1150899001,"1;339,158;0,0")]
    [InlineData(1200,0,0,42,"D9F0DEC169269A04320C1C99CED7B99BF8837978019A4073649AEA2FAE0A04A8",1631355923,"1;864,155;0,0")]
    [InlineData(1200,0,0,1458,"9F4C5D5752417AA627B6250C68A352CF61D09356B439F9C7EC9EE00BB68E60EE",1916656655,"0;0,0;0,0")]
    [InlineData(1200,0,0,1,"6ED1B8AECEBC3E83A0BFF508B57AE2C8C6FB457786CFBD11952599966A669CF0",1340927114,"2;339,156;859,166")]
    [InlineData(1200,0,0,14,"64C0275BF93CCFC8A6D9F3E1D0A8048016ED04A8403EBE9E21FA9B8229EFDFF1",962927982,"1;339,158;0,0")]
    [InlineData(1200,0,1,42,"9F4C5D5752417AA627B6250C68A352CF61D09356B439F9C7EC9EE00BB68E60EE",302596119,"0;0,0;0,0")]
    [InlineData(1200,0,1,1458,"9F4C5D5752417AA627B6250C68A352CF61D09356B439F9C7EC9EE00BB68E60EE",1335025742,"0;0,0;0,0")]
    [InlineData(1200,0,1,1,"BF39F3C6A08F240B68F84F408192A56829DC9E7C3989B86E0BF810168AA8F6B5",558159189,"1;863,157;0,0")]
    [InlineData(1200,0,1,14,"E77267163523C7AAD012E8C19EA7F2DB6F8AF1619C3F940EB3341D59082BBB9A",1288079086,"1;853,156;0,0")]
    [InlineData(1200,1,-1,42,"EE7168EF404CF4E8F8C7ABFB97967C8AEF72FAA1498E783CDF1A91C08003DA8C",302596119,"0;0,0;0,0")]
    [InlineData(1200,1,-1,1458,"EE7168EF404CF4E8F8C7ABFB97967C8AEF72FAA1498E783CDF1A91C08003DA8C",1335025742,"0;0,0;0,0")]
    [InlineData(1200,1,-1,1,"5E3EF36B123925C3BA36231DAE6C2341BACA7D01FDD2442260433BEAB5100890",1637983373,"1;333,164;0,0")]
    [InlineData(1200,1,-1,14,"C7E82FFBC59C6D3F14554554D391AF381743223517CE4B216EC7E028E7BECB41",1807390524,"1;346,158;0,0")]
    [InlineData(1200,1,0,42,"6617F31ECD9EAF7FE8F707EC19CB248F3104728A7AC2BCC04C77673DEFBAB6C4",211412952,"1;863,153;0,0")]
    [InlineData(1200,1,0,1458,"EE7168EF404CF4E8F8C7ABFB97967C8AEF72FAA1498E783CDF1A91C08003DA8C",1916656655,"0;0,0;0,0")]
    [InlineData(1200,1,0,1,"5E3EF36B123925C3BA36231DAE6C2341BACA7D01FDD2442260433BEAB5100890",1770013527,"1;333,164;0,0")]
    [InlineData(1200,1,0,14,"C7E82FFBC59C6D3F14554554D391AF381743223517CE4B216EC7E028E7BECB41",1129571067,"1;346,158;0,0")]
    [InlineData(1200,1,1,42,"EE7168EF404CF4E8F8C7ABFB97967C8AEF72FAA1498E783CDF1A91C08003DA8C",302596119,"0;0,0;0,0")]
    [InlineData(1200,1,1,1458,"EE7168EF404CF4E8F8C7ABFB97967C8AEF72FAA1498E783CDF1A91C08003DA8C",1335025742,"0;0,0;0,0")]
    [InlineData(1200,1,1,1,"F62D7C7DB4132432905784F78616E26768249464F6DAE491088E6E5A0C32C3C8",1929327680,"1;856,168;0,0")]
    [InlineData(1200,1,1,14,"01249BFD18343DF2516E5964B4BD70DBE41C5A0C4E21F29C42E593EAD9B4DD9D",1343917749,"1;863,150;0,0")]
    [InlineData(1200,2,-1,42,"EE030E6D2DFBBED1F27530260E9588AF2743BC7966B4A4EABCEEE402DA3DD0E1",302596119,"0;0,0;0,0")]
    [InlineData(1200,2,-1,1458,"EE030E6D2DFBBED1F27530260E9588AF2743BC7966B4A4EABCEEE402DA3DD0E1",1335025742,"0;0,0;0,0")]
    [InlineData(1200,2,-1,1,"1E05D8FBC6D438A9C57D923033507244B1B6BCD80D54F5227369FAA0EC7CB257",299132592,"1;345,163;0,0")]
    [InlineData(1200,2,-1,14,"ADB1DED57305FB4095F433623F4B6C2031535E9C8E9F8182F0DAFD1AC0201855",153619314,"1;338,155;0,0")]
    [InlineData(1200,2,0,42,"29FC472EBDE897C36CDB1A323BA55D7DBC0124321CCA9D80985B6BBA5C33B9BC",211412952,"1;863,153;0,0")]
    [InlineData(1200,2,0,1458,"EE030E6D2DFBBED1F27530260E9588AF2743BC7966B4A4EABCEEE402DA3DD0E1",1916656655,"0;0,0;0,0")]
    [InlineData(1200,2,0,1,"8EB66A3378DFAABB19F2D108B4DD0E07407B241C165800972F26DF4B1AAB43B6",1728023235,"2;345,163;884,165")]
    [InlineData(1200,2,0,14,"6A334CBEE8EBD0D519B66E684A432ABEC571D51A705B51E4C33D69B557E38AD3",675984463,"2;338,155;860,156")]
    [InlineData(1200,2,1,42,"EE030E6D2DFBBED1F27530260E9588AF2743BC7966B4A4EABCEEE402DA3DD0E1",302596119,"0;0,0;0,0")]
    [InlineData(1200,2,1,1458,"EE030E6D2DFBBED1F27530260E9588AF2743BC7966B4A4EABCEEE402DA3DD0E1",1335025742,"0;0,0;0,0")]
    [InlineData(1200,2,1,1,"4768B7B540C6CB802C76D460FCCB7152B508BD0BCADC50C4491AD7DA1EFEA5E9",1929327680,"1;856,168;0,0")]
    [InlineData(1200,2,1,14,"8448CDBEF0FFA4AD3AD1A0CE5C786552CB54536CD651C38FB7010574B6563DB1",1343917749,"1;863,150;0,0")]
    [InlineData(1201,0,-1,42,"60CFCC65C4FD0231B1E6C83067EDB29BC23382A114CB1D2C5E07A820ACB1BCAE",302596119,"0;0,0;0,0")]
    [InlineData(1201,0,-1,1458,"60CFCC65C4FD0231B1E6C83067EDB29BC23382A114CB1D2C5E07A820ACB1BCAE",1335025742,"0;0,0;0,0")]
    [InlineData(1201,0,-1,1,"747C02F28E862906A3A7E29779CC292C2D8E48BEE2388FD674412E6997238BA4",476723227,"1;339,156;0,0")]
    [InlineData(1201,0,-1,14,"17458F254914C31EB9BB7B99B2044841F82D7149B0F484B05C39D18D2584F4ED",1150899001,"1;339,158;0,0")]
    [InlineData(1201,0,0,42,"6269C5502A20BC4706D6A3B1882E53B0ABC117F811AA79D05064142C8F52BFA4",91696837,"1;861,155;0,0")]
    [InlineData(1201,0,0,1458,"60CFCC65C4FD0231B1E6C83067EDB29BC23382A114CB1D2C5E07A820ACB1BCAE",1916656655,"0;0,0;0,0")]
    [InlineData(1201,0,0,1,"26315CDFF2982AABCA394446A34FACFC735931528FF475DC77463A223267E553",823155208,"2;339,156;854,162")]
    [InlineData(1201,0,0,14,"17458F254914C31EB9BB7B99B2044841F82D7149B0F484B05C39D18D2584F4ED",962927982,"1;339,158;0,0")]
    [InlineData(1201,0,1,42,"60CFCC65C4FD0231B1E6C83067EDB29BC23382A114CB1D2C5E07A820ACB1BCAE",302596119,"0;0,0;0,0")]
    [InlineData(1201,0,1,1458,"60CFCC65C4FD0231B1E6C83067EDB29BC23382A114CB1D2C5E07A820ACB1BCAE",1335025742,"0;0,0;0,0")]
    [InlineData(1201,0,1,1,"8517911C912F7B7EB39D07E2B92317F89962947E2CAC0C6FB2CAED5D036EAEB3",876822149,"1;860,158;0,0")]
    [InlineData(1201,0,1,14,"621051388199D312143853717C2EEC32BF5250557BD7AA0A698C812E7F70E8F8",327922704,"1;850,156;0,0")]
    [InlineData(1201,1,-1,42,"A4F9F0265AD2CD0779BFC522B2653D3191D367AB7AD5F6E02A118AF711172E16",302596119,"0;0,0;0,0")]
    [InlineData(1201,1,-1,1458,"A4F9F0265AD2CD0779BFC522B2653D3191D367AB7AD5F6E02A118AF711172E16",1335025742,"0;0,0;0,0")]
    [InlineData(1201,1,-1,1,"CD4A7A44758AD9BAD76C5A0DF223BCB3A70B2286A1A9E439BC02A3C38C312BF0",1637983373,"1;333,164;0,0")]
    [InlineData(1201,1,-1,14,"5EACB600D93912A41D5339CA634A3B4C34685A71F87CB5F0B67124931E869501",1807390524,"1;346,158;0,0")]
    [InlineData(1201,1,0,42,"4F875519BD0B9ECDDB24451BFB3905991D7CB65C28269DE3BE90BEF8329002F8",2128337881,"1;865,156;0,0")]
    [InlineData(1201,1,0,1458,"A4F9F0265AD2CD0779BFC522B2653D3191D367AB7AD5F6E02A118AF711172E16",1916656655,"0;0,0;0,0")]
    [InlineData(1201,1,0,1,"CD4A7A44758AD9BAD76C5A0DF223BCB3A70B2286A1A9E439BC02A3C38C312BF0",1770013527,"1;333,164;0,0")]
    [InlineData(1201,1,0,14,"5EACB600D93912A41D5339CA634A3B4C34685A71F87CB5F0B67124931E869501",1129571067,"1;346,158;0,0")]
    [InlineData(1201,1,1,42,"A4F9F0265AD2CD0779BFC522B2653D3191D367AB7AD5F6E02A118AF711172E16",302596119,"0;0,0;0,0")]
    [InlineData(1201,1,1,1458,"A4F9F0265AD2CD0779BFC522B2653D3191D367AB7AD5F6E02A118AF711172E16",1335025742,"0;0,0;0,0")]
    [InlineData(1201,1,1,1,"E3E0AB3A9BC24E7621737EDBA4ACFE432248852E0BC31655C584D8D610292ABE",1689922774,"1;863,159;0,0")]
    [InlineData(1201,1,1,14,"1C875D32D6AEFD418E1D97EBA61B7A9A3E136B7D95B043168C23FC4E26F12FB0",798449113,"1;854,158;0,0")]
    [InlineData(1201,2,-1,42,"AB657E8D0699D8EFB276149E6A4DA2E2F04B76332FECDC7A722E66A47D76A3A9",302596119,"0;0,0;0,0")]
    [InlineData(1201,2,-1,1458,"AB657E8D0699D8EFB276149E6A4DA2E2F04B76332FECDC7A722E66A47D76A3A9",1335025742,"0;0,0;0,0")]
    [InlineData(1201,2,-1,1,"CFC111BA891157797CB4C9D79B8B0B57ECD781412DD530A848282E4935E6FFA2",299132592,"1;345,163;0,0")]
    [InlineData(1201,2,-1,14,"BC5147339B687520929810CB2B8C7443940D40AC898D2D2C3CFEB5C497B7CB36",153619314,"1;338,155;0,0")]
    [InlineData(1201,2,0,42,"2E28542474AE992774E5B3BC480A5BDF0870057639ED35C17C2831D457A95592",2128337881,"1;865,156;0,0")]
    [InlineData(1201,2,0,1458,"AB657E8D0699D8EFB276149E6A4DA2E2F04B76332FECDC7A722E66A47D76A3A9",1916656655,"0;0,0;0,0")]
    [InlineData(1201,2,0,1,"8AE056445622B3862420C8C43161E6D62C8183E73EA309410D081601036CF221",1403545670,"2;345,163;869,159")]
    [InlineData(1201,2,0,14,"049F9BF62CD6083242ED77FFE4B6C6F84E94FA14A4F9C6446C96743BA04FFC31",1157746344,"2;338,155;859,156")]
    [InlineData(1201,2,1,42,"AB657E8D0699D8EFB276149E6A4DA2E2F04B76332FECDC7A722E66A47D76A3A9",302596119,"0;0,0;0,0")]
    [InlineData(1201,2,1,1458,"AB657E8D0699D8EFB276149E6A4DA2E2F04B76332FECDC7A722E66A47D76A3A9",1335025742,"0;0,0;0,0")]
    [InlineData(1201,2,1,1,"4C31401000CCD9FF584AAF720FB3CCCC2249075A06B821B89214B50E9CFB2E17",846182956,"1;860,171;0,0")]
    [InlineData(1201,2,1,14,"F3B1376863DDE9191574AA02B93B785FC578489036EDE6312F0D0D42A85A30A8",601126072,"1;857,157;0,0")]
    public void Production_pass_matches_official(
        int width, int fixture, int dungeonSide, int seed, string cellHash, int next, string treasure)
    {
        Workspace workspace = CreateWorkspace(width, fixture, dungeonSide);
        var random = new RandomAdapter(seed);
        Execute(workspace, random, TestContext.Current.CancellationToken);

        string expected = $"{cellHash}|{next}|{treasure}";
        string actual = $"{HashCells(workspace)}|{random.Next()}|{FormatTreasure(workspace)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    // GenVars.numOceanCaveTreasure plus both GenVars.oceanCaveTreasure slots; absent slots are the source's zeroes.
    private static string FormatTreasure(Workspace workspace)
    {
        ReadOnlySpan<WorldGenerationPoint> anchors = workspace.VanillaOceanCaveTreasure;
        string first = anchors.Length > 0 ? $"{anchors[0].X},{anchors[0].Y}" : "0,0";
        string second = anchors.Length > 1 ? $"{anchors[1].X},{anchors[1].Y}" : "0,0";
        return $"{anchors.Length};{first};{second}";
    }

    [Fact]
    public void Treasure_anchors_are_empty_before_the_pass_runs()
    {
        Workspace workspace = CreateWorkspace(1200, 1, 0);
        Assert.Equal(0, workspace.VanillaOceanCaveTreasure.Length);
    }

    private static void Execute(Workspace workspace, IWorldGenerationVanillaRandom random, CancellationToken cancellation)
    {
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Ocean cave fixture", 42,
            workspace.WidthTiles, workspace.HeightTiles) { SeedText = "42" };
        new DungeonPass1458(DungeonStage1458.CreateOceanCaves, new DungeonState1458())
            .Execute(new Context(request, workspace, random, cancellation));
    }

    private static Workspace CreateWorkspace(int width, int fixture, int dungeonSide)
    {
        var workspace = new Workspace(width, Height);
        // Only the Reset fields the ocean geometry actually reads are pinned here; the rest stay neutral.
        workspace.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458
        {
            HellChestItems = [],
            TreeX = [],
            TreeStyle = [],
            CaveBackX = [],
            CaveBackStyle = [],
            ForestBackgroundStyles = [],
            DungeonSide = dungeonSide,
            LeftBeachEnd = 340,
            RightBeachStart = width - 340,
            DungeonLocation = width / 2,
        });
        Assert.True(workspace.TrySetLayers(140.25, 220));
        for (int x = 0; x < width; x++)
        for (int y = 0; y < Height; y++)
            workspace.TileStore.Set(x, y, Fixture(fixture, x, y, width));
        return workspace;
    }

    // Normalized 11-byte cell record matching the official probe: type, wall, frames, shape flags, liquid.
    private static string HashCells(Workspace workspace)
    {
        int width = workspace.WidthTiles;
        var bytes = new byte[width * Height * 11];
        int index = 0;
        for (int x = 0; x < width; x++)
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
            // Runtime Shape is 0 full, 1 half-brick, 2..5 for vanilla slopes 1..4.
            int slope = tile.Shape >= 2 ? tile.Shape - 1 : 0;
            bytes[index++] = (byte)((tile.IsActive ? 1 : 0) | (tile.Shape == 1 ? 2 : 0) | (slope << 2));
            bytes[index++] = tile.LiquidAmount;
            bytes[index++] = (byte)tile.LiquidKind;
        }

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTile Fixture(int fixture, int x, int y, int width)
    {
        ReadOnlySpan<ushort> materials = [0, 1, 53, 59, 147, 161, 396, 397, 57, 60, 38, 40];
        ReadOnlySpan<ushort> walls = [1, 2, 3, 40, 64, 86, 61, 15];

        int surface = fixture == 0 ? 120 : 108 + x % 23 - x / 400;
        bool active = y >= surface;
        ushort type = fixture == 0
            ? (ushort)(y < 200 ? 53 : 1)
            : materials[(x / 5 + y / 9) % materials.Length];
        ushort wall = y >= surface - 4 ? walls[(x / 3 + y / 7) % walls.Length] : (ushort)0;
        byte liquid = (byte)((x + y) % 7 == 0 ? 60 : 0);
        var liquidKind = (WorldLiquidKind)((x + y) % 3);

        if (fixture == 2)
        {
            // Deeper, more irregular shoreline plus pre-existing pockets of air and lava near both edges.
            if ((x < 200 || x > width - 200) && y >= surface && y < surface + 6 && (x + y) % 5 == 0)
                active = false;
            if (y >= surface + 30 && y < surface + 34 && x % 11 == 0)
            {
                active = false;
                liquid = 255;
                liquidKind = WorldLiquidKind.Lava;
            }
        }

        return new WorldTile
        {
            Type = type,
            Wall = wall,
            FrameX = 18,
            FrameY = 36,
            LiquidAmount = liquid,
            LiquidKind = liquidKind,
            // Vanilla slope 1 maps to runtime Shape 2.
            Shape = (byte)((x + y) % 4 == 0 ? 2 : 0),
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
