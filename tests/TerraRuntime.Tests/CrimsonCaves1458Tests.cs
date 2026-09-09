using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class CrimsonCaves1458Tests
{
    // Official 1.4.5.8 CrimStart(500,140) + CrimPlaceHearts, not candidate-derived goldens.
    // All 16-byte normalized cells (x-major), next RNG and ordered heart endpoint coordinates.
    [Theory]
    [InlineData(0,42,"D59345615879FAB5FACDEF7762D5F062E4E10982CA79D1439F16E7E6B92FE535",9215990,"603,305;408,290;432,342;566,325;405,319;636,301;614,313;414,288")]
    [InlineData(0,1458,"29F0A11118EA6A21D1EDEFB92074D522E7A1E5300A16EB5146A6AF6B589E628D",588386473,"329,284;567,280;487,379;509,367;384,340;405,377;388,317;520,353")]
    [InlineData(0,8675309,"590AA7060244CEA77D7FCB0ED863B13FCE32860C8372F5749D5342C11E03C187",2059349492,"416,291;570,336;450,350;457,336;427,343;559,377;373,290;468,358")]
    [InlineData(1,42,"D1C32CABFE22B880BE6A8A022BD5A5E255E2522EF777A58406216FEE60A926DF",1543982930,"583,319;463,371;646,278;591,345;433,353;573,360;548,354;642,283")]
    [InlineData(1,1458,"C67C40F118ACEED476DB68C4354367447A00266FB0EBF3883A583820F7875877",230184297,"495,355;534,315;427,378;545,291;561,302")]
    [InlineData(1,8675309,"BA423C674203C9C11A57E39A1EAD629A91DA3897203A2E0832FC35047AB7F246",33307719,"586,338;565,376;551,341;488,398;451,346")]
    [InlineData(2,42,"56FC3DA3D71674E60D642B4DACB1F48D4BBF8D8AC83704079EA49CBCC0ADC0A9",1129621446,"443,305;248,290;272,342;406,325;245,319;476,301;454,313;254,288;755,333;765,303;774,303;719,357;655,375;715,357")]
    [InlineData(2,1458,"B91E412DDF86C1AEE83E452CE15A4A01283DCFEA79731717DFDCBDB623A59C7E",618160425,"169,284;407,280;327,379;349,367;224,340;245,377;228,317;360,353;716,380;622,338;736,363;610,338;604,317")]
    [InlineData(2,8675309,"4CBDC8A3C9CB8B4C0DB16CA2CD124B89D662990E1DB5EE657929BB4439006EE2",345267452,"256,291;410,336;290,350;297,336;267,343;399,377;213,290;308,358;594,322;624,344;630,341;649,377;658,346;753,325;614,308")]
    public void Geometry_cells_frames_random_and_hearts_match_official(int fixture, int seed, string hash, int next, string hearts)
    {
        var store = Fixture(fixture);
        var random = new RandomAdapter(seed);
        var caves = new CrimsonCaves1458(store, random, 180, TestContext.Current.CancellationToken);
        if (fixture == 2) { caves.Start(340,140); caves.Start(660,140); }
        else caves.Start(500,140);
        caves.PlaceHearts();
        var cells = new WorldTile[1000 * 600];
        for (int x = 0; x < 1000; x++)
        for (int y = 0; y < 600; y++) cells[x * 600 + y] = store.Get(x,y);
        Assert.Equal(hash, Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(cells.AsSpan()))));
        Assert.Equal(next, random.Next());
        Assert.Equal(hearts, string.Join(";", caves.HeartPositions.Select(p => $"{p.X},{p.Y}")));
    }

    [Fact]
    public void Cancellation_does_not_mutate_or_consume_random()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var store = new WorldTileStore(new WorldDimensions(100,100));
        var random = new RandomAdapter(42); var expected = new RandomAdapter(42);
        var caves = new CrimsonCaves1458(store,random,50,cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => caves.Start(50,10));
        Assert.Empty(caves.HeartPositions); Assert.Equal(expected.Next(),random.Next());
        foreach (WorldTile tile in store.Tiles) Assert.Equal(default, tile);
    }

    [Fact]
    public void Missing_ground_aborts_without_inventing_a_cave_anchor()
    {
        var random = new RandomAdapter(42); var expected = new RandomAdapter(42);
        var caves = new CrimsonCaves1458(new WorldTileStore(new WorldDimensions(100,100)),
            random,50,TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() => caves.Start(50,10));
        Assert.Empty(caves.HeartPositions); Assert.Equal(expected.Next(),random.Next());
    }

    private static WorldTileStore Fixture(int fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(1000,600));
        for (int x = 0; x < 1000; x++)
        for (int y = 0; y < 600; y++)
        {
            var tile = new WorldTile { Type = (ushort)(y < 200 ? 0 : 1), FrameX = 18, FrameY = 36,
                Flags = y >= 160 ? WorldTileFlags.Active : WorldTileFlags.None };
            if (fixture == 1 && x % 31 == 0 && y >= 200) tile.Wall = 7;
            if (fixture == 1 && x % 37 == 0 && y >= 200) tile.Type = 481;
            store.Set(x,y,tile);
        }
        return store;
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
