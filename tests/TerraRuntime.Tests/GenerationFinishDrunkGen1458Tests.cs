using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationFinishDrunkGen1458Tests
{
    [Theory]
    [InlineData(4, 19, 5001, 964038662,
        "9a3321b4b01faa1a584942197b3867efd75de004f0e0c8c817d873ee4f4238d4")]
    [InlineData(1458, 20, 0, 1928520153,
        "f3fbd7fd8d6ad875e12da67f82891f7af17c93c428c85e741a3e2dbcd6e60955")]
    public void Direct_official_finish_drunk_gen_matches_tiles_chest_and_next_shared_rng(
        int seed, byte dungeonPaint, int bonusItem, int nextRandom, string worldHash)
    {
        // Direct TerrariaServer 1.4.5.8 WorldGen.FinishDrunkGen reflection fixture:
        // 600x500 active stone with the probe's 60x180 empty chamber,
        // the listed brick/hive/wall cells, chest slot 0
        // containing items 1 and 2, and a fresh genRand seed. The ignored
        // .cache/spider-probe command uses exactly this source setup.
        var world = new Workspace(600, 500);
        world.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.DrunkWorld,
            VanillaSecretWorldSeed1458.None));
        var stone = new WorldTile { Flags = WorldTileFlags.Active, Type = 1 };
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++) world.TileStore.Set(x, y, in stone);
        for (int x = 120; x < 180; x++)
            for (int y = 120; y < 300; y++) world.TileStore.Set(x, y, default);
        Put(12, 12, 44);
        Put(13, 12, 43);
        Put(14, 12, 41);
        Put(15, 15, 481);
        Put(16, 16, 60);
        Put(15, 16, 147);
        Put(17, 16, 161);
        Put(16, 17, 226);
        Put(18, 18, 137, frameY: 18);
        Put(19, 19, 226);
        Wall(20, 20, 7);
        Wall(21, 21, 87);
        PlaceChestFootprint(world, 30, 30);
        WorldGenerationChestItem[] loot =
        [
            new(1, new ItemTypeId(1)),
            new(1, new ItemTypeId(2))
        ];
        Assert.True(world.TryAddChest(30, 30, string.Empty, loot));
        var random = new RandomAdapter(seed);

        FinalPass1458.FinishDrunkGen(world, random, TestContext.Current.CancellationToken);

        var sourceProjection = new StringBuilder(600 * 500 * 20);
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++)
            {
                WorldTile tile = world.TileStore.Get(x, y);
                sourceProjection.Append(tile.IsActive ? '1' : '0').Append(',')
                    .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.LiquidAmount).Append(',').Append(tile.TileColor).Append(',')
                    .Append(tile.WallColor).Append(';');
            }
        Assert.Equal(worldHash, Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(sourceProjection.ToString()))));

        foreach ((int x, int y) in new[] { (12, 12), (13, 12), (14, 12), (15, 15) })
            Assert.Equal(dungeonPaint, world.TileStore.Get(x, y).TileColor);
        Assert.Equal((ushort)59, world.TileStore.Get(15, 16).Type);
        Assert.Equal((ushort)59, world.TileStore.Get(17, 16).Type);
        foreach ((int x, int y) in new[] { (16, 16), (16, 17), (18, 18), (19, 19) })
            Assert.Equal((byte)16, world.TileStore.Get(x, y).TileColor);
        Assert.Equal((byte)25, world.TileStore.Get(20, 20).WallColor);
        Assert.Equal((byte)16, world.TileStore.Get(21, 21).WallColor);
        WorldChest chest = Assert.Single(world.CaptureGeneratedChests());
        Assert.Equal(bonusItem, chest.Items[2].ItemType);
        Assert.Equal(nextRandom, random.Next());

        void Put(int x, int y, ushort type, short frameY = 0)
        {
            WorldTile tile = world.TileStore.Get(x, y);
            tile.Type = type;
            tile.FrameY = frameY;
            world.TileStore.Set(x, y, in tile);
        }

        void Wall(int x, int y, ushort wall)
        {
            WorldTile tile = world.TileStore.Get(x, y);
            tile.Wall = wall;
            world.TileStore.Set(x, y, in tile);
        }
    }

    [Fact]
    public void Drunk_tail_uses_last_dungeon_brick_color_then_paints_cells_and_adds_both_chest_bonuses()
    {
        var world = Create(VanillaSpecialWorldSeed1458.DrunkWorld);
        Put(12, 12, 44);
        Put(13, 12, 43);
        Put(14, 12, 41);
        Put(15, 15, 481); // TileID.Sets.CrackedBricks, not Main.tileDungeon.
        Put(16, 16, 60);
        Put(15, 16, 147);
        Put(17, 16, 161);
        Put(16, 17, 226);
        Put(18, 18, 137, frameY: 18);
        Put(19, 19, 226);
        var dungeonWall = new WorldTile { Wall = 7 };
        world.TileStore.Set(20, 20, in dungeonWall);
        var hiveWall = new WorldTile { Wall = 87 };
        world.TileStore.Set(21, 21, in hiveWall);
        PlaceChestFootprint(world, 30, 30);
        WorldGenerationChestItem[] loot =
        [
            new(1, new ItemTypeId(1)),
            new(1, new ItemTypeId(2))
        ];
        Assert.True(world.TryAddChest(30, 30, string.Empty, loot));

        // WorldGen.FinishDrunkGen consumes this exact source-order sequence:
        // initial color; Pink/Green/Blue dungeon-brick choices; two chest chances.
        var random = new ScriptedRandom(
            new(13, 25, 17), new(13, 15, 13), new(0, 2, 0),
            new(23, 25, 24), new(15, 19, 18), new(19, 23, 21),
            new(0, 15, 0), new(0, 30, 0));
        FinalPass1458.FinishDrunkGen(world, random, TestContext.Current.CancellationToken);
        random.AssertExhausted();

        Assert.Equal((byte)21, world.TileStore.Get(12, 12).TileColor);
        Assert.Equal((byte)21, world.TileStore.Get(13, 12).TileColor);
        Assert.Equal((byte)21, world.TileStore.Get(14, 12).TileColor);
        Assert.Equal((byte)21, world.TileStore.Get(15, 15).TileColor);
        Assert.Equal((byte)25, world.TileStore.Get(20, 20).WallColor);
        Assert.Equal((byte)16, world.TileStore.Get(21, 21).WallColor);
        Assert.Equal((ushort)59, world.TileStore.Get(15, 16).Type);
        Assert.Equal((ushort)59, world.TileStore.Get(17, 16).Type);
        Assert.Equal((byte)16, world.TileStore.Get(16, 16).TileColor);
        Assert.Equal((byte)16, world.TileStore.Get(16, 17).TileColor);
        Assert.Equal((byte)16, world.TileStore.Get(18, 18).TileColor);
        Assert.Equal((byte)16, world.TileStore.Get(19, 19).TileColor);

        WorldChest chest = Assert.Single(world.CaptureGeneratedChests());
        Assert.Equal(1, chest.Items[0].ItemType);
        Assert.Equal(2, chest.Items[1].ItemType);
        Assert.Equal(5001, chest.Items[2].ItemType);
        Assert.Equal(678, chest.Items[3].ItemType);

        void Put(int x, int y, ushort type, short frameY = 0)
        {
            var tile = new WorldTile { Flags = WorldTileFlags.Active, Type = type, FrameY = frameY };
            world.TileStore.Set(x, y, in tile);
        }
    }

    [Fact]
    public void Not_the_bees_combination_preserves_hive_tiles_but_paints_wall_87()
    {
        var world = Create(VanillaSpecialWorldSeed1458.DrunkWorld | VanillaSpecialWorldSeed1458.NotTheBees);
        var hive = new WorldTile { Flags = WorldTileFlags.Active, Type = 226, Wall = 87 };
        world.TileStore.Set(20, 20, in hive);
        var random = new ScriptedRandom(new Draw(13, 25, 17));

        FinalPass1458.FinishDrunkGen(world, random, TestContext.Current.CancellationToken);
        random.AssertExhausted();

        Assert.Equal((byte)0, world.TileStore.Get(20, 20).TileColor);
        Assert.Equal((byte)16, world.TileStore.Get(20, 20).WallColor);
    }

    private static Workspace Create(VanillaSpecialWorldSeed1458 special)
    {
        var world = new Workspace(60, 60);
        world.SetVanillaSeedProfile(new(special, VanillaSecretWorldSeed1458.None));
        return world;
    }

    private static void PlaceChestFootprint(Workspace world, int left, int top)
    {
        for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                var tile = new WorldTile
                {
                    Flags = WorldTileFlags.Active,
                    Type = 21,
                    FrameX = (short)(dx * 18),
                    FrameY = (short)(dy * 18)
                };
                world.TileStore.Set(left + dx, top + dy, in tile);
            }
    }

    private readonly record struct Draw(int Minimum, int Maximum, int Value);

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int maximum) => random.Next(maximum);
        public int Next(int minimum, int maximum) => random.Next(minimum, maximum);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }

    private sealed class ScriptedRandom(params Draw[] draws) : IWorldGenerationVanillaRandom
    {
        private int index;

        public void AssertExhausted() => Assert.Equal(draws.Length, index);
        public int Next() => Take(0, int.MaxValue);
        public int Next(int max) => Take(0, max);
        public int Next(int min, int max) => Take(min, max);
        public double NextDouble() => throw new InvalidOperationException("Unexpected floating-point RNG draw.");
        public void NextBytes(byte[] bytes) => throw new InvalidOperationException("Unexpected byte RNG draw.");

        private int Take(int min, int max)
        {
            Assert.True(index < draws.Length, "Unexpected extra RNG draw.");
            Draw expected = draws[index++];
            Assert.Equal((min, max), (expected.Minimum, expected.Maximum));
            return expected.Value;
        }
    }
}
