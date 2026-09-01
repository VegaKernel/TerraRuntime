using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaTravelingMerchantShop1458Tests
{
    [Fact]
    public void Same_seed_and_luck_produce_identical_40_slot_image()
    {
        VanillaTravelingMerchantWorldFacts1458 world = ProgressionHeavyWorld();
        var firstRandom = new SeededRandom(1458);
        var secondRandom = new SeededRandom(1458);

        ItemTypeId[] first = VanillaTravelingMerchantShop1458.Generate(in world, 0.35f, firstRandom);
        ItemTypeId[] second = VanillaTravelingMerchantShop1458.Generate(in world, 0.35f, secondRandom);

        Assert.Equal(VanillaTravelingMerchantShop1458.MaximumSlots, first.Length);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(-0.75f)]
    [InlineData(0f)]
    [InlineData(0.75f)]
    public void Generated_shop_is_compact_unique_and_zero_filled(float luck)
    {
        VanillaTravelingMerchantWorldFacts1458 world = ProgressionHeavyWorld();
        var random = new SeededRandom(326 + (int)(luck * 100f));

        ItemTypeId[] shop = VanillaTravelingMerchantShop1458.Generate(in world, luck, random);
        int occupied = Array.FindIndex(shop, static item => item.Value == 0);
        if (occupied < 0)
            occupied = shop.Length;

        Assert.InRange(occupied, 1, VanillaTravelingMerchantShop1458.MaximumSlots);
        Assert.All(shop.AsSpan(occupied).ToArray(), static item => Assert.Equal(0, item.Value));
        Assert.Equal(
            occupied,
            shop.AsSpan(0, occupied).ToArray().Select(static item => item.Value).Distinct().Count());
    }

    [Fact]
    public void Destination_must_expose_all_vanilla_travel_shop_slots()
    {
        VanillaTravelingMerchantWorldFacts1458 world = default;
        var random = new SeededRandom(1);
        ItemTypeId[] tooSmall = new ItemTypeId[VanillaTravelingMerchantShop1458.MaximumSlots - 1];

        Assert.Throws<ArgumentException>(() =>
            VanillaTravelingMerchantShop1458.Generate(in world, 0f, random, tooSmall));
    }

    private static VanillaTravelingMerchantWorldFacts1458 ProgressionHeavyWorld() => new(
        HardMode: true,
        ExpertMode: true,
        TenthAnniversaryWorld: true,
        GetGoodWorld: false,
        DontStarveWorld: false,
        PeddlersSatchelWasUsed: true,
        ShadowOrbSmashed: true,
        DownedBoss1: true,
        DownedBoss2: true,
        DownedBoss3: true,
        DownedQueenBee: true,
        DownedDeerclops: true,
        DownedSlimeKing: true,
        DownedMechBossAny: true,
        DownedMechBoss1: true,
        DownedMechBoss2: true,
        DownedMechBoss3: true,
        DownedFrost: true,
        DownedMartians: true,
        DownedMoonLord: true);

    private sealed class SeededRandom(int seed) : IVanillaTravelingMerchantRandom1458
    {
        private readonly Random random = new(seed);

        public int NextInt32(int exclusiveMax) => random.Next(exclusiveMax);

        public int NextInt32(int inclusiveMin, int exclusiveMax) => random.Next(inclusiveMin, exclusiveMax);

        public float NextFloat() => random.NextSingle();
    }
}
