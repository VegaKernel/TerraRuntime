using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationFinishAnniversary1458Tests
{
    [Fact]
    public void Worthy_anniversary_direct_official_tail_matches_all_painted_cells_and_next_rng()
    {
        // Direct TerrariaServer 1.4.5.8 WorldGen.FinishTenthAnniversaryWorld
        // from the ignored spider-probe: getGoodWorldGen=true, 600x500 stone,
        // the probe's empty chamber and these paint targets. The worthy branch
        // reaches all six paint helpers but skips sky, crystal and chest work.
        var world = new Workspace(600, 500);
        Assert.True(world.TrySetLayers(200, 300));
        world.SetVanillaSeedProfile(new(
            VanillaSpecialWorldSeed1458.CelebrationMk10 | VanillaSpecialWorldSeed1458.ForTheWorthy,
            VanillaSecretWorldSeed1458.None));
        var stone = new WorldTile { Flags = WorldTileFlags.Active, Type = 1 };
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++) world.TileStore.Set(x, y, in stone);
        for (int x = 120; x < 180; x++)
            for (int y = 120; y < 300; y++) world.TileStore.Set(x, y, default);

        Put(40, 40, 41, 7); Put(41, 40, 481); Put(42, 40, 19, frameY: 108);
        Put(50, 50, 192, 244); Put(51, 50, 191);
        Put(52, 50, 192); Put(52, 51, 52);
        Put(53, 50, 187); Put(53, 51, 192);
        Put(60, 60, 226, 87); Put(61, 60, 61); Put(61, 61, 226);
        Put(62, 60, 137, frameY: 18);
        Put(70, 70, 189, 73); Put(71, 70, 717);
        Put(80, 220, 53, 187); Put(80, 219, 185); Put(80, 221, 165);
        Put(81, 220, 396, 216); Put(81, 219, 186);
        Put(90, 90, 151, 34);
        var inactiveWall = new WorldTile { Type = 1, Wall = 34 };
        world.TileStore.Set(100, 100, in inactiveWall);

        var random = new RandomAdapter(1458);
        FinalPass1458.FinishTenthAnniversaryWorld(world, random,
            TestContext.Current.CancellationToken);

        var snapshot = new StringBuilder(600 * 500 * 20);
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++)
            {
                WorldTile tile = world.TileStore.Get(x, y);
                snapshot.Append(tile.IsActive ? '1' : '0').Append(',')
                    .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.LiquidAmount).Append(',').Append(tile.TileColor).Append(',')
                    .Append(tile.WallColor).Append(';');
            }
        Assert.Equal("22855cd691b9d43cd473bc97d0fcbdfdd495299e63e8b43d9b52f77a2a331ef8",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString()))));
        Assert.Equal(906992634, random.Next());

        void Put(int x, int y, ushort type, ushort wall = 0, short frameY = 0)
        {
            var tile = new WorldTile
            {
                Flags = WorldTileFlags.Active,
                Type = type,
                Wall = wall,
                FrameY = frameY
            };
            world.TileStore.Set(x, y, in tile);
        }
    }

    [Fact]
    public void Anniversary_without_admitted_sky_or_chest_owner_refuses_before_mutation()
    {
        var world = new Workspace(120, 120);
        Assert.True(world.TrySetLayers(40, 70));
        world.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.CelebrationMk10,
            VanillaSecretWorldSeed1458.None));
        var target = new WorldTile { Flags = WorldTileFlags.Active, Type = 41 };
        world.TileStore.Set(60, 60, in target);
        var random = new RandomAdapter(1458);
        int next = new RandomAdapter(1458).Next();

        Assert.Throws<NotSupportedException>(() =>
            FinalPass1458.FinishTenthAnniversaryWorld(world, random,
                TestContext.Current.CancellationToken));
        Assert.Equal(target, world.TileStore.Get(60, 60));
        Assert.Equal(next, random.Next());
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
