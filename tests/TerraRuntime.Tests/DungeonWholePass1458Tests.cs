using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.Tests;

public sealed class DungeonWholePass1458Tests
{
    [Theory]
    [InlineData(0)] [InlineData(13)] [InlineData(ushort.MaxValue)]
    public void Unsupported_base_wall_rejects_before_any_mutation_or_rng(int wall)
    {
        var workspace = new Workspace(128, 128);
        workspace.SetVanillaDungeonSetupProfile(new(new(0, 41, (ushort)wall, 481, 91, 96, 8, 1386),
            DungeonEntranceKind1458.Legacy, 42));
        workspace.SetVanillaBootstrapState(new()
        {
            HellChestItems = [], TreeX = [], TreeStyle = [], CaveBackX = [], CaveBackStyle = [],
            ForestBackgroundStyles = []
        });
        var graph = new DungeonGraph1458([], new(40, 40), 41, (ushort)wall)
            { FeatureBounds = new(20, 20, 100, 100) };
        WorldTile[] before = workspace.TileStore.Tiles.ToArray();

        var error = Assert.Throws<InvalidOperationException>(() => DungeonFeaturePipeline1458.Apply(
            workspace, graph, new NoRandom(), 50, 80, TestContext.Current.CancellationToken));

        Assert.Equal("Unsupported ordinary dungeon base wall.", error.Message);
        Assert.Equal(before, workspace.TileStore.Tiles.ToArray());
        Assert.Empty(workspace.CaptureGeneratedChests());
    }

    private sealed class NoRandom : IWorldGenerationVanillaRandom
    {
        public int Next() => throw new InvalidOperationException("Unexpected RNG before admission.");
        public int Next(int maximumExclusive) => Next();
        public int Next(int minimumInclusive, int maximumExclusive) => Next();
        public double NextDouble() => Next();
        public void NextBytes(byte[] buffer) => Next();
    }

    // Independent, UNMODIFIED official TerrariaServer1.4.5.8 Dungeon GenPass.ApplyPass.
    // SHA256 4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034.
    // Flat input is specified here; original setup profiles and final output hashes are literal captures.
    // Every normalized cell, next shared RNG, ordered chests/items/prefixes and Old Man anchor are checked.
    public static IEnumerable<object[]> Cases => Reference.Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(row => new object[] { row.Trim() });

    [Theory, MemberData(nameof(Cases))]
    public void Complete_ordinary_dungeon_matches_original(string reference)
    {
        string[] p = reference.Split('|');
        int seed = int.Parse(p[0]), side = int.Parse(p[2]), color = int.Parse(p[3]);
        var kind = Enum.Parse<DungeonEntranceKind1458>(p[4]);
        int entranceSeed = int.Parse(p[5]);
        const int width = 4200, height = 1200;
        const double surface = 250.25, rock = 400;
        int location = side < 0 ? 400 : width - 400;
        var palette = color switch
        {
            0 => new DungeonPalette1458(0, 41, 7, 481, 91, 96, 8, 1386),
            1 => new DungeonPalette1458(1, 43, 8, 482, 92, 94, 9, 1385),
            2 => new DungeonPalette1458(2, 44, 9, 483, 90, 98, 7, 1384),
            _ => throw new InvalidOperationException("Unknown original palette.")
        };
        var workspace = new Workspace(width, height);
        workspace.SetVanillaDungeonSetupProfile(new(palette, kind, entranceSeed));
        workspace.SetVanillaBootstrapState(new()
        {
            HellChestItems = [], TreeX = [], TreeStyle = [], CaveBackX = [], CaveBackStyle = [],
            ForestBackgroundStyles = [], CopperBar = 20, IronBar = 22, SilverBar = 21, GoldBar = 19,
            SilverOre = 9, DungeonLocation = location, DungeonSide = side
        });
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
            workspace.TileStore.Set(x, y, new WorldTile
            {
                Type = y == 250 ? (ushort)2 : y >= 400 ? (ushort)1 : (ushort)0,
                Wall = y > 255 ? (ushort)2 : (ushort)0,
                Flags = y >= 250 ? WorldTileFlags.Active : WorldTileFlags.None
            });
        var random = new DungeonSurfaceBuildings1458Tests.RandomAdapter(seed);
        var graph = DungeonGraphGenerator1458.Generate(workspace, random, surface, rock, height - 200,
            location, side, TestContext.Current.CancellationToken);
        DungeonFeaturePipeline1458.Apply(workspace, graph, random, surface, rock, TestContext.Current.CancellationToken);
        Assert.Equal(p[6], DungeonSurfaceBuildings1458Tests.Hash(workspace.TileStore));
        Assert.Equal(int.Parse(p[7]), random.Next());
        string chests = string.Join('|', workspace.CaptureGeneratedChests().Select(c =>
            $"{c.X},{c.Y}:{string.Join(';', c.Items.Where(i => !i.IsEmpty).Select(i => $"{i.ItemType},{i.Stack},{i.Prefix}"))}"));
        Assert.Equal(p[8], Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chests))));
        Assert.Equal(p[9], $"{graph.Anchor.X},{graph.Anchor.Y}");
    }

    private const string Reference = """
42|42|-1|2|Tower|1122627734|DF32FABA78A5B7486DE49A953DA25BE1BC19D8C9881579E0798BEDE4BAADB625|1349991750|4FB7086BD1A9817769749E2E827D766C565A49799A4E4C37C5FC442D1FA94F1B|401,219
42|42|1|2|Tower|1122627734|6F6417B94124B937FFD5E3784A2C0459E7BDD6B8F3D6286AE624976E22375351|608987150|555915FDD4B31B1C426BC640B6106A9B7CB0399F28183828B99CF49C278ADECB|3746,220
42|1|-1|0|Dome|1657007234|24BF33C35221168D74969F14D975575067BB17DE0A67C43E50844C54D889186E|1918553012|7B29729F761CAEE6B80FEFBDAA9A46999881BE97512478D5D3DF6A34863E5589|401,219
42|1|1|0|Dome|1657007234|31589D70C7C23F77C46C6865439205DF444744020336AD55FCF18FAE71464757|1665768670|30187D4E5E7F44351F93D20A3639D1A15AA5B14086DF84F8D837284D0A75EEE5|3746,220
42|1458|-1|1|Legacy|1627693788|6606331585573079E3DEF45A6BFC634BDCED0DED1894213FD36FC584128C4FAF|743474758|BE6B2A8C09E8452950746484E028B842499E1F508D17420ADEF127C7A9FE145B|439,220
42|1458|1|1|Legacy|1627693788|DFDF7C215A4F26A54BED24B424A11B5E26961D07609D571BABB552051037D83C|1147326067|4BB99CC896847396F4F1D35468C6DF9B45326AE01F04BA563F2B0B5F69086FD9|3762,220
42|326|-1|0|Legacy|301842919|96E432131316312AE71B0427FE56BBF4EA65AC823BFBBF33058D4F22BB299695|13494526|FEACEDFA167423676D43FA2F6BCFD3B8BBC85407049C5273D3AC60F8C1D30601|439,220
42|326|1|0|Legacy|301842919|239804FD7F70239539425A9B8DF4B6F40E674F888EA90E3C483C977A8CC45FFC|46857143|FA712D5D7780D1EC863B8CA6052FF4E7763D4263FCD32A1F4FA4E8AF3BB7C5E8|3762,220
1458|42|-1|2|Tower|1122627734|E5CEF9560B5EEBF5B0D1D546FD3EEA572916AD4B1FD5FF884ACFDC99B89D651A|1569117678|DA03ADF6CE147DB8128257890C3F66090B35D0594AE998630D3C25E22BB61EFE|479,219
1458|42|1|2|Tower|1122627734|FE3B420FCB517769CD85C0FB4463F07717046E1A170435F16438235A58A13CB8|2102429404|5F4DFF7AA1E3082B42EB325BB0B6B98CB57EC16751FC9FB662928E8A7959F6AD|3770,219
1458|1|-1|0|Dome|1657007234|59828C66037405CF12CA3C01566794E96AAC5B83622328DD3DA49A5145A231FB|1959270980|35DEFA1E1FF85E27492A41DC08F37C52D26C4C20AA7C80B24D4EB3630C8BDC53|479,219
1458|1|1|0|Dome|1657007234|12A51B8DDD0D69F37683B553B560BDE93CFCC41EF90A85DC6D1AD95553534394|1531911980|7E53BAA5632102CE5FCCB5650D5CF0C0F8506DB206BE49130A03BD33BA34BBE9|3770,219
1458|1458|-1|1|Legacy|1627693788|C99AE3F096169B727EDC6E6AF7F18A68FE8A6018D2EB23442DEAD7DDF432B5A6|1103955982|D6C9DE20C89462E08DD2207D3214ECDF7B24FF2BBAEDE1C332CF545971B3CD76|577,243
1458|1458|1|1|Legacy|1627693788|3F67C470C0BD54272F6B8B58C0C36FFCF1C5101DDBBDE4E2D1F4D2B8E1170154|697340746|D3C5828F638847D0A0A5DB49DF6B6634BF567208A9F776383437C07DAE02BD98|3644,243
1458|326|-1|0|Legacy|301842919|EA78830E2A4E4E96F484CDDE93F6B5AEC26F47318C3BC7D5A5F33EA458090017|655242262|1FF6F228BB6DF3D4647C8BF369438CEA9E5EEA2A4BEFAA55FA8F7D347406BDDF|577,243
1458|326|1|0|Legacy|301842919|4926057300D91F203F9A6092CCDEEC973D85A06C0D88A21E36B0E8983EB29B36|2012141311|AE7B0F13D5FC480A41D44686351D8912663CFA4AEF6CAE8E2444A8473FCEC780|3644,243
326|42|-1|2|Tower|1122627734|C4187EEDD539B79BCE6190A5DDDA1C60C60403B2D267084D49200DE4F843C4AB|1594529394|E721C87D470C7A8A8A997AC9C68F9B6763E6EB4281BEB5550066E5A7468CAC70|498,219
326|42|1|2|Tower|1122627734|2BC82A05DBAEA18E96BBECD145366D151EB9666617A07A4CF5E7B3A403D54D28|677300927|D9FF38C12C89D429BD99586E49D0CA95D1E5CE7461CC1A3B3960816A4D9648BA|3761,220
326|1|-1|0|Dome|1657007234|A26386E6D9220B7B64F8BD0EB6E7A3FDD10E241364945CB8E4B545F0B6B26E75|1500412144|6B44CFA6B98B76DFFAE6CA89348285410693DFD3941B7C4439F07184A5AB78A8|498,219
326|1|1|0|Dome|1657007234|3D9FDFE003B55BDD54DDB658DDA27F296CC1BFCDAC933A96BF587C5482EA7AB1|1922278255|B39BEB9BC19DF2F12955922654CC7477E9C3DB8398684383CB2ECA0B340442E6|3761,220
326|1458|-1|1|Legacy|1627693788|AA60A693ECFB72AE0D69500A6CABA03F96D7FD2304A3266BEF51D2CB7422236F|1782296184|8500B7004681F108F385976A718DD0BE4BB1B077EC6CD87E8D01C67FC278F835|563,243
326|1458|1|1|Legacy|1627693788|54CBF7A4EE11E3C06D6ACA7B43D72615D6FB773DBDAFBAD777D0C960B9E949D3|1610132089|8E915370F05C7331FC1568EB6DDB9E5CF2EBDA10A85C4BE784520DA8BE5C7603|3745,243
326|326|-1|0|Legacy|301842919|D11398801859BA2C1DBF004C5C8E68FCC65B125AFF38EA502797EC904E010868|1054851091|B551717B5AD9C59EA331C289AC14D31C047084620D20469AB29323E594E66D0D|563,243
326|326|1|0|Legacy|301842919|6DADB2859FF90A2967AD4753BA55C56488A7EE0C8791022A7D4B9EA9A794861B|351854491|3288F28156C4525C951ABB720CF84D580D749201ABE078AC361CA4AD51EECA7F|3745,243
""";
}
