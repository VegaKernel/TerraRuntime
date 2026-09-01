using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class VanillaTownShopCatalog1458Tests
{
    [Fact]
    public void Merchant_baseline_matches_pinned_setupshop_order()
    {
        Assert.True(VanillaTownShopCatalog1458.TryResolve(
            VanillaNpcIds.Merchant,
            new VanillaTownShopContext(DayTime: true),
            [],
            out ItemTypeId[] items));

        Assert.Equal([88, 87, 35, 1991, 3509, 3506, 8, 28, 110, 40, 42, 965, 1786], Raw(items));
    }

    [Fact]
    public void Merchant_progression_biome_and_inventory_conditions_are_source_ordered()
    {
        var context = new VanillaTownShopContext(
            HardMode: true,
            BloodMoon: true,
            DayTime: false,
            PartyIsUp: true,
            ZoneSnow: true,
            ZoneJungle: true,
            DownedBoss2: true,
            DownedBoss3: true);
        ItemTypeId[] inventory = [new(930), new(3107)];

        Assert.True(VanillaTownShopCatalog1458.TryResolve(VanillaNpcIds.Merchant, context, inventory, out ItemTypeId[] items));
        Assert.Contains(188, Raw(items));
        Assert.Contains(189, Raw(items));
        Assert.Contains(967, Raw(items));
        Assert.Contains(33, Raw(items));
        Assert.Contains(279, Raw(items));
        Assert.Contains(282, Raw(items));
        Assert.Contains(5643, Raw(items));
        Assert.Contains(931, Raw(items));
        Assert.Contains(1614, Raw(items));
        Assert.Contains(3108, Raw(items));
        Assert.True(items.Length <= VanillaTownShopCatalog1458.MaximumVanillaShopSlots);
    }

    [Fact]
    public void Arms_dealer_uses_alternate_silver_ammo_branch_and_owned_weapon_unlocks()
    {
        var context = new VanillaTownShopContext(HardMode: true, Halloween: true, SavedSilverOreType: 168);
        ItemTypeId[] inventory = [new(1258), new(1835), new(1782), new(1784)];

        Assert.True(VanillaTownShopCatalog1458.TryResolve(VanillaNpcIds.ArmsDealer, context, inventory, out ItemTypeId[] items));
        int[] raw = Raw(items);
        Assert.Contains(4915, raw);
        Assert.DoesNotContain(278, raw);
        Assert.Contains(1261, raw);
        Assert.Contains(1836, raw);
        Assert.Contains(1783, raw);
        Assert.Contains(1785, raw);
        Assert.Contains(1736, raw);
        Assert.Contains(1737, raw);
        Assert.Contains(1738, raw);
    }

    [Fact]
    public void Dryad_crimson_bloodmoon_and_moon_phase_branch_match_source_shape()
    {
        var context = new VanillaTownShopContext(
            BloodMoon: true,
            Crimson: true,
            HardMode: true,
            MoonPhase: VanillaMoonPhase.Empty);

        Assert.True(VanillaTownShopCatalog1458.TryResolve(VanillaNpcIds.Dryad, context, [], out ItemTypeId[] items));
        int[] raw = Raw(items);
        Assert.Equal([2886, 2171, 4508], raw[..3]);
        Assert.Contains(3218, raw);
        Assert.DoesNotContain(3217, raw);
        Assert.Contains(4436, raw);
        Assert.Contains(4437, raw);
        Assert.Contains(4438, raw);
    }

    [Fact]
    public void Demolitionist_unlocks_progression_and_player_owned_explosives()
    {
        var context = new VanillaTownShopContext(
            HardMode: true,
            DayTime: false,
            DownedBoss1: true,
            DownedPlantera: true,
            DownedPirates: true);
        ItemTypeId[] inventory = [new(4827), new(4824), new(4825), new(4826)];

        Assert.True(VanillaTownShopCatalog1458.TryResolve(VanillaNpcIds.Demolitionist, context, inventory, out ItemTypeId[] items));
        Assert.Equal([168, 166, 5542, 167, 265, 5481, 5464, 937, 1347, 4827, 4824, 4825, 4826], Raw(items));
    }


    public static TheoryData<NpcTypeId, int[]> OrdinaryVendorBaselines => new()
    {
        { VanillaNpcIds.Clothier, [254, 981, 242, 245, 246, 269, 270, 271, 5308, 5630] },
        { VanillaNpcIds.GoblinTinkerer, [128, 486, 398, 84, 407, 161, 5324] },
        { VanillaNpcIds.Wizard, [487, 496, 500, 507, 508, 531, 149, 576, 3186] },
        { VanillaNpcIds.Mechanic, [509, 850, 851, 3612, 510, 530, 513, 538, 529, 541, 542, 543, 852, 853, 4261, 3707, 2739, 849, 1263, 3616, 3725, 2799, 3619, 3627, 3629, 585, 584, 583, 4484, 4485] },
        { VanillaNpcIds.SantaClaus, Enumerable.Range(1873, 33).Prepend(596).Prepend(598).Prepend(597).Prepend(590).Prepend(589).Prepend(588).ToArray() },
        { VanillaNpcIds.Truffle, [868, 1181, 5231, 783] },
        { VanillaNpcIds.Steampunker, [779, 839, 840, 841, 3603, 3604, 3607, 3605, 3606, 3608, 3618, 3602, 3663, 3609, 3610, 995, 4142, 780] },
        { VanillaNpcIds.DyeTrader, [1120, 5920, 1037, 2874, 1969, 2871, 2872] },
        { VanillaNpcIds.PartyGirl, [859, 1000, 1168, 1449, 1345, 1450, 3253, 4553, 2700, 2738, 4470, 4681, 4791, 3747, 3732, 3742] },
        { VanillaNpcIds.Cyborg, [771, 5598, 5599, 5928] },
        { VanillaNpcIds.Painter, [1071, 1072, 1100, 1073, 1074, 1075, 1076, 1077, 1078, 1079, 1080, 1081, 1082, 1083, 1084, 1097, 1099, 1098, 1966] },
        { VanillaNpcIds.WitchDoctor, [1430, 986, 909, 910, 940, 941, 942, 943, 944, 945, 4922, 4417] },
        { VanillaNpcIds.Pirate, [928, 929, 876, 877, 878, 2434] },
        { VanillaNpcIds.Stylist, [1990, 1979, 1981, 5104] }
    };

    [Theory]
    [MemberData(nameof(OrdinaryVendorBaselines))]
    public void Remaining_ordinary_vendor_baselines_match_setupshop_source_order(NpcTypeId npcType, int[] expected)
    {
        Assert.True(VanillaTownShopCatalog1458.TryResolve(
            npcType,
            new VanillaTownShopContext(DayTime: true, MoonPhase: VanillaMoonPhase.Full),
            [],
            out ItemTypeId[] items));

        Assert.Equal(expected, Raw(items));
        Assert.True(items.Length <= VanillaTownShopCatalog1458.MaximumVanillaShopSlots);
    }

    [Fact]
    public void Clothier_progression_event_and_capacity_tail_follow_source_order()
    {
        var context = new VanillaTownShopContext(
            HardMode: true,
            BloodMoon: true,
            DayTime: false,
            PartyIsUp: true,
            Halloween: true,
            ZoneSnow: true,
            ZoneGraveyard: true,
            DownedClown: true,
            DownedAncientCultist: true,
            DownedFrost: true,
            HasTaxCollector: true,
            GolferScore: 2500,
            MoonPhase: VanillaMoonPhase.HalfAtRight);

        Assert.True(VanillaTownShopCatalog1458.TryResolve(VanillaNpcIds.Clothier, context, [], out ItemTypeId[] items));
        int[] raw = Raw(items);
        Assert.Contains(3362, raw);
        Assert.Contains(3363, raw);
        Assert.Contains(2857, raw);
        Assert.Contains(2859, raw);
        Assert.Contains(3242, raw);
        Assert.Contains(4685, raw);
        Assert.Contains(873, raw);
        Assert.Contains(874, raw);
        Assert.Contains(875, raw);
        Assert.Contains(1276, raw);
        Assert.Contains(3735, raw);
        Assert.Contains(5308, raw);
        Assert.True(raw.Length <= 40);
    }

    [Fact]
    public void Steampunker_selects_crimson_event_solution_and_late_progression_inventory()
    {
        var context = new VanillaTownShopContext(
            HardMode: true,
            BloodMoon: true,
            ZoneJungle: true,
            ZoneSnow: true,
            ZoneSky: true,
            Crimson: true,
            DownedBoss1: true,
            DownedBoss2: true,
            DownedBoss3: true,
            DownedGolemBoss: true,
            DownedMoonLord: true,
            MoonPhase: VanillaMoonPhase.HalfAtRight);

        Assert.True(VanillaTownShopCatalog1458.TryResolve(VanillaNpcIds.Steampunker, context, [], out ItemTypeId[] items));
        int[] raw = Raw(items);
        Assert.Contains(748, raw);
        Assert.DoesNotContain(839, raw);
        Assert.Contains(2203, raw);
        Assert.Contains(2193, raw);
        Assert.Contains(2204, raw);
        Assert.Contains(2195, raw);
        Assert.Contains(2198, raw);
        Assert.Contains(2197, raw);
        Assert.Contains(784, raw);
        Assert.Contains(5392, raw);
        Assert.Contains(5393, raw);
        Assert.Contains(5394, raw);
    }

    [Fact]
    public void Stylist_unlocks_health_mana_money_team_and_progression_hair_dyes()
    {
        var context = new VanillaTownShopContext(
            HardMode: true,
            DayTime: false,
            ZoneGraveyard: true,
            DownedMechBossAny: true,
            DownedMechBoss1: true,
            DownedMechBoss2: true,
            DownedMechBoss3: true,
            DownedMartians: true,
            HasPartyGirl: true,
            PlayerLifeMax: 500,
            PlayerManaMax: 200,
            PlayerCoinValueCopper: 1_000_000,
            PlayerTeam: 1,
            MultiplayerClient: true,
            MoonPhase: VanillaMoonPhase.ThreeQuartersAtLeft);

        Assert.True(VanillaTownShopCatalog1458.TryResolve(VanillaNpcIds.Stylist, context, [], out ItemTypeId[] items));
        Assert.Equal([1990, 1979, 1977, 1978, 1980, 1981, 1982, 1983, 1984, 1985, 1986, 2863, 3259, 5104, 5577], Raw(items));
    }

    [Fact]
    public void Witch_doctor_and_pirate_use_live_npc_and_owned_item_unlocks()
    {
        var context = new VanillaTownShopContext(
            HardMode: true,
            DayTime: false,
            Halloween: true,
            ZoneJungle: true,
            ZoneBeach: true,
            ZoneGraveyard: true,
            DownedPlantera: true,
            DownedMechBossAny: true,
            HasWizard: true,
            HasPartyGirl: true);
        ItemTypeId[] inventory = [new(1835), new(1258)];

        Assert.True(VanillaTownShopCatalog1458.TryResolve(VanillaNpcIds.WitchDoctor, context, inventory, out ItemTypeId[] witchDoctor));
        Assert.Contains(2999, Raw(witchDoctor));
        Assert.Contains(6147, Raw(witchDoctor));
        Assert.Contains(1162, Raw(witchDoctor));
        Assert.Contains(1836, Raw(witchDoctor));
        Assert.Contains(1261, Raw(witchDoctor));

        Assert.True(VanillaTownShopCatalog1458.TryResolve(VanillaNpcIds.Pirate, context, [], out ItemTypeId[] pirate));
        Assert.Contains(5926, Raw(pirate));
        Assert.Contains(1180, Raw(pirate));
        Assert.Contains(1337, Raw(pirate));
    }

    [Fact]
    public void Unsupported_vendor_fails_closed()
    {
        Assert.False(VanillaTownShopCatalog1458.TryResolve(VanillaNpcIds.Nurse, new VanillaTownShopContext(), [], out ItemTypeId[] items));
        Assert.Empty(items);
    }

    [Fact]
    public void Invalid_moon_phase_is_rejected_at_the_gameplay_boundary()
    {
        var context = new VanillaTownShopContext(MoonPhase: (VanillaMoonPhase)byte.MaxValue);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VanillaTownShopCatalog1458.TryResolve(VanillaNpcIds.Merchant, in context, [], out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VanillaSpecialTownShopCatalog1458.TryResolve(
                VanillaTownShopId1458.SkeletonMerchant,
                in context,
                [],
                [],
                out _));
    }

    private static int[] Raw(ItemTypeId[] items) => items.Select(static item => item.Value).ToArray();
}
