using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>
/// Active-player facts consumed by TerrariaServer 1.4.5.8 town-NPC spawn eligibility. The runtime derives these
/// from authoritative packet-5 inventory and player-vitals state; the evaluator never reads transport packets.
/// </summary>
public readonly record struct VanillaTownSpawnPlayerFacts1458(
    bool Active,
    short MaxLife,
    long CoinValue,
    bool HasBulletAmmoOrWeapon,
    bool HasDemolitionistBomb,
    bool HasDyeTraderItem);

/// <summary>Persisted/event facts used by Main.UpdateTime_SpawnTownNPCs in TerrariaServer 1.4.5.8.</summary>
public readonly record struct VanillaTownSpawnWorldFacts1458(
    bool InfectedSeed,
    bool VampireSeed,
    bool TenthAnniversaryWorld,
    bool GetGoodWorld,
    bool Christmas,
    bool GenuineParty,
    bool HardMode,
    bool DownedBoss1,
    bool DownedBoss2,
    bool DownedBoss3,
    bool DownedQueenBee,
    bool DownedMechBossAny,
    bool DownedPlantBoss,
    bool DownedFrost,
    bool DownedPirates,
    bool SavedGoblin,
    bool SavedWizard,
    bool SavedMechanic,
    bool SavedAngler,
    bool SavedStylist,
    bool SavedTaxCollector,
    bool SavedGolfer,
    bool SavedBartender,
    bool BoughtCat,
    bool BoughtDog,
    bool BoughtBunny,
    bool UnlockedMerchantSpawn,
    bool UnlockedDemolitionistSpawn,
    bool UnlockedPartyGirlSpawn,
    bool UnlockedDyeTraderSpawn,
    bool UnlockedArmsDealerSpawn,
    bool UnlockedNurseSpawn,
    bool UnlockedPrincessSpawn,
    bool UnlockedSlimeBlueSpawn,
    bool UnlockedSlimeGreenSpawn,
    bool UnlockedSlimeOldSpawn,
    bool UnlockedSlimePurpleSpawn,
    bool UnlockedSlimeRainbowSpawn,
    bool UnlockedSlimeRedSpawn,
    bool UnlockedSlimeYellowSpawn,
    bool UnlockedSlimeCopperSpawn,
    float BestiaryCompletionPercent,
    bool PartyGirlRollSucceeded)
{
    /// <summary>Persisted TerrariaServer 1.4.5.8 NPC.unlockedTruffleSpawn state.</summary>
    public bool UnlockedTruffleSpawn { get; init; }

    public bool IsValid =>
        float.IsFinite(BestiaryCompletionPercent) &&
        BestiaryCompletionPercent is >= 0f and <= 1f;
}

/// <summary>
/// One source-shaped 1.4.5.8 eligibility pass. EligibleTypes mirrors the set of true Main.townNPCCanSpawn flags.
/// PrioritizedType mirrors WorldGen.prioritizedTownNPCType. When non-zero it is placed first in EligibleTypes so the
/// existing room-aware runtime coordinator consumes the source priority before considering non-prioritized fallbacks.
/// </summary>
public sealed record VanillaTownSpawnEligibility1458(
    NpcTypeId[] EligibleTypes,
    NpcTypeId PrioritizedType)
{
    public bool CanSpawn(NpcTypeId type) => Array.IndexOf(EligibleTypes, type) >= 0;
}

/// <summary>
/// Clean-room projection of TerrariaServer 1.4.5.8 Main.UpdateTime_SpawnTownNPCs eligibility and its independent
/// WorldGen.prioritizedTownNPCType chain. Physical placement remains a separate WorldGen concern.
/// </summary>
public static class VanillaTownNpcSpawnEligibility1458
{
    private const int NpcTypeCount1458 = 697;
    private const int MerchantRequiredCopper = 5_000;

    public static VanillaTownSpawnEligibility1458 Evaluate(
        in VanillaTownSpawnWorldFacts1458 world,
        ReadOnlySpan<VanillaTownSpawnPlayerFacts1458> players,
        ReadOnlySpan<NpcTypeId> activeTownNpcTypes)
    {
        if (!world.IsValid)
            throw new ArgumentOutOfRangeException(nameof(world));

        var counts = new int[NpcTypeCount1458];
        int totalTownNpcCount = 0;
        foreach (NpcTypeId type in activeTownNpcTypes)
        {
            if ((uint)type.Value >= NpcTypeCount1458)
                continue;
            counts[type.Value]++;
            totalTownNpcCount++;
        }

        bool merchantAllowed = world.UnlockedMerchantSpawn;
        bool armsDealerAllowed = world.UnlockedArmsDealerSpawn;
        bool nurseAllowed = world.UnlockedNurseSpawn;
        bool dyeTraderAllowed = world.UnlockedDyeTraderSpawn;
        bool demolitionistAllowed = world.UnlockedDemolitionistSpawn;
        if (!merchantAllowed || !armsDealerAllowed || !nurseAllowed || !dyeTraderAllowed || !demolitionistAllowed)
        {
            long coinValue = 0;
            foreach (VanillaTownSpawnPlayerFacts1458 player in players)
            {
                if (!player.Active)
                    continue;

                coinValue = Math.Min((long)MerchantRequiredCopper, coinValue + Math.Max(0L, player.CoinValue));
                merchantAllowed |= coinValue >= MerchantRequiredCopper;
                armsDealerAllowed |= player.HasBulletAmmoOrWeapon;
                nurseAllowed |= player.MaxLife / 20 > 5;
                dyeTraderAllowed |= player.HasDyeTraderItem;
                demolitionistAllowed |= player.HasDemolitionistBomb;
            }
        }

        var eligible = new List<NpcTypeId>(40);
        void AddIfMissing(bool condition, NpcTypeId type)
        {
            if (condition && Count(type) == 0)
                eligible.Add(type);
        }

        int Count(NpcTypeId type) => (uint)type.Value < NpcTypeCount1458 ? counts[type.Value] : 0;

        bool partyGirlAllowed = world.UnlockedPartyGirlSpawn ||
            (world.PartyGirlRollSucceeded && totalTownNpcCount >= 20);
        bool greenSlimeAllowed = world.UnlockedSlimeGreenSpawn || world.GenuineParty;
        bool zoologistAllowed = (world.VampireSeed && !world.InfectedSeed) || world.BestiaryCompletionPercent >= 0.1f;
        bool dryadAllowed = world.InfectedSeed || world.DownedBoss1 || world.DownedBoss2 || world.DownedBoss3;
        bool steampunkerAllowed = (world.TenthAnniversaryWorld && !world.GetGoodWorld) || world.DownedMechBossAny;

        AddIfMissing(true, VanillaNpcIds.Guide);
        AddIfMissing(merchantAllowed, VanillaNpcIds.Merchant);
        AddIfMissing(nurseAllowed && Count(VanillaNpcIds.Merchant) > 0, VanillaNpcIds.Nurse);
        AddIfMissing(armsDealerAllowed, VanillaNpcIds.ArmsDealer);
        AddIfMissing(dryadAllowed, VanillaNpcIds.Dryad);
        AddIfMissing(demolitionistAllowed && Count(VanillaNpcIds.Merchant) > 0, VanillaNpcIds.Demolitionist);
        AddIfMissing(world.SavedStylist, VanillaNpcIds.Stylist);
        AddIfMissing(world.SavedAngler, VanillaNpcIds.Angler);
        AddIfMissing(world.DownedBoss3, VanillaNpcIds.Clothier);
        AddIfMissing(world.SavedGoblin, VanillaNpcIds.GoblinTinkerer);
        AddIfMissing(world.SavedTaxCollector, VanillaNpcIds.TaxCollector);
        AddIfMissing(world.SavedWizard, VanillaNpcIds.Wizard);
        AddIfMissing(world.SavedMechanic, VanillaNpcIds.Mechanic);
        AddIfMissing(world.DownedFrost && world.Christmas, VanillaNpcIds.SantaClaus);
        AddIfMissing(steampunkerAllowed, VanillaNpcIds.Steampunker);
        AddIfMissing(dyeTraderAllowed && totalTownNpcCount >= 4, VanillaNpcIds.DyeTrader);
        AddIfMissing(world.DownedQueenBee, VanillaNpcIds.WitchDoctor);
        AddIfMissing(world.DownedPirates, VanillaNpcIds.Pirate);
        AddIfMissing(world.HardMode, VanillaNpcIds.Truffle);
        AddIfMissing(world.HardMode && world.DownedPlantBoss, VanillaNpcIds.Cyborg);
        AddIfMissing(totalTownNpcCount >= 8, VanillaNpcIds.Painter);
        AddIfMissing(partyGirlAllowed, VanillaNpcIds.PartyGirl);
        AddIfMissing(world.SavedBartender, VanillaNpcIds.Tavernkeep);
        AddIfMissing(world.SavedGolfer, VanillaNpcIds.Golfer);
        AddIfMissing(zoologistAllowed, VanillaNpcIds.Zoologist);
        AddIfMissing(world.BoughtCat, VanillaNpcIds.TownCat);
        AddIfMissing(world.BoughtDog, VanillaNpcIds.TownDog);
        AddIfMissing(world.BoughtBunny, VanillaNpcIds.TownBunny);
        AddIfMissing(world.UnlockedSlimeBlueSpawn, VanillaNpcIds.TownSlimeBlue);
        AddIfMissing(greenSlimeAllowed, VanillaNpcIds.TownSlimeGreen);
        AddIfMissing(world.UnlockedSlimeOldSpawn, VanillaNpcIds.TownSlimeOld);
        AddIfMissing(world.UnlockedSlimePurpleSpawn, VanillaNpcIds.TownSlimePurple);
        AddIfMissing(world.UnlockedSlimeRainbowSpawn, VanillaNpcIds.TownSlimeRainbow);
        AddIfMissing(world.UnlockedSlimeRedSpawn, VanillaNpcIds.TownSlimeRed);
        AddIfMissing(world.UnlockedSlimeYellowSpawn, VanillaNpcIds.TownSlimeYellow);
        AddIfMissing(world.UnlockedSlimeCopperSpawn, VanillaNpcIds.TownSlimeCopper);

        bool princessPopulationComplete = HasPrincessPopulation(counts);
        bool princessAllowed = world.UnlockedPrincessSpawn ||
            (world.TenthAnniversaryWorld && !world.GetGoodWorld) ||
            princessPopulationComplete;
        AddIfMissing(princessAllowed, VanillaNpcIds.Princess);

        NpcTypeId prioritized = ResolvePrioritizedType(
            in world,
            merchantAllowed,
            armsDealerAllowed,
            nurseAllowed,
            dyeTraderAllowed,
            demolitionistAllowed,
            partyGirlAllowed,
            greenSlimeAllowed,
            princessAllowed,
            totalTownNpcCount,
            counts);

        if (prioritized.Value != 0)
        {
            int priorityIndex = eligible.IndexOf(prioritized);
            if (priorityIndex > 0)
            {
                eligible.RemoveAt(priorityIndex);
                eligible.Insert(0, prioritized);
            }
        }

        return new VanillaTownSpawnEligibility1458(eligible.ToArray(), prioritized);
    }

    private static NpcTypeId ResolvePrioritizedType(
        in VanillaTownSpawnWorldFacts1458 world,
        bool merchantAllowed,
        bool armsDealerAllowed,
        bool nurseAllowed,
        bool dyeTraderAllowed,
        bool demolitionistAllowed,
        bool partyGirlAllowed,
        bool greenSlimeAllowed,
        bool princessAllowed,
        int totalTownNpcCount,
        int[] counts)
    {
        NpcTypeId prioritized = default;
        int Count(NpcTypeId type) => (uint)type.Value < NpcTypeCount1458 ? counts[type.Value] : 0;
        void Take(bool condition, NpcTypeId type)
        {
            if (prioritized.Value == 0 && condition && Count(type) == 0)
                prioritized = type;
        }

        Take(world.InfectedSeed, VanillaNpcIds.Dryad);
        Take(world.VampireSeed && !world.InfectedSeed, VanillaNpcIds.Zoologist);
        Take(true, VanillaNpcIds.Guide);
        Take(merchantAllowed, VanillaNpcIds.Merchant);
        Take(nurseAllowed && Count(VanillaNpcIds.Merchant) > 0, VanillaNpcIds.Nurse);
        Take(armsDealerAllowed, VanillaNpcIds.ArmsDealer);
        Take(world.SavedGoblin, VanillaNpcIds.GoblinTinkerer);
        Take(world.SavedWizard, VanillaNpcIds.Wizard);
        Take(world.DownedBoss1 || world.DownedBoss2 || world.DownedBoss3, VanillaNpcIds.Dryad);
        Take(demolitionistAllowed && Count(VanillaNpcIds.Merchant) > 0, VanillaNpcIds.Demolitionist);
        Take(world.DownedQueenBee, VanillaNpcIds.WitchDoctor);
        Take(world.DownedMechBossAny, VanillaNpcIds.Steampunker);
        Take(world.SavedMechanic, VanillaNpcIds.Mechanic);
        Take(world.SavedAngler, VanillaNpcIds.Angler);
        Take(world.HardMode && world.DownedPlantBoss, VanillaNpcIds.Cyborg);
        Take(world.DownedPirates, VanillaNpcIds.Pirate);
        Take(world.DownedBoss3, VanillaNpcIds.Clothier);
        Take(world.SavedStylist, VanillaNpcIds.Stylist);
        Take(totalTownNpcCount >= 4 && dyeTraderAllowed, VanillaNpcIds.DyeTrader);
        Take(totalTownNpcCount >= 8, VanillaNpcIds.Painter);
        Take(partyGirlAllowed, VanillaNpcIds.PartyGirl);
        Take(world.DownedFrost && world.Christmas, VanillaNpcIds.SantaClaus);
        Take(world.SavedBartender, VanillaNpcIds.Tavernkeep);
        Take(world.SavedGolfer, VanillaNpcIds.Golfer);
        Take(world.SavedTaxCollector, VanillaNpcIds.TaxCollector);
        Take(world.HardMode, VanillaNpcIds.Truffle);
        Take(world.BestiaryCompletionPercent >= 0.1f, VanillaNpcIds.Zoologist);
        Take(princessAllowed, VanillaNpcIds.Princess);
        Take(world.UnlockedSlimeCopperSpawn, VanillaNpcIds.TownSlimeCopper);
        Take(world.UnlockedSlimeBlueSpawn, VanillaNpcIds.TownSlimeBlue);
        Take(greenSlimeAllowed, VanillaNpcIds.TownSlimeGreen);
        Take(world.UnlockedSlimeOldSpawn, VanillaNpcIds.TownSlimeOld);
        Take(world.UnlockedSlimePurpleSpawn, VanillaNpcIds.TownSlimePurple);
        Take(world.UnlockedSlimeRedSpawn, VanillaNpcIds.TownSlimeRed);
        Take(world.UnlockedSlimeYellowSpawn, VanillaNpcIds.TownSlimeYellow);
        Take(world.UnlockedSlimeRainbowSpawn, VanillaNpcIds.TownSlimeRainbow);
        Take(world.BoughtBunny, VanillaNpcIds.TownBunny);
        Take(world.BoughtCat, VanillaNpcIds.TownCat);
        Take(world.BoughtDog, VanillaNpcIds.TownDog);
        return prioritized;
    }

    private static bool HasPrincessPopulation(int[] counts)
    {
        ReadOnlySpan<int> required =
        [
            17, 18, 20, 19, 22, 38, 54, 108, 107, 124, 160, 178,
            207, 208, 209, 227, 228, 229, 353, 369, 441, 550, 588, 633
        ];
        foreach (int type in required)
        {
            if (counts[type] == 0)
                return false;
        }
        return true;
    }
}

/// <summary>Source-pinned item predicates used by the five inventory-dependent town spawn gates.</summary>
public static class VanillaTownNpcSpawnItemFacts1458
{
    private static ReadOnlySpan<int> DemolitionistBombItemIds =>
    [168, 2586, 3116, 166, 235, 3115, 167, 2896, 3547, 3196, 4423, 1130, 1168, 4824, 4825, 4826, 4827, 4908, 4909, 5594, 5595];

    private static ReadOnlySpan<int> BulletAmmoOrWeaponItemIds =>
    [95, 96, 97, 98, 164, 219, 234, 278, 434, 515, 533, 534, 546, 679, 800, 964, 1179, 1254, 1255, 1265, 1302, 1319, 1335, 1342, 1349, 1350, 1351, 1352, 1553, 1870, 1929, 2269, 2270, 2797, 3104, 3475, 3567, 3788, 4703, 4915, 5117];

    public static long GetCoinValue(ItemTypeId type, int stack) => type.Value switch
    {
        71 => Math.Max(0, stack),
        72 => Math.Max(0, stack) * 100L,
        73 => Math.Max(0, stack) * 10_000L,
        74 => Math.Max(0, stack) * 1_000_000L,
        _ => 0L
    };

    public static bool CountsForDemolitionist(ItemTypeId type) => DemolitionistBombItemIds.Contains(type.Value);

    public static bool CountsForArmsDealer(ItemTypeId type) => BulletAmmoOrWeaponItemIds.Contains(type.Value);

    public static bool CountsForDyeTrader(ItemTypeId type) =>
        type.Value is >= 1107 and <= 1120 or >= 3385 and <= 3388 ||
        DyeItemIds.Contains(type.Value);

    // Terraria.Initializers.DyeInitializer.LoadArmorDyes in pinned TerrariaServer 1.4.5.8.
    private static ReadOnlySpan<int> DyeItemIds =>
    [
        1007, 1008, 1009, 1010, 1011, 1012, 1013, 1014, 1015, 1016, 1017, 1018,
        1019, 1020, 1021, 1022, 1023, 1024, 1025, 1026, 1027, 1028, 1029, 1030,
        1031, 1032, 1033, 1034, 1035, 1036, 1037, 1038, 1039, 1040, 1041, 1042,
        1043, 1044, 1045, 1046, 1047, 1048, 1049, 1050, 1051, 1052, 1053, 1054,
        1055, 1056, 1057, 1058, 1059, 1060, 1061, 1062, 1063, 1064, 1065, 1066,
        1067, 1068, 1069, 1070, 1969, 2864, 2869, 2870, 2871, 2872, 2873, 2874,
        2875, 2876, 2877, 2878, 2879, 2883, 2884, 2885, 3024, 3025, 3026, 3027,
        3028, 3038, 3039, 3040, 3041, 3042, 3190, 3526, 3527, 3528, 3529, 3530,
        3533, 3534, 3535, 3550, 3551, 3552, 3553, 3554, 3555, 3556, 3557, 3558,
        3559, 3560, 3561, 3562, 3597, 3598, 3599, 3600, 3978, 4662, 4663, 4778
    ];
}
