using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

/// <summary>
/// Comparison for the ordinary generation-time TerrariaServer 1.4.5.8
/// <c>WorldGen.GrowTreeWithSettings</c> with each <c>GrowTreeSettings.Profiles.GemTree_*</c> profile.
/// </summary>
/// <remarks>
/// Expectations were produced by calling the unmodified official method itself on the same synthetic input in both
/// pinned dedicated-server builds (Windows <c>d87e3faf...</c> and Linux <c>4b87890a...</c>), which agreed on all 63
/// cases. Each case runs 46 growth attempts across the fixture and checks every cell's type, wall, frames,
/// active/half-brick/slope, liquid and paint, the next shared RNG and how many attempts succeeded.
/// This covers the seven gem profiles only; item-driven planting and the other settings profiles are separate.
/// </remarks>
public sealed class GemTreeGrowth1458Tests
{
    private const int Width = 400;
    private const int Height = 300;

    // GemTree trunk identities in WorldGen.TryGrowingTreeByType order.
    private static ReadOnlySpan<ushort> Profiles => [583, 584, 585, 586, 587, 588, 589];

    [Theory]
    [InlineData(0,0,42,"EB18A5A6664D8156BADA88EED5E78BAA2B66FFF4940D8D4CBABEC1A10E75B5F2",1953536788,22)]
    [InlineData(0,0,1458,"8DE4B0C5B2F5476348CFA8AC8FFAC654DE3433B3D9FEDE17FE2C966BB8591943",509980761,22)]
    [InlineData(0,0,7,"495A43585BFC9B574D1D6E9D8326DC7BDAD6C6DDADA8EDF7C77C59C80842AFF1",2002832052,22)]
    [InlineData(0,1,42,"A79B406BD234D7E6821A4A03EB92439540B01FAE738C6BF244D1FEED4ED16715",1374009224,12)]
    [InlineData(0,1,1458,"346E9DA4F47793508B012CE7697B0DB1EA845E5908DC3929D91B083E180912D1",1163936186,10)]
    [InlineData(0,1,7,"3F1CA378AC1A38317FCDD98F320D811F20B485F9CDD6A21CDB37FB080ADE5584",2047619209,9)]
    [InlineData(0,2,42,"7DE96E58956DD7F983EF4B8454EC4D2A3E0FAD3BEE3BB41101C1C28C429A1878",636078777,9)]
    [InlineData(0,2,1458,"2125DBB3E5BA6952B45D0A3C10E882E6BDABAE39494E610635673141F0308B26",1163936186,10)]
    [InlineData(0,2,7,"B83998AD6EFF220336540E86DD45B191D0DA1B525BD07003899F798BCDAAAB5E",1034333174,9)]
    [InlineData(1,0,42,"9BE7E9D8EA459D2C518506E13709ADD06DF9BB4327ED7B85EB62F2C106092547",1953536788,22)]
    [InlineData(1,0,1458,"558AADC9497283B6672EECF8A6AA12B0C58732CBFC15A23E6A7390D45A40B3FB",509980761,22)]
    [InlineData(1,0,7,"26A00B066C1F2F7CD2199FFB38BF094B8B1FB7B954AD5BE81D3DB092A308244E",2002832052,22)]
    [InlineData(1,1,42,"893856DCD04B97613934AFFBA9149B5C52D46ADD1428A7C1FC6799D589B6696E",1374009224,12)]
    [InlineData(1,1,1458,"C92F942522CCD10DD04C723CFC7916138A4B2F773F1799A93B10972172E696E6",1163936186,10)]
    [InlineData(1,1,7,"FAEF78249F569D96CDCE4A8A6A409B877F086210E2AACA3DD33B8B672758459D",2047619209,9)]
    [InlineData(1,2,42,"7B5A31C21528E3149C5DE69A8086D8FCADCAE222660A25A92F561716C59BAFBF",636078777,9)]
    [InlineData(1,2,1458,"D6F3E2CC3C53D6F24709074D1EB7615F42244A96CAAC4D4CD8E5D578EB98CA47",1163936186,10)]
    [InlineData(1,2,7,"EDA4AE5B6FEC8BB783765A6A0D33D5FC90B766E1B32A6488EF0D2345214E0934",1034333174,9)]
    [InlineData(2,0,42,"66480B8AA70E202E3ED17F942F2FCC3DE4504B036F2C390C679E22CB028A8172",1953536788,22)]
    [InlineData(2,0,1458,"53B962C4F916875624740A65B7AB6311B728B05087BA3B21CE8276373835D6A9",509980761,22)]
    [InlineData(2,0,7,"97545EB0B7BE3955C9CFC813BFC1A49E9E8261DA36464C497C2DE30B5EEFB1ED",2002832052,22)]
    [InlineData(2,1,42,"BC5A13D08560616416516B01A613C37C9D6E3B296C6CA66E23ACA098BDA87A16",1374009224,12)]
    [InlineData(2,1,1458,"B77A102A43CAADAE94ED41DB468C2F23737ED12928742B50C435FC4EFBC24E80",1163936186,10)]
    [InlineData(2,1,7,"84A508D2E0CEE4B8D0C5066533E924E39FABD2B20D3C5238598FE89CFB6E3064",2047619209,9)]
    [InlineData(2,2,42,"A85F9514FA192FFAE100FAA058A9DFE47198E65D244C3E1DAEDF0BA6F2F96C54",636078777,9)]
    [InlineData(2,2,1458,"2918E970234620DFC5477F76F9A72C647C1D903365685959EC5556EF61F8813C",1163936186,10)]
    [InlineData(2,2,7,"D732ADE44991AADACB1754E5880B89CC568C619BD4653E6E4CC0DDA43AAC6ED4",1034333174,9)]
    [InlineData(3,0,42,"CFA8539DBA06C5CF058D78787585D356139415F525FB5A69A028B566526607F0",1953536788,22)]
    [InlineData(3,0,1458,"507B80F9539529B880E1E10D00630ED8E4124589F43799695DBB626510D0EFC1",509980761,22)]
    [InlineData(3,0,7,"526044CC99B221F5F95EC90C81588FEF2B5415EA0513CFB6A23E0CB61B7F22D6",2002832052,22)]
    [InlineData(3,1,42,"2E615911FCA66047C18FE0E4B491837D3DA09A0F4AFE18F0AE865F7E4EACCD49",1374009224,12)]
    [InlineData(3,1,1458,"AEFAE51726195E094A9BFBC9E38322E18F44390912621A71B030663D96D7D3A6",1163936186,10)]
    [InlineData(3,1,7,"AD91C30AEC17F582D1A2CCA2E6407C3FEC394092BC2A96D7044DB0D8B46D4804",2047619209,9)]
    [InlineData(3,2,42,"FF07DCC3835D28DC837EAAFC23E4447A39C015080E040BF5FBEDD5F872D50A12",636078777,9)]
    [InlineData(3,2,1458,"66C2ABFFA2E2CB278E862D6057DF853DA43456C4D5E100481E07E53A3F2EA845",1163936186,10)]
    [InlineData(3,2,7,"1FB43407759EFF83520C390523C4A4D9360F72D211782C63E5E664A41052ABC9",1034333174,9)]
    [InlineData(4,0,42,"F772ABD07748F6C27078EC137E479ADDC3B346535DBF0D0C47C12101B40D139B",1953536788,22)]
    [InlineData(4,0,1458,"DA80E67ECC98B09323E05C22D7FC5002C7A3BA2CC22AE0EE85C6AD8072F4230A",509980761,22)]
    [InlineData(4,0,7,"E700FFE474FFD43A492E403945CED9ED12E388A72B559966949B24DCD05C13FE",2002832052,22)]
    [InlineData(4,1,42,"F3B2C2C6908693DFD5D3DC7A124E52F8DEA34F702795E05C6A046CC18E776258",1374009224,12)]
    [InlineData(4,1,1458,"745A6350C3F8C76894C3D1EDE1BA4F68526A046987624784930343A56557F429",1163936186,10)]
    [InlineData(4,1,7,"0F3374A7FB0DB70808E53D182BD6CB6469D01A59DD41D2C0E853F14CA269284F",2047619209,9)]
    [InlineData(4,2,42,"75F2191693A46B3544EC93DA231E3B5C13B10934A6EAC5B4157E2A87243AA8B3",636078777,9)]
    [InlineData(4,2,1458,"D80EF99D1B0F5197866B1A7A0233658B30B1A214FFDCC75485A843EF4E05C451",1163936186,10)]
    [InlineData(4,2,7,"13380D6B366A1359B8BB9D6313E963910C62644095A227312625A2EEA8B0D951",1034333174,9)]
    [InlineData(5,0,42,"156970FEB496EA43F951491F70E10454A229170B92895B74E9696580D24C8ED0",1953536788,22)]
    [InlineData(5,0,1458,"AF4F8732D7840B8D47B6C8F57635A507DA9B70E4EC8173334F2A1F9305A80557",509980761,22)]
    [InlineData(5,0,7,"41BDCE294894586BF14876A8011AA86477A7AE4B5BE1265C066B8DF36A7E3410",2002832052,22)]
    [InlineData(5,1,42,"FA370E2C1B4B05D66F4C4D7B903ACB713B3650C0A93F2867A66DEEDF4325829D",1374009224,12)]
    [InlineData(5,1,1458,"A0FD4B2A75F869740DF1DA3B21302706025FCADA5E576E17DB43FCB7ECB6A847",1163936186,10)]
    [InlineData(5,1,7,"056174075BD69EE4DC9CE799B46FEB52270684457A40D5DBEA8BCCDC592B91D3",2047619209,9)]
    [InlineData(5,2,42,"2BD5C58149FA53DF74B49686AF812AAEBE2F34D2A0AACD0C3EC3927C798680DC",636078777,9)]
    [InlineData(5,2,1458,"D0AEA4AD86D0F2DE6EF1D48D6412D99AE4CA524ACC2DED58BA4A55D99F7FE211",1163936186,10)]
    [InlineData(5,2,7,"6CBEBADA24163179B6B4A0222BB2A0582B5CA853033B10B8CDE536194F1E7607",1034333174,9)]
    [InlineData(6,0,42,"E19404E54C84854405CE38EAC30A045C4428059128E08023BEA3C123F3D8E8E9",1953536788,22)]
    [InlineData(6,0,1458,"AF4933FC37388A65A4615D51E466CB8FB2207CB802A399516FFC829D7BA9BEB9",509980761,22)]
    [InlineData(6,0,7,"63C679830F099E94281A63936B03493E32D06267DBAAB7CBC3886F90CDF53D25",2002832052,22)]
    [InlineData(6,1,42,"75F3007B74127642D068B626DEB8F67EEA8BAAAEAF777785DDAACD9E5D34C34C",1374009224,12)]
    [InlineData(6,1,1458,"626E64F56AA915A94ED507D7C0724D8041B99AFC2A580EA1FAC890C9A3781C6B",1163936186,10)]
    [InlineData(6,1,7,"69447FDED6974022BD2B74B4B4699DB26C3A8B0E6422BF7DC24E74DF761DF47C",2047619209,9)]
    [InlineData(6,2,42,"F98EF5FA89E283DABCBC59DF3FBE27986C95D1F8F851259F187F7AE5F774355A",636078777,9)]
    [InlineData(6,2,1458,"78B232F66EF7A0DCFC15CC573C4F937178E7CD13A45417909A65ABECD6315C7B",1163936186,10)]
    [InlineData(6,2,7,"7DB0402A5C298A68906ED1FE5913DF0D9CAC158AA846FA7C2DE50C1E6E47CC5C",1034333174,9)]
    public void Production_grower_matches_official(
        int profile, int fixture, int seed, string cellHash, int next, int grown) =>
        Verify(profile, fixture, seed, cellHash, next, grown);

    private static void Verify(int profile, int fixture, int seed, string cellHash, int next, int grown)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            store.Set(x, y, Fixture(fixture, x, y));

        var random = new RandomAdapter(seed);
        SettingsTreeProfile1458 settings = SettingsTreeGrower1458.GemTree(Profiles[profile]);
        int actualGrown = 0;
        for (int x = 40; x < Width - 40; x += 7)
            if (SettingsTreeGrower1458.TryGrow(store, settings, x, 120, random)) actualGrown++;

        string expected = $"{cellHash}|{next}|{grown}";
        string actual = $"{HashCells(store)}|{random.Next()}|{actualGrown}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    // Normalized 11-byte cell record matching the official probe.
    private static string HashCells(WorldTileStore store)
    {
        var bytes = new byte[Width * Height * 11];
        int index = 0;
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = store.Get(x, y);
            bytes[index++] = (byte)(tile.Type & 0xFF);
            bytes[index++] = (byte)(tile.Type >> 8);
            bytes[index++] = (byte)(tile.Wall & 0xFF);
            bytes[index++] = (byte)(tile.Wall >> 8);
            bytes[index++] = (byte)(tile.FrameX & 0xFF);
            bytes[index++] = (byte)((tile.FrameX >> 8) & 0xFF);
            bytes[index++] = (byte)(tile.FrameY & 0xFF);
            bytes[index++] = (byte)((tile.FrameY >> 8) & 0xFF);
            int slope = tile.Shape >= 2 ? tile.Shape - 1 : 0;
            bytes[index++] = (byte)((tile.IsActive ? 1 : 0) | (tile.Shape == 1 ? 2 : 0) | (slope << 2));
            bytes[index++] = tile.LiquidAmount;
            bytes[index++] = tile.TileColor;
        }

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTile Fixture(int fixture, int x, int y)
    {
        ReadOnlySpan<ushort> grounds = [1, 1, 1, 117, 25, 182, 179, 0, 59, 53];
        ReadOnlySpan<ushort> walls = [2, 0, 54, 61, 1, 3, 196, 15];

        bool active = y >= 120;
        ushort type = grounds[x / 3 % grounds.Length];
        ushort wall = walls[(x / 5 + y / 9) % walls.Length];
        byte liquid = 0;

        if (fixture >= 1)
        {
            // Ceilings and obstacles that make the crown clearance test fail for some columns.
            if (y >= 105 && y < 108 && x % 23 < 9) { active = true; type = 1; }
            if (y == 119 && x % 31 == 0) liquid = 80;
        }

        if (fixture == 2)
        {
            if (x % 17 == 0 && y >= 120) type = 53;
            if (x % 13 == 0 && y >= 120) type = 182;
        }

        return new WorldTile
        {
            Type = type,
            Wall = wall,
            FrameX = active ? (short)0 : (short)-1,
            FrameY = active ? (short)0 : (short)-1,
            LiquidAmount = liquid,
            Flags = active ? WorldTileFlags.Active : 0,
        };
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
