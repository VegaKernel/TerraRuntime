using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

/// <summary>
/// Comparison for the ordinary TerrariaServer 1.4.5.8 <c>WorldGen.ShimmerMakeBiome</c>, including both cave
/// openings, the shape-zero stone columns and the five hundred gem-tree attempts.
/// </summary>
/// <remarks>
/// Expectations were produced by calling the unmodified official method itself on the same synthetic input in both
/// pinned dedicated-server builds (Windows <c>d87e3faf...</c> and Linux <c>4b87890a...</c>), which agreed on all 36
/// cases. Each case checks every cell's type, wall, frames, active/half-brick/slope, both liquid fields and both
/// paint channels, the next shared RNG and whether the biome was accepted. Candidate selection and the pass-level
/// retry loop are verified separately.
/// </remarks>
public sealed class ShimmerBiome1458Tests
{
    private const int Width = 700;
    private const int Height = 700;

    [Theory]
    [InlineData(0,330,42,"A5EAFFD8D47B67D67172CC0AE54F1CA46B92945E58B08734751448D067D9CC8C",1964419600,true)]
    [InlineData(0,330,1458,"6CB8389BB7E70337284B5380DF782F6470A329C76409CBA6625C564E701F4C94",584977514,true)]
    [InlineData(0,330,7,"F4E176CF61FB561EF48433741174E147253522DEE7AA66E5E177414C37E425B1",883749566,true)]
    [InlineData(0,330,2,"0D66D136B15C92B619AD43B47BCAC5FEA2F87356099E8330C1F3D9A1A945170F",1169999690,true)]
    [InlineData(0,330,11,"B9DF0D79F483FF05B9AC912ED3D6E48771B120BF7F7D7711D12A88DCA50529B2",751972871,true)]
    [InlineData(0,330,13,"0BD947E890A465DD8FEC2A0CAEDF386ADBB38B9174BC5B4595F2289076507F9A",83738434,true)]
    [InlineData(0,350,42,"24926BB9D15A40FB57323C40E72E7C86369134D0EF004F7C2C0AF9C3F5A9CFC1",1964419600,true)]
    [InlineData(0,350,1458,"D12B4F4F8C68176FA5F4F98D6E58257658DFBD5584754B0C0E63F18EEAD0E1F8",584977514,true)]
    [InlineData(0,350,7,"ECCA29DB69295BA8B147C8EC485747FE018B50C4EAED15467EC04D771BB6C332",883749566,true)]
    [InlineData(0,350,2,"95A2ECF832AB59A7461B838B5455C418EAEE1D9777B6B35A31476420EA269787",1169999690,true)]
    [InlineData(0,350,11,"E8D49CD81156C6454E412A6A8FCF203B3BCD2B19DAA8E7EA4B5F82FF57B94D1B",751972871,true)]
    [InlineData(0,350,13,"B4FECB98F5ECD6C56DC56AE833646CEA727676B34E8EB1C70E7985772F91E472",83738434,true)]
    [InlineData(1,330,42,"D3354847E5382495B870A7CCC484375BA1ABE38480C5E6034BF6BA9AD0DCC08C",1062671920,true)]
    [InlineData(1,330,1458,"717106DFDCDB7E3B6CBF7FC1F21107FE2C244B2E34788C82A3580C5748E9D6D8",1024371649,true)]
    [InlineData(1,330,7,"05DF880B7EEAE4252C4C1B20E720B33DBF5301F8AB02DADE086703B1FAF4B454",2095640034,true)]
    [InlineData(1,330,2,"969A88CAF935DDD7F5645C6D8E33DEC6561D62B62FA1F1544005E0B3CF173FF9",1169999690,true)]
    [InlineData(1,330,11,"2BA934A66FC71BE5FE2D492DBB2266AE48BE613E55A08BE1824FD5C307AFDD4A",1485784686,true)]
    [InlineData(1,330,13,"8BCCCB0991282B65FA605F973247385D47C07003B8180A75BA3CA271D4B85BC2",1995793492,true)]
    [InlineData(1,350,42,"68C54A5684915394C1646C19C4390C265549156458F8B9B464A7FF96A78B5FC5",1476417900,true)]
    [InlineData(1,350,1458,"4A4B91FF218BA2E702C298A343C2911EBFC07201539F9EE4FD48387593243CCE",1420029008,true)]
    [InlineData(1,350,7,"157AEBEB3FF236258042E80A8EE08B2C8B8E7189B508A30035A1A17BDB949451",2095640034,true)]
    [InlineData(1,350,2,"8E1A73FF84BF3691F009E5D3A61E5F091185D70ABF44B740330EECD9130AA192",457778700,true)]
    [InlineData(1,350,11,"D39F62819087EED0442EC23E708C52AA3FCCA6F361C48919B49BBABC13E7C856",399986792,true)]
    [InlineData(1,350,13,"7F999847D9ABAC7FE3757415945E17A98D8A912A3F65C0F86CBA60D365C492D8",178439484,true)]
    [InlineData(2,330,42,"12377D2772FA43A077F6B239C4CF40D286CEDBCE68B6A7DC0FAEED88DB1759AE",1555655117,false)]
    [InlineData(2,330,1458,"12377D2772FA43A077F6B239C4CF40D286CEDBCE68B6A7DC0FAEED88DB1759AE",370853568,false)]
    [InlineData(2,330,7,"12377D2772FA43A077F6B239C4CF40D286CEDBCE68B6A7DC0FAEED88DB1759AE",91104737,false)]
    [InlineData(2,330,2,"12377D2772FA43A077F6B239C4CF40D286CEDBCE68B6A7DC0FAEED88DB1759AE",1722583523,false)]
    [InlineData(2,330,11,"12377D2772FA43A077F6B239C4CF40D286CEDBCE68B6A7DC0FAEED88DB1759AE",1792398814,false)]
    [InlineData(2,330,13,"12377D2772FA43A077F6B239C4CF40D286CEDBCE68B6A7DC0FAEED88DB1759AE",1569304029,false)]
    [InlineData(2,350,42,"12377D2772FA43A077F6B239C4CF40D286CEDBCE68B6A7DC0FAEED88DB1759AE",1555655117,false)]
    [InlineData(2,350,1458,"12377D2772FA43A077F6B239C4CF40D286CEDBCE68B6A7DC0FAEED88DB1759AE",370853568,false)]
    [InlineData(2,350,7,"12377D2772FA43A077F6B239C4CF40D286CEDBCE68B6A7DC0FAEED88DB1759AE",91104737,false)]
    [InlineData(2,350,2,"12377D2772FA43A077F6B239C4CF40D286CEDBCE68B6A7DC0FAEED88DB1759AE",1722583523,false)]
    [InlineData(2,350,11,"12377D2772FA43A077F6B239C4CF40D286CEDBCE68B6A7DC0FAEED88DB1759AE",1792398814,false)]
    [InlineData(2,350,13,"12377D2772FA43A077F6B239C4CF40D286CEDBCE68B6A7DC0FAEED88DB1759AE",1569304029,false)]
    public void Production_builder_matches_official(
        int fixture, int centerX, int seed, string cellHash, int next, bool made)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            store.Set(x, y, Fixture(fixture, x, y));

        var random = new RandomAdapter(seed);
        var biome = new ShimmerBiome1458(store, random, TestContext.Current.CancellationToken);
        bool actualMade = biome.TryMake(centerX, 350);

        string expected = $"{cellHash}|{next}|{made}";
        string actual = $"{HashCells(store)}|{random.Next()}|{actualMade}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    // Normalized 13-byte cell record matching the official probe.
    private static string HashCells(WorldTileStore store)
    {
        var bytes = new byte[Width * Height * 13];
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
            bytes[index++] = (byte)tile.LiquidKind;
            bytes[index++] = tile.TileColor;
            bytes[index++] = tile.WallColor;
        }

        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTile Fixture(int fixture, int x, int y)
    {
        ReadOnlySpan<ushort> materials = [1, 1, 1, 0, 59, 147, 161, 396, 53, 60, 117, 179];
        ReadOnlySpan<ushort> walls = [1, 2, 3, 40, 61, 15, 196, 0];

        bool active = y >= 120;
        ushort type = materials[(x / 6 + y / 8) % materials.Length];
        ushort wall = walls[(x / 4 + y / 11) % walls.Length];
        byte liquid = (byte)((x + y) % 9 == 0 ? 90 : 0);
        var liquidKind = (WorldLiquidKind)((x + y) % 3);

        if (fixture >= 1)
        {
            // Pre-existing caves so the openings must walk through mixed terrain.
            if ((x / 17 + y / 13) % 5 == 0 && y > 200) active = false;
        }

        if (fixture == 2)
        {
            // A refusal band: Ebonstone inside the candidate square.
            if (x > 400 && x < 404 && y > 300 && y < 304) type = 25;
        }

        return new WorldTile
        {
            Type = type,
            Wall = wall,
            FrameX = active ? (short)0 : (short)-1,
            FrameY = active ? (short)0 : (short)-1,
            LiquidAmount = liquid,
            LiquidKind = liquidKind,
            TileColor = (byte)((x + y) % 4 == 0 ? 3 : 0),
            WallColor = (byte)((x * 3 + y) % 5 == 0 ? 4 : 0),
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
