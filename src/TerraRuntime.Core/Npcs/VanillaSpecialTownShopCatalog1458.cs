using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

public enum VanillaTownShopId1458
{
    TravelingMerchant = 19,
    SkeletonMerchant = 20,
    Tavernkeep = 21,
    Golfer = 22,
    Zoologist = 23,
    Princess = 24,
    PainterDecor = 25
}

public enum VanillaTownShopCurrency1458
{
    Coins = 0,
    DefenderMedals = 1
}

public readonly record struct VanillaTownShopEntry1458(
    int Slot,
    ItemTypeId Item,
    int? CustomPrice = null,
    VanillaTownShopCurrency1458 Currency = VanillaTownShopCurrency1458.Coins);

/// <summary>
/// Source-pinned TerrariaServer 1.4.5.8 Chest.SetupShop special branches 19..25.
/// Unlike ordinary shops, this model preserves explicit slots and custom-currency prices.
/// </summary>
public static class VanillaSpecialTownShopCatalog1458
{
    private static readonly int[] TravelingUnlockInventory = [5667, 5663, 5664, 5665, 5666, 6174, 6148, 6149, 6150, 6151];

    public static bool TryResolve(
        VanillaTownShopId1458 shop,
        in VanillaTownShopContext context,
        ReadOnlySpan<ItemTypeId> playerInventory,
        ReadOnlySpan<ItemTypeId> travelingInventory,
        out VanillaTownShopEntry1458[] entries)
    {
        var result = new List<VanillaTownShopEntry1458>(VanillaTownShopCatalog1458.MaximumVanillaShopSlots);
        switch (shop)
        {
            case VanillaTownShopId1458.TravelingMerchant:
                ResolveTravelingMerchant(playerInventory, travelingInventory, result);
                break;
            case VanillaTownShopId1458.SkeletonMerchant:
                ResolveSkeletonMerchant(in context, playerInventory, result);
                break;
            case VanillaTownShopId1458.Tavernkeep:
                ResolveTavernkeep(in context, result);
                break;
            case VanillaTownShopId1458.Golfer:
                ResolveGolfer(in context, result);
                AppendPylons(in context, result);
                break;
            case VanillaTownShopId1458.Zoologist:
                ResolveZoologist(in context, result);
                AppendPylons(in context, result);
                break;
            case VanillaTownShopId1458.Princess:
                ResolvePrincess(in context, result);
                AppendPylons(in context, result);
                break;
            case VanillaTownShopId1458.PainterDecor:
                ResolvePainterDecor(in context, result);
                AppendPylons(in context, result);
                break;
            default:
                entries = [];
                return false;
        }

        entries = result.OrderBy(static entry => entry.Slot).ToArray();
        return true;
    }

    private static void ResolveTravelingMerchant(
        ReadOnlySpan<ItemTypeId> playerInventory,
        ReadOnlySpan<ItemTypeId> travelingInventory,
        List<VanillaTownShopEntry1458> result)
    {
        int slot = 0;
        bool hasUnlock = false;
        foreach (int id in TravelingUnlockInventory)
        {
            if (Contains(playerInventory, id))
            {
                hasUnlock = true;
                break;
            }
        }
        if (hasUnlock)
        {
            Add(result, ref slot, 5735);
            Add(result, ref slot, 5736);
        }

        foreach (ItemTypeId item in travelingInventory)
        {
            if (item.Value != 0)
                Add(result, ref slot, item.Value);
        }
    }

    private static void ResolveSkeletonMerchant(
        in VanillaTownShopContext context,
        ReadOnlySpan<ItemTypeId> inventory,
        List<VanillaTownShopEntry1458> result)
    {
        int slot = 0;
        int phase = Math.Clamp(context.MoonPhase, 0, 7);
        Add(result, ref slot, phase switch
        {
            0 => 284, 1 => 946, 2 => context.RemixWorld ? 517 : 3069, 3 => 4341,
            4 => 285, 5 => 953, 6 => 3068, _ => 3084
        });
        if ((phase & 1) == 0) Add(result, ref slot, 3001);
        else
        {
            Add(result, ref slot, 28);
            if (context.HardMode) Add(result, ref slot, 188);
        }

        if (context.IsNight || phase == 0)
        {
            Add(result, ref slot, 3002);
            if (Contains(inventory, 930)) Add(result, ref slot, 5377);
        }
        else Add(result, ref slot, 282);

        Add(result, ref slot, context.WorldTime % 60d * 60d * 6d <= 10800d ? 3004 : 8);
        Add(result, ref slot, phase is 0 or 1 or 4 or 5 ? 3003 : 40);
        Add(result, ref slot, (phase % 4) switch { 0 => 3310, 1 => 3313, 2 => 3312, _ => 3311 });
        if (phase is 1 or 2) Add(result, ref slot, 5640);
        else if (phase is 3 or 5) Add(result, ref slot, 5641);
        else if (phase is 6 or 7) Add(result, ref slot, 5642);
        Add(result, ref slot, 166);
        Add(result, ref slot, 965);
        if (context.HardMode)
        {
            Add(result, ref slot, 3316);
            Add(result, ref slot, 3334);
            if (context.DownedMechBossAny) Add(result, ref slot, 5540);
            if (context.BloodMoon) Add(result, ref slot, 3258);
        }
        if (phase == 0 && context.IsNight) Add(result, ref slot, 3043);
        if (!context.AteArtisanBread && phase is >= 3 and <= 5) Add(result, ref slot, 5326);
    }

    private static void ResolveTavernkeep(in VanillaTownShopContext context, List<VanillaTownShopEntry1458> result)
    {
        bool mech = context.HardMode && context.DownedMechBossAny;
        bool golem = context.HardMode && context.DownedGolemBoss;
        AddAt(result, 0, 353);
        AddAt(result, 1, 3828, golem ? 40000 : mech ? 10000 : 2500);
        AddAt(result, 2, 3816);
        AddAt(result, 3, 3813, 50, VanillaTownShopCurrency1458.DefenderMedals);
        AddMedalRun(result, 10, 5, [3818, 3824, 3832, 3829]);

        if (mech)
        {
            AddMedalRun(result, 20, 15, [3819, 3825, 3833, 3830]);
            AddMedalRun(result, 4, 15, [3800, 3801, 3802]);
            AddMedalRun(result, 14, 15, [3797, 3798, 3799]);
            AddMedalRun(result, 24, 15, [3803, 3804, 3805]);
            AddMedalRun(result, 34, 15, [3806, 3807, 3808]);
        }
        if (golem)
        {
            AddMedalRun(result, 30, 60, [3820, 3826, 3834, 3831]);
            AddMedalRun(result, 7, 50, [3871, 3872, 3873]);
            AddMedalRun(result, 17, 50, [3874, 3875, 3876]);
            AddMedalRun(result, 27, 50, [3877, 3878, 3879]);
            AddMedalRun(result, 37, 50, [3880, 3881, 3882]);
        }
    }

    private static void ResolveGolfer(in VanillaTownShopContext context, List<VanillaTownShopEntry1458> result)
    {
        int slot = 0;
        Add(result, ref slot, 4587, 4590, 4589, 4588, 4083, 4084, 4085, 4086, 4087, 4088);
        if (context.GolferScore >= 500) Add(result, ref slot, 4039, 4094, 4093, 4092);
        Add(result, ref slot, 4089, 3989, 4095, 4040, 4319, 4320);
        if (context.GolferScore > 1000) Add(result, ref slot, 4591, 4594, 4593, 4592);
        Add(result, ref slot, 4135, 4138, 4136, 4137, 4049);
        if (context.GolferScore > 500) Add(result, ref slot, 4265);
        if (context.GolferScore > 2000)
        {
            Add(result, ref slot, 4595, 4598, 4597, 4596);
            if (context.DownedBoss3) Add(result, ref slot, 4264);
        }
        if (context.GolferScore > 500) Add(result, ref slot, 4599);
        if (context.GolferScore >= 1000) Add(result, ref slot, 4600);
        if (context.GolferScore >= 2000)
        {
            Add(result, ref slot, 4601);
            Add(result, ref slot, context.MoonPhase switch { 0 or 1 => 4658, 2 or 3 => 4659, 4 or 5 => 4660, _ => 4661 });
        }
    }

    private static void ResolveZoologist(in VanillaTownShopContext context, List<VanillaTownShopEntry1458> result)
    {
        int slot = 0;
        float progress = Math.Clamp(context.BestiaryCompletion, 0f, 1f);
        if (context.FairyTorchAvailable) Add(result, ref slot, 4776);
        Add(result, ref slot, 4767);
        if (context.MoonPhase == 0 && context.IsNight) Add(result, ref slot, 5253);
        if (progress >= .45f) Add(result, ref slot, 5635);
        if (progress >= .10f) Add(result, ref slot, 4759);
        if (progress >= .03f) Add(result, ref slot, 4672);
        Add(result, ref slot, 4829);
        if (progress >= .25f) Add(result, ref slot, 4830);
        if (progress >= .45f) Add(result, ref slot, 4910);
        if (progress >= .30f) Add(result, ref slot, 4871, 4907);
        if (context.DownedTowerSolar) Add(result, ref slot, 4677);
        if (progress >= .10f) Add(result, ref slot, 4676);
        if (progress >= .30f) Add(result, ref slot, 4762);
        if (progress >= .25f) Add(result, ref slot, 4716);
        if (progress >= .30f) Add(result, ref slot, 4785, 4786, 4787);
        if (progress >= .30f && context.HardMode) Add(result, ref slot, 4788);
        if (progress >= .25f) Add(result, ref slot, 4763);
        if (progress >= .40f) Add(result, ref slot, 4955);
        if (context.HardMode && context.BloodMoon) Add(result, ref slot, 4736);
        if (context.DownedPlantera) Add(result, ref slot, 4701);
        if (progress >= .50f) Add(result, ref slot, 4765, 4766, 5285, 4777);
        if (progress >= .70f) Add(result, ref slot, 4735);
        if (progress >= 1f) Add(result, ref slot, 4951);
        if (context.PartyIsUp) Add(result, ref slot, 5466);
        Add(result, ref slot, context.MoonPhase switch { 0 or 1 => 4768, 2 or 3 => 4770, 4 or 5 => 4772, _ => 4560 });
        Add(result, ref slot, context.MoonPhase switch { 0 or 1 => 4769, 2 or 3 => 4771, 4 or 5 => 4773, _ => 4775 });
        if (context.VampireSeed && !context.InfectedSeed) Add(result, ref slot, 8);
    }

    private static void ResolvePrincess(in VanillaTownShopContext context, List<VanillaTownShopEntry1458> result)
    {
        int slot = 0;
        Add(result, ref slot, 5071, 5072, 5073, 5076, 5077, 5078, 5079, 5080, 5081, 5082, 5083, 5084, 5085, 5086, 5087, 5310, 5222, 5228);
        if (context.DownedSlimeKing && context.DownedQueenSlime) Add(result, ref slot, 5266);
        if (context.HardMode && context.DownedMoonLord) Add(result, ref slot, 5044);
        if (context.TenthAnniversaryWorld)
        {
            Add(result, ref slot, 1309, 1859, 1358);
            if (context.ZoneDesert) Add(result, ref slot, 857);
            if (context.BloodMoon) Add(result, ref slot, 4144);
            if (context.HardMode && context.DownedPirates)
                Add(result, ref slot, context.MoonPhase switch { 0 or 1 => 2584, 2 or 3 => 854, 4 or 5 => 855, _ => 905 });
        }
        Add(result, ref slot, 5088);
    }

    private static void ResolvePainterDecor(in VanillaTownShopContext context, List<VanillaTownShopEntry1458> result)
    {
        int slot = 0;
        if (context.XMas)
            for (int id = 1948; id <= 1957 && slot < 39; id++) Add(result, ref slot, id);
        for (int id = 2158; id <= 2160 && slot < 39; id++) Add(result, ref slot, id);
        for (int id = 2008; id <= 2014 && slot < 39; id++) Add(result, ref slot, id);
        if (!context.ZoneGraveyard)
        {
            Add(result, ref slot, 1490);
            Add(result, ref slot, context.MoonPhase <= 1 ? 1481 : context.MoonPhase <= 3 ? 1482 : context.MoonPhase <= 5 ? 1483 : 1484);
        }
        if (context.ShoppingZoneForest) Add(result, ref slot, 5245);
        if (context.ZoneCrimson) Add(result, ref slot, 1492);
        if (context.ZoneCorrupt) Add(result, ref slot, 1488);
        if (context.ZoneHallow) Add(result, ref slot, 1489);
        if (context.ZoneJungle) Add(result, ref slot, 1486);
        if (context.ZoneSnow) Add(result, ref slot, 5491, 1487);
        if (context.ZoneDesert) Add(result, ref slot, 1491);
        if (context.BloodMoon) Add(result, ref slot, 1493);
        if (!context.ZoneGraveyard)
        {
            if (context.ZoneSky) Add(result, ref slot, 1485);
            if (context.ZoneSky && context.HardMode) Add(result, ref slot, 1494);
        }
        if (context.Storming) Add(result, ref slot, 5251);
        if (context.ZoneGraveyard) Add(result, ref slot, 4723, 4724, 4725, 4726, 4727, 5257, 4728, 4729);
    }

    private static void AppendPylons(in VanillaTownShopContext context, List<VanillaTownShopEntry1458> result)
    {
        if (!context.HasEnoughTownNpcsForPylon || context.ZoneCorrupt || context.ZoneCrimson)
            return;
        var items = result.OrderBy(static e => e.Slot).Select(static e => e.Item).ToList();
        int before = items.Count;
        VanillaTownShopCatalog1458.AppendPylons(in context, items);
        int slot = result.Count == 0 ? 0 : result.Max(static e => e.Slot) + 1;
        foreach (ItemTypeId item in items.Skip(before))
            Add(result, ref slot, item.Value);
    }

    private static void AddMedalRun(List<VanillaTownShopEntry1458> result, int firstSlot, int price, ReadOnlySpan<int> items)
    {
        for (int i = 0; i < items.Length; i++)
            AddAt(result, firstSlot + i, items[i], price, VanillaTownShopCurrency1458.DefenderMedals);
    }

    private static void AddAt(
        List<VanillaTownShopEntry1458> result,
        int slot,
        int item,
        int? customPrice = null,
        VanillaTownShopCurrency1458 currency = VanillaTownShopCurrency1458.Coins)
    {
        if ((uint)slot >= VanillaTownShopCatalog1458.MaximumVanillaShopSlots)
            return;
        result.Add(new VanillaTownShopEntry1458(slot, new ItemTypeId(item), customPrice, currency));
    }

    private static void Add(List<VanillaTownShopEntry1458> result, ref int slot, params int[] items)
    {
        foreach (int item in items)
        {
            if (slot >= VanillaTownShopCatalog1458.MaximumVanillaShopSlots)
                return;
            AddAt(result, slot++, item);
        }
    }

    private static bool Contains(ReadOnlySpan<ItemTypeId> inventory, int item)
    {
        foreach (ItemTypeId candidate in inventory)
            if (candidate.Value == item) return true;
        return false;
    }
}
