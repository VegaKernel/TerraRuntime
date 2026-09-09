using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Worlds;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class DungeonProductionPrefix1458Tests
{
    // The real production prefix supplies the SAME input to the unmodified official Dungeon GenPass
    // and our actual DungeonPass. These goldens prove this pass on real terrain, not parity of the prefix.
    // Original executable: TerrariaServer 1.4.5.8 Linux, SHA256
    // 4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034.
    public static IEnumerable<object[]> Cases => Reference.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(static row => new object[] { row.Trim() });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Complete_pass_matches_official_cells_chests_anchor_and_rng_on_real_prefix(string row)
    {
        string[] p = row.Split('|');
        int width = int.Parse(p[0]), seed = int.Parse(p[1]);
        bool crimson = bool.Parse(p[2]);
        int height = width switch { 4200 => 1200, 6400 => 1800, 8400 => 2400, _ => throw new InvalidOperationException() };
        var request = new WorldGenerationRequest(Provider1458.GeneratorId, "Dungeon prefix", (ulong)seed, width, height)
        {
            SeedText = seed.ToString(),
            Options = new(WorldGenerationGameMode.Classic, crimson ? WorldGenerationEvil.Crimson : WorldGenerationEvil.Corruption)
        };
        var workspace = new Workspace(width, height);
        var provider = new PrefixProvider();
        var result = RuntimeWorldGenerationExecutor.Execute(provider, in request, workspace,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.ToString());
        Assert.Equal(p[3], DungeonSurfaceBuildings1458Tests.Hash(workspace.TileStore));
        Assert.Equal(int.Parse(p[4]), provider.NextRandom);
        string chests = string.Join('|', workspace.CaptureGeneratedChests().Select(c =>
            $"{c.X},{c.Y}:{string.Join(';', c.Items.Where(i => !i.IsEmpty).Select(i => $"{i.ItemType},{i.Stack},{i.Prefix}"))}"));
        Assert.Equal(p[5], Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chests))));
        Assert.True(workspace.TryGetDungeon(out var anchor));
        Assert.Equal(p[6], $"{anchor.X},{anchor.Y}");
    }

    private sealed class PrefixProvider : IWorldGenerationProvider
    {
        public WorldGeneratorId Id => Provider1458.GeneratorId;
        public int? NextRandom { get; private set; }
        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) =>
            new SourceBackedFinal1458().BuildPlan(in request, new PrefixBuilder(builder, this));

        private sealed class PrefixBuilder(IWorldGenerationPlanBuilder target, PrefixProvider owner) : IWorldGenerationPlanBuilder
        {
            private bool complete;
            public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass)
            {
                if (complete) return;
                if (descriptor.Id.Value == "terraria:1.4.5.8/Dungeon")
                {
                    complete = true;
                    pass = new ObserveDungeon(pass, owner);
                }
                target.Add(descriptor, pass);
            }
        }

        private sealed class ObserveDungeon(IWorldGenerationPass inner, PrefixProvider owner) : IWorldGenerationPass
        {
            public void Execute(IWorldGenerationContext context)
            {
                inner.Execute(context);
                owner.NextRandom = context.VanillaRandom!.Next();
            }
        }
    }

    private const string Reference = """
6400|8675309|false|5E1654189A764341FC481C309AB3D4F529F49C862007F5CB966E7AE8447D50EF|948111240|55FCE27B30991E1B6D4E9C1298C4E7DEA20974A1E7E364B517FED96DC4D78E1A|595,335
8400|8675309|false|0749E2049CF37A616D931E924E955D62D13ECE5F473F718B92440694051DFBE6|1166708097|13EC24C3C3E926B262167022886C72F1E688E80B0228CE3627CE10C6FA548351|1187,442
4200|1458|false|4F4198302D28EE3DC2AE07DCD70A6D166496B3315EB2B986EB73916F91AC40E2|1903353982|5C0B73CBE3C9A9B477A250B5E1C0C882FD1514F31E09AD66BC96740CF85B0756|595,240
4200|42|false|8E02584A1E5A895B6B7FFF1BD397CE561BAAF75A88EE8FED143695BF66C1FF57|215814313|7CE982AE60B57E1B29A54B3AD9F9932D41B917E7835C0CED0F1F0423DD2D6767|3519,200
4200|1|false|2245EB57F92C917EA3A20EA122FF069E0DDAFB3B2C226214BA6E96650A9E49E3|1451160210|A3032ABD28FBF06B3A3137B6BE83024E9161B568DE04DF9B586EFD585711F62A|3455,174
4200|326|false|3F4D3359D776E094EA99F6144A3F8E08F882D4F999D9C5958928909784317F4B|675757680|15CD2523AF206866E50368DF4BE246D03B367B26F8159D6EAAFD92CA3BFC7088|776,247
4200|8675309|false|3F83DA6C2CA7BDC2FE88AA025FE7C97D464B3FFB7E0FC8C2F62B69CF2A03B3D8|1490254570|E9C4BE2D1809BA0A6805FB188EF1A2604DB0988816A705ABFD376F0DFA638F9E|3315,255
6400|1458|false|EB7CF800E073A33E71EBFE6C98ECE01D197C9036D076E04ECB51EB55A2730E31|1131265385|C8382C25A627F597EAE6346D53A7407A9033A8E5AE59D28D9BA9558C2663BC73|857,382
6400|42|false|05C384644C62A9AE5D66870E056E6D0AA195906FD7C432DEAA81FE2037CBBCDB|2021486979|B8019D118A3EB449A904145B383E0FA1C5B7894246FD8AD3326EAF583FFFD271|798,355
6400|1|false|32606743C408B53CBEDBB4409ABCF03E5F28D6654374BE7A6BE0E72AAB7698F2|1840417391|07487E9E766D9DBDF1CFB388D447352B71B96481D244F4A1D09C9029AD1611EE|5149,436
8400|1458|false|824E4CE0D1AF2A42DACB8E7E02B14B01296A6383EFB0E647852AA207DD3BBB9C|290629091|ECD3732E98160025CE8403D1FF95F3D45EDC169E26BE8AB71D13F21EC649ECCA|7432,512
8400|42|false|14A49D292E231EE6B38964A1A0C470835F68FBACD3E4FF330428B9EC7C206FA8|75117197|B079EBAF97A8D47BED3A18CCAE66BC4769E70765D20C926DAC7BD30E93153D7C|963,476
8400|1|false|08AB3B7DE205BC5DB77F5402BE1A8EC3EAA92D38974C83E87CC9B183DB7E4DD9|248596413|5FC5AC1947A865572A74A0ECCDACDAB507BF8B897A041F55B608F72E136A2AA0|854,380
4200|1458|true|5F2AB1F123AD632175E83C0C3C5F10F04D442BAC229B2C723C6B0555401590E7|1903353982|66D143B7A94D3D6C9B839B9889645D1655BD91245FA64264DF59F714E7A9DC49|595,240
6400|1458|true|A7573FF8B2E7C57658A3B15B739CE93A7A1117664E3EBFDDFA441B10F1998632|1131265385|47BC89D88A93EFCD9E0B58C0EE06C7201C15F66B541A52714DB89392C87EC1CF|857,382
8400|1458|true|676BB7C5A3358FAE8E4E2C9CAF5B50FA1A462D12ECE5900D5FC2D9EE236F5EA6|290629091|26F42F1769C4D45E2D44AA9BC6CEBB1C6DC01C1807EB6E6A2C2F5CE888FF2185|7432,512
""";
}
