using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Gameplay.Npcs;

/// <summary>Detached source facts. Nullable fields distinguish missing ownership from known false/zero.</summary>
public readonly record struct VanillaNpcGlobalLootContext1458(
    NpcTypeId NpcType, float NpcValue, int NpcDamage, int NpcDefense, int NpcLifeMax,
    bool? NpcFriendly, bool NpcBoss, int NpcTarget, float PositionX, float PositionY, int NpcWidth,
    float? Difficulty, int WorldWidth, int WorldHeight, bool HardMode, bool RemixWorld,
    bool IsInSimulation = false)
{
    public bool? Halloween { get; init; }
    public bool? Christmas { get; init; }
    public PlayerZoneSnapshot1458? Zones { get; init; }
    public double? RockLayer { get; init; }
    public double? WorldSurface { get; init; }
    public bool? SkeletronDowned { get; init; }
    public bool? AnyMechDowned { get; init; }

    public bool IsValid => NpcType.IsAssigned && float.IsFinite(NpcValue) &&
        float.IsFinite(PositionX) && float.IsFinite(PositionY) && NpcWidth > 0 && NpcLifeMax >= 0 &&
        (!Difficulty.HasValue || (float.IsFinite(Difficulty.Value) && Difficulty.Value is >= .5f and <= 4f)) && WorldWidth > 0 && WorldHeight > 0 &&
        (!RockLayer.HasValue || double.IsFinite(RockLayer.Value)) &&
        (!WorldSurface.HasValue || double.IsFinite(WorldSurface.Value));
}

/// <summary>The seventeen registered globals after MechBossSpawners and SlimeBody, in source order.</summary>
public enum VanillaNpcGlobalLootRule1458 : byte
{
    HalloweenWeapons, JungleKey, CorruptionKey, CrimsonKey, HallowedKey, FrozenKey, DesertKey,
    GoodieBag, Present, LivingFire, SoulOfLight, SoulOfNight, PirateMap, Cascade, Amarok, Yelets, HelFire
}

public static class VanillaNpcGlobalLoot1458
{
    public const int RuleCount = 17;

    /// <summary>Checks all conditional ownership requirements without consuming RNG.</summary>
    public static bool TryValidateContext(in VanillaNpcGlobalLootContext1458 context)
    {
        for (int i = 0; i < RuleCount; i++)
            if (!TryGetEligibility(i, in context, out _)) return false;
        return true;
    }

    public static bool TryGetEligibility(int index, in VanillaNpcGlobalLootContext1458 c, out bool eligible)
    {
        eligible = false;
        if ((uint)index >= RuleCount || !c.IsValid) return false;
        bool value = c.NpcValue > 0f;
        bool key = value && c.HardMode && !c.IsInSimulation && c.NpcType != VanillaNpcIds.MeteorHead;
        bool holiday = value && c.NpcLifeMax > 1 && c.NpcDamage > 0 && c.NpcFriendly != true &&
            c.NpcType != VanillaNpcIds.Slimer && c.NpcType != VanillaNpcIds.MeteorHead && !c.IsInSimulation;
        bool yoyo = value && c.NpcLifeMax > 5 && c.NpcFriendly != true &&
            c.NpcTarget is >= 0 and < 255 && !c.IsInSimulation;
        switch ((VanillaNpcGlobalLootRule1458)index)
        {
            case VanillaNpcGlobalLootRule1458.HalloweenWeapons:
                if (!value || c.NpcDefense >= 20 || c.IsInSimulation || c.Halloween == false) return true;
                if (c.Difficulty is not { } difficulty) return false;
                if (c.NpcValue >= 500f * MoneyMultiplier(difficulty) ||
                    c.NpcDamage >= 40f * DamageMultiplier(difficulty)) return true;
                return Known(c.Halloween, out eligible);
            case >= VanillaNpcGlobalLootRule1458.JungleKey and <= VanillaNpcGlobalLootRule1458.DesertKey:
                if (!key) return true;
                if (c.Zones is not { } keyZones) return false;
                eligible = (VanillaNpcGlobalLootRule1458)index switch
                {
                    VanillaNpcGlobalLootRule1458.JungleKey => keyZones.Jungle,
                    VanillaNpcGlobalLootRule1458.CorruptionKey => keyZones.Corrupt,
                    VanillaNpcGlobalLootRule1458.CrimsonKey => keyZones.Crimson,
                    VanillaNpcGlobalLootRule1458.HallowedKey => keyZones.Hallow,
                    VanillaNpcGlobalLootRule1458.FrozenKey => keyZones.Snow,
                    _ => keyZones.Desert && !keyZones.Beach
                };
                return true;
            case VanillaNpcGlobalLootRule1458.GoodieBag:
                return !holiday || c.Halloween == false || (c.NpcFriendly.HasValue && Known(c.Halloween, out eligible));
            case VanillaNpcGlobalLootRule1458.Present:
                return !holiday || c.Christmas == false || (c.NpcFriendly.HasValue && Known(c.Christmas, out eligible));
            case VanillaNpcGlobalLootRule1458.LivingFire:
                eligible = value && c.NpcLifeMax > 5 && c.NpcFriendly != true && c.HardMode &&
                    c.PositionY / 16f > c.WorldHeight - 200 && !c.IsInSimulation;
                return !eligible || c.NpcFriendly.HasValue;
            case VanillaNpcGlobalLootRule1458.SoulOfLight:
            case VanillaNpcGlobalLootRule1458.SoulOfNight:
                // These source conditions intentionally omit IsInSimulation.
                if (c.NpcBoss || !c.HardMode || c.NpcLifeMax <= 1 || c.NpcFriendly == true || c.NpcValue < 1f ||
                    c.NpcType == VanillaNpcIds.MeteorHead || c.NpcType == VanillaNpcIds.BlueSlime ||
                    c.NpcType == VanillaNpcIds.EaterOfWorldsHead || c.NpcType == VanillaNpcIds.EaterOfWorldsBody ||
                    c.NpcType == VanillaNpcIds.EaterOfWorldsTail || c.NpcType == VanillaNpcIds.Slimer ||
                    c.NpcType == VanillaNpcIds.SpikedSlime) return true;
                if (!c.NpcFriendly.HasValue) return false;
                if (!c.RemixWorld)
                {
                    if (c.RockLayer is not { } rock) return false;
                    if (c.PositionY <= rock * 16d) return true;
                }
                if (c.Zones is not { } soulZones) return false;
                eligible = index == (int)VanillaNpcGlobalLootRule1458.SoulOfLight
                    ? soulZones.Hallow : soulZones.Corrupt || soulZones.Crimson;
                return true;
            case VanillaNpcGlobalLootRule1458.PirateMap:
                if (!value || !c.HardMode || c.IsInSimulation) return true;
                float centerTileX = (c.PositionX + c.NpcWidth * .5f) / 16f;
                if (centerTileX >= 380 && centerTileX <= c.WorldWidth - 380) return true;
                if (c.WorldSurface is not { } surface) return false;
                eligible = c.PositionY / 16f < surface + 10d;
                return true;
            case VanillaNpcGlobalLootRule1458.Cascade:
                if (!yoyo || c.HardMode || c.PositionY / 16f <= c.WorldHeight - 350) return true;
                return c.NpcFriendly.HasValue && Known(c.SkeletronDowned, out eligible);
            case VanillaNpcGlobalLootRule1458.Amarok:
                if (!yoyo || !c.HardMode) return true;
                if (c.Zones is not { } snowZones) return false;
                eligible = snowZones.Snow;
                return !eligible || c.NpcFriendly.HasValue;
            case VanillaNpcGlobalLootRule1458.Yelets:
                if (!yoyo || !c.HardMode) return true;
                if (c.Zones is not { } jungleZones) return false;
                if (!jungleZones.Jungle) return true;
                return c.NpcFriendly.HasValue && Known(c.AnyMechDowned, out eligible);
            case VanillaNpcGlobalLootRule1458.HelFire:
                if (!yoyo || !c.HardMode) return true;
                if (c.Zones is not { } dungeonZones) return false;
                if (dungeonZones.Dungeon) return true;
                if (c.RockLayer is not { } depth) return false;
                eligible = c.PositionY / 16f > (depth + c.WorldHeight * 2) / 3d;
                return !eligible || c.NpcFriendly.HasValue;
            default: return false;
        }
    }

    /// <summary>Materialize a returned drop before evaluating the next index to preserve source RNG order.</summary>
    public static bool TryEvaluateRule(int index, in VanillaNpcGlobalLootContext1458 context,
        INpcLootRollSource random, out bool dropped, out NpcLootDrop drop)
    {
        ArgumentNullException.ThrowIfNull(random);
        dropped = false; drop = default;
        if (!TryGetEligibility(index, in context, out bool eligible)) return false;
        if (!eligible) return true;
        (ItemTypeId Item, int Chance) rule = (VanillaNpcGlobalLootRule1458)index switch
        {
            VanillaNpcGlobalLootRule1458.HalloweenWeapons => (VanillaGlobalNpcDropItemIds.BloodyMachete, 2000),
            VanillaNpcGlobalLootRule1458.JungleKey => (VanillaGlobalNpcDropItemIds.JungleKey, 2500),
            VanillaNpcGlobalLootRule1458.CorruptionKey => (VanillaGlobalNpcDropItemIds.CorruptionKey, 2500),
            VanillaNpcGlobalLootRule1458.CrimsonKey => (VanillaGlobalNpcDropItemIds.CrimsonKey, 2500),
            VanillaNpcGlobalLootRule1458.HallowedKey => (VanillaGlobalNpcDropItemIds.HallowedKey, 2500),
            VanillaNpcGlobalLootRule1458.FrozenKey => (VanillaGlobalNpcDropItemIds.FrozenKey, 2500),
            VanillaNpcGlobalLootRule1458.DesertKey => (VanillaGlobalNpcDropItemIds.DesertKey, 2500),
            VanillaNpcGlobalLootRule1458.GoodieBag => (VanillaGlobalNpcDropItemIds.GoodieBag, 80),
            VanillaNpcGlobalLootRule1458.Present => (VanillaGlobalNpcDropItemIds.Present, 13),
            VanillaNpcGlobalLootRule1458.LivingFire => (VanillaGlobalNpcDropItemIds.LivingFireBlock, 50),
            VanillaNpcGlobalLootRule1458.SoulOfLight => (VanillaGlobalNpcDropItemIds.SoulOfLight, 5),
            VanillaNpcGlobalLootRule1458.SoulOfNight => (VanillaGlobalNpcDropItemIds.SoulOfNight, 5),
            VanillaNpcGlobalLootRule1458.PirateMap => (VanillaGlobalNpcDropItemIds.PirateMap, 100),
            VanillaNpcGlobalLootRule1458.Cascade => (VanillaGlobalNpcDropItemIds.Cascade, 400),
            VanillaNpcGlobalLootRule1458.Amarok => (VanillaGlobalNpcDropItemIds.Amarok, 300),
            VanillaNpcGlobalLootRule1458.Yelets => (VanillaGlobalNpcDropItemIds.Yelets, 200),
            _ => (VanillaGlobalNpcDropItemIds.HelFire, 400)
        };
        if (random.RollLuck(rule.Chance) != 0)
        {
            if (index != (int)VanillaNpcGlobalLootRule1458.HalloweenWeapons || random.RollLuck(2000) != 0) return true;
            rule.Item = VanillaGlobalNpcDropItemIds.BladedGlove;
        }
        int minimum = index == (int)VanillaNpcGlobalLootRule1458.LivingFire ? 20 : 1;
        int maximum = index == (int)VanillaNpcGlobalLootRule1458.LivingFire ? 50 : 1;
        drop = new(rule.Item, checked((short)random.NextInt32(minimum, maximum + 1)));
        dropped = true;
        return true;
    }

    private static bool Known(bool? fact, out bool value) { value = fact.GetValueOrDefault(); return fact.HasValue; }
    private static float MoneyMultiplier(float difficulty) => difficulty <= 1f ? 1f : difficulty < 2f
        ? 1f + (difficulty - 1f) * 1.5f : difficulty <= 3f ? 2.5f : 2.5f + difficulty - 3f;
    private static float DamageMultiplier(float difficulty) => difficulty <= 3f ? difficulty
        : 3f + (difficulty - 3f) * (5.3333335f - 3f);
}
