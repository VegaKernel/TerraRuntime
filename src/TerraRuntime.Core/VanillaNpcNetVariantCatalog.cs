using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

/// <summary>
/// One source-backed negative NPC net identity whose gameplay type is shared with another NPC.
/// Values are the classic-world result of TerrariaServer 1.4.5.8 NPC.SetDefaultsFromNetId.
/// </summary>
public readonly record struct VanillaNpcNetVariantDefinition(
    NpcNetId NetId,
    NpcTypeId Type,
    string Name,
    float Scale,
    int Damage,
    int Defense,
    int LifeMax,
    float KnockBackResist)
{
    public bool IsValid =>
        NetId.Value < 0 &&
        Type.IsAssigned &&
        !string.IsNullOrWhiteSpace(Name) &&
        float.IsFinite(Scale) &&
        Scale > 0f &&
        Damage >= 0 &&
        Defense >= 0 &&
        LifeMax > 0 &&
        float.IsFinite(KnockBackResist) &&
        KnockBackResist >= 0f;

    public VanillaNpcDefinition ApplyTo(in VanillaNpcDefinition baseDefinition) =>
        baseDefinition with
        {
            Scale = Scale,
            Damage = Damage,
            Defense = Defense,
            LifeMax = LifeMax,
            KnockBackResist = KnockBackResist
        };
}

/// <summary>
/// Version-pinned admitted net variants from NPCID.NetIdMap and NPC.SetDefaultsFromNetId.
/// </summary>
public static class VanillaNpcNetVariantCatalog
{
    public static readonly NpcNetId Slimeling = new(-1);
    public static readonly NpcNetId Slimer2 = new(-2);
    public static readonly NpcNetId GreenSlime = new(-3);
    public static readonly NpcNetId Pinky = new(-4);
    public static readonly NpcNetId BabySlime = new(-5);
    public static readonly NpcNetId BlackSlime = new(-6);
    public static readonly NpcNetId PurpleSlime = new(-7);
    public static readonly NpcNetId RedSlime = new(-8);
    public static readonly NpcNetId YellowSlime = new(-9);
    public static readonly NpcNetId JungleSlime = new(-10);
    public static readonly NpcNetId LittleEater = new(-11);
    public static readonly NpcNetId BigEater = new(-12);
    public static readonly NpcNetId LittleStinger = new(-16);
    public static readonly NpcNetId BigStinger = new(-17);
    public static readonly NpcNetId TinyMossHornet = new(-18);
    public static readonly NpcNetId LittleMossHornet = new(-19);
    public static readonly NpcNetId BigMossHornet = new(-20);
    public static readonly NpcNetId GiantMossHornet = new(-21);
    public static readonly NpcNetId LittleCrimera = new(-22);
    public static readonly NpcNetId BigCrimera = new(-23);
    public static readonly NpcNetId CataractEye2 = new(-38);
    public static readonly NpcNetId SleepyEye2 = new(-39);
    public static readonly NpcNetId DilatedEye2 = new(-40);
    public static readonly NpcNetId GreenEye2 = new(-41);
    public static readonly NpcNetId PurpleEye2 = new(-42);
    public static readonly NpcNetId DemonEye2 = new(-43);
    public static readonly NpcNetId LittleHornetFatty = new(-56);
    public static readonly NpcNetId BigHornetFatty = new(-57);
    public static readonly NpcNetId LittleHornetHoney = new(-58);
    public static readonly NpcNetId BigHornetHoney = new(-59);
    public static readonly NpcNetId LittleHornetLeafy = new(-60);
    public static readonly NpcNetId BigHornetLeafy = new(-61);
    public static readonly NpcNetId LittleHornetSpikey = new(-62);
    public static readonly NpcNetId BigHornetSpikey = new(-63);
    public static readonly NpcNetId LittleHornetStingy = new(-64);
    public static readonly NpcNetId BigHornetStingy = new(-65);

    private static readonly VanillaNpcNetVariantDefinition[] Entries =
    [
        Variant(Slimeling, VanillaNpcIds.CorruptSlime, "Slimeling", 0.6f, 45, 10, 90, 1.2f),
        Variant(Slimer2, VanillaNpcIds.CorruptSlime, "Slimer", 0.9f, 45, 20, 90, 1.2f),
        Variant(GreenSlime, "Green Slime", 0.9f, 6, 0, 14, 1.2f),
        Variant(Pinky, "Pinky", 0.6f, 5, 5, 150, 1.4f),
        Variant(BabySlime, "Baby Slime", 0.9f, 13, 4, 30, 0.95f),
        Variant(BlackSlime, "Black Slime", 1.05f, 15, 4, 45, 1f),
        Variant(PurpleSlime, "Purple Slime", 1.2f, 12, 6, 40, 0.9f),
        Variant(RedSlime, "Red Slime", 1.025f, 12, 4, 35, 1f),
        Variant(YellowSlime, "Yellow Slime", 1.2f, 15, 7, 45, 1f),
        Variant(JungleSlime, "Jungle Slime", 1.1f, 18, 6, 60, 1f),
        ScaledVariant(LittleEater, VanillaNpcIds.EaterOfSouls, "Little Eater", 0.85f, 22, 8, 40, 0.5f),
        ScaledVariant(BigEater, VanillaNpcIds.EaterOfSouls, "Big Eater", 1.15f, 22, 8, 40, 0.5f),
        ScaledVariant(LittleStinger, VanillaNpcIds.Hornet, "Little Hornet", 0.85f, 26, 12, 48, 0.5f),
        ScaledVariant(BigStinger, VanillaNpcIds.Hornet, "Big Hornet", 1.2f, 26, 12, 48, 0.5f),
        ScaledVariant(TinyMossHornet, VanillaNpcIds.MossHornet, "Tiny Moss Hornet", 0.8f, 70, 22, 220, 0.5f),
        ScaledVariant(LittleMossHornet, VanillaNpcIds.MossHornet, "Little Moss Hornet", 0.9f, 70, 22, 220, 0.5f),
        ScaledVariant(BigMossHornet, VanillaNpcIds.MossHornet, "Big Moss Hornet", 1.1f, 70, 22, 220, 0.5f),
        ScaledVariant(GiantMossHornet, VanillaNpcIds.MossHornet, "Giant Moss Hornet", 1.2f, 70, 22, 220, 0.5f),
        ScaledVariant(LittleCrimera, VanillaNpcIds.Crimera, "Little Crimera", 0.85f, 22, 8, 40, 0.5f),
        ScaledVariant(BigCrimera, VanillaNpcIds.Crimera, "Big Crimera", 1.15f, 22, 8, 40, 0.5f),
        Variant(CataractEye2, VanillaNpcIds.CataractEye, "Large Cataract Eye", 1.15f, 20, 4, 74, 0.595f),
        Variant(SleepyEye2, VanillaNpcIds.SleepyEye, "Large Sleepy Eye", 1.1f, 17, 2, 66, 0.765f),
        Variant(DilatedEye2, VanillaNpcIds.DilatedEye, "Small Dilated Eye", 0.9f, 16, 1, 45, 0.88f),
        Variant(GreenEye2, VanillaNpcIds.GreenEye, "Small Green Eye", 0.85f, 17, 0, 51, 0.92f),
        Variant(PurpleEye2, VanillaNpcIds.PurpleEye, "Large Purple Eye", 1.1f, 15, 4, 66, 0.72f),
        Variant(DemonEye2, VanillaNpcIds.DemonEye, "Large Demon Eye", 1.15f, 20, 2, 69, 0.68f),
        ScaledVariant(LittleHornetFatty, VanillaNpcIds.FattyHornet, "Little Fatty Hornet", 0.85f, 22, 16, 50, 0.3f),
        ScaledVariant(BigHornetFatty, VanillaNpcIds.FattyHornet, "Big Fatty Hornet", 1.25f, 22, 16, 50, 0.3f),
        ScaledVariant(LittleHornetHoney, VanillaNpcIds.HoneyHornet, "Little Honey Hornet", 0.8f, 28, 12, 42, 0.6f),
        ScaledVariant(BigHornetHoney, VanillaNpcIds.HoneyHornet, "Big Honey Hornet", 1.15f, 28, 12, 42, 0.6f),
        ScaledVariant(LittleHornetLeafy, VanillaNpcIds.LeafyHornet, "Little Leafy Hornet", 0.92f, 30, 14, 38, 0.45f),
        ScaledVariant(BigHornetLeafy, VanillaNpcIds.LeafyHornet, "Big Leafy Hornet", 1.1f, 30, 14, 38, 0.45f),
        ScaledVariant(LittleHornetSpikey, VanillaNpcIds.SpikeyHornet, "Little Spikey Hornet", 0.78f, 32, 6, 42, 0.55f),
        ScaledVariant(BigHornetSpikey, VanillaNpcIds.SpikeyHornet, "Big Spikey Hornet", 1.16f, 32, 6, 42, 0.55f),
        ScaledVariant(LittleHornetStingy, VanillaNpcIds.StingyHornet, "Little Stingy Hornet", 0.87f, 34, 4, 38, 0.6f),
        ScaledVariant(BigHornetStingy, VanillaNpcIds.StingyHornet, "Big Stingy Hornet", 1.21f, 34, 4, 38, 0.6f)
    ];

    public static int Count => Entries.Length;

    public static ReadOnlySpan<VanillaNpcNetVariantDefinition> All => Entries;

    public static bool TryGet(NpcNetId netId, out VanillaNpcNetVariantDefinition variant)
    {
        foreach (VanillaNpcNetVariantDefinition candidate in Entries)
        {
            if (candidate.NetId == netId)
            {
                variant = candidate;
                return true;
            }
        }

        variant = default;
        return false;
    }

    private static VanillaNpcNetVariantDefinition Variant(
        NpcNetId netId,
        string name,
        float scale,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist) =>
        Variant(
            netId,
            VanillaNpcIds.BlueSlime,
            name,
            scale,
            damage,
            defense,
            lifeMax,
            knockBackResist);

    private static VanillaNpcNetVariantDefinition Variant(
        NpcNetId netId,
        NpcTypeId type,
        string name,
        float scale,
        int damage,
        int defense,
        int lifeMax,
        float knockBackResist) =>
        new(netId, type, name, scale, damage, defense, lifeMax, knockBackResist);

    private static VanillaNpcNetVariantDefinition ScaledVariant(
        NpcNetId netId,
        NpcTypeId type,
        string name,
        float scale,
        int baseDamage,
        int baseDefense,
        int baseLifeMax,
        float baseKnockBackResist) =>
        Variant(
            netId,
            type,
            name,
            scale,
            (int)(baseDamage * scale),
            (int)(baseDefense * scale),
            (int)(baseLifeMax * scale),
            baseKnockBackResist * (2f - scale));
}
