using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class EvilAltarPlacement1458Tests
{
    [Fact]
    public void Placement_matches_all_official_material_shape_actuator_and_style_results()
    {
        var results = new byte[18_096]; int index = 0;
        var store = new WorldTileStore(new WorldDimensions(40,40));
        foreach (bool crimson in new[] { false, true })
        for (ushort material = 0; material < 754; material++)
        for (byte shape = 0; shape < 6; shape++)
        foreach (bool actuated in new[] { false, true })
        {
            var original = new WorldTile { Type = 1, Wall = 83, LiquidAmount = 123, LiquidKind = WorldLiquidKind.Lava,
                TileColor = 3, WallColor = 4, Shape = shape,
                Flags = WorldTileFlags.WireRed | (actuated ? WorldTileFlags.Inactive : WorldTileFlags.None) };
            for (int x = 19; x <= 21; x++)
            for (int y = 19; y <= 21; y++)
            {
                WorldTile cell = original;
                if (y == 21) { cell.Type = material; cell.Flags |= WorldTileFlags.Active; }
                store.Set(x,y,cell);
            }
            bool success = EvilAltarPlacement1458.TryPlace(store,20,20,crimson);
            results[index++] = success ? (byte)1 : (byte)0;
            for (int dx = 0; dx < 3; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                WorldTile expected = original;
                if (success)
                {
                    expected.Type = 26; expected.Flags |= WorldTileFlags.Active;
                    expected.FrameX = (short)((crimson ? 54 : 0) + dx * 18); expected.FrameY = (short)(dy * 18);
                }
                Assert.Equal(expected, store.Get(19+dx,19+dy));
            }
        }
        // Direct official Place3x2 calls, ordered style/material/shape/actuation; not a candidate-derived digest.
        Assert.Equal("A05D92B16E9F1ADA2291D27DC59DC74F6561BD7F414626601C26F2E479CDC415",
            Convert.ToHexString(SHA256.HashData(results)));
    }

    [Theory]
    [InlineData(0,42,"92F89CE916E43318D0A0CAFAC548EC4EF4D4A433C825A7CC300605453640C228",753750391)]
    [InlineData(0,1458,"FC8DCB6EDF7B452C63497599A3BA6E9451E5806A2E5CD2C3B39FEB3134FAA966",1203568581)]
    [InlineData(0,8675309,"17743706C138D33069EA71C066FB4628710D6E35B34044CC708697E054F29E44",1350934019)]
    [InlineData(1,42,"452456C64B756658BF462A9300FB803274713CCCA9FF83552B01A126A7B73D52",1465370753)]
    [InlineData(1,1458,"452456C64B756658BF462A9300FB803274713CCCA9FF83552B01A126A7B73D52",1833775040)]
    [InlineData(1,8675309,"452456C64B756658BF462A9300FB803274713CCCA9FF83552B01A126A7B73D52",1894573435)]
    [InlineData(2,42,"4838C4EF5BDC40B9C67A90B482461F0AC0184B5D6BEDFB6F0EEDCC1D0482B84B",753750391)]
    [InlineData(2,1458,"095161788D4AB055EEB518FB66353A6F98717559E578C40CE84651839B633488",1203568581)]
    [InlineData(2,8675309,"3FE14712BFCA893FE63EF0B74A6D5DF28A98E1452B39B0FE27947F7F96DC0935",1350934019)]
    public void Global_search_matches_official_complete_cells_and_next_random(int fixture, int seed, string hash, int next)
    {
        var store = new WorldTileStore(new WorldDimensions(2000,900));
        for (int x = 0; x < 2000; x++)
        for (int y = 0; y < 900; y++)
            store.Set(x,y,new WorldTile { Type = 1, Wall = 83, FrameX = 18, FrameY = 36,
                Flags = fixture == 1 || y % 20 == 0 ? WorldTileFlags.Active : WorldTileFlags.None });
        var random = new RandomAdapter(seed);
        int placed = new EvilAltarPlacement1458(store,random,180.5,350.25,TestContext.Current.CancellationToken)
            .Generate(new(600,280),fixture == 2);
        Assert.Equal(fixture == 1 ? 0 : 5, placed);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var column = new WorldTile[900];
        for (int x = 0; x < 2000; x++)
        {
            for (int y = 0; y < 900; y++) column[y] = store.Get(x,y);
            digest.AppendData(MemoryMarshal.AsBytes(column.AsSpan()));
        }
        Assert.Equal(hash,Convert.ToHexString(digest.GetHashAndReset())); Assert.Equal(next,random.Next());
    }

    [Fact]
    public void Crimson_search_descends_to_crimstone_and_places_ten_separate_altars()
    {
        var store = new WorldTileStore(new WorldDimensions(1200,600));
        for (int x = 0; x < 1200; x++) store.Set(x,300,new WorldTile { Type = 203, Flags = WorldTileFlags.Active });
        var random = new SearchRandom();
        new EvilAltarPlacement1458(store,random,200,350,TestContext.Current.CancellationToken).GenerateCrimsonRegion(400,800);
        Assert.Equal(21,random.Calls);
        for (int i = 0; i < 10; i++)
        {
            WorldTile cell = store.Get(500+i*8,299);
            Assert.True(cell.IsActive); Assert.Equal(26,cell.Type); Assert.Equal(72,cell.FrameX); Assert.Equal(18,cell.FrameY);
        }
    }

    [Fact]
    public void Cancelled_search_consumes_no_random_or_terrain()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var store = new WorldTileStore(new WorldDimensions(1200,600));
        var random = new SearchRandom();
        var placement = new EvilAltarPlacement1458(store,random,200,350,cancellation.Token);
        Assert.Throws<OperationCanceledException>(() => placement.GenerateCrimsonRegion(400,800));
        Assert.Throws<OperationCanceledException>(() => placement.Generate(new(600,280),false));
        Assert.Equal(0,random.Calls);
        foreach (WorldTile tile in store.Tiles) Assert.Equal(default,tile);
    }

    [Theory]
    [InlineData(4,20,false)]
    [InlineData(5,20,true)]
    [InlineData(35,20,true)]
    [InlineData(36,20,false)]
    [InlineData(20,4,false)]
    [InlineData(20,5,true)]
    [InlineData(20,35,true)]
    [InlineData(20,36,false)]
    public void Source_border_is_inclusive_at_five(int x, int y, bool accepted)
    {
        var store = new WorldTileStore(new WorldDimensions(40,40));
        for (int dx = -1; dx <= 1; dx++) store.Set(x+dx,y+1,new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Assert.Equal(accepted,EvilAltarPlacement1458.TryPlace(store,x,y,false));
    }

    [Fact]
    public void Missing_crimson_floor_aborts_instead_of_fabricating_support()
    {
        var store = new WorldTileStore(new WorldDimensions(1200,600));
        Assert.Throws<InvalidOperationException>(() => new EvilAltarPlacement1458(store,new SearchRandom(),200,350,
            TestContext.Current.CancellationToken).GenerateCrimsonRegion(400,800));
        foreach (WorldTile tile in store.Tiles) Assert.Equal(default,tile);
    }

    private sealed class SearchRandom : IWorldGenerationVanillaRandom
    {
        public int Calls { get; private set; }
        public int Next(int min, int max)
        {
            int call = Calls++;
            int result = call == 0 ? 10 : call % 2 == 1 ? 500 + (call / 2) * 8 : 250;
            Assert.InRange(result,min,max-1); return result;
        }
        public int Next() => throw new NotSupportedException();
        public int Next(int max) => throw new NotSupportedException();
        public double NextDouble() => throw new NotSupportedException();
        public void NextBytes(byte[] bytes) => throw new NotSupportedException();
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
