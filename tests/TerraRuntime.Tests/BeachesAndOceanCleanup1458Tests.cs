using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

/// <summary>
/// Complete-delegate comparison for the ordinary TerrariaServer 1.4.5.8 <c>BeachesAndOceanCleanup</c> pass.
/// </summary>
/// <remarks>
/// Expectations were produced by running the registered official pass delegate itself — located by
/// <c>GenPassNameID.BeachesAndOceanCleanup</c> in <c>WorldGen.AddPasses()</c> and invoked through
/// <c>GenPass.Apply</c> — on the same synthetic input, in both pinned dedicated-server builds (Windows
/// <c>d87e3faf08637f6be8882c63e7f11fb7e792b0230006309618473ece0f863e1e</c> and Linux
/// <c>4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034</c>), which agreed on all 72 cases. Seeds 1 and 14 are included because they take the one-in-four
/// Florida-style branch on the left and right beach respectively, which the ordinary 42/1458 seeds never reach.
/// The hash covers type, wall, frames, active/half-brick/slope and both liquid fields for every cell. This closes
/// the pass on synthetic input; it is not a claim about the real generated prefix or special seeds.
/// </remarks>
public sealed class BeachesAndOceanCleanup1458Tests
{
    private const int Height = 600;

    [Theory]
    [InlineData(1200,0,-1,42,"9F2E01D960602AC4ABEC49187B9272479BD77F1AED0FC847CA04170110CB66F5",1608464386,223,120,925,120)]
    [InlineData(1200,0,-1,1458,"932A9596BE0E0EC204C2445FFD5ECD10F166AF180ADB3B1B71FD622BC1D8DF50",1474721165,243,120,926,120)]
    [InlineData(1200,0,-1,1,"E6EE261AD8894C155E0F37F2400EC347CA1798264295E527F4F6DD1FD0CA4BCA",1466099108,210,120,925,120)]
    [InlineData(1200,0,-1,14,"6EB2791955DAD0B95750CF56B796B63E7E2A761D7AC481D926CC4C43B8125B29",340989925,240,120,953,120)]
    [InlineData(1200,0,0,42,"3247F31C0283E5F337BD4E54A4C69D2CE3349202202A230A47B631AB5ECBE2FE",1702015010,223,120,944,120)]
    [InlineData(1200,0,0,1458,"01B6CABCE35037254CBCDD2196C11B287E69C77807949CB25457A67BD389909A",2005809534,243,120,972,120)]
    [InlineData(1200,0,0,1,"A17E14BF90E3B29B1A39ACBFC84B54C3D84B164224BE962556326C12CB0C2C7E",1401629356,210,120,949,120)]
    [InlineData(1200,0,0,14,"E62D95D45772F462F76CF4F71EAADF316C356D84A5732C884D0441398774B5CD",220208441,240,120,1005,120)]
    [InlineData(1200,0,1,42,"C5EAD020913FC6A114D774F0E9AE484725E5636293C05ECC0E2C6CEA4F6CF1BA",907110113,273,120,975,120)]
    [InlineData(1200,0,1,1458,"8B32357A4E52E764DF088E11888C2090F6E4E8159DF938C21B6D4665B01E45CD",438215284,274,120,949,120)]
    [InlineData(1200,0,1,1,"9327F6BFE8CC13C315BE8E8459CA894C6AC0EFD67867804A13CAABE2C4ADF0A6",1664586095,247,120,980,120)]
    [InlineData(1200,0,1,14,"F761A98A0D94A0D24FB01A8326D355FB5FCF1D194216EAF2BE08380D5DD5E500",1216104280,273,120,982,120)]
    [InlineData(1200,1,-1,42,"978B4904453CE97CDBBA419BD96F85DAD44D8F048C5C74EC3F4B67A36DD2BB10",1608464386,223,125,925,111)]
    [InlineData(1200,1,-1,1458,"035CB8B788863F556CD31BB13B094F3CC0F54C2C7750C43A77A6A4A7504F152B",1474721165,243,121,926,111)]
    [InlineData(1200,1,-1,1,"64893674587539185ABFF1163E19F8E58B0721F0043FFBD65B595BF62B09A7A9",1466099108,210,115,925,111)]
    [InlineData(1200,1,-1,14,"E903B28C4BE8A63A66DABF39A9B05DBCEFCD79B44F2341C862B3E8456DC0B437",340989925,240,119,953,111)]
    [InlineData(1200,1,0,42,"4E87D22D488F79E7208D84E9DC3A97739B34B5199554015B44FE59D48416AB83",1702015010,223,125,944,107)]
    [InlineData(1200,1,0,1458,"AAE71CE454FE858A16884F26647EB14E3623F8666110404E26D480FCBA24F9C5",2005809534,243,121,972,111)]
    [InlineData(1200,1,0,1,"02287F43F23079D4C2DD4684F470872554E3027981AB1CFCFB0D7BC11E71D766",1401629356,210,115,949,112)]
    [InlineData(1200,1,0,14,"B760C50DED1436054DEE4BB4F51F8F0327E07534999DFE77575C365033B2B761",220208441,240,119,1005,117)]
    [InlineData(1200,1,1,42,"5E73B3B1D45D9A36F80AB4CBB2422A9F673044FBE4890A1FE725043064A0DBDE",907110113,273,129,975,114)]
    [InlineData(1200,1,1,1458,"E4D33D3C9CCFAE602BB0B917A7E869D3DCADAF8C875387DEF3AB713E96E75D22",438215284,274,129,949,111)]
    [InlineData(1200,1,1,1,"9DFC1ACB2BAFC4AD6C127FB82D58E59DEE0761D581FE9D058F6B9CA320632D0D",1664586095,247,129,980,120)]
    [InlineData(1200,1,1,14,"B06791FFDBD93B5681B20B6DD0B5BAFD29BCB1F0A50D019D512E036EFD990B4F",1216104280,273,129,982,118)]
    [InlineData(1200,2,-1,42,"B190872C0F22C395EFD04EA6411E04F06FC3A46BCA99438988762C30E90FA8DC",1608464386,223,125,925,111)]
    [InlineData(1200,2,-1,1458,"CAE6759FEE71FD129E1BFD3782953BDB13A3E4A3161091829A9D42452028B769",1474721165,243,121,926,111)]
    [InlineData(1200,2,-1,1,"7E7E09720EB9C28EC7C04E8278F389208A277445B5B7E2811E663B4332AF80EE",1466099108,210,115,925,111)]
    [InlineData(1200,2,-1,14,"63950041E0DF4288A314C9682BBB604B876AAD267636385684A9A35B32393951",340989925,240,119,953,111)]
    [InlineData(1200,2,0,42,"558865615FD2271EE3734F5972E6C09764ADC384F4FEEC4E2EAF5541261B73C6",1702015010,223,125,944,107)]
    [InlineData(1200,2,0,1458,"3FB5FC507BC73B7710609E2D01AEFECE5878FC87B6B766E84746E8A35857B161",2005809534,243,121,972,111)]
    [InlineData(1200,2,0,1,"7B430E6C67F43077CF578EB4988696B386DFF6F8D97EA3E98069F8BA35F4D9F9",1401629356,210,115,949,112)]
    [InlineData(1200,2,0,14,"CC16AB7C8C3421E5E2309867E06596616BBAF5304329A3A4342029C76067AA3D",220208441,240,119,1005,117)]
    [InlineData(1200,2,1,42,"1B039791A6AAC8E70EB7CFE397812CFF4F395500304E36B3BDDCFCD01711687F",907110113,273,129,975,114)]
    [InlineData(1200,2,1,1458,"228698287516910753684434A8EC15EF5B7A680FA6C98269EF12D71D86A13B87",438215284,274,129,949,111)]
    [InlineData(1200,2,1,1,"A738A98D75BD77A0F3F44E8E8D1BE623E7EEC14B3C96BA3DBA038FCC3B084A3C",1664586095,247,129,980,120)]
    [InlineData(1200,2,1,14,"A9E1C095C3D22F6E8193C2148B32D520EB29390228BE822936D51E7742FB601C",1216104280,273,129,982,118)]
    [InlineData(1201,0,-1,42,"CCEA04FA03B5A15307A6F3844BE403E83B7129A376CB723BDF639B52B744EE28",1608464386,223,120,926,120)]
    [InlineData(1201,0,-1,1458,"C34EF7360C01241A0B3357432BA0B2DCB813A6CF3EADE9F3D9467CC4BCD9B391",1474721165,243,120,927,120)]
    [InlineData(1201,0,-1,1,"EC935CD56DE67CF423FE10E2DA2AAE592F057F8DA4413AD7DAAA2214D1F47679",1466099108,210,120,926,120)]
    [InlineData(1201,0,-1,14,"837071B9826CFBACA876254349E9AAE8DC7CBEA911BE700FB9BE0AD1AD3D9F3A",340989925,240,120,954,120)]
    [InlineData(1201,0,0,42,"49FAAFB01E2605B32F89D6D9694DEC9831DD1F4DE7A7CF020F169B73EAE3EFE0",1702015010,223,120,945,120)]
    [InlineData(1201,0,0,1458,"4D65005AF7ED4020DE66C2604DF3E677D79212A4B957E1E0B50130FF9B662089",2005809534,243,120,973,120)]
    [InlineData(1201,0,0,1,"545AE8D6B88BFE2FB1DF764CE788AA712F51C874CE8D65BD0079CDEB36843986",1401629356,210,120,950,120)]
    [InlineData(1201,0,0,14,"9C70B3A6B8BAF386AB59FCE84109A2D095570F1E8469D9BA946607946D580A22",220208441,240,120,1006,120)]
    [InlineData(1201,0,1,42,"EAB446202E64AAB0C54E7EBAE23C6BE66D21D1DF9564013389C777D9CBDDFF66",907110113,273,120,976,120)]
    [InlineData(1201,0,1,1458,"EB664A2A76EDD7DBAFF4DA0E106F82B5BBBDC519A0747A3E4F673DFF128AD28A",438215284,274,120,950,120)]
    [InlineData(1201,0,1,1,"FCD87772C3FB7EA1075D63617762BB732869C4127D9E20D8E8651AF2B855BECF",1664586095,247,120,981,120)]
    [InlineData(1201,0,1,14,"7489076AEDF14DE8CDC1C0554C6DB052593B50FFACCDD0C2E8C6012C5B015C36",1216104280,273,120,983,120)]
    [InlineData(1201,1,-1,42,"97A479F9FB6760A3C8D934D4D3B8BE583F13216B3E76D7BBD66AA5486BEAB94B",1608464386,223,125,926,112)]
    [InlineData(1201,1,-1,1458,"40B8006AF8C29441BED17E58D9498E68BF6D376DFA290F53DC991F7BB4CC746C",1474721165,243,121,927,112)]
    [InlineData(1201,1,-1,1,"025C0C58CE6049A26A0E853613CDF0CC98A80B9541EFFF40AA2F9C48959F6CBA",1466099108,210,115,926,112)]
    [InlineData(1201,1,-1,14,"F8F7EF8C0AF67DF0A451EF8C09AB6AABFE2ED1D8C4E02B7355511919ADCB530E",340989925,240,119,954,112)]
    [InlineData(1201,1,0,42,"74B2B41F4383B1E10B6FFF89B93678B286B9990EF700A5D99BA5ABC8E350F3FA",1702015010,223,125,945,108)]
    [InlineData(1201,1,0,1458,"29A37DF409ED3036111E3E77835713BC144C807D4C52D2210D9B42B356CB89EE",2005809534,243,121,973,112)]
    [InlineData(1201,1,0,1,"9FA4C4E9CFA8B42F4EE1DF9FFDBF599FFCCD7760448D3EF622DAB46A84019C89",1401629356,210,115,950,113)]
    [InlineData(1201,1,0,14,"717FE4FB521E1560B167A46492D2DF0F6E8AA8FEC3647E27DF073E0F8FD50C00",220208441,240,119,1006,118)]
    [InlineData(1201,1,1,42,"53D855D9A2BCA3E9D9AB3505EDCAD429309C17B9A6B9E089C743CAE7865D4825",907110113,273,129,976,115)]
    [InlineData(1201,1,1,1458,"AB49D071AA7D037E22BAF8A8525C1AA186E6C5801C7D57AE09A8EDED33A2ADB2",438215284,274,129,950,112)]
    [InlineData(1201,1,1,1,"A0DD5C9C2905A2731DEBF30425B6E428300F10A837941ABCDAF073D3145099BD",1664586095,247,129,981,121)]
    [InlineData(1201,1,1,14,"996C72F00A4B0FA6A6BE4D3A6B3149ECE1EBD405F350C2B8F3488672507AC8CF",1216104280,273,129,983,119)]
    [InlineData(1201,2,-1,42,"30D5EFF9DD6A85AE8BBF1EA417B8959744ACE6A3DCC74784C54C8D913ADC32CB",1608464386,223,125,926,112)]
    [InlineData(1201,2,-1,1458,"22252042C099901FA73BB88CE7AC2D545FB0EEBDF159FF3A019D767839AC1B57",1474721165,243,121,927,112)]
    [InlineData(1201,2,-1,1,"802CCC18C179B0AFAF202EB37652B33D749BEAC120E0236740FA3DF8DB48C734",1466099108,210,115,926,112)]
    [InlineData(1201,2,-1,14,"148000D825E3DB4285A19A113D0F1EEC0153C5F067C0E2FD1040C3618B6116FB",340989925,240,119,954,112)]
    [InlineData(1201,2,0,42,"854CA50D6A5886AD2958A54350D836DAA6A481D4009FC502C62B2F555826FF07",1702015010,223,125,945,108)]
    [InlineData(1201,2,0,1458,"953E5CE3FF008EEB10DF4FD8EB1E1174C379BA4BDFE18CF43FF2F4A0EA5A9349",2005809534,243,121,973,112)]
    [InlineData(1201,2,0,1,"FCB7D1B3DBA55E1EB3F937FE482388194D99924BA7151EE16AE74DDAAADE3CA5",1401629356,210,115,950,113)]
    [InlineData(1201,2,0,14,"3CF977206FA3F7DDAEAD33661C5A9EA205DABE2CFEF9C71CA6E64A1D311A1BE4",220208441,240,119,1006,118)]
    [InlineData(1201,2,1,42,"70C2B43A9F6CBCCD7DAC730B776D72BE2BA5C2CC4B29B424594ACE322C20E50C",907110113,273,129,976,115)]
    [InlineData(1201,2,1,1458,"99D4A1885CD8B34ACC62F1F89AF8AEC4520CD92EE8D55212968A99A43B256639",438215284,274,129,950,112)]
    [InlineData(1201,2,1,1,"EB6234020C4811A2C6D5BAA7FB0C41A63C51E2C40CA96A0AFFB67B79AE9D8C79",1664586095,247,129,981,121)]
    [InlineData(1201,2,1,14,"71925F949C0DC641256E8BE96CD7A4664A56C6C62A6D1DBF226E7F3C0D082682",1216104280,273,129,983,119)]
    public void Production_pass_matches_official(
        int width, int fixture, int dungeonSide, int seed,
        string cellHash, int next, int shellLeftX, int shellLeftY, int shellRightX, int shellRightY)
    {
        Workspace workspace = CreateWorkspace(width, fixture, dungeonSide);
        var random = new RandomAdapter(seed);
        Execute(workspace, random, TestContext.Current.CancellationToken);

        string expected = $"{cellHash}|{next}|{shellLeftX},{shellLeftY};{shellRightX},{shellRightY}";
        string actual =
            $"{HashCells(workspace)}|{random.Next()}|" +
            $"{workspace.VanillaLeftShellAnchor.X},{workspace.VanillaLeftShellAnchor.Y};" +
            $"{workspace.VanillaRightShellAnchor.X},{workspace.VanillaRightShellAnchor.Y}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    [Fact]
    public void Shell_anchors_are_unavailable_before_the_pass_runs()
    {
        Workspace workspace = CreateWorkspace(1200, 1, 0);
        Assert.Throws<InvalidOperationException>(() => workspace.VanillaLeftShellAnchor);
        Assert.Throws<InvalidOperationException>(() => workspace.VanillaRightShellAnchor);
    }

    private static void Execute(Workspace workspace, IWorldGenerationVanillaRandom random, CancellationToken cancellation)
    {
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Beaches fixture", 42,
            workspace.WidthTiles, workspace.HeightTiles) { SeedText = "42" };
        new DungeonPass1458(DungeonStage1458.Beaches, new DungeonState1458())
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
