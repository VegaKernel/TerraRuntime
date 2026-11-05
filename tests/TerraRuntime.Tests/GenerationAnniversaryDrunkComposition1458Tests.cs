using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationAnniversaryDrunkComposition1458Tests
{
    [Theory]
    [InlineData(1, "9efe24a498788620e419d534c176be8c593f56e832b14865da9aff5c3da73a24",
        76, 65, 57, 57, 68, 82, 83, 0, 665, 1249963013)]
    [InlineData(1458, "487277cfcdda175ed557c4215d73849eb2ff30c50ce8c4ce4de7104dd21760a7",
        68, 65, 82, 83, 76, 82, 83, 5001, 138, 63073556)]
    public void Direct_official_anniversary_then_drunk_helpers_match_grid_chests_and_rng(
        int seed, string worldHash, byte a0, byte a1, byte r0, byte m0,
        byte a2, byte r1, byte m1, int bonus, ushort firstFire, int nextRandom)
    {
        // Actual TerrariaServer 1.4.5.8 direct helper composition on the
        // 600x500 source fixture: Anniversary+Drunk skips sky conversion,
        // paints only dungeon/temple/sand, scans fireplaces, prefixes all
        // admitted chest items, then Drunk visits slot0 and stops at hole1.
        Workspace world = Create();
        AddChests(world, unsupported: false);
        var random = new RandomAdapter(seed);
        Assert.Equal("f7a3c9a7f528175cb18fa7f8c2d48d88fc33699b7e060321c3f0f1adc5fe733e",
            HashPaintedGrid(world));

        FinalPass1458.FinishTenthAnniversaryWorld(world, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishDrunkGen(world, random,
            TestContext.Current.CancellationToken);

        Assert.Equal(worldHash, HashPaintedGrid(world));
        Assert.Equal(firstFire, world.TileStore.Get(230, 130).Type);
        WorldChest[] chests = world.CaptureGeneratedChests();
        Assert.Equal(new short[] { 0, 2 }, chests.Select(chest => chest.SlotId));
        Assert.Equal(new byte[] { a0, a1, r0, m0 },
            chests[0].Items.Take(4).Select(item => item.Prefix));
        Assert.Equal(new byte[] { a2, r1, m1 },
            chests[1].Items.Take(3).Select(item => item.Prefix));
        Assert.Equal(bonus, chests[0].Items[4].ItemType);
        Assert.Equal(0, chests[1].Items[3].ItemType);
        Assert.Equal(nextRandom, random.Next());
    }

    [Fact]
    public void Unsupported_chest_refuses_composed_tail_before_paint_or_rng()
    {
        Workspace world = Create();
        AddChests(world, unsupported: true);
        string before = HashPaintedGrid(world);
        var random = new RandomAdapter(1458);
        int expectedNext = new RandomAdapter(1458).Next();
        WorldChest[] beforeChests = world.CaptureGeneratedChests();

        Assert.Throws<NotSupportedException>(() =>
            FinalPass1458.FinishTenthAnniversaryWorld(world, random,
                TestContext.Current.CancellationToken));
        Assert.Equal(before, HashPaintedGrid(world));
        Assert.Equal(beforeChests[0].Items, world.CaptureGeneratedChests()[0].Items);
        Assert.Equal(expectedNext, random.Next());
    }

    private static Workspace Create()
    {
        var world = new Workspace(600, 500);
        Assert.True(world.TrySetLayers(200, 300));
        world.SetVanillaSeedProfile(new(
            VanillaSpecialWorldSeed1458.CelebrationMk10 | VanillaSpecialWorldSeed1458.DrunkWorld,
            VanillaSecretWorldSeed1458.None));
        var stone = new WorldTile { Flags = WorldTileFlags.Active, Type = 1 };
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++) world.TileStore.Set(x, y, in stone);

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
        for (int x = 230; x < 236; x += 2)
            for (int dx = 0; dx < 2; dx++)
                for (int dy = 0; dy < 2; dy++)
                {
                    var fire = new WorldTile
                    {
                        Flags = WorldTileFlags.Active, Type = 138,
                        FrameX = (short)(dx * 18), FrameY = (short)(dy * 18)
                    };
                    world.TileStore.Set(x + dx, 130 + dy, in fire);
                }
        foreach ((int left, int top) in new[] { (30, 30), (40, 40), (50, 50) })
            for (int dx = 0; dx < 2; dx++)
                for (int dy = 0; dy < 2; dy++)
                {
                    var chest = new WorldTile
                    {
                        Flags = WorldTileFlags.Active, Type = 21,
                        FrameX = (short)(dx * 18), FrameY = (short)(dy * 18)
                    };
                    world.TileStore.Set(left + dx, top + dy, in chest);
                }
        for (int x = 120; x < 180; x++)
            for (int y = 120; y < 300; y++) world.TileStore.Set(x, y, default);
        return world;
    }

    private static void AddChests(Workspace world, bool unsupported)
    {
        Assert.True(world.TryAddChest(30, 30, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(53)),
             new(1, new ItemTypeId(unsupported ? 39 : 96)), new(1, new ItemTypeId(113))]));
        Assert.True(world.TryAddChest(40, 40, string.Empty, []));
        Assert.True(world.TryAddChest(50, 50, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(219)),
             new(1, new ItemTypeId(165))]));
        Assert.True(world.TryRemoveEmptyGeneratedChest(40, 40));
    }

    private static string HashPaintedGrid(Workspace world)
    {
        var text = new StringBuilder(600 * 500 * 20);
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++)
            {
                WorldTile tile = world.TileStore.Get(x, y);
                text.Append(tile.IsActive ? '1' : '0').Append(',')
                    .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.LiquidAmount).Append(',').Append(tile.TileColor).Append(',')
                    .Append(tile.WallColor).Append(';');
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
