using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationAnniversaryChestPrefix1458Tests
{
    [Theory]
    [InlineData(1, 65, 65, 57, 76, 72, 57, 760389092)]
    [InlineData(2, 76, 68, 82, 76, 65, 82, 1722583523)]
    [InlineData(1458, 68, 72, 60, 76, 68, 60, 370853568)]
    public void Direct_official_ranged_and_accessory_prefixes_share_sparse_chest_order(
        int seed, byte a0, byte a1, byte r0, byte a2, byte a3, byte r1, int nextRandom)
    {
        // Direct original ImproveAllChestContents: source item 96 Musket and
        // 219 Phoenix Blaster accept all three GoodPrefixIdsForRangedWeapon;
        // weak Wooden Bow 39 does not, and is outside this bounded branch.
        Workspace world = Create();
        PlaceChest(world, 30, 30);
        PlaceChest(world, 40, 40);
        PlaceChest(world, 50, 50);
        Assert.True(world.TryAddChest(30, 30, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(53)),
             new(1, new ItemTypeId(2)), new(1, new ItemTypeId(96))]));
        Assert.True(world.TryAddChest(40, 40, string.Empty, []));
        Assert.True(world.TryAddChest(50, 50, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(53)),
             new(1, new ItemTypeId(219))]));
        Assert.True(world.TryRemoveEmptyGeneratedChest(40, 40));
        var random = new RandomAdapter(seed);

        Assert.Equal(6, AnniversaryChestPrefix1458.ImproveAccessoryAndRangedChests(world,
            random, TestContext.Current.CancellationToken));

        WorldChest[] chests = world.CaptureGeneratedChests();
        Assert.Equal(new short[] { 0, 2 }, chests.Select(chest => chest.SlotId));
        Assert.Equal(new byte[] { a0, a1, 0, r0 }, chests[0].Items.Take(4).Select(item => item.Prefix));
        Assert.Equal(new byte[] { a2, a3, r1 }, chests[1].Items.Take(3).Select(item => item.Prefix));
        Assert.Equal(nextRandom, random.Next());
    }

    [Theory]
    [InlineData(1, 65, 65, 68, 76, 1412011072)]
    [InlineData(2, 76, 68, 65, 76, 234085668)]
    [InlineData(1458, 68, 72, 76, 76, 1014131397)]
    public void Direct_official_accessory_improvement_preserves_sparse_chest_slots_and_rng(
        int seed, byte first, byte second, byte third, byte fourth, int nextRandom)
    {
        // TerrariaServer 1.4.5.8 direct ImproveAllChestContents: slot0 items
        // 49/53/2, empty slot1, slot2 items49/53, genRand=seed. Prefix(-3)
        // accepts the accessories without a draw, then four positive options
        // each consume exactly one selection draw. Null slot1 is skipped.
        Workspace world = Create();
        PlaceChest(world, 30, 30);
        PlaceChest(world, 40, 40);
        PlaceChest(world, 50, 50);
        Assert.True(world.TryAddChest(30, 30, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(53)),
             new(1, new ItemTypeId(2))]));
        Assert.True(world.TryAddChest(40, 40, string.Empty, []));
        Assert.True(world.TryAddChest(50, 50, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(53))]));
        Assert.True(world.TryRemoveEmptyGeneratedChest(40, 40));
        Assert.Equal(new short[] { 0, 2 }, world.CaptureGeneratedChests().Select(chest => chest.SlotId));
        var random = new RandomAdapter(seed);

        Assert.Equal(4, AnniversaryChestPrefix1458.ImproveAccessoryChests(world, random,
            TestContext.Current.CancellationToken));

        WorldChest[] chests = world.CaptureGeneratedChests();
        Assert.Equal(new short[] { 0, 2 }, chests.Select(chest => chest.SlotId));
        Assert.Equal(first, chests[0].Items[0].Prefix);
        Assert.Equal(second, chests[0].Items[1].Prefix);
        Assert.Equal((byte)0, chests[0].Items[2].Prefix);
        Assert.Equal(third, chests[1].Items[0].Prefix);
        Assert.Equal(fourth, chests[1].Items[1].Prefix);
        Assert.Equal(nextRandom, random.Next());
    }

    [Theory]
    [InlineData(4)] // melee family has a different good-prefix list.
    [InlineData(39)] // weak bow enters Item.Prefix positive-request fallback.
    public void Unsupported_weapon_chest_refuses_before_prefix_or_rng_mutation(int unsupported)
    {
        Workspace world = Create();
        PlaceChest(world, 30, 30);
        Assert.True(world.TryAddChest(30, 30, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(unsupported))]));
        var random = new RandomAdapter(1458);
        int expectedNext = new RandomAdapter(1458).Next();
        WorldChest before = Assert.Single(world.CaptureGeneratedChests());

        Assert.Throws<NotSupportedException>(() =>
            AnniversaryChestPrefix1458.ImproveAccessoryChests(world, random,
                TestContext.Current.CancellationToken));
        Assert.Equal(before.Items, Assert.Single(world.CaptureGeneratedChests()).Items);
        Assert.Equal(expectedNext, random.Next());
    }

    private static Workspace Create()
    {
        var world = new Workspace(120, 120);
        world.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.CelebrationMk10,
            VanillaSecretWorldSeed1458.None));
        return world;
    }

    private static void PlaceChest(Workspace world, int left, int top)
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
