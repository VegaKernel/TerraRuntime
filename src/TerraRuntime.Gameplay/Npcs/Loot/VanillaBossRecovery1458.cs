using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>World-ephemeral NPC.EoCKilledToday/WoFKilledToday; reset at dusk and on world replacement.</summary>
public sealed class VanillaBossRecoveryDailyState1458
{
    public bool EyeKilled { get; private set; }
    public bool WallKilled { get; private set; }
    public void Reset() { EyeKilled = false; WallKilled = false; }
    public VanillaBossRecoveryDailyState1458 CreatePreview() => new() { EyeKilled = EyeKilled, WallKilled = WallKilled };
    public void CopyFrom(VanillaBossRecoveryDailyState1458 source)
    { ArgumentNullException.ThrowIfNull(source); EyeKilled = source.EyeKilled; WallKilled = source.WallKilled; }
    public bool Record(NpcTypeId type)
    {
        if (type == VanillaNpcIds.EyeOfCthulhu) EyeKilled = true;
        if (type == VanillaNpcIds.WallOfFlesh) WallKilled = true;
        if (!EyeKilled || !WallKilled) return false;
        Reset();
        return true;
    }
}

public interface IBossRecoveryLootDeliverySink1458
{
    bool CanDeliverWorldItem(ItemTypeId type);
    bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random);
}

/// <summary>NPC.DoDeathEvents_DropBossPotionsAndHearts, called after imported loot and boss-specific death events.</summary>
public static class VanillaBossRecovery1458
{
    public const int MaximumRecoveryDrops = 11; // One potion stack, at most nine hearts, one Badger's Hat.
    public static ItemTypeId Potion(NpcTypeId type)
    {
        if (type == VanillaNpcIds.WallOfFlesh || type == VanillaNpcIds.SkeletronHead || type == VanillaNpcIds.Deerclops)
            return VanillaWallOfFleshItemIds.HealingPotion;
        if (type == VanillaNpcIds.QueenBee) return VanillaBossRecoveryItemIds1458.BottledHoney;
        if (type == VanillaNpcIds.MoonLordCore) return VanillaBossRecoveryItemIds1458.SuperHealingPotion;
        if ((type.Value > VanillaNpcIds.WallOfFlesh.Value && type.Value < VanillaNpcIds.QueenBee.Value) ||
            type == VanillaNpcIds.QueenSlime || type == VanillaNpcIds.EmpressOfLight || type == VanillaNpcIds.Golem ||
            type == VanillaNpcIds.Plantera || type == VanillaNpcIds.DukeFishron || type == VanillaNpcIds.LunaticCultist ||
            type == VanillaNpcIds.MartianSaucerCore)
            return VanillaItemIds.GreaterHealingPotion;
        return VanillaBossRecoveryItemIds1458.LesserHealingPotion;
    }

    public static bool IsAdmittedRoot(NpcTypeId type) => type == VanillaNpcIds.KingSlime || type == VanillaNpcIds.EyeOfCthulhu ||
        type == VanillaNpcIds.SkeletronHead || type == VanillaNpcIds.WallOfFlesh || type == VanillaNpcIds.QueenBee ||
        type == VanillaNpcIds.Deerclops || type == VanillaNpcIds.BrainOfCthulhu || type == VanillaNpcIds.Retinazer ||
        type == VanillaNpcIds.Spazmatism || type == VanillaNpcIds.SkeletronPrime || type == VanillaNpcIds.Destroyer ||
        type == VanillaNpcIds.QueenSlime || type == VanillaNpcIds.Plantera || type == VanillaNpcIds.Golem ||
        type == VanillaNpcIds.DukeFishron || type == VanillaNpcIds.LunaticCultist || type == VanillaNpcIds.EmpressOfLight ||
        type == VanillaNpcIds.MoonLordCore;

    public static bool TryExecute(NpcTypeId type, in NpcLootWorldItemOrigin origin, VanillaBossRecoveryDailyState1458 daily,
        INpcLootRollSource random, IBossRecoveryLootDeliverySink1458 sink)
    {
        ArgumentNullException.ThrowIfNull(daily); ArgumentNullException.ThrowIfNull(random); ArgumentNullException.ThrowIfNull(sink);
        ItemTypeId potion = Potion(type);
        if (!origin.IsValid || !sink.CanDeliverWorldItem(potion) || !sink.CanDeliverWorldItem(VanillaWallOfFleshItemIds.Heart) ||
            !sink.CanDeliverWorldItem(VanillaWallOfFleshItemIds.BadgersHat)) return false;
        var potions = new NpcLootDrop(potion, checked((short)random.NextInt32(5, 16)));
        if (!sink.TryDeliverWorldItem(in origin, in potions, random)) return false;
        int hearts = random.NextInt32(0, 5) + 5;
        for (int index = 0; index < hearts; index++)
        {
            var heart = new NpcLootDrop(VanillaWallOfFleshItemIds.Heart, 1);
            if (!sink.TryDeliverWorldItem(in origin, in heart, random)) return false;
        }
        if (daily.Record(type))
        {
            var hat = new NpcLootDrop(VanillaWallOfFleshItemIds.BadgersHat, 1);
            if (!sink.TryDeliverWorldItem(in origin, in hat, random)) return false;
        }
        return true;
    }
}
