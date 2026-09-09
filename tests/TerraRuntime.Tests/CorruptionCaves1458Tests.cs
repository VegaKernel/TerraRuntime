using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class CorruptionCaves1458Tests
{
    // Official 1.4.5.8 executable: complete x-major normalized cells and next RNG.
    // Modes: left/right sideways; main with orb; short main without orb.
    // Fixtures: ordinary terrain; protected walls/materials/ore/orbs; painted/coated mixed walls.
    [Theory]
    [InlineData(0,0,42,"41B5245D0C163B9756D2EAF82F9FD93EA173AE67733FD665A1D801CD44E5916D",159755230)]
    [InlineData(0,0,1458,"2FEA04F499DDE870E6C986221B5E0D8FEBAB7A6EF14E0A48642692D907D9ACF2",966306342)]
    [InlineData(0,0,8675309,"EC28515ED63B376A5BCE153EBF3B1D6FC8E33103B8E9E09BA23CD6ECA38032D1",1602928760)]
    [InlineData(0,1,42,"C4004580EEAF9A24AB0096340406E81635CFA36857E57203E6E652BD8C3236F2",1964860214)]
    [InlineData(0,1,1458,"633FF015FE1520EB2C649C2F45826D8B9FD0B5CBC567D71FE27E2055DE27A27A",280705957)]
    [InlineData(0,1,8675309,"579CD01F8363DB675B9147D1169FD4D7D56324AB97E01CE828BE38281E2E331E",692508212)]
    [InlineData(0,2,42,"FBEF02AA450B96884396CCED895997ACAF29B5D76FF56339CC3293DA24742EE9",1693138651)]
    [InlineData(0,2,1458,"B360755208FC03592A548F63EF348D690AC00C83D8E085615120CDDE88D60D30",1152849775)]
    [InlineData(0,2,8675309,"940D0A8AE47001E42068ED3FC4A1646C1FA489263D9B9321BD76D2C070A7CC7D",1917848523)]
    [InlineData(0,3,42,"FE83C0AFBCF4E77ECD3BEBF748CD4EEC637C53018903B4D91E859727833B01D1",1146240857)]
    [InlineData(0,3,1458,"D55BFB9DFB6F28D798372EA901625B16B169099C1E671361154CE19B7AD80F3E",396576322)]
    [InlineData(0,3,8675309,"756F64A2256E01FC3B222CA227EDCF11FD9DB6ABA672022C344DCCED954EA110",1053500609)]
    [InlineData(1,0,42,"DA896AC4EBF7A8F6C2A1E6B9A480EDFFD62FA66C6C83A0BF7752DB1D22431CD0",2035686788)]
    [InlineData(1,0,1458,"8E2335B9D2E09664BC5BC7BB46A56C1AAB5DAC9423D0FF1E629D2409D661DF3F",1427348785)]
    [InlineData(1,0,8675309,"D29B319F687FEC22EE3DE4B14FBBBFAB1D602E733A4888F54FFC6D64F37312D7",871327414)]
    [InlineData(1,1,42,"F6942B194B26E3B5856643D3A6922215B9CD46DD918E4BB29627043809857959",1740853059)]
    [InlineData(1,1,1458,"A54C560B280368202033E498DBD3220DD1E905ADACC7182A7E378F77FB19B533",1585317490)]
    [InlineData(1,1,8675309,"D7D9AAC021B085C8466BCC1DF85BD2FA44AD2BAFC5A021E56A59C6EAA4EA1751",465746494)]
    [InlineData(1,2,42,"D9EDBCF9183DFD09419B748FB38CA56B4287CDD7DCA815A94536061CF5B2EE3C",1600339491)]
    [InlineData(1,2,1458,"34EEBE9B0D8D20AE93F19ED5EBCE0687307E917B5AE20D982DB048C8EB60AE1F",652017011)]
    [InlineData(1,2,8675309,"80611A47E31F9757591D109AE2ADD56547E157F962C873B5086678D0A351F783",687344687)]
    [InlineData(1,3,42,"1A529B1DE0785E7A2161A459595121EEC628E01A4EDF2378ECD15C6DF1E821B6",1822737195)]
    [InlineData(1,3,1458,"0F87B1C778C212819BE121901667428A2F97ACECE54B9B56D307D45860427545",1849974406)]
    [InlineData(1,3,8675309,"9FF6173E1B5C9DA900CA8A653789160D2A2A09DCFF5F1BFE66FAA91D9B0B973D",476424824)]
    [InlineData(2,0,42,"52B05305A5556D71BAE0BA170B3BA1AB19D2C19F794090104D3A41789F49BF8E",1854413107)]
    [InlineData(2,0,1458,"F1EFC162E94EF11D7717D9A4CAE8D1E7D9FE70119A765B686CBA5AF7F2CBFD95",365012065)]
    [InlineData(2,0,8675309,"E3260CBB8575BC4B7845F5C2C298613A1B34B33593EB7B30AC924EA246AD57E3",586087845)]
    [InlineData(2,1,42,"0CF9C6490E0C13A44420BAF9D4ACF8CDE32A18058CC0AE0E5B5FF6CD46E00578",365174716)]
    [InlineData(2,1,1458,"5ABB1ABBFD0AEFD938F13B041FF85BA5D029FCE95503A21F596058DD4D26FCA7",656821331)]
    [InlineData(2,1,8675309,"5AD8C3A858600009A1F0A948BD695447E92BA0EA130ABF30D314DD07069565E2",377538120)]
    [InlineData(2,2,42,"E8BD614F50090F1EB7E9017421C8368850DD3659AEFF3A7352C0D1411D816D49",1162342262)]
    [InlineData(2,2,1458,"E03234E16293BF57FA7D1710C453D5F4401586BF0FF6E1CB5EFDFAE6E632C0EF",1805616287)]
    [InlineData(2,2,8675309,"315D86013F06E9E47ACD4CC2394CF6ABCE86061A2A4DA2226B2D815133D09503",1909914035)]
    [InlineData(2,3,42,"300FF7913DDC5DE42BC8592B96F31391374D356AE1CF00C0E47B85CBBE363E04",1213866)]
    [InlineData(2,3,1458,"522A51124027111BEBBE75B14AE29E5FE8576F87D51601F908542989162532A8",1036232829)]
    [InlineData(2,3,8675309,"54EA873636B3E9772A406ABABAE817B87C6C5A1F1EDA2666714EB1322536E3EE",611206009)]
    public void Chasms_match_official_cells_and_random(int fixture, int mode, int seed, string hash, int next)
    {
        var store = new WorldTileStore(new WorldDimensions(1000,600));
        for (int x = 0; x < 1000; x++)
        for (int y = 0; y < 600; y++)
        {
            var tile = new WorldTile { Type = (ushort)(y < 200 ? 0 : 1), FrameX = 18, FrameY = 36,
                Flags = y >= 160 ? WorldTileFlags.Active : WorldTileFlags.None };
            if (fixture == 1 && y >= 160)
            {
                if (x % 31 == 0) tile.Wall = 7;
                if (x % 37 == 0) tile.Type = 481;
                if (x % 17 == 0) tile.Type = 22;
                if (x % 23 == 0) tile.Type = 31;
            }
            if (fixture == 2)
            {
                tile.Wall = (ushort)(x % 3 == 0 ? 2 : x % 3 == 1 ? 0 : 83);
                tile.WallColor = 4;
                tile.Flags |= WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall;
            }
            store.Set(x,y,tile);
        }
        var random = new RandomAdapter(seed);
        var caves = new CorruptionCaves1458(store,random,180,300,new(400,500),TestContext.Current.CancellationToken);
        if (mode < 2) caves.Sideways(500,230,mode == 0 ? -1 : 1,30);
        else caves.Run(500,160,mode == 2 ? 170 : 5,mode == 2);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var column = new WorldTile[600];
        for (int x = 0; x < 1000; x++)
        {
            for (int y = 0; y < 600; y++) column[y] = store.Get(x,y);
            digest.AppendData(MemoryMarshal.AsBytes(column.AsSpan()));
        }
        Assert.Equal(hash,Convert.ToHexString(digest.GetHashAndReset()));
        Assert.Equal(next,random.Next());
    }

    [Fact]
    public void Cancellation_precedes_random_or_tile_mutation()
    {
        var store = new WorldTileStore(new WorldDimensions(100,100));
        var random = new RandomAdapter(42);
        var caves = new CorruptionCaves1458(store,random,30,50,new(60,80),new CancellationToken(true));
        Assert.Throws<OperationCanceledException>(() => caves.Run(50,30,20,true));
        Assert.Throws<OperationCanceledException>(() => caves.Sideways(50,30,-1,20));
        Assert.Equal(new RandomAdapter(42).Next(),random.Next());
        Assert.All(store.Tiles.ToArray(), tile => Assert.Equal(default,tile));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min,max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
