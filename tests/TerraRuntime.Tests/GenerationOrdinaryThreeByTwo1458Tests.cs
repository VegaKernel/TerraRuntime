using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationOrdinaryThreeByTwo1458Tests
{
    [Fact]
    public void Registered_final_cleanup_matches_thirty_one_furniture_and_small_cage_identities()
    {
        // Untouched official1.4.5.8 registered FinalCleanup, synthetic1000x500 predecessor,
        //31identities x12variants. Participating Main flags are source-initialized; netMode0 with proper source Main/engine initialization.
        var workspace = CreateWorkspace();
        void Put(int x, int y, ushort type, short fx = 0, short fy = 0, ushort wall = 0, byte shape = 0, bool inactive = false, bool independentHalf = false)
        {
            var tile = new WorldTile
            {
                Type = type,
                FrameX = fx,
                FrameY = fy,
                Wall = wall,
                Flags = WorldTileFlags.Active | (inactive ? WorldTileFlags.Inactive : WorldTileFlags.None)
            };
            Assert.True(tile.TrySetSourceShape(shape == 1 || independentHalf, (byte)(shape > 1 ? shape - 1 : 0)));
            workspace.TileStore.Set(x, y, in tile);
        }
        ushort[] identities = [14, 17, 26, 77, 86, 87, 89, 114, 133, 215, 217, 218, 237, 244, 285, 286, 298, 299, 310, 339, 361, 362, 363, 364, 377, 391, 392, 393, 394, 469, 538];
        for (int n = 0; n < identities.Length; n++)
            for (int scenario = 0; scenario < 12; scenario++)
            {
                ushort type = identities[n];
                int x = 60 + n % 8 * 112;
                int y = 45 + n / 8 * 105 + scenario * 7;
                int styleX = scenario == 3 ? 54 : 0;
                int styleY = scenario == 3 ? 36 : 0;
                for (int dx = 0; dx < 3; dx++)
                    for (int dy = 0; dy < 2; dy++)
                        if (scenario != 1 || dx != 2 || dy != 1)
                            Put(x + dx, y + dy, type, (short)(styleX + dx * 18), (short)(styleY + dy * 18), inactive: scenario == 10 && dx == 1 && dy == 1);
                for (int dx = 0; dx < 3; dx++)
                    if (scenario != 2 || dx != 2)
                        Put(x + dx, y + 2, 1, shape: (byte)(scenario is 7 or 9 ? 4 : scenario == 8 ? 1 : scenario == 11 ? 2 : 0),
                            inactive: scenario == 6 && dx == 2, independentHalf: scenario == 9);
                if (scenario == 4)
                    Put(x + 2, y + 1, type, 18, 18);
                if (scenario == 5)
                    Put(x + 2, y + 1, 1);
            }

        var random = new RandomAdapter(1458);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(
            new Context(new WorldGenerationRequest(Provider1458.GeneratorId, "Fixture", 1458, 1000, 500), workspace, random));
        Assert.Equal("5630251AFB7FC8F85AD87091B10F9C947F817FA0E93E747882E4F17CD0887390", GenerationNaturalPrefix49Tests.GridHash(workspace));
        Assert.Equal("E91D1672104409562EF398B1E5647ECE0C1066ECFC93B0291072F07A16277FB3", GenerationNaturalPrefix49Tests.RandomHash(random, includeReport: false));
        Assert.Equal(36, workspace.VanillaAnglerQuest!.Quest);
        Assert.Equal("515B1A1E29D497DC858365CC839CC6087FC5B373FE53091E3371FAB8143BA159", HashFixture(workspace));
        Assert.Equal("F6D33588D9E60C639FF41FAC13F164D39932E54093B7F3A36D9A78A4148212FF", random.Fingerprint());
        Assert.Equal(662296535, random.Next());
        for (int n = 0; n < identities.Length; n++)
            for (int scenario = 0; scenario < 12; scenario++)
                Assert.Equal(scenario is 0 or 3 or 7 or 10,
                    workspace.TileStore.Get(60 + n % 8 * 112, 45 + n / 8 * 105 + scenario * 7).IsActive);
    }

    [Theory]
    [InlineData("table-style-0", (ushort)14, 0, 1, (ushort)1, (byte)0, false, (short)0, (short)0, false, false, false, false, true, "8E1F0341F52855B02BFE9A60F0A8C4B47929A8D719ED37D7A8AF4290075A8788", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("table-style-24", (ushort)14, 24, 1, (ushort)1, (byte)0, false, (short)0, (short)0, false, false, false, false, true, "95DFEAFD0CE4B9EE00EDA513D3D9323157D5B948E356F93812DC1997DAF40839", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("table-style-25", (ushort)14, 25, 1, (ushort)1, (byte)0, false, (short)0, (short)0, false, false, false, false, true, "449AE35D85A4251AF4A60CCB3CDB42FDCF7E2FE9037DDE397AB23EEBA064F756", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("table-style-26", (ushort)14, 26, 1, (ushort)1, (byte)0, false, (short)0, (short)0, false, false, false, false, true, "2D6757964DA0974D8DFBFFE4766D81382B20AA8DE4A14866DBD3CCD98437709A", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("table25-broken-neighbour", (ushort)14, 25, 1, (ushort)1, (byte)0, false, (short)0, (short)0, false, true, false, true, false, "3C1B43D984EDB56615E30E3627627E260E47A90F85B0096FB9CCD871272251E1", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("ordinary-broken-neighbour", (ushort)17, 0, 0, (ushort)1, (byte)0, false, (short)0, (short)0, false, true, false, true, false, "5C7A984CEF0C6F40B646B14615D637BF05BDD184D780EB6B0F359A9A7EB92E73", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("ordinary-valid-neighbour", (ushort)17, 0, 0, (ushort)1, (byte)0, false, (short)0, (short)0, false, false, false, true, true, "B0EDC84073F299EA4F44FEF0A2CFDE34F306E3D7BC82556F7417E6A495CE15EF", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("boulder-138-type-14", (ushort)14, 0, 0, (ushort)138, (byte)0, false, (short)0, (short)0, false, false, true, false, true, "AD4F253639C4CDC609ED7334465621A5E9A06BAECA49802DD49386E41CF8A7D6", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("boulder-138-type-26", (ushort)26, 0, 0, (ushort)138, (byte)0, false, (short)0, (short)0, false, false, true, false, false, "07B3726F7E79CD36AD5240A5734C3523AC668EC04D5B4FDBDDC07908C767E48C", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    [InlineData("boulder-484-type-14", (ushort)14, 0, 0, (ushort)484, (byte)0, false, (short)0, (short)0, false, false, true, false, true, "955AC5A775D44710CB575B1D2D4735C3409AF2EE0B6DAAF34173C5171E4D60D9", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("boulder-484-type-26", (ushort)26, 0, 0, (ushort)484, (byte)0, false, (short)0, (short)0, false, false, true, false, false, "BAA02D7D4A3B6265966E0A66219B6EEF1DFA55394C29373BEF21372C99AD46D5", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    [InlineData("boulder-664-type-14", (ushort)14, 0, 0, (ushort)664, (byte)0, false, (short)0, (short)0, false, false, true, false, true, "5DC58C0B0B56B12F03E8211BFE096C8DD56ECB6CD34A55929C82919AD16FCD63", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("boulder-664-type-26", (ushort)26, 0, 0, (ushort)664, (byte)0, false, (short)0, (short)0, false, false, true, false, false, "D5058B0BCE7B54C91F8724F14EBC0D1C5BBDFAED68F626D26D92FA2577CB618D", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    [InlineData("boulder-665-type-14", (ushort)14, 0, 0, (ushort)665, (byte)0, false, (short)0, (short)0, false, false, true, false, false, "CBD76C61129F774267E525B6795B3C5F76EDD227BB34A09A563F4F9290245E27", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("boulder-665-type-26", (ushort)26, 0, 0, (ushort)665, (byte)0, false, (short)0, (short)0, false, false, true, false, false, "CBD76C61129F774267E525B6795B3C5F76EDD227BB34A09A563F4F9290245E27", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    [InlineData("boulder-711-type-14", (ushort)14, 0, 0, (ushort)711, (byte)0, false, (short)0, (short)0, false, false, true, false, true, "EBCEF324299E5AB859727A1F23EB0B094BACD003C8DA3495B7EB07DC112BB8CE", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("boulder-711-type-26", (ushort)26, 0, 0, (ushort)711, (byte)0, false, (short)0, (short)0, false, false, true, false, false, "59490F8079A0AAEB3B97AF0840A729B42F1276974EAFE0369FEF6A7FD711D9B1", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    [InlineData("boulder-712-type-14", (ushort)14, 0, 0, (ushort)712, (byte)0, false, (short)0, (short)0, false, false, true, false, true, "596F4F19F2C62CA899E6034773156DB598640843ECDBC87DEA445EC684668831", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("boulder-712-type-26", (ushort)26, 0, 0, (ushort)712, (byte)0, false, (short)0, (short)0, false, false, true, false, false, "407DDC461097742F444AF202C2FC9A9336E8A4445703D59E4C79D909C981AB1A", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    [InlineData("boulder-713-type-14", (ushort)14, 0, 0, (ushort)713, (byte)0, false, (short)0, (short)0, false, false, true, false, true, "3C54017F983026FC47423958D2108CE1965072A8971BD384CEA58ED12C3DCE86", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("boulder-713-type-26", (ushort)26, 0, 0, (ushort)713, (byte)0, false, (short)0, (short)0, false, false, true, false, false, "06FA3713A3EA03F5657604397C22234B7FE55954B6255B0AD06013F138FA1123", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    [InlineData("boulder-714-type-14", (ushort)14, 0, 0, (ushort)714, (byte)0, false, (short)0, (short)0, false, false, true, false, true, "477DBB77E2BFE49F43476DD3AD71198DF4C3BF817223544F053DF34F1965A22F", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("boulder-714-type-26", (ushort)26, 0, 0, (ushort)714, (byte)0, false, (short)0, (short)0, false, false, true, false, false, "01082C38ECFC8304E01E32544C63DB404613A26DF94DC93F25336FD5B729640F", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    [InlineData("boulder-715-type-14", (ushort)14, 0, 0, (ushort)715, (byte)0, false, (short)0, (short)0, false, false, true, false, true, "FB8549DFAA4460F9AF1D1EB6ACC8341CACACDFF50B0E2BC95DF0AE963A1414EF", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("boulder-715-type-26", (ushort)26, 0, 0, (ushort)715, (byte)0, false, (short)0, (short)0, false, false, true, false, false, "8BE90263C8F077C71BCFDB4338153195C0093709F2DD6E1C5DEC8A21F1561363", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    [InlineData("boulder-716-type-14", (ushort)14, 0, 0, (ushort)716, (byte)0, false, (short)0, (short)0, false, false, true, false, true, "28C600E59D748F859C7A4AFBA17D5EE93F2605E1D5B97B5551B3F9F3CCE7ED3B", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("boulder-716-type-26", (ushort)26, 0, 0, (ushort)716, (byte)0, false, (short)0, (short)0, false, false, true, false, false, "63911BFFB5695CE313D6525F9EDBEA76D140869DED708ED118DDB0AA2CC86AEC", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    [InlineData("cage-platform-flat", (ushort)285, 0, 0, (ushort)19, (byte)0, false, (short)0, (short)0, false, false, false, false, true, "44AEE732F0820CA22AE92B794BCC86DA063C5675B6E5C472E85BB824F81B005C", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("cage-platform-half", (ushort)285, 0, 0, (ushort)19, (byte)1, false, (short)0, (short)0, false, false, false, false, false, "D8F3B5505D7D934809C17C39B7CDC29FE3385EC098BF49AEF471F3BDB126D614", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    [InlineData("cage-platform-combined", (ushort)285, 0, 0, (ushort)19, (byte)4, true, (short)0, (short)0, false, false, false, false, false, "D8F3B5505D7D934809C17C39B7CDC29FE3385EC098BF49AEF471F3BDB126D614", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    [InlineData("cage-platform-bottom", (ushort)285, 0, 0, (ushort)19, (byte)4, false, (short)0, (short)0, false, false, false, false, true, "44AEE732F0820CA22AE92B794BCC86DA063C5675B6E5C472E85BB824F81B005C", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("cage-platform-top-proper", (ushort)285, 0, 0, (ushort)19, (byte)2, false, (short)0, (short)0, false, false, false, false, true, "44AEE732F0820CA22AE92B794BCC86DA063C5675B6E5C472E85BB824F81B005C", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("cage-platform-top-badfx", (ushort)285, 0, 0, (ushort)19, (byte)2, false, (short)144, (short)0, false, false, false, false, false, "F152090D81F6D663F1C1A3A342735856405583784EC32A07C5FEFA63C1B50EFB", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    [InlineData("cage-platform-actuated", (ushort)285, 0, 0, (ushort)19, (byte)0, false, (short)0, (short)0, true, false, false, false, false, "D8F3B5505D7D934809C17C39B7CDC29FE3385EC098BF49AEF471F3BDB126D614", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    [InlineData("cage-platform-flat-fy18", (ushort)285, 0, 0, (ushort)19, (byte)0, false, (short)0, (short)18, false, false, false, false, true, "B7DF43583957B789913A7484C5E322847277563ECAE5863EE0C36595327438BD", "511931AF068F0673225AD8F7DE74437E97374E2890E34F8E887387CFFB6CDA29", 906992634)]
    [InlineData("cage-platform-half-fy18", (ushort)285, 0, 0, (ushort)19, (byte)1, false, (short)0, (short)18, false, false, false, false, false, "4080E4C0484C92169CDF02F21FDB3919238E280886266E0B2C10B487B2966C64", "D0593D4CF0692E3EBA37836C26F2C87233FE311A31F6749B7BC70F20804D771B", 495104663)]
    public void Direct_frames_preserve_single_row_table_boulder_refusal_strict_platform_floor_and_halo(
        string profile, ushort type, int style, int verticalStyle, ushort support, byte shape, bool independentHalf,
        short frameX, short frameY, bool inactive, bool missingMember, bool boulder, bool neighbour,
        bool survives, string digest, string rngDigest, int nextRandom)
    {
        _ = profile;
        var workspace = CreateWorkspace();
        void Put(int x, int y, ushort type, short fx = 0, short fy = 0, ushort wall = 0, byte shape = 0, bool inactive = false, bool independentHalf = false)
        {
            var tile = new WorldTile
            {
                Type = type,
                FrameX = fx,
                FrameY = fy,
                Wall = wall,
                Flags = WorldTileFlags.Active | (inactive ? WorldTileFlags.Inactive : WorldTileFlags.None)
            };
            Assert.True(tile.TrySetSourceShape(shape == 1 || independentHalf, (byte)(shape > 1 ? shape - 1 : 0)));
            workspace.TileStore.Set(x, y, in tile);
        }
        int objectHeight = type == 14 && style == 25 ? 1 : 2;
        for (int dx = 0; dx < 3; dx++)
        {
            for (int dy = 0; dy < objectHeight; dy++)
                if (!missingMember || dx != 2 || dy != objectHeight - 1)
                    Put(903 + dx, 330 + dy, type, (short)(style * 54 + dx * 18), (short)(verticalStyle * 36 + dy * 18));
            Put(903 + dx, 330 + objectHeight, support, frameX, frameY, shape: shape, inactive: inactive, independentHalf: independentHalf);
        }
        if (boulder)
        {
            // Complete stable2x2 boulder under the first two columns, grounded on stone. No unsupported
            // single-cell boulder is introduced by the altar's post-destruction halo.
            for (int dx = 0; dx < 2; dx++)
                for (int dy = 0; dy < 2; dy++)
                    Put(903 + dx, 330 + objectHeight + dy, support, (short)(dx * 18), (short)(dy * 18));
            Put(905, 330 + objectHeight, 1);
            for (int dx = 0; dx < 2; dx++)
                Put(903 + dx, 332 + objectHeight, 1);
        }
        if (neighbour)
        {
            for (int dx = 0; dx < 2; dx++)
                for (int dy = 0; dy < 3; dy++)
                    if (dx != 0 || dy != 2)
                        Put(901 + dx, (type == 14 && style == 25 ? 333 : 330) + dy, 245, (short)(dx * 18), (short)(dy * 18), wall: 1);
        }
        var random = new RandomAdapter(1458);
        new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(903, 330);
        Assert.Equal(digest, HashFixture(workspace));
        Assert.Equal(rngDigest, random.Fingerprint());
        Assert.Equal(nextRandom, random.Next());
        Assert.Equal(survives, workspace.TileStore.Get(903, 330).IsActive);
        if (neighbour)
            Assert.Equal(!missingMember, workspace.TileStore.Get(901, type == 14 && style == 25 ? 333 : 330).IsActive);
    }

    private static Workspace CreateWorkspace()
    {
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, TerraRuntime.World.VanillaSpecialWorldSeed1458.None));
        return workspace;
    }

    private static string HashFixture(Workspace workspace)
    {
        var snapshot = new StringBuilder(workspace.WidthTiles * workspace.HeightTiles * 20);
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                snapshot.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(';');
            }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private sealed class Context(WorldGenerationRequest request, Workspace workspace, IWorldGenerationVanillaRandom random) : IWorldGenerationContext
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
        public string Fingerprint()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var cursor = typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random);
            var slots = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cursor + ";" + string.Join(",", slots))));
        }
        public int Next() => random.Next();
        public int Next(int maxValue) => random.Next(maxValue);
        public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
