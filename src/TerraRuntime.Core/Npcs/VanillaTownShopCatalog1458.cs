using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>
/// Runtime-owned inputs consumed by the source-pinned TerrariaServer 1.4.5.8 Chest.SetupShop branches.
/// This intentionally names only state that changes the currently implemented vanilla shop slice.
/// </summary>
public readonly record struct VanillaTownShopContext(
    bool HardMode = false,
    bool BloodMoon = false,
    bool DayTime = true,
    bool HappyWindyDay = false,
    bool PartyIsUp = false,
    bool Halloween = false,
    bool ZoneSnow = false,
    bool ZoneJungle = false,
    bool ZoneGraveyard = false,
    bool ZoneUnderworld = false,
    bool ZoneGlowshroom = false,
    bool DownedBoss1 = false,
    bool DownedBoss2 = false,
    bool DownedBoss3 = false,
    bool DownedSlimeKing = false,
    bool DownedPlantera = false,
    bool DownedPirates = false,
    bool Crimson = false,
    bool RemixWorld = false,
    bool TenthAnniversaryWorld = false,
    bool NotTheBeesWorld = false,
    bool InfectedSeed = false,
    bool GoodWorld = false,
    bool VampireSeed = false,
    bool Eclipse = false,
    bool LanternsUp = false,
    bool ZoneBeach = false,
    bool ZoneSky = false,
    bool ZoneHallow = false,
    bool ZoneDesert = false,
    bool ZoneCorrupt = false,
    bool ZoneCrimson = false,
    bool ShoppingZoneForest = false,
    bool ShoppingZoneBelowSurface = false,
    bool DownedClown = false,
    bool DownedAncientCultist = false,
    bool DownedFrost = false,
    bool DownedMechBossAny = false,
    bool DownedMechBoss1 = false,
    bool DownedMechBoss2 = false,
    bool DownedMechBoss3 = false,
    bool DownedGolemBoss = false,
    bool DownedMoonLord = false,
    bool DownedQueenSlime = false,
    bool DownedTowerSolar = false,
    bool DownedMartians = false,
    bool HasTaxCollector = false,
    bool HasAngler = false,
    bool HasWizard = false,
    bool HasMechanic = false,
    bool HasPirate = false,
    bool HasPartyGirl = false,
    bool MultiplayerClient = true,
    bool AteArtisanBread = false,
    bool FairyTorchAvailable = false,
    bool XMas = false,
    bool Storming = false,
    bool HasEnoughTownNpcsForPylon = false,
    int GolferScore = 0,
    int PlayerLifeMax = 100,
    int PlayerManaMax = 20,
    long PlayerCoinValueCopper = 0,
    int PlayerTeam = 0,
    int MoonPhase = 0,
    int SavedSilverOreType = 0,
    double WorldTime = 0d,
    float BestiaryCompletion = 0f,
    float PlayerTileX = 0f,
    float PlayerTileY = 0f,
    double WorldSurface = 0d,
    double RockLayer = 0d,
    int MaxTilesX = 8400,
    int MaxTilesY = 2400)
{
    public bool IsNight => !DayTime;
}

/// <summary>
/// Source-backed vanilla town shop inventory for ordinary vendor branches 1..18 in TerrariaServer 1.4.5.8
/// Chest.SetupShop, from Merchant through Stylist. Traveling/Skeleton Merchant and the newer special-currency/
/// secondary shop branches remain separate gates because their slot/currency semantics need richer contracts.
/// Prices/happiness are deliberately not folded into inventory membership; ShopHelper parity is a separate gate.
/// </summary>
public static class VanillaTownShopCatalog1458
{
    public const int MaximumVanillaShopSlots = 40;

    public static bool TryResolve(
        NpcTypeId npcType,
        in VanillaTownShopContext context,
        ReadOnlySpan<ItemTypeId> playerInventory,
        out ItemTypeId[] items)
    {
        var result = new List<ItemTypeId>(MaximumVanillaShopSlots);
        if (npcType == VanillaNpcIds.Merchant)
            ResolveMerchant(in context, playerInventory, result);
        else if (npcType == VanillaNpcIds.ArmsDealer)
            ResolveArmsDealer(in context, playerInventory, result);
        else if (npcType == VanillaNpcIds.Dryad)
            ResolveDryad(in context, result);
        else if (npcType == VanillaNpcIds.Demolitionist)
            ResolveDemolitionist(in context, playerInventory, result);
        else if (npcType == VanillaNpcIds.Clothier)
            ResolveClothier(in context, result);
        else if (npcType == VanillaNpcIds.GoblinTinkerer)
            ResolveGoblinTinkerer(result);
        else if (npcType == VanillaNpcIds.Wizard)
            ResolveWizard(in context, result);
        else if (npcType == VanillaNpcIds.Mechanic)
            ResolveMechanic(in context, result);
        else if (npcType == VanillaNpcIds.SantaClaus)
            ResolveSantaClaus(result);
        else if (npcType == VanillaNpcIds.Truffle)
            ResolveTruffle(in context, result);
        else if (npcType == VanillaNpcIds.Steampunker)
            ResolveSteampunker(in context, result);
        else if (npcType == VanillaNpcIds.DyeTrader)
            ResolveDyeTrader(in context, result);
        else if (npcType == VanillaNpcIds.PartyGirl)
            ResolvePartyGirl(in context, playerInventory, result);
        else if (npcType == VanillaNpcIds.Cyborg)
            ResolveCyborg(in context, playerInventory, result);
        else if (npcType == VanillaNpcIds.Painter)
            ResolvePainter(in context, result);
        else if (npcType == VanillaNpcIds.WitchDoctor)
            ResolveWitchDoctor(in context, playerInventory, result);
        else if (npcType == VanillaNpcIds.Pirate)
            ResolvePirate(in context, result);
        else if (npcType == VanillaNpcIds.Stylist)
            ResolveStylist(in context, result);
        else
        {
            items = [];
            return false;
        }

        AppendPylons(in context, result);

        if (result.Count > MaximumVanillaShopSlots)
            throw new InvalidOperationException($"Vanilla shop resolved {result.Count} slots, exceeding {MaximumVanillaShopSlots}.");

        items = result.ToArray();
        return true;
    }

    private static void ResolveMerchant(
        in VanillaTownShopContext context,
        ReadOnlySpan<ItemTypeId> inventory,
        List<ItemTypeId> items)
    {
        Add(items, 88, 87, 35, 1991, 3509, 3506, 8);
        if (context.NotTheBeesWorld && !context.RemixWorld)
            Add(items, 4388);
        Add(items, 28);
        if (context.HardMode)
            Add(items, 188);
        Add(items, 110);
        if (context.HardMode)
            Add(items, 189);
        Add(items, 40, 42, 965);
        if (context.ZoneSnow)
            Add(items, 967);
        if (context.ZoneJungle || (context.TenthAnniversaryWorld && context.NotTheBeesWorld && !context.RemixWorld))
            Add(items, 33);
        if (context.DayTime && context.HappyWindyDay)
            Add(items, 4074);
        if (context.BloodMoon)
            Add(items, 279);
        if (context.IsNight)
            Add(items, 282);
        if (context.PartyIsUp)
            Add(items, 5643);
        if (context.DownedBoss3)
            Add(items, 346);
        if (context.HardMode)
            Add(items, 488);
        if (Contains(inventory, 930))
            Add(items, 931, 1614);
        Add(items, 1786);
        if (context.HardMode)
            Add(items, 1348, 3198);
        if (context.DownedBoss2 || context.DownedBoss3 || context.HardMode)
            Add(items, 4063, 4673);
        if (Contains(inventory, 3107))
            Add(items, 3108);
    }

    private static void ResolveArmsDealer(
        in VanillaTownShopContext context,
        ReadOnlySpan<ItemTypeId> inventory,
        List<ItemTypeId> items)
    {
        Add(items, 97);
        if (context.BloodMoon || context.HardMode)
            Add(items, context.SavedSilverOreType == 168 ? 4915 : 278);
        if ((context.DownedBoss2 && context.IsNight) || context.HardMode)
            Add(items, 47);
        Add(items, 95, 98);
        if (context.ZoneGraveyard && context.DownedBoss3)
            Add(items, 4703);
        if (context.IsNight)
            Add(items, 324);
        if (context.HardMode)
            Add(items, 534, 1432, 2177);
        if (Contains(inventory, 1258)) Add(items, 1261);
        if (Contains(inventory, 1835)) Add(items, 1836);
        if (Contains(inventory, 3107)) Add(items, 3108);
        if (Contains(inventory, 1782)) Add(items, 1783);
        if (Contains(inventory, 1784)) Add(items, 1785);
        if (context.Halloween)
            Add(items, 1736, 1737, 1738);
    }

    private static void ResolveDryad(in VanillaTownShopContext context, List<ItemTypeId> items)
    {
        if (context.BloodMoon)
        {
            if (context.Crimson)
            {
                if (!context.RemixWorld || (context.TenthAnniversaryWorld && !context.GoodWorld)) Add(items, 2886);
                Add(items, 2171, 4508);
            }
            else
            {
                if (!context.RemixWorld || (context.TenthAnniversaryWorld && !context.GoodWorld)) Add(items, 67);
                Add(items, 59, 4504);
            }
        }
        else
        {
            if (!context.RemixWorld || context.InfectedSeed || (context.TenthAnniversaryWorld && !context.GoodWorld)) Add(items, 66);
            Add(items, 62, 63, 745);
        }

        if (context.HardMode && context.ZoneGraveyard)
            Add(items, context.Crimson ? 59 : 2171);

        Add(items, 27, 5309, 114, 1828, 747);
        if (context.HardMode)
            Add(items, 746, 369, 4505);
        if (context.ZoneUnderworld)
            Add(items, 5214);
        else if (context.ZoneGlowshroom)
            Add(items, 194);
        if (context.Halloween)
            Add(items, 1853, 1854);

        Add(items, 3215, 3216, 3219, context.Crimson ? 3218 : 3217, 3220, 3221, 3222,
            4047, 4045, 4044, 4043, 4042, 4046, 4041, 4241, 4048);

        int phase = Math.Clamp(context.MoonPhase, 0, 7) / 2;
        int[] moonItems = phase switch
        {
            0 => context.HardMode ? [4430, 4431, 4432] : [4430, 4431],
            1 => context.HardMode ? [4433, 4434, 4435] : [4433, 4434],
            2 => context.HardMode ? [4436, 4437, 4438] : [4436, 4437],
            _ => context.HardMode ? [4439, 4440, 4441] : [4439, 4440]
        };
        Add(items, moonItems);

        if (!context.HardMode && context.VampireSeed && context.InfectedSeed)
            Add(items, 8, context.Crimson ? 4386 : 4385);
    }

    private static void ResolveDemolitionist(
        in VanillaTownShopContext context,
        ReadOnlySpan<ItemTypeId> inventory,
        List<ItemTypeId> items)
    {
        Add(items, 168, 166);
        if ((context.DownedBoss1 || context.DownedSlimeKing) && context.IsNight)
            Add(items, 5542);
        Add(items, 167);
        if (context.HardMode)
            Add(items, 265);
        Add(items, 5481, 5464);
        if (context.HardMode && context.DownedPlantera && context.DownedPirates)
            Add(items, 937);
        if (context.HardMode)
            Add(items, 1347);
        if (Contains(inventory, 4827)) Add(items, 4827);
        if (Contains(inventory, 4824)) Add(items, 4824);
        if (Contains(inventory, 4825)) Add(items, 4825);
        if (Contains(inventory, 4826)) Add(items, 4826);
    }


    private static void ResolveClothier(in VanillaTownShopContext context, List<ItemTypeId> items)
    {
        Add(items, 254, 981);
        if (context.DayTime) Add(items, 242);
        if (context.MoonPhase == 0)
        {
            Add(items, 245, 246);
            if (context.IsNight) Add(items, 1288, 1289);
        }
        else if (context.MoonPhase == 1)
        {
            Add(items, 325, 326);
        }

        Add(items, 269, 270, 271);
        if (context.DownedClown) Add(items, 503, 504, 505);
        if (context.BloodMoon)
        {
            Add(items, 322);
            if (context.IsNight) Add(items, 3362, 3363);
        }
        if (context.DownedAncientCultist)
            Add(items, context.DayTime ? 2856 : 2857, context.DayTime ? 2858 : 2859);
        if (context.HasTaxCollector) Add(items, 3242, 3243, 3244);
        if (context.ZoneGraveyard) Add(items, 4685, 4686, 4704, 4705, 4706, 4707, 4708, 4709);
        if (context.ZoneSnow) Add(items, 1429);
        if (context.Halloween) Add(items, 1740);
        if (context.HardMode)
        {
            switch (Math.Clamp(context.MoonPhase, 0, 7))
            {
                case 2: Add(items, 869); break;
                case 3: Add(items, 4994, 4997); break;
                case 4: Add(items, 864, 865); break;
                case 5: Add(items, 4995, 4998); break;
                case 6: Add(items, 873, 874, 875); break;
                case 7: Add(items, 4996, 4999); break;
            }
        }
        if (context.DownedFrost) Add(items, context.DayTime ? 1275 : 1276);
        if (context.Halloween) Add(items, 3246, 3247);
        if (context.PartyIsUp) Add(items, 3730, 3731, 3733, 3734, 3735);
        if (items.Count < 38 && context.GolferScore >= 2000) Add(items, 4744);
        Add(items, 5308);
        if (items.Count < 38) Add(items, 5630);
    }

    private static void ResolveGoblinTinkerer(List<ItemTypeId> items) =>
        Add(items, 128, 486, 398, 84, 407, 161, 5324);

    private static void ResolveWizard(in VanillaTownShopContext context, List<ItemTypeId> items)
    {
        Add(items, 487, 496, 500, 507, 508, 531, 149, 576, 3186);
        if (context.HardMode && context.BloodMoon) Add(items, 5461);
        if (context.Halloween) Add(items, 1739);
    }

    private static void ResolveMechanic(in VanillaTownShopContext context, List<ItemTypeId> items)
    {
        Add(items, 509, 850, 851, 3612, 510, 530, 513, 538, 529, 541, 542, 543, 852, 853,
            4261, 3707, 2739, 849, 1263, 3616, 3725, 2799, 3619, 3627, 3629, 585, 584, 583, 4484, 4485);
        if (context.ZoneGraveyard) Add(items, 4409);
        if (context.HasAngler && (Math.Clamp(context.MoonPhase, 0, 7) & 1) != 0) Add(items, 2295);
    }

    private static void ResolveSantaClaus(List<ItemTypeId> items)
    {
        Add(items, 588, 589, 590, 597, 598, 596);
        for (int rawType = 1873; rawType < 1906; rawType++) Add(items, rawType);
    }

    private static void ResolveTruffle(in VanillaTownShopContext context, List<ItemTypeId> items)
    {
        if (context.DownedMechBossAny) Add(items, 756, 787);
        Add(items, 868);
        if (context.DownedPlantera) Add(items, 1551);
        Add(items, 1181, 5231);
        if (!context.RemixWorld || (context.TenthAnniversaryWorld && !context.GoodWorld)) Add(items, 783);
    }

    private static void ResolveSteampunker(in VanillaTownShopContext context, List<ItemTypeId> items)
    {
        bool normalSolutionWorld = !context.RemixWorld || (context.TenthAnniversaryWorld && !context.GoodWorld);
        if (normalSolutionWorld) Add(items, 779);
        if (context.MoonPhase >= 4 && context.HardMode) Add(items, 748);
        else Add(items, 839, 840, 841);
        if (context.DownedGolemBoss) Add(items, 948);
        if (context.HardMode) Add(items, 3623);
        Add(items, 3603, 3604, 3607, 3605, 3606, 3608, 3618, 3602, 3663, 3609, 3610);
        if (context.HardMode || !context.GoodWorld) Add(items, 995);
        if (context.DownedBoss1 && context.DownedBoss2 && context.DownedBoss3) Add(items, 2203);
        Add(items, context.Crimson ? 2193 : 4142);
        if (context.ZoneGraveyard) Add(items, 2192);
        if (context.ZoneJungle) Add(items, 2204);
        if (context.ZoneJungle && context.DownedGolemBoss) Add(items, 2195);
        if (context.ZoneSnow) Add(items, 2198);
        if (context.ZoneSky) Add(items, 2197);
        if (normalSolutionWorld)
        {
            if (context.Eclipse || context.BloodMoon) Add(items, context.Crimson ? 784 : 782);
            else if (context.ZoneHallow) Add(items, 781);
            else Add(items, 780);
            if (context.DownedMoonLord) Add(items, 5392, 5393, 5394);
        }
        if (context.HardMode) Add(items, 1344, 4472);
        if (context.Halloween) Add(items, 1742);
    }

    private static void ResolveDyeTrader(in VanillaTownShopContext context, List<ItemTypeId> items)
    {
        Add(items, 1120, 5920);
        if (context.Halloween) Add(items, 3248, 1741);
        Add(items, 1037, 2874);
        if (context.MultiplayerClient) Add(items, 1969);
        if (context.MoonPhase == 0) Add(items, 2871, 2872);
        if (context.IsNight && context.BloodMoon) Add(items, 4663);
        if (context.ZoneGraveyard) Add(items, 4662);
    }

    private static void ResolvePartyGirl(
        in VanillaTownShopContext context,
        ReadOnlySpan<ItemTypeId> inventory,
        List<ItemTypeId> items)
    {
        Add(items, 859);
        if (context.GolferScore >= 500) Add(items, 4743);
        Add(items, 1000, 1168, context.DayTime ? 1449 : 4552, 1345, 1450, 3253, 4553, 2700, 2738, 4470, 4681);
        if (context.ZoneGraveyard) Add(items, 4682);
        if (context.LanternsUp) Add(items, 4702);
        if (Contains(inventory, 3548)) Add(items, 3548);
        if (context.HasPirate) Add(items, 3369);
        if (context.DownedGolemBoss) Add(items, 3546);
        if (context.HardMode) Add(items, 3214, 2868, 970, 971, 972, 973);
        Add(items, 4791, 3747, 3732, 3742);
        if (context.PartyIsUp) Add(items, 3749, 3746, 3739, 3740, 3741, 3737, 3738, 3736, 3745, 3744, 3743);
    }

    private static void ResolveCyborg(
        in VanillaTownShopContext context,
        ReadOnlySpan<ItemTypeId> inventory,
        List<ItemTypeId> items)
    {
        Add(items, 771);
        if (context.BloodMoon) Add(items, 772);
        if (context.IsNight || context.Eclipse) Add(items, 773);
        if (context.Eclipse) Add(items, 774);
        if (context.DownedMartians)
        {
            Add(items, 4445);
            if (context.BloodMoon || context.Eclipse) Add(items, 4446);
        }
        if (context.HardMode) Add(items, 4459, 760, 1346, 5452, 5451, 5738);
        Add(items, 5598, 5599);
        if (context.ZoneGraveyard) Add(items, 4409, 4392);
        if (context.Halloween) Add(items, 1743, 1744, 1745);
        if (context.DownedMartians) Add(items, 2862, 3109);
        if (Contains(inventory, 3384) || Contains(inventory, 3664)) Add(items, 3664);
        Add(items, 5928);
    }

    private static void ResolvePainter(in VanillaTownShopContext context, List<ItemTypeId> items)
    {
        Add(items, 1071, 1072, 1100);
        for (int rawType = 1073; rawType <= 1084; rawType++) Add(items, rawType);
        Add(items, 1097, 1099, 1098, 1966);
        if (context.HardMode) Add(items, 1967, 1968);
        if (context.ZoneGraveyard)
        {
            Add(items, 4668);
            if (context.DownedPlantera || context.HasMechanic) Add(items, 5344);
        }
    }

    private static void ResolveWitchDoctor(
        in VanillaTownShopContext context,
        ReadOnlySpan<ItemTypeId> inventory,
        List<ItemTypeId> items)
    {
        Add(items, 1430, 986);
        if (context.HasWizard) Add(items, 2999);
        if (context.ZoneJungle) Add(items, 6147);
        if (context.IsNight) Add(items, 1158);
        if (context.HardMode && context.DownedPlantera)
        {
            Add(items, 1159, 1160, 1161);
            if (context.ZoneJungle) Add(items, 1167);
            Add(items, 1339);
        }
        if (context.HardMode && context.ZoneJungle)
        {
            Add(items, 1171);
            if (context.IsNight && context.DownedPlantera) Add(items, 1162);
        }
        Add(items, 909, 910, 940, 941, 942, 943, 944, 945, 4922, 4417);
        if (Contains(inventory, 1835)) Add(items, 1836);
        if (Contains(inventory, 1258)) Add(items, 1261);
        if (context.Halloween) Add(items, 1791);
    }

    private static void ResolvePirate(in VanillaTownShopContext context, List<ItemTypeId> items)
    {
        Add(items, 928, 929, 876, 877, 878, 2434);
        if (context.ZoneGraveyard) Add(items, 5926);
        if (context.ZoneBeach) Add(items, 1180);
        if (context.HardMode && context.DownedMechBossAny && context.HasPartyGirl) Add(items, 1337);
    }

    private static void ResolveStylist(in VanillaTownShopContext context, List<ItemTypeId> items)
    {
        Add(items, 1990, 1979);
        if (context.PlayerLifeMax >= 400) Add(items, 1977);
        if (context.PlayerManaMax >= 200) Add(items, 1978);
        if (Math.Clamp(context.PlayerCoinValueCopper, 0L, 9_999_999_999L) >= 1_000_000L) Add(items, 1980);
        int moonPhase = Math.Clamp(context.MoonPhase, 0, 7);
        if (((moonPhase & 1) == 0 && context.DayTime) || ((moonPhase & 1) != 0 && context.IsNight)) Add(items, 1981);
        if (context.PlayerTeam != 0 && context.MultiplayerClient) Add(items, 1982);
        if (context.HardMode) Add(items, 1983);
        if (context.HasPartyGirl) Add(items, 1984);
        if (context.HardMode && context.DownedMechBoss1 && context.DownedMechBoss2 && context.DownedMechBoss3) Add(items, 1985);
        if (context.HardMode && context.DownedMechBossAny) Add(items, 1986);
        if (context.HardMode && context.DownedMartians) Add(items, 2863, 3259);
        Add(items, 5104);
        if (context.ZoneGraveyard) Add(items, 5577);
    }

    private static bool Contains(ReadOnlySpan<ItemTypeId> inventory, int rawType)
    {
        foreach (ItemTypeId item in inventory)
        {
            if (item.Value == rawType)
                return true;
        }
        return false;
    }

    internal static void AppendPylons(in VanillaTownShopContext context, List<ItemTypeId> items)
    {
        if (!context.HasEnoughTownNpcsForPylon || context.ZoneCorrupt || context.ZoneCrimson)
            return;

        static void AddIfRoom(List<ItemTypeId> target, int item)
        {
            if (target.Count < MaximumVanillaShopSlots - 1)
                target.Add(new ItemTypeId(item));
        }

        bool neutralSurface = !context.ZoneSnow && !context.ZoneDesert && !context.ZoneBeach &&
                              !context.ZoneJungle && !context.ZoneHallow && !context.ZoneGlowshroom;
        if (neutralSurface)
        {
            if (context.RemixWorld)
            {
                if (context.PlayerTileY > context.RockLayer && context.PlayerTileY < context.MaxTilesY - 350)
                    AddIfRoom(items, 4876);
            }
            else if (!context.ShoppingZoneBelowSurface)
            {
                AddIfRoom(items, 4876);
            }
        }

        if (context.ZoneSnow) AddIfRoom(items, 4920);
        if (context.ZoneDesert) AddIfRoom(items, 4919);
        if (context.ZoneUnderworld)
        {
            AddIfRoom(items, 5652);
        }
        else if (context.RemixWorld)
        {
            if (!context.ZoneSnow && !context.ZoneDesert && !context.ZoneBeach && !context.ZoneJungle &&
                !context.ZoneHallow && context.ShoppingZoneBelowSurface)
                AddIfRoom(items, 4917);
        }
        else if (!context.ZoneSnow && !context.ZoneDesert && !context.ZoneBeach && !context.ZoneJungle &&
                 !context.ZoneHallow && !context.ZoneGlowshroom && context.ShoppingZoneBelowSurface)
        {
            AddIfRoom(items, 4917);
        }

        bool ocean = context.ZoneBeach && context.PlayerTileY < context.WorldSurface;
        if (context.RemixWorld)
        {
            ocean |= (context.PlayerTileX < context.MaxTilesX * 0.43f || context.PlayerTileX > context.MaxTilesX * 0.57f) &&
                     context.PlayerTileY > context.RockLayer && context.PlayerTileY < context.MaxTilesY - 350;
        }
        if (ocean) AddIfRoom(items, 4918);
        if (context.ZoneJungle) AddIfRoom(items, 4875);
        if (context.ZoneHallow) AddIfRoom(items, 4916);
        if (context.ZoneGlowshroom && (!context.RemixWorld || !context.ZoneUnderworld)) AddIfRoom(items, 4921);
    }

    private static void Add(List<ItemTypeId> items, params ReadOnlySpan<int> rawTypes)
    {
        foreach (int rawType in rawTypes)
        {
            if (!VanillaItemIds.TryCreate(rawType, out ItemTypeId item))
                throw new InvalidOperationException($"Pinned shop item id {rawType} is outside Terraria 1.4.5.8 ItemID.Count.");
            items.Add(item);
        }
    }
}
