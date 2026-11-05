using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationAnniversaryChestPrefix1458Tests
{
    [Theory]
    [InlineData(1, "65,65,0,57,60,96,59,59,0", "76,65,57,83,59,81,57,0", 1464848243)]
    [InlineData(1458, "68,72,0,60,60,95,57,81,0", "76,68,60,57,96,57,81,0", 1162070998)]
    public void Direct_official_no_category_items_preserve_prefix_and_rng_between_melee_items(
        int seed, string firstChest, string secondChest, int nextRandom)
    {
        // Actual original ImproveAllChestContents: Harpoon160 and Whip4672
        // have a rollable melee family, but all five source category flags are
        // false. They remain at prefix0 and cost no draws in mixed chests.
        Workspace world = Create();
        PlaceChest(world, 30, 30);
        PlaceChest(world, 40, 40);
        PlaceChest(world, 50, 50);
        Assert.True(world.TryAddChest(30, 30, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(53)),
             new(1, new ItemTypeId(2)), new(1, new ItemTypeId(96)),
             new(1, new ItemTypeId(113)), new(1, new ItemTypeId(1157)),
             new(1, new ItemTypeId(155)), new(1, new ItemTypeId(162)),
             new(1, new ItemTypeId(160))]));
        Assert.True(world.TryAddChest(40, 40, string.Empty, []));
        Assert.True(world.TryAddChest(50, 50, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(53)),
             new(1, new ItemTypeId(219)), new(1, new ItemTypeId(165)),
             new(1, new ItemTypeId(1309)), new(1, new ItemTypeId(55)),
             new(1, new ItemTypeId(3389)), new(1, new ItemTypeId(4672))]));
        Assert.True(world.TryRemoveEmptyGeneratedChest(40, 40));
        var random = new RandomAdapter(seed);

        Assert.Equal(14, AnniversaryChestPrefix1458.ImproveQualifiedChests(world, random,
            TestContext.Current.CancellationToken));

        WorldChest[] chests = world.CaptureGeneratedChests();
        Assert.Equal(new short[] { 0, 2 }, chests.Select(chest => chest.SlotId));
        Assert.Equal(Parse(firstChest), chests[0].Items.Take(9).Select(item => item.Prefix));
        Assert.Equal(Parse(secondChest), chests[1].Items.Take(8).Select(item => item.Prefix));
        Assert.Equal(nextRandom, random.Next());

        static byte[] Parse(string value) => value.Split(',').Select(byte.Parse).ToArray();
    }

    [Theory]
    [InlineData(1, 81, 1002897798)]
    [InlineData(2, 59, 356479430)]
    [InlineData(3, 57, 1857544709)]
    [InlineData(1458, 59, 1916656655)]
    public void Direct_official_eye_of_cthulhu_shield_runs_accessory_then_melee_prefix_passes(
        int seed, byte finalPrefix, int nextRandom)
    {
        // Original Item.SetDefaults(3097): accessory=true and melee=true.
        // ImproveAllChestContents therefore selects accessory good prefix,
        // then a melee good prefix on the same item, consuming two draws.
        Workspace world = Create();
        PlaceChest(world, 30, 30);
        Assert.True(world.TryAddChest(30, 30, string.Empty,
            [new(1, new ItemTypeId(3097))]));
        var random = new RandomAdapter(seed);

        Assert.Equal(1, AnniversaryChestPrefix1458.ImproveQualifiedChests(world,
            random, TestContext.Current.CancellationToken));

        WorldChest chest = Assert.Single(world.CaptureGeneratedChests());
        Assert.Equal((short)0, chest.SlotId);
        Assert.Equal(3097, chest.Items[0].ItemType);
        Assert.Equal(finalPrefix, chest.Items[0].Prefix);
        Assert.Equal(nextRandom, random.Next());
    }

    [Theory]
    [InlineData(1, "65,65,0,57,60,96,59,59", "76,65,57,83,59,81,57", 1464848243)]
    [InlineData(2, "76,68,0,82,60,85,81,57", "68,65,82,60,85,81,59", 820542731)]
    [InlineData(3, "68,72,0,60,83,95,81,81", "76,68,57,57,97,57,81", 176237219)]
    [InlineData(1458, "68,72,0,60,60,95,57,81", "76,68,60,57,96,57,81", 1162070998)]
    public void Direct_official_melee_prefixes_cover_four_families_after_other_categories(
        int seed, string firstChest, string secondChest, int nextRandom)
    {
        // TerrariaServer 1.4.5.8 ImproveAllChestContents with Muramasa155,
        // Ball O' Hurt162, Enchanted Boomerang55 and Terrarian3389 in four
        // source melee prefix families. Null slot1 is skipped, not a terminator.
        Workspace world = Create();
        PlaceChest(world, 30, 30);
        PlaceChest(world, 40, 40);
        PlaceChest(world, 50, 50);
        Assert.True(world.TryAddChest(30, 30, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(53)),
             new(1, new ItemTypeId(2)), new(1, new ItemTypeId(96)),
             new(1, new ItemTypeId(113)), new(1, new ItemTypeId(1157)),
             new(1, new ItemTypeId(155)), new(1, new ItemTypeId(162))]));
        Assert.True(world.TryAddChest(40, 40, string.Empty, []));
        Assert.True(world.TryAddChest(50, 50, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(53)),
             new(1, new ItemTypeId(219)), new(1, new ItemTypeId(165)),
             new(1, new ItemTypeId(1309)), new(1, new ItemTypeId(55)),
             new(1, new ItemTypeId(3389))]));
        Assert.True(world.TryRemoveEmptyGeneratedChest(40, 40));
        var random = new RandomAdapter(seed);

        Assert.Equal(14, AnniversaryChestPrefix1458.ImproveQualifiedChests(world, random,
            TestContext.Current.CancellationToken));

        WorldChest[] chests = world.CaptureGeneratedChests();
        Assert.Equal(new short[] { 0, 2 }, chests.Select(chest => chest.SlotId));
        Assert.Equal(Parse(firstChest), chests[0].Items.Take(8).Select(item => item.Prefix));
        Assert.Equal(Parse(secondChest), chests[1].Items.Take(7).Select(item => item.Prefix));
        Assert.Equal(nextRandom, random.Next());

        static byte[] Parse(string value) => value.Split(',').Select(byte.Parse).ToArray();
    }

    [Theory]
    [InlineData(1, 65, 65, 57, 60, 96, 68, 68, 60, 83, 96, 61497087)]
    [InlineData(2, 76, 68, 82, 60, 85, 68, 76, 57, 83, 85, 1643613848)]
    [InlineData(3, 68, 72, 60, 83, 95, 65, 68, 60, 57, 59, 1078246962)]
    [InlineData(1458, 68, 72, 60, 60, 95, 72, 65, 60, 57, 97, 955664633)]
    public void Direct_official_summon_prefixes_follow_accessory_ranged_magic_sparse_order(
        int seed, byte a0, byte a1, byte r0, byte m0, byte s0,
        byte a2, byte a3, byte r1, byte m1, byte s1, int nextRandom)
    {
        // Direct original ImproveAllChestContents: Pygmy Staff1157 and Slime
        // Staff1309 cover all five GoodPrefixIdsForSummonerWeapon across seeds.
        Workspace world = Create();
        PlaceChest(world, 30, 30);
        PlaceChest(world, 40, 40);
        PlaceChest(world, 50, 50);
        Assert.True(world.TryAddChest(30, 30, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(53)),
             new(1, new ItemTypeId(2)), new(1, new ItemTypeId(96)),
             new(1, new ItemTypeId(113)), new(1, new ItemTypeId(1157))]));
        Assert.True(world.TryAddChest(40, 40, string.Empty, []));
        Assert.True(world.TryAddChest(50, 50, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(53)),
             new(1, new ItemTypeId(219)), new(1, new ItemTypeId(165)),
             new(1, new ItemTypeId(1309))]));
        Assert.True(world.TryRemoveEmptyGeneratedChest(40, 40));
        var random = new RandomAdapter(seed);

        Assert.Equal(10, AnniversaryChestPrefix1458.ImproveQualifiedChests(world, random,
            TestContext.Current.CancellationToken));

        WorldChest[] chests = world.CaptureGeneratedChests();
        Assert.Equal(new short[] { 0, 2 }, chests.Select(chest => chest.SlotId));
        Assert.Equal(new byte[] { a0, a1, 0, r0, m0, s0 },
            chests[0].Items.Take(6).Select(item => item.Prefix));
        Assert.Equal(new byte[] { a2, a3, r1, m1, s1 },
            chests[1].Items.Take(5).Select(item => item.Prefix));
        Assert.Equal(nextRandom, random.Next());
    }

    [Theory]
    [InlineData(1, 65, 65, 57, 60, 72, 68, 57, 60, 217468053)]
    [InlineData(2, 76, 68, 82, 60, 65, 68, 60, 57, 483147644)]
    [InlineData(3, 68, 72, 60, 83, 72, 65, 82, 60, 748827235)]
    [InlineData(1458, 68, 72, 60, 60, 68, 72, 82, 60, 765575680)]
    public void Direct_official_magic_prefixes_compose_with_accessory_ranged_and_sparse_slots(
        int seed, byte a0, byte a1, byte r0, byte m0, byte a2, byte a3, byte r1,
        byte m1, int nextRandom)
    {
        // Direct official ImproveAllChestContents on the same sparse 0/hole/2
        // image as the ranged fixture, adding Magic Missile113 and Water Bolt165.
        // Both source Magic entries accept all three 83/57/60 options.
        Workspace world = Create();
        PlaceChest(world, 30, 30);
        PlaceChest(world, 40, 40);
        PlaceChest(world, 50, 50);
        Assert.True(world.TryAddChest(30, 30, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(53)),
             new(1, new ItemTypeId(2)), new(1, new ItemTypeId(96)),
             new(1, new ItemTypeId(113))]));
        Assert.True(world.TryAddChest(40, 40, string.Empty, []));
        Assert.True(world.TryAddChest(50, 50, string.Empty,
            [new(1, new ItemTypeId(49)), new(1, new ItemTypeId(53)),
             new(1, new ItemTypeId(219)), new(1, new ItemTypeId(165))]));
        Assert.True(world.TryRemoveEmptyGeneratedChest(40, 40));
        var random = new RandomAdapter(seed);

        Assert.Equal(8, AnniversaryChestPrefix1458.ImproveQualifiedChests(world, random,
            TestContext.Current.CancellationToken));

        WorldChest[] chests = world.CaptureGeneratedChests();
        Assert.Equal(new short[] { 0, 2 }, chests.Select(chest => chest.SlotId));
        Assert.Equal(new byte[] { a0, a1, 0, r0, m0 },
            chests[0].Items.Take(5).Select(item => item.Prefix));
        Assert.Equal(new byte[] { a2, a3, r1, m1 },
            chests[1].Items.Take(4).Select(item => item.Prefix));
        Assert.Equal(nextRandom, random.Next());
    }

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
    [InlineData(39)] // weak bow enters Item.Prefix positive-request fallback.
    [InlineData(1244)] // weak magic item also enters positive-request fallback.
    [InlineData(4758)] // weak summon item enters positive-request fallback.
    [InlineData(196)] // Weak melee item needs positive-prefix fallback.
    [InlineData(3506)] // Another weak melee item needs positive-prefix fallback.
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
