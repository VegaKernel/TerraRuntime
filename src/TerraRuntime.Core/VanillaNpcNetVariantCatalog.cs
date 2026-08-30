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
/// Version-pinned slime net variants from NPCID.NetIdMap and NPC.SetDefaultsFromNetId.
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
        Variant(JungleSlime, "Jungle Slime", 1.1f, 18, 6, 60, 1f)
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
}
