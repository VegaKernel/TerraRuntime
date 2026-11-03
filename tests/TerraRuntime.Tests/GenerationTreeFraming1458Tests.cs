using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationTreeFraming1458Tests
{
    [Theory]
    [InlineData(0, "E9215743CC680926CBF93BF563052D4300E56508649B941456ED683F23B6BCED", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634)]
    [InlineData(1, "0A8EC510E69C60D3FC86E1899685D182BA56E8CC613E6034C0D5FA61FCAAFD0B", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634)]
    [InlineData(2, "E600966B4F76BA35460E2736714D33D746740A3FFB77D1B5E6E3AD9469589E88", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634)]
    [InlineData(3, "E9215743CC680926CBF93BF563052D4300E56508649B941456ED683F23B6BCED", "5CCC3069036C6D54F3CA441FD8C36C6A4491F8CE77EAEB20E42DF6C870ED87C7", 906992634)]
    public void Woody_propagation_requires_both_changed_coordinates_and_nonnegative_original_frames(
        int profile, string expectedGrid, string expectedRandom, int expectedNext)
    {
        var workspace = new Workspace(80, 80);
        PopulatePropagation((x, y, type, fx, fy, active, actuated, shape) =>
            Put(workspace, x, y, type, fx, fy, active, actuated, shape), profile);
        var random = new RandomAdapter(1458);
        new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(30, 30);
        Assert.Equal(expectedGrid, Hash(workspace));
        Assert.Equal(expectedRandom, random.CapturePhysicalRandomHash());
        Assert.Equal(expectedNext, random.Next());
    }

    [Theory]
    [InlineData(5, false, "E83F32CE6D5CA87E7A3539E6A36848BAA49F6DBE591480CFDD4AE70F238C5061")]
    [InlineData(5, true, "331A362A68A36C8DDD11F4F466D4CB7C62E4A9F09CACCF73FBAF5D6D8CD30D5D")]
    [InlineData(323, false, "1FF4D3B3284AA504C64EFBF98032175D4B00B5AD44C09C41068B1F1A3BE196C5")]
    [InlineData(323, true, "54E83471A630B4B833C078F810B127DF72FAB772F6FA485A1021CC9148DF6134")]
    [InlineData(583, false, "67E23709A0D9B615FF62435F62684AF331426AAF94B12EC87485F8045308F1A7")]
    [InlineData(583, true, "BA27C4E17FDF8B2D790233110A4D1791DD642AB74D1197D08669CB90A76A052D")]
    [InlineData(584, false, "D51181471027E8985BD297CE987B54DB89BFCEF63EB343DB2CFA1992589D5A32")]
    [InlineData(584, true, "8BD2AFB4B98EBC255F3F0D39F0BEA0610B4EB2F9B6557F39151D02B9FE313510")]
    [InlineData(585, false, "AEFBE8D5A6A15EF18303CE89206C2F6AF72E65E0E7C5E89DC2752AB0A80B607E")]
    [InlineData(585, true, "A7FB10AE11200AA585C39220D7ED7B3B38F3026923C4D56844A8806123BE24D7")]
    [InlineData(586, false, "4E88347C8631D5E7BCE2BF7FAFC1E6768984F5DCCCC22E1918C91B11B413965C")]
    [InlineData(586, true, "F7F50E51751286E905638BDB2B634E9D0D70C3E5E9AA655A5F003BC6A33B909A")]
    [InlineData(587, false, "91C7EDFE235BC6D10D9EB74FBF744C8B235F6D6C9470605513326A093F58E976")]
    [InlineData(587, true, "E8A86ADCD063B73F4890215EF0146E682B89C6B6EAED2C56015FA12A7C0C4AB6")]
    [InlineData(588, false, "4E2EB014D4C909E1EBFE993AF412C71721DD6F6D8486EDEC7C56B09EEE144E88")]
    [InlineData(588, true, "4C6E39F95CCCE13865ECFADD75547FC132C06C1E9F27C025BC7D8975638997AF")]
    [InlineData(589, false, "9A65917803A0C2081721614AFA8204A0CB0112EC714B36D313C64CF07CA682BF")]
    [InlineData(589, true, "C91163ED9B67B80A9C3F2C0C4F8175FC1AFCB9851FB457F53A095FB8B93F89B0")]
    [InlineData(596, false, "CA959B08CAE8D15E69A0C01FAFBAE71408152181AF4828B72BEDEF7F196993E2")]
    [InlineData(596, true, "A4AD5DEFE97CEFE243B4F8580683180A55E559E206C42B625B0011B67E896FB5")]
    [InlineData(616, false, "B54D5079BD58E39C851B1E9B5FE6325E84A22ADCA773C0051F359EB92F22BD40")]
    [InlineData(616, true, "5F9C7E0B611A7CA358568ECB372BEC615ADD663F80680D39403B293D771174D2")]
    [InlineData(634, false, "5F1AC9ADCAE08AC13DF071BADECE76EEDB269245AD34B9658EB6B4C99C049260")]
    [InlineData(634, true, "DE55822D62C26C7578276DCD5A6DCBC9A7C5F3C5949AA34E37590BA11488F64E")]
    [InlineData(72, false, "E7F596115E146BA581E28BE23A7FFFACB2917266A5176A45FA915871AE22512A")]
    [InlineData(72, true, "7A1626F5A7BA6A2E295B8AE275CD52C70B3CCA85E37A557167EB1FE73A5F7C15")]
    public void Woody_single_calls_match_untouched_original_tile_grid_and_physical_random(
        int identity, bool terrain, string expected)
    {
        var observations = new StringBuilder();
        for (int profile = 0; profile < 96; profile++)
        {
            var workspace = new Workspace(80, 80);
            PopulateSite((x, y, type, fx, fy, active, actuated, shape) =>
                Put(workspace, x, y, type, fx, fy, active, actuated, shape), (ushort)identity, profile, 30, 30, terrain);
            var random = new RandomAdapter(1458);
            new GenerationTileFraming1458(workspace.TileStore, random).TileFrame(30, 30);
            observations.Append(profile).Append(':').Append(Hash(workspace)).Append(':')
                .Append(random.CapturePhysicalRandomHash()).Append(':').Append(random.Next()).Append(';');
        }
        Assert.Equal(expected, Digest(observations.ToString()));
    }

    [Theory]
    [InlineData(1, false, "CEBB5577FDFD4DE7E2F24AD22211FAC3537804F98A370703297B1B351AF860D8", "608A9A641FB45711BF0F638A1FEAB4AE7B6D7C49D5131DA8A184812EF551BE8F", 1444933476, 40)]
    [InlineData(1, true, "43FA5B820F939F30F9EEED5B2ED677D8CA55BB27D39E664FEBA575B4E0647A1D", "948363FA8824DFCC609FAF52A09D8942FEC45DC9598A78630BB9668707D51F4B", 338090868, 11)]
    [InlineData(42, false, "C5188C1F51A4F7E7D68D7811287EAF1FD6407E120F1D1B2B6C297835A9978231", "B896094D68F8B57D281428BAFACEDCF638B2349CC4502104BF6A790BE4748C3F", 425999661, 36)]
    [InlineData(42, true, "A3BBDF0452D774649705081C15AB2A69028A18385509F47BCE31AC1EBCDC83BF", "690E7CF8D049392461C2A111DEE12CE1E649CECAD90014A266F30143C0BAFF25", 1772209500, 22)]
    [InlineData(1458, false, "ECFA8B6252DA5A30688BAEFA119FFFCBD22947FB36CFB93F419A089BE4FA6C44", "5DDDA984D5543C33EED98EB15AD2F06E336C27FE752E537F4723C4F2B17ABCFE", 380908078, 17)]
    [InlineData(1458, true, "27EEF9FF537EAA21CEF8370A70F71BC98B0FFC20E12125FC0D96C644BC863B58", "4C46E82746169D80886F5E5D05F5203B33F143D99BCC145064729E4AD1317A1E", 1245578474, 28)]
    public void Woody_validators_match_actual_registered_FinalCleanup(int seed, bool terrain, string expectedGrid,
        string expectedRandom, int expectedNext, int expectedQuest)
    {
        // Actual server-mode source pass includes AnglerQuestSwap; old client-mode goldens skipped it.
        var workspace = new Workspace(1000, 500);
        Assert.True(workspace.TrySetLayers(140, 200));
        workspace.SetVanillaBootstrapState(BootstrapPass1458.Run(new RandomAdapter(1), 4200, false, TerraRuntime.World.VanillaSpecialWorldSeed1458.None));
        PopulatePass((x, y, type, fx, fy, active, actuated, shape) =>
            Put(workspace, x, y, type, fx, fy, active, actuated, shape), terrain);
        var random = new RandomAdapter(seed);
        new FinalPass1458(FinalStage1458.FinalCleanup, new FinalState1458()).Execute(new Context(workspace, random, seed));
        Assert.Equal(expectedGrid, Hash(workspace));
        Assert.Equal(expectedRandom, random.CapturePhysicalRandomHash());
        Assert.Equal(expectedNext, random.Next());
        Assert.Equal(expectedQuest, workspace.VanillaAnglerQuest!.Quest);
    }

    private static void Put(Workspace workspace, int x, int y, ushort type, short fx, short fy,
        bool active, bool actuated, byte shape)
    {
        var tile = new WorldTile
        {
            Type = type,
            FrameX = fx,
            FrameY = fy,
            Shape = shape,
            Flags = (active ? WorldTileFlags.Active : WorldTileFlags.None) |
                    (actuated ? WorldTileFlags.Inactive : WorldTileFlags.None)
        };
        workspace.TileStore.Set(x, y, in tile);
    }

    private static void PopulatePropagation(Action<int, int, ushort, short, short, bool, bool, byte> write, int profile)
    {
        short fx = profile switch { 1 => 22, 3 => -1, _ => 0 };
        short fy = profile switch { 1 => 22, 2 => 132, _ => 0 };
        write(30, 30, 5, fx, fy, true, false, 0);
        write(30, 31, 2, 0, 0, true, false, 0);
        // Its only neighbour is a tree crown, which is insufficient to support a side torch.
        // The torch remains until the validator actually propagates to its cell.
        write(29, 30, 4, 0, 0, true, false, 0);
    }

    private static void PopulateSite(Action<int, int, ushort, short, short, bool, bool, byte> write,
        ushort identity, int profile, int x, int y, bool terrain)
    {
        void Put(int dx, int dy, ushort type, short fx = 0, short fy = 0,
            bool active = true, bool actuated = false, byte shape = 0) =>
            write(x + dx, y + dy, type, fx, fy, active, actuated, shape);
        // Original tree dust scans down the trunk/branch column without a world-edge check.
        // A bounded solid strip supplies the terrain that real generated trees grow above.
        if (terrain) for (int dx = -4; dx <= 4; dx++) Put(dx, 8, 1);
        ushort ground = identity switch
        {
            323 => 53,
            72 => 70,
            >= 583 and <= 589 => 1,
            634 => 633,
            _ => 2
        };
        if (profile >= 64)
        {
            // Conversion sets and near misses, with independent active, actuation and shape probes.
            ushort[] floors = [2, 23, 661, 477, 60, 70, 109, 147, 199, 662, 492, 1, 25, 117, 203,
                182, 180, 179, 381, 183, 181, 534, 536, 539, 625, 627, 633, 53, 234, 116, 112, 0];
            int index = profile - 64;
            Put(0, 0, identity);
            Put(0, 1, floors[index], active: index != 31, actuated: index == 27, shape: (byte)(index % 6));
            return;
        }
        int atlas = profile % 16, arrangement = profile / 16;
        short[] frameXs = [0, 22, 44, 66, 88, 110, 132, 154, 66, 44, 66, 0, 66, 88, 22, 44];
        short[] frameYs = [0, 22, 44, 0, 88, 88, 154, 22, 198, 220, 220, 154, 154, 176, 154, 176];
        short fx = frameXs[atlas], fy = frameYs[atlas];
        if (identity == 323)
        {
            short[] palmXs = [0, 22, 44, 66, 88, 154, 176, 198, 220, -1, 45, 65, 67, 219, 221, 300];
            fx = palmXs[atlas]; fy = (short)(atlas % 3 * 22);
        }
        else if (identity == 72) { fx = (short)(atlas % 4 * 18); fy = (short)(atlas % 3 * 18); }
        Put(0, 0, identity, fx, fy, actuated: atlas == 15, shape: (byte)(atlas == 14 ? 3 : 0));
        if (arrangement is 1 or 2) Put(0, -1, identity, 0, 22);
        if (arrangement is 1 or 2) Put(-1, 0, identity, 66, 0);
        if (arrangement is 1 or 3) Put(1, 0, identity, 88, 66);
        if (arrangement == 0) Put(0, 1, ground);
        if (arrangement == 1) Put(0, 1, 0);
        if (arrangement == 2)
        {
            Put(0, 1, identity, identity == 323 ? (short)66 : (short)0, 132);
            Put(0, 2, ground);
        }
    }

    private static void PopulatePass(Action<int, int, ushort, short, short, bool, bool, byte> write, bool terrain)
    {
        ushort[] identities = [5, 323, 583, 584, 585, 586, 587, 588, 589, 596, 616, 634, 72];
        for (int n = 0; n < identities.Length; n++)
            for (int profile = 0; profile < 32; profile++)
                PopulateSite(write, identities[n], profile * 3, 80 + n * 65, 50 + profile * 12, terrain);
    }

    private static string Hash(Workspace workspace)
    {
        var text = new StringBuilder();
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                text.Append(tile.IsActive ? '1' : '0').Append(',').Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',').Append(tile.LiquidAmount).Append(';');
            }
        return Digest(text.ToString());
    }

    private static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed class Context(Workspace workspace, IWorldGenerationVanillaRandom random, int seed) : IWorldGenerationContext
    {
        public WorldGenerationRequest Request => new(Provider1458.GeneratorId, "Fixture", (ulong)seed, 1000, 500);
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
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
        public string CapturePhysicalRandomHash()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            using var bytes = new MemoryStream();
            using var writer = new BinaryWriter(bytes);
            writer.Write((uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!);
            foreach (int value in (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!) writer.Write(value);
            return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
        }
    }
}
