using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class DungeonLayout1458Tests
{
    // Actual LegacyDungeonLayoutProvider.LegacyDungeonLayout, official Linux TerrariaServer1.4.5.8.
    // SHA256 4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034.
    public static IEnumerable<object[]> OfficialLayouts() =>
        Reference.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(row => new object[] { row });

    [Theory]
    [MemberData(nameof(OfficialLayouts))]
    public void Layout_matches_official_cells_room_bounds_cursor_direction_next_rng_and_entrance_origin(string row)
    {
        string[] fields = row.Split('|');
        int fixture = Int(fields[0]), seed = Int(fields[1]), palette = fixture % 3;
        var tiles = Scene();
        var random = new RandomAdapter(seed);
        var cursor = new DungeonPoint1458(fixture < 4 ? 250 : 750, 450);
        DungeonPoint1458 lastHall = default;
        DungeonPoint1458? entrance = (fixture & 1) == 0 ? null : new(fixture < 4 ? 320 : 680, 180);
        int steps = fixture < 2 ? 0 : fixture < 4 ? 20 : 35;
        var components = DungeonGraphGenerator1458.GenerateLayout(Renderer(tiles, palette), random,
            ref cursor, ref lastHall, steps, entrance, TestContext.Current.CancellationToken);
        Assert.Equal(fields[2], Hash(MemoryMarshal.AsBytes(tiles.Tiles)));
        DungeonSamplingBoundsReference1458.AssertMatches($"Layout|{fixture}|{seed}", tiles.Dimensions,
            new(fixture < 4 ? 250 : 750, 450), components);
        Assert.Equal(Int(fields[3]), random.Next());
        Assert.Equal(Point(fields[4]), cursor);
        Assert.Equal(Point(fields[5]), lastHall);
        Assert.Equal(Point(fields[6]), DungeonGraphGenerator1458.ResolveEntranceOrigin(components));
        var rooms = components.Where(c => c.InnerBounds is not null).ToArray();
        Assert.Equal(Int(fields[8]), rooms.Length);
        string sequence = string.Join(';', rooms.Select(c => FormattableString.Invariant(
            $"{c.Start.X},{c.Start.Y}:{c.End.X},{c.End.Y}:{c.InnerBounds!.Value.Left},{c.InnerBounds.Value.Top},{c.InnerBounds.Value.Right},{c.InnerBounds.Value.Bottom}")));
        Assert.Equal(fields[7], Hash(Encoding.UTF8.GetBytes(sequence)));
    }

    [Fact]
    public void Highest_room_uses_interior_not_shell_and_retains_first_equal_height_room()
    {
        DungeonComponent1458 Room(int outerTop, DungeonBounds1458 inner) =>
            new(DungeonComponentKind1458.Room, default, default, new(10, outerTop, 90, 100), 42) { InnerBounds = inner };
        var first = Room(5, new(20, 30, 40, 80));
        var second = Room(10, new(50, 20, 71, 80));
        var tied = Room(0, new(80, 20, 90, 80));
        Assert.Equal(new(60, 20), DungeonGraphGenerator1458.ResolveEntranceOrigin([first, second, tied]));
    }

    [Fact]
    public void Missing_room_metadata_never_substitutes_outer_bounds()
    {
        Assert.Throws<InvalidOperationException>(() => DungeonGraphGenerator1458.ResolveEntranceOrigin([]));
        Assert.Throws<InvalidOperationException>(() => DungeonGraphGenerator1458.ResolveEntranceOrigin(
            [new(DungeonComponentKind1458.Room, default, default, new(10, 20, 30, 40), 42)]));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Invalid_or_cancelled_layout_does_not_consume_rng_or_mutate_tiles(bool cancel)
    {
        var tiles = Scene();
        string before = Hash(MemoryMarshal.AsBytes(tiles.Tiles));
        var random = new RandomAdapter(42);
        DungeonPoint1458 cursor = new(250, 450), last = default;
        void Run() => DungeonGraphGenerator1458.GenerateLayout(Renderer(tiles, 0), random,
            ref cursor, ref last, cancel ? 20 : -1, new(320, 180), new CancellationToken(cancel));
        if (cancel) Assert.Throws<OperationCanceledException>(Run); else Assert.Throws<ArgumentOutOfRangeException>(Run);
        Assert.Equal(before, Hash(MemoryMarshal.AsBytes(tiles.Tiles)));
        Assert.Equal(new RandomAdapter(42).Next(), random.Next());
        Assert.Equal(new(250, 450), cursor);
    }

    private static WorldTileStore Scene()
    {
        var tiles = new WorldTileStore(new WorldDimensions(1000, 800));
        tiles.Tiles.Fill(new WorldTile { Type = 1, Wall = 40, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed,
            TileColor = 3, WallColor = 4, FrameX = 18, FrameY = 36 });
        return tiles;
    }
    private static DungeonGraphGenerator1458.Renderer Renderer(WorldTileStore tiles, int palette) =>
        new(tiles, palette == 0 ? (ushort)41 : palette == 1 ? (ushort)43 : (ushort)44,
            (ushort)(481 + palette), (ushort)(7 + palette), 100, 200, 600, 1, 998, 25, 20, 35, 10, TestContext.Current.CancellationToken);
    private static int Int(string text) => int.Parse(text, CultureInfo.InvariantCulture);
    private static DungeonPoint1458 Point(string text) { var fields = text.Split(','); return new(Int(fields[0]), Int(fields[1])); }
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }

    private const string Reference = """
0|42|85DCA2F4E24FBD55C9150FDA3648DC397254D9D32636FCF6F0BE9DEBAE0ADEDD|361709742|250,450|0,0|246,421|2CBDB4CDFADE5A045C112CBAAAC1F0A5A56716CE61FBF1100B0215E47229B4E6|2
0|1458|FB01BA25A032F7C5A70756C96F2B31A44558DF6AEA6B1EB50CF584E212CDEA51|1014131397|250,450|0,0|255,419|A7E8373E73B020E66EE68C786B1D131BF642DE628151055665594758F923CB46|2
0|8675309|593AAF733FDFA39A0A4A0C91A37E9A68C13A673DA92EB962855EEE20EB0858E8|1090045550|250,450|0,0|246,412|2E0837D555935948F1B3DD5E592B2F276A0DA3D8F46296DAEC38FC4BDF67E9E7|2
1|42|35B5276F701593A603DC5E8128A83ED77C7A9F7E4ADBEF2518910A9FA8299864|563913476|323,210|0,0|323,188|75E5D49398956D4F3FE7363B8D05E7499DD08D0722D56BE5B237710025165139|2
1|1458|7DA2CC42BF2A7C9A703C040C87C58A4AF4C8DE7A043E95113569AE636A5FDCD3|1507290721|318,210|0,0|310,179|D2865E378F014115639B54CF93B1D93C2FB3F9890FD6827FDCC587B2B6E13615|2
1|8675309|38DEF6EE0AF300F21D1BD265AE80C53836B29D8BFFC3E870B6739BC34BC9A757|142331800|325,210|0,0|322,180|0AFA4C786B1A10C0293C6339BDD7BFE364E7C55A06DD79A53F6C5AA1D9A8622B|2
2|42|C8C683B3BF21C8E4ECEA74D3C3694149D9E3655C483A1C1D1B7D61325249FA61|1658777195|194,374|1,0|197,343|F167BEFDE9B9B9406FEB27A1D872B08F5C98E161CF3C19476CBD88586D722E2B|5
2|1458|A662C7EEE42FEC7B058E9632AD923E33C63C25696D0AE28852BF55DC4E751055|1984828414|238,342|0,1|241,293|89BF6120F956E1AC72CA3F48576D3C840F3542697BA10345BAEBBACC4830C993|5
2|8675309|F89661B3D2623D866297A6F9530209356D09DE0E2CE09F98453D3E6FEFBC9E70|763761081|255,398|-1,0|258,370|6020EBFB919BD4C4316FAE84A6110EAF8FFC27789E77FF4329E2A8FE646DACE9|4
3|42|FA385B4AAFFEA8CA17CCE203CF914B69A71B320B5416B6E1CC087BC78651962B|1658777195|282,407|-1,0|319,189|55A85C572FF11A80837B347C11145CEC2229FC19DCFC47EB689147229F800D44|4
3|1458|A1C91CE5C19D707D09AAB230FD4AB6B6EA850ACFF5721CA3CE83686DDF44BCE5|498875442|213,383|1,0|318,187|AA539B943EF5428FC6D2627C362607A2BDD632C4BAB8B68545DAC703C5AD1828|5
3|8675309|B47C31720DC28A6908E451D746F8446252CE5861E1EDA4963BD8D1D9CA0E656E|676860148|234,497|0,1|317,192|05C6DD7A0CDC1884554BE2535300FA8EE43821F0122062334CB1963BD04064B5|5
4|42|48AB2F8EE04ACC9E48108D7E7275ACAA5F7B1530412D513CC0589E1E9C53C872|304685193|750,284|0,1|762,229|0B73272C710249E17A3B9119B5FEF67B064BFBCA20E09BFFD0C0901843D0BBAA|8
4|1458|639905C6ABD2290D96C4070968389D7B077487BEADBCC0C42C59C22C63195BD3|1064729579|840,460|1,0|784,222|E0D927EA5108C7A440A8D7C7B732C03525EB8F7BE429CDA743469082508C8155|7
4|8675309|29F7A12C01899A64E79FAF39FF51320A69602FBCBC65FC76589D76D89C7CF6B7|3488751|837,333|1,0|838,305|FB701FEC89A60B9A2FB7C4FD6705428D11C280D6E6DE3277BF3842F2AE9284CE|7
5|42|88221A2675E058927A389D99F8DFD53C0C0ACEF5A9D1825D3E47D40439F30F0C|304685193|747,486|1,0|679,189|B4F9E49AC95B01DAB4AD6170F24429E4EF81A0B1E2C74667214F499EE403B6EF|7
5|1458|060A582B5DC3C4A38AE235BB478E975D19D0A4333AE3871DF13C351A3E0BE44B|575883921|735,376|-1,0|678,187|38A2AF25478DC14B9AD0C8993670312F5162B512E2058405FCEE5476F7C90F1D|7
5|8675309|E15045CC3E217A37348E7E60F88B722C51A5EA0146722A9F9C2BF3D215B22333|1204884894|770,429|0,-1|677,192|454D0AFF4B2016391B7B31FF9C30460DB208AD785284225DCF468101B4D45AAE|7
""";
}
