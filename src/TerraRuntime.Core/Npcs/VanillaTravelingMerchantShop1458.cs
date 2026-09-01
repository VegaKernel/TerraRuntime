using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

public readonly record struct VanillaTravelingMerchantWorldFacts1458(
    bool HardMode = false,
    bool ExpertMode = false,
    bool TenthAnniversaryWorld = false,
    bool GetGoodWorld = false,
    bool DontStarveWorld = false,
    bool PeddlersSatchelWasUsed = false,
    bool ShadowOrbSmashed = false,
    bool DownedBoss1 = false,
    bool DownedBoss2 = false,
    bool DownedBoss3 = false,
    bool DownedQueenBee = false,
    bool DownedDeerclops = false,
    bool DownedSlimeKing = false,
    bool DownedMechBossAny = false,
    bool DownedMechBoss1 = false,
    bool DownedMechBoss2 = false,
    bool DownedMechBoss3 = false,
    bool DownedFrost = false,
    bool DownedMartians = false,
    bool DownedMoonLord = false);

/// <summary>
/// Owns the exact TerrariaServer 1.4.5.8 Main.rand operations consumed by Chest.SetupTravelShop and Luck.RollLuck.
/// Player-luck selection is an explicit input to Generate rather than hidden inside the random source.
/// </summary>
public interface IVanillaTravelingMerchantRandom1458
{
    int NextInt32(int exclusiveMax);
    int NextInt32(int inclusiveMin, int exclusiveMax);
    float NextFloat();
}

/// <summary>
/// Source-shaped TerrariaServer 1.4.5.8 Chest.SetupTravelShop implementation.
/// The generated array is the authoritative 40-slot Main.travelShop image, including bundled vanity-set slots
/// and zero-filled tail slots. RNG ordering intentionally follows the source, including overwrite-style independent ifs.
/// </summary>
public static class VanillaTravelingMerchantShop1458
{
    public const int MaximumSlots = 40;
    private const int MaximumRaritySearchAttempts = 5_000;

    public static void Generate(
        in VanillaTravelingMerchantWorldFacts1458 world,
        float highestPlayerLuck,
        IVanillaTravelingMerchantRandom1458 random,
        Span<ItemTypeId> destination)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (destination.Length < MaximumSlots)
            throw new ArgumentException($"Travel shop destination must expose at least {MaximumSlots} slots.", nameof(destination));

        destination[..MaximumSlots].Clear();

        int targetAdded = random.NextInt32(4, 7);
        if (RollLuck(highestPlayerLuck, 4, random) == 0) targetAdded++;
        if (RollLuck(highestPlayerLuck, 8, random) == 0) targetAdded++;
        if (RollLuck(highestPlayerLuck, 16, random) == 0) targetAdded++;
        if (RollLuck(highestPlayerLuck, 32, random) == 0) targetAdded++;
        if (world.ExpertMode && RollLuck(highestPlayerLuck, 2, random) == 0) targetAdded++;
        if (world.PeddlersSatchelWasUsed) targetAdded++;
        if (world.TenthAnniversaryWorld)
        {
            if (!world.GetGoodWorld) targetAdded++;
            targetAdded++;
        }

        int count = 0;
        int added = 0;
        int[] rarity = [100, 200, 300, 400, 500, 600];

        if (world.HardMode)
        {
            int item = 0;
            for (int attempts = 1; attempts <= MaximumRaritySearchAttempts; attempts++)
            {
                AdjustSlotRarities(attempts, rarity);
                GetItem(random, highestPlayerLuck, in world, rarity, ref item, minimumRarity: 2);
                if (!CanAdd(destination, item))
                    continue;

                AddToShop(destination, random, item, ref added, ref count);
                break;
            }
        }

        // Vanilla has no attempt cap here. A valid RollLuck/Random source eventually produces an admissible item.
        while (added < targetAdded)
        {
            int item = 0;
            GetItem(random, highestPlayerLuck, in world, rarity, ref item);
            if (CanAdd(destination, item))
                AddToShop(destination, random, item, ref added, ref count);
        }

        int painting = 0;
        for (int attempts = 1; attempts <= MaximumRaritySearchAttempts; attempts++)
        {
            AdjustSlotRarities(attempts, rarity);
            GetPainting(random, highestPlayerLuck, in world, rarity, ref painting);
            if (!CanAdd(destination, painting))
                continue;

            AddToShop(destination, random, painting, ref added, ref count);
            break;
        }
    }

    public static ItemTypeId[] Generate(
        in VanillaTravelingMerchantWorldFacts1458 world,
        float highestPlayerLuck,
        IVanillaTravelingMerchantRandom1458 random)
    {
        var result = new ItemTypeId[MaximumSlots];
        Generate(in world, highestPlayerLuck, random, result);
        return result;
    }

    private static void AddToShop(
        Span<ItemTypeId> shop,
        IVanillaTravelingMerchantRandom1458 random,
        int itemId,
        ref int added,
        ref int count)
    {
        if (itemId == 0)
            return;

        added++;
        AddSlot(shop, ref count, itemId);
        switch (itemId)
        {
            case 2260:
                AddSlot(shop, ref count, 2261);
                AddSlot(shop, ref count, 2262);
                break;
            case 5680:
                AddSlot(shop, ref count, 5681);
                AddSlot(shop, ref count, 5682);
                break;
            case 4555:
                AddSlot(shop, ref count, 4556);
                AddSlot(shop, ref count, 4557);
                break;
            case 4321:
                AddSlot(shop, ref count, 4322);
                break;
            case 4323:
                AddSlot(shop, ref count, 4324);
                AddSlot(shop, ref count, 4365);
                break;
            case 5390:
                AddSlot(shop, ref count, 5386);
                AddSlot(shop, ref count, 5387);
                break;
            case 4666:
                AddSlot(shop, ref count, 4664);
                AddSlot(shop, ref count, 4665);
                break;
            case 3637:
                count--;
                (int first, int second) = random.NextInt32(6) switch
                {
                    0 => (3637, 3642),
                    1 => (3621, 3622),
                    2 => (3634, 3639),
                    3 => (3633, 3638),
                    4 => (3635, 3640),
                    _ => (3636, 3641)
                };
                AddSlot(shop, ref count, first);
                AddSlot(shop, ref count, second);
                break;
        }
    }

    private static void AddSlot(Span<ItemTypeId> shop, ref int count, int itemId)
    {
        if ((uint)count >= MaximumSlots)
            throw new InvalidOperationException("TerrariaServer 1.4.5.8 generated more than 40 Traveling Merchant slots.");
        shop[count++] = new ItemTypeId(itemId);
    }

    private static bool CanAdd(ReadOnlySpan<ItemTypeId> shop, int itemId)
    {
        if (itemId == 0)
            return false;

        for (int i = 0; i < MaximumSlots; i++)
        {
            int current = shop[i].Value;
            if (current == itemId)
                return false;
            if (itemId == 3637 && ((uint)(current - 3621) <= 1u || (uint)(current - 3633) <= 9u))
                return false;
        }
        return true;
    }

    private static void AdjustSlotRarities(int attempts, int[] rarity)
    {
        if (rarity[5] > 1 && attempts > 4700) rarity[5] = 1;
        if (rarity[4] > 1 && attempts > 4600) rarity[4] = 1;
        if (rarity[3] > 1 && attempts > 4500) rarity[3] = 1;
        if (rarity[2] > 1 && attempts > 4400) rarity[2] = 1;
        if (rarity[1] > 1 && attempts > 4300) rarity[1] = 1;
        if (rarity[0] > 1 && attempts > 4200) rarity[0] = 1;
    }

    private static int RollLuck(float luck, int range, IVanillaTravelingMerchantRandom1458 random)
    {
        if (luck > 0f && random.NextFloat() < luck)
            return random.NextInt32(random.NextInt32(range / 2, range));
        if (luck < 0f && random.NextFloat() < -luck)
            return random.NextInt32(random.NextInt32(range, range * 2));
        return random.NextInt32(range);
    }

    private static void GetPainting(
        IVanillaTravelingMerchantRandom1458 random,
        float luck,
        in VanillaTravelingMerchantWorldFacts1458 world,
        int[] rarity,
        ref int item,
        int minimumRarity = 0)
    {
        if (RollLuck(luck, rarity[3], random) == 0 && !world.DontStarveWorld) item = 5121;
        if (RollLuck(luck, rarity[3], random) == 0 && !world.DontStarveWorld) item = 5122;
        if (RollLuck(luck, rarity[3], random) == 0 && !world.DontStarveWorld) item = 5124;
        if (RollLuck(luck, rarity[3], random) == 0 && !world.DontStarveWorld) item = 5123;
        if (minimumRarity > 2) return;

        if (RollLuck(luck, rarity[2], random) == 0 && world.HardMode && world.DownedMoonLord) item = 3596;
        if (RollLuck(luck, rarity[2], random) == 0 && world.HardMode && world.DownedMartians) item = 2865;
        if (RollLuck(luck, rarity[2], random) == 0 && world.HardMode && world.DownedMartians) item = 2866;
        if (RollLuck(luck, rarity[2], random) == 0 && world.HardMode && world.DownedMartians) item = 2867;
        if (RollLuck(luck, rarity[2], random) == 0 && world.DownedFrost) item = 3055;
        if (RollLuck(luck, rarity[2], random) == 0 && world.DownedFrost) item = 3056;
        if (RollLuck(luck, rarity[2], random) == 0 && world.DownedFrost) item = 3057;
        if (RollLuck(luck, rarity[2], random) == 0 && world.DownedFrost) item = 3058;
        if (RollLuck(luck, rarity[2], random) == 0 && world.DownedFrost) item = 3059;
        if (RollLuck(luck, rarity[2], random) == 0 && world.HardMode && world.DownedMoonLord) item = 5243;
        if (RollLuck(luck, rarity[2], random) == 0) item = 5530;
        if (RollLuck(luck, rarity[2], random) == 0) item = 5633;
        if (RollLuck(luck, rarity[2], random) == 0) item = 5636;
        if (minimumRarity > 1) return;

        if (RollLuck(luck, rarity[1], random) == 0 && world.DontStarveWorld) item = 5121;
        if (RollLuck(luck, rarity[1], random) == 0 && world.DontStarveWorld) item = 5122;
        if (RollLuck(luck, rarity[1], random) == 0 && world.DontStarveWorld) item = 5124;
        if (RollLuck(luck, rarity[1], random) == 0 && world.DontStarveWorld) item = 5123;
        if (RollLuck(luck, rarity[1], random) == 0) item = 5225;
        if (RollLuck(luck, rarity[1], random) == 0) item = 5229;
        if (RollLuck(luck, rarity[1], random) == 0) item = 5232;
        if (RollLuck(luck, rarity[1], random) == 0) item = 5389;
        if (RollLuck(luck, rarity[1], random) == 0) item = 5233;
        if (RollLuck(luck, rarity[1], random) == 0) item = 5241;
        if (RollLuck(luck, rarity[1], random) == 0) item = 5244;
        if (RollLuck(luck, rarity[1], random) == 0) item = 5487;
        if (RollLuck(luck, rarity[1], random) == 0) item = 5242;
        if (RollLuck(luck, rarity[1], random) == 0) item = 5531;
    }

    private static void GetItem(
        IVanillaTravelingMerchantRandom1458 random,
        float luck,
        in VanillaTravelingMerchantWorldFacts1458 world,
        int[] rarity,
        ref int item,
        int minimumRarity = 0)
    {
        if (minimumRarity <= 4 && RollLuck(luck, rarity[4], random) == 0) item = 3309;
        if (minimumRarity <= 3 && RollLuck(luck, rarity[3], random) == 0) item = 3314;
        if (RollLuck(luck, rarity[5], random) == 0) item = 1987;
        if (minimumRarity > 4) return;

        if (RollLuck(luck, rarity[4], random) == 0 && world.HardMode) item = 2270;
        if (RollLuck(luck, rarity[4], random) == 0 && world.HardMode) item = 4760;
        if (RollLuck(luck, rarity[4], random) == 0) item = 2278;
        if (RollLuck(luck, rarity[4], random) == 0) item = 2271;
        if (minimumRarity > 3) return;

        if (RollLuck(luck, rarity[3], random) == 0 && world.HardMode && world.DownedMechBoss1 && world.DownedMechBoss2 && world.DownedMechBoss3) item = 2223;
        if (RollLuck(luck, rarity[3], random) == 0) item = 2272;
        if (RollLuck(luck, rarity[3], random) == 0) item = 2276;
        if (RollLuck(luck, rarity[3], random) == 0) item = 2284;
        if (RollLuck(luck, rarity[3], random) == 0) item = 2285;
        if (RollLuck(luck, rarity[3], random) == 0) item = 2286;
        if (RollLuck(luck, rarity[3], random) == 0) item = 2287;
        if (RollLuck(luck, rarity[3], random) == 0) item = 4744;
        if (RollLuck(luck, rarity[3], random) == 0 && world.DownedBoss3) item = 2296;
        if (RollLuck(luck, rarity[3], random) == 0) item = 3628;
        if (RollLuck(luck, rarity[3], random) == 0 && world.HardMode) item = 4091;
        if (RollLuck(luck, rarity[3], random) == 0) item = 4603;
        if (RollLuck(luck, rarity[3], random) == 0) item = 4604;
        if (RollLuck(luck, rarity[3], random) == 0) item = 5297;
        if (RollLuck(luck, rarity[3], random) == 0) item = 4605;
        if (RollLuck(luck, rarity[3], random) == 0) item = 4550;
        if (minimumRarity > 2) return;

        if (RollLuck(luck, rarity[2], random) == 0) item = 5680;
        if (RollLuck(luck, rarity[2], random) == 0) item = 2268;
        if (RollLuck(luck, rarity[2], random) == 0 && world.ShadowOrbSmashed) item = 2269;
        if (RollLuck(luck, rarity[2], random) == 0) item = 1988;
        if (RollLuck(luck, rarity[2], random) == 0) item = 2275;
        if (RollLuck(luck, rarity[2], random) == 0) item = 2279;
        if (RollLuck(luck, rarity[2], random) == 0) item = 2277;
        if (RollLuck(luck, rarity[2], random) == 0) item = 4555;
        if (RollLuck(luck, rarity[2], random) == 0) item = 4321;
        if (RollLuck(luck, rarity[2], random) == 0) item = 4323;
        if (RollLuck(luck, rarity[2], random) == 0) item = 5390;
        if (RollLuck(luck, rarity[2], random) == 0) item = 4549;
        if (RollLuck(luck, rarity[2], random) == 0) item = 4561;
        if (RollLuck(luck, rarity[2], random) == 0) item = 4774;
        if (RollLuck(luck, rarity[2], random) == 0) item = 5136;
        if (RollLuck(luck, rarity[2], random) == 0) item = 5305;
        if (RollLuck(luck, rarity[2], random) == 0) item = 4562;
        if (RollLuck(luck, rarity[2], random) == 0) item = 4558;
        if (RollLuck(luck, rarity[2], random) == 0) item = 4559;
        if (RollLuck(luck, rarity[2], random) == 0) item = 4563;
        if (RollLuck(luck, rarity[2], random) == 0) item = 4666;
        if (RollLuck(luck, rarity[2], random) == 0 &&
            (world.DownedDeerclops || world.DownedSlimeKing || world.DownedBoss1 || world.DownedBoss2 ||
             world.DownedBoss3 || world.DownedQueenBee || world.HardMode))
        {
            item = 4347;
            if (world.HardMode)
                item = 4348;
        }
        if (RollLuck(luck, rarity[2], random) == 0 && world.DownedBoss1) item = 3262;
        if (RollLuck(luck, rarity[2], random) == 0 && world.DownedMechBossAny) item = 3284;
        if (minimumRarity > 1) return;

        if (RollLuck(luck, rarity[1], random) == 0) item = 5600;
        if (RollLuck(luck, rarity[1], random) == 0) item = 2267;
        if (RollLuck(luck, rarity[1], random) == 0) item = 2214;
        if (RollLuck(luck, rarity[1], random) == 0) item = 2215;
        if (RollLuck(luck, rarity[1], random) == 0) item = 2216;
        if (RollLuck(luck, rarity[1], random) == 0) item = 2217;
        if (RollLuck(luck, rarity[1], random) == 0) item = 3624;
        if (RollLuck(luck, rarity[1], random) == 0) item = 2273;
        if (RollLuck(luck, rarity[1], random) == 0) item = 2274;
        if (minimumRarity > 0) return;

        if (RollLuck(luck, rarity[0], random) == 0) item = 2266;
        if (RollLuck(luck, rarity[0], random) == 0) item = 2281 + random.NextInt32(3);
        if (RollLuck(luck, rarity[0], random) == 0) item = 2258;
        if (RollLuck(luck, rarity[0], random) == 0) item = 2242;
        if (RollLuck(luck, rarity[0], random) == 0) item = 2260;
        if (RollLuck(luck, rarity[0], random) == 0) item = 3637;
        if (RollLuck(luck, rarity[0], random) == 0) item = 4420;
        if (RollLuck(luck, rarity[0], random) == 0) item = 3119;
        if (RollLuck(luck, rarity[0], random) == 0) item = 3118;
        if (RollLuck(luck, rarity[0], random) == 0) item = 3099;
    }
}
