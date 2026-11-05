using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationAnniversarySkyConversion1458Tests
{
    [Theory]
    [InlineData(42, 1634773126)]
    [InlineData(1458, 1928520153)]
    public void Direct_official_sky_branch_mix_matches_growth_willow_and_cloud_wall(
        int seed, int nextRandom)
    {
        Workspace world = Create(realShapes: true);
        var random = new RandomAdapter(seed);
        Assert.Equal("c54fb49e48a67893b781a29856bfd95b8ad8575d6b41559bd0df9825c26232cc",
            HashSixFields(world));

        Assert.Equal(7, AnniversarySkyIslandConversion1458.ConvertClearGrassOffers(world, random,
            TestContext.Current.CancellationToken));

        Assert.Equal("136c548695ee85744ea3a4d1e7dfcc09dc0bde34f0da5707219858e1009e8781",
            HashSixFields(world));
        Assert.Equal((ushort)110, world.TileStore.Get(290, 109).Type);
        Assert.Equal((ushort)109, world.TileStore.Get(291, 109).Type);
        Assert.Equal((ushort)113, world.TileStore.Get(292, 109).Type);
        Assert.Equal((ushort)0, world.TileStore.Get(293, 109).Type);
        Assert.Equal((ushort)73, world.TileStore.Get(294, 110).Wall);
        Assert.Equal(nextRandom, random.Next());
    }

    [Theory]
    [InlineData(1458, 1335025742)]
    [InlineData(1, 237820880)]
    public void Direct_official_sky_grass_with_vanity_tree_matches_pre_conversion_kill(
        int seed, int nextRandom)
    {
        Workspace world = Create(withTree: true);
        var random = new RandomAdapter(seed);
        Assert.Equal("b1fd8a7683e09b68c10a0def463ed1b27b569d04c777abf1bfd0f27d0a3814e8",
            HashSixFields(world));

        Assert.Equal(1, AnniversarySkyIslandConversion1458.ConvertClearGrassOffers(world, random,
            TestContext.Current.CancellationToken));

        Assert.Equal("a4e136e17eab169c508ab63c61e92a40d6e08af913e2801aa88e3313f3047f15",
            HashSixFields(world));
        WorldTile tree = world.TileStore.Get(300, 109);
        Assert.False(tree.IsActive);
        Assert.Equal((ushort)0, tree.Type);
        Assert.Equal((short)-1, tree.FrameX);
        Assert.Equal((short)-1, tree.FrameY);
        Assert.Equal(nextRandom, random.Next());
    }

    [Theory]
    [InlineData(1458, 1335025742)]
    [InlineData(1, 237820880)]
    public void Direct_official_sky_grass_with_herb_matches_kill_frame_grid_and_rng(
        int seed, int nextRandom)
    {
        Workspace world = Create(withHerb: true);
        var random = new RandomAdapter(seed);
        Assert.Equal("b558d8e32a66482ff1441162dc413b42b8f46488550cfbaaad4d357afc26dc19",
            HashSixFields(world));

        Assert.Equal(1, AnniversarySkyIslandConversion1458.ConvertClearGrassOffers(world, random,
            TestContext.Current.CancellationToken));

        Assert.Equal("a4e136e17eab169c508ab63c61e92a40d6e08af913e2801aa88e3313f3047f15",
            HashSixFields(world));
        WorldTile herb = world.TileStore.Get(300, 109);
        Assert.False(herb.IsActive);
        Assert.Equal((ushort)0, herb.Type);
        Assert.Equal((short)-1, herb.FrameX);
        Assert.Equal((short)-1, herb.FrameY);
        Assert.Equal(nextRandom, random.Next());
    }

    [Theory]
    [InlineData(1458, 1335025742)] // grow-tree roll 1: skipped
    [InlineData(1, 237820880)] // grow-tree roll 0: source attempts and rejects
    public void Direct_official_convert_sky_islands_matches_clear_grass_grid_and_rng(int seed, int nextRandom)
    {
        // Direct TerrariaServer 1.4.5.8 ConvertSkyIslands(2, true), isolated
        // with netMode=0 only to suppress transport. The 600x500 official probe
        // starts from active stone, clears its standard cave chamber and this
        // sky rectangle, then places one grass offer under a cloud ceiling.
        Workspace world = Create();
        var random = new RandomAdapter(seed);
        const string original = "fe6eb3b0885b230a4e8215889bff09dfa873f8b4f98009b4c0ab69df570b1b64";
        Assert.Equal("147343b486ef417d1b5c71dfab843d142d5b2d5aa327dfb7a491e197183d1c6c",
            HashSixFields(world));

        int converted = AnniversarySkyIslandConversion1458.ConvertClearGrassOffers(
            world, random, TestContext.Current.CancellationToken);

        Assert.Equal(1, converted);
        Assert.Equal(original, HashSixFields(world));
        Assert.Equal((ushort)109, world.TileStore.Get(300, 110).Type);
        Assert.Equal(nextRandom, random.Next());
    }

    [Fact]
    public void Unsupported_overhead_object_is_refused_before_any_tile_or_rng_change()
    {
        Workspace world = Create();
        var plant = new WorldTile { Flags = WorldTileFlags.Active, Type = 21 };
        world.TileStore.Set(300, 109, in plant);
        var random = new RandomAdapter(1458);
        string before = HashSixFields(world);
        int expectedNext = new RandomAdapter(1458).Next();

        Assert.Throws<NotSupportedException>(() =>
            AnniversarySkyIslandConversion1458.ConvertClearGrassOffers(
                world, random, TestContext.Current.CancellationToken));
        Assert.Equal(before, HashSixFields(world));
        Assert.Equal(expectedNext, random.Next());
    }

    private static Workspace Create(bool withHerb = false, bool withTree = false,
        bool realShapes = false)
    {
        var world = new Workspace(600, 500);
        Assert.True(world.TrySetLayers(150, 250));
        var stone = new WorldTile { Flags = WorldTileFlags.Active, Type = 1 };
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++) world.TileStore.Set(x, y, in stone);
        for (int x = 120; x < 180; x++)
            for (int y = 120; y < 300; y++) world.TileStore.Set(x, y, default);
        for (int x = 280; x < 320; x++)
            for (int y = 100; y < 130; y++) world.TileStore.Set(x, y, default);
        var grass = new WorldTile { Flags = WorldTileFlags.Active, Type = 2 };
        var cloud = new WorldTile { Flags = WorldTileFlags.Active, Type = 189 };
        world.TileStore.Set(300, 110, in grass);
        world.TileStore.Set(300, 111, in stone);
        world.TileStore.Set(300, 120, in cloud);
        if (withHerb)
        {
            var herb = new WorldTile { Flags = WorldTileFlags.Active, Type = 82 };
            world.TileStore.Set(300, 109, in herb);
        }
        if (withTree)
        {
            var tree = new WorldTile { Flags = WorldTileFlags.Active, Type = 596 };
            world.TileStore.Set(300, 109, in tree);
        }
        if (realShapes)
            foreach ((int x, ushort above, ushort wall) in new[]
            {
                (290, (ushort)3, (ushort)0), (291, (ushort)2, (ushort)0),
                (292, (ushort)73, (ushort)0), (293, (ushort)616, (ushort)0),
                (294, (ushort)0, (ushort)73)
            })
            {
                var candidate = new WorldTile { Flags = WorldTileFlags.Active, Type = 2, Wall = wall };
                world.TileStore.Set(x, 110, in candidate);
                world.TileStore.Set(x, 111, in stone);
                if (above == 0) continue;
                var overhead = new WorldTile { Flags = WorldTileFlags.Active, Type = above };
                world.TileStore.Set(x, 109, in overhead);
            }
        return world;
    }

    private static string HashSixFields(Workspace world)
    {
        var text = new StringBuilder(600 * 500 * 15);
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++)
            {
                WorldTile tile = world.TileStore.Get(x, y);
                text.Append(tile.IsActive ? '1' : '0').Append(',')
                    .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.LiquidAmount).Append(';');
            }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int maximum) => random.Next(maximum);
        public int Next(int minimum, int maximum) => random.Next(minimum, maximum);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
