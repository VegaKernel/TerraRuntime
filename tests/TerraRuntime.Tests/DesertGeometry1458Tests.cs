using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class DesertGeometry1458Tests
{
    // Independently invoked official DesertDescription/SandMound, optionally followed
    // by private DesertHive cluster placement (before decoration). X-major WorldTile ABI,
    // normalized from official getters; no cosmetic wall-frame fields in this ABI.
    [Theory]
    [InlineData(1458, false, 1453904749, "4CDF8D837DC658DE55CA6FD05C9FD5BC4603171CB0F189A47C6CCD3CC2302D07")]
    [InlineData(42, false, 840044686, "777066CA1F01FF5A46B4D85418ED3F36BCC9C571B36A7C437FD2CD10F175DB2B")]
    [InlineData(8675309, false, 1963163678, "3D5B20A39F9B73BD2E8AE5914931A94028B85100875B96684E5F8EC2182A1C48")]
    [InlineData(1, false, 1585620599, "EF6C92B869B2FE477896D73A2BABCEFB139396A7BA80DA9BCC979E97267B1302")]
    [InlineData(17, false, 206514952, "990F46A74F39EB3F2D95731E68AFD096EE806E5D98C9EA67E35FA1D7843F6B59")]
    [InlineData(1458, true, 1834989871, "BF48F6E8C174047E89992F645BD329B0198765BCBD5ABFEE04C1DC5C467D42AC")]
    [InlineData(42, true, 97266372, "E1A97E509886ADCE00B9298064C11BFD88E6ECD354D1002B5E8FDF83FD69A74D")]
    [InlineData(8675309, true, 1472919188, "F4098679BB8AFA0BCD9041CE3D064D594C2AFB6187AE8DDCA7ED3B3449BEA565")]
    [InlineData(1, true, 1378022692, "7B273C2759582A61037C1BC3E718A085334A3138778D5CD1C452CE8353FF003A")]
    [InlineData(17, true, 673048843, "BAF338358EC677494950D241C2D29931C51359A1FC652AC4EA7BE88FFB0DBEED")]
    public void Surface_and_cluster_fields_match_official_binary(int seed, bool clusters, int next, string hash)
    {
        WorldTileStore tiles = Fixture();
        var random = new Random(seed);
        DesertSurface1458 description = Assert.IsType<DesertSurface1458>(DesertSurface1458.TryDescribe(tiles, random, 300, 180.5, false, TestContext.Current.CancellationToken));
        Assert.Equal(278, description.Combined.X);
        Assert.Equal(44, description.Combined.Width);
        Assert.Equal(115, description.Combined.Y);
        description.PlaceMound(tiles, random, 180.5, TestContext.Current.CancellationToken);
        if (clusters) DesertHive1458.PlaceClusters(tiles, description, random, seed, 180.5, TestContext.Current.CancellationToken);
        Assert.Equal(next, random.Next());
        var normalized = new WorldTile[600 * 400];
        for (int x = 0; x < 600; x++)
        for (int y = 0; y < 400; y++) normalized[x * 400 + y] = At(tiles, x, y);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(normalized.AsSpan()))));
    }

    [Theory]
    [InlineData(189)]
    [InlineData(196)]
    [InlineData(460)]
    [InlineData(717)]
    [InlineData(718)]
    [InlineData(719)]
    public void Deeper_cloud_resets_surface_search_without_clamping_average(ushort cloud)
    {
        var tiles = new WorldTileStore(new WorldDimensions(80, 400));
        At(tiles, 30, 80) = new() { Type = 0, Flags = WorldTileFlags.Active };
        At(tiles, 30, 120) = new() { Type = cloud, Flags = WorldTileFlags.Active };
        At(tiles, 30, 200) = new() { Type = 1, Flags = WorldTileFlags.Active };
        DesertSurface1458.Surface surface = DesertSurface1458.Scan(tiles, 30, 2, 180.5, TestContext.Current.CancellationToken);
        Assert.Equal(200, surface[30]);
        Assert.Equal(250, surface[31]);
        Assert.Equal(200, surface.Top);
        Assert.Equal(170, surface.Bottom);
        Assert.Equal(225, surface.Average);
    }

    [Theory]
    [InlineData(59)]
    [InlineData(60)]
    [InlineData(147)]
    [InlineData(161)]
    public void Inactive_invalid_row_identity_rejects_without_drawing_hive_offset(ushort type)
    {
        WorldTileStore tiles = Fixture();
        At(tiles, 300, 120).Type = type;
        At(tiles, 300, 120).Flags &= ~WorldTileFlags.Active;
        var random = new Random(1458);
        var control = new Random(1458);
        _ = control.NextDouble();
        Assert.Null(DesertSurface1458.TryDescribe(tiles, random, 300, 180.5, false, TestContext.Current.CancellationToken));
        Assert.Equal(control.Next(), random.Next());
        Assert.NotNull(DesertSurface1458.TryDescribe(tiles, new Random(1458), 300, 180.5, true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Unknown_mound_wall_aborts_before_mutation()
    {
        WorldTileStore tiles = Fixture();
        var random = new Random(1458);
        DesertSurface1458 description = DesertSurface1458.TryDescribe(tiles, random, 300, 180.5, false, TestContext.Current.CancellationToken)!;
        At(tiles, 300, 150).Wall = 21;
        WorldTile before = At(tiles, 278, 140);
        Assert.Throws<InvalidOperationException>(() => description.PlaceMound(tiles, random, 180.5, TestContext.Current.CancellationToken));
        Assert.Equal(before, At(tiles, 278, 140));
    }

    [Theory]
    [InlineData(0, 1458, 277047853, "300FBE2B79C580FE80F3594B6AF67795830C11BF3F9DDEBE24E919EE5EE59EE6")]
    [InlineData(0, 42, 1233581318, "8B4C5F54B5EC8025E1317A8AC5CCC339C3201ECA02970FF9EACA4148AD8FB0A2")]
    [InlineData(0, 8675309, 1426386334, "4DEA06453CF6B4AD7FE6DAF19792CCEBC95590F10F2303D19C4FB6B3B045C8BE")]
    [InlineData(0, 1, 1507369630, "40CCF1A32AB22C9CE27F9085924472AAB318F52F0E08FA71254CF0CC2BE60210")]
    [InlineData(0, 17, 1038712051, "DA10E045E5448A37EB1AAD2B0E47D53637B386EE6C3EC63D37555FD32AA2E254")]
    [InlineData(1, 1458, 1173618192, "7AD8FAC15BF11C0620A97CE03715051D42B73958098CA55C07E810194EE900D3")]
    [InlineData(1, 42, 1936778713, "B64E96CF9094F749EF92FD23BF6EDB0DC4CDBEECEBED8732872282F9E9434E97")]
    [InlineData(1, 8675309, 1711622747, "8671B54107AF14745D17535E27E89ABB5FD3DD41800489AFD3FE83A7D8E54609")]
    [InlineData(1, 1, 1660598726, "88CA2EAC750CE86A3497DE144DFD9E1B0CEC2C870FEB9D081DDC771B1D6A8A1A")]
    [InlineData(1, 17, 1952469505, "A109AACE9E492AF7DC7BC819F33C632799A534A0BFC04FB7F8A3F29980789D00")]
    [InlineData(2, 1458, 97654478, "8FA01B1B247C21C968D11D8D9B8E7238FE0CA6D5D2DA87A0E716FB5C4BFEC364")]
    [InlineData(2, 42, 1486321220, "B8658B0AF903FC4C2E92EABA342D8FFAD7D5BA46C6E97D748D4C7158D6E4B4E2")]
    [InlineData(2, 8675309, 216440086, "14C1561BBD334AB623266B61CF10C81C762306424BE2B2EBA5E049BB5BF781D4")]
    [InlineData(2, 1, 1636206256, "EC6ED964C090418CB2B452117D56C394E6F8E84E9DCD1B38C5A048451693E9FF")]
    [InlineData(2, 17, 299772522, "131AD663B0818128DD1B9EC5F3E5B5DF41F5B6CB4B832A2ECC3C4A5F2F2CD06F")]
    [InlineData(3, 1458, 735896780, "F40098372D1AC806A41A484A4E7CF7666C5355A8855D58ACE8EC8FF0859E470E")]
    [InlineData(3, 42, 1791014166, "8F17F594641C9E5D850A957B5E35F0465FEA40C024E582C27EF65FDBA33870C6")]
    [InlineData(3, 8675309, 1421693461, "0BAF4EA96F2B54B957521E2E1AA5FDDBEBE4C7F449F10D736A331142B98D81C1")]
    [InlineData(3, 1, 690133810, "26CFB4AA9548E2A1C8375A52BD25BFD069C81E158034472963FDFEFFB6572F9C")]
    [InlineData(3, 17, 1069146118, "091F3CDC7A0203D9A21CDF18333EF64A5DDAEB7CA688F0341760401CDD9187D7")]
    public void Entrance_matches_official_binary(int kind, int seed, int next, string hash)
    {
        WorldTileStore tiles = Fixture(1200, 600);
        var random = new Random(seed);
        DesertSurface1458 description = DesertSurface1458.TryDescribe(tiles, random, 600, 180.5, false, TestContext.Current.CancellationToken)!;
        description.PlaceMound(tiles, random, 180.5, TestContext.Current.CancellationToken);
        new DesertEntrances1458(tiles, description, random, TestContext.Current.CancellationToken).Place(kind);
        Assert.Equal(next, random.Next());
        var normalized = new WorldTile[1200 * 600];
        for (int x = 0; x < 1200; x++)
        for (int y = 0; y < 600; y++) normalized[x * 600 + y] = At(tiles, x, y);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(normalized.AsSpan()))));
    }

    [Theory]
    [InlineData(1458, 335521546, "1AD0D31A8FDA38AD41F66688F797CACD3BA3D6DD2B78B7A97EAF9C489964EFA3")]
    [InlineData(42, 960903128, "E7D8BA56AF4265A55092BC0C320F37A3234B51C599BA674D21F386A08557BEC3")]
    [InlineData(8675309, 52226680, "FE632E41A99B7EDC3C833DF7A4C61ED9A544A48AA530557FEA4C985F6719C1C3")]
    [InlineData(1, 565951821, "16A2DCB6F768D07A134E364C9EFBED0FAD15616A9B5E1619D5B961B92470294D")]
    [InlineData(17, 916745811, "AA188E3A8D32D1509F9E26C95C403155D668D3C197A96C6EA05618F50947F401")]
    public void Decorated_hive_matches_official_binary(int seed, int next, string hash)
    {
        WorldTileStore tiles = Fixture();
        var random = new Random(seed);
        DesertSurface1458 description = DesertSurface1458.TryDescribe(tiles, random, 300, 180.5, false, TestContext.Current.CancellationToken)!;
        description.PlaceMound(tiles, random, 180.5, TestContext.Current.CancellationToken);
        DesertHive1458.PlaceClusters(tiles, description, random, seed, 180.5, TestContext.Current.CancellationToken);
        new DesertDecoration1458(tiles, random).Apply(description, TestContext.Current.CancellationToken);
        Assert.Equal(next, random.Next());
        var normalized = new WorldTile[600 * 400];
        for (int x = 0; x < 600; x++)
        for (int y = 0; y < 400; y++) normalized[x * 400 + y] = At(tiles, x, y);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(normalized.AsSpan()))));
    }

    private static WorldTileStore Fixture(int width = 600, int height = 400)
    {
        var tiles = new WorldTileStore(new WorldDimensions(width, height));
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            bool solid = y >= 100 + x * 13 % 21;
            At(tiles, x, y) = new WorldTile
            {
                Type = (ushort)(solid ? 0 : 1), Wall = (ushort)(y >= 140 ? 2 : 0),
                FrameX = 18, FrameY = 36, LiquidAmount = 123, LiquidKind = (WorldLiquidKind)(y % 4),
                TileColor = 3, WallColor = 4, Shape = 1,
                Flags = (solid ? WorldTileFlags.Active : 0) | WorldTileFlags.WireRed | WorldTileFlags.InvisibleWall
            };
        }
        return tiles;
    }

    private static ref WorldTile At(WorldTileStore tiles, int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
    private sealed class Random(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
