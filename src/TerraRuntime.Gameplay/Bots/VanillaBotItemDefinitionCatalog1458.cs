using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Bots;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 item facts used by runtime-owned fake players.
/// This catalog is deliberately tiny. Absence means the bot must not pick up or auto-use the item.
/// Width/height and potion fields come from Item.SetDefaults; healing selection mirrors Player.QuickHeal.
/// </summary>
public enum VanillaBotItemKind : byte
{
    RequiredAmmo = 1,
    HealingPotion = 2,
    UsefulBuffPotion = 3
}

public readonly record struct VanillaBotItemDefinition1458(
    ItemTypeId ItemType,
    VanillaBotItemKind Kind,
    int Width,
    int Height,
    int HealLife,
    BuffTypeId BuffType,
    int BuffTimeTicks)
{
    public bool IsValid =>
        !ItemType.IsNone &&
        Enum.IsDefined(Kind) &&
        Width > 0 &&
        Height > 0 &&
        HealLife >= 0 &&
        BuffTimeTicks >= 0 &&
        (Kind != VanillaBotItemKind.HealingPotion || HealLife > 0) &&
        (Kind != VanillaBotItemKind.UsefulBuffPotion || (BuffType.Value > 0 && BuffTimeTicks > 0));
}

public static class VanillaBotItemDefinitionCatalog1458
{
    // Terraria.Player.defaultItemGrabRange = 42 in 1.4.5.8. Bot presets currently equip no grab-range accessories.
    public const int DefaultItemGrabRangePixels = 42;
    // Terraria.Item.potionDelay = 3600. Supported healing entries below all use the ordinary potion-delay branch.
    public const int OrdinaryHealingPotionDelayTicks = 3_600;
    // Terraria.Item.CommonMaxStack in the pinned 1.4.5.8 source.
    public const int CommonMaxStack = 9_999;

    private static readonly VanillaBotItemDefinition1458 MusketBall = new(
        VanillaItemIds.MusketBall,
        VanillaBotItemKind.RequiredAmmo,
        Width: 8,
        Height: 8,
        HealLife: 0,
        BuffType: VanillaBuffIds.None,
        BuffTimeTicks: 0);

    private static readonly VanillaBotItemDefinition1458 WoodenArrow = new(
        VanillaItemIds.WoodenArrow,
        VanillaBotItemKind.RequiredAmmo,
        Width: 10,
        Height: 28,
        HealLife: 0,
        BuffType: VanillaBuffIds.None,
        BuffTimeTicks: 0);

    private static readonly VanillaBotItemDefinition1458 HealingPotion = new(
        VanillaWallOfFleshItemIds.HealingPotion,
        VanillaBotItemKind.HealingPotion,
        Width: 14,
        Height: 24,
        HealLife: 100,
        BuffType: VanillaBuffIds.None,
        BuffTimeTicks: 0);

    private static readonly VanillaBotItemDefinition1458 GreaterHealingPotion = new(
        VanillaItemIds.GreaterHealingPotion,
        VanillaBotItemKind.HealingPotion,
        Width: 14,
        Height: 24,
        HealLife: 150,
        BuffType: VanillaBuffIds.None,
        BuffTimeTicks: 0);

    private static readonly VanillaBotItemDefinition1458 SuperHealingPotion = new(
        VanillaItemIds.SuperHealingPotion,
        VanillaBotItemKind.HealingPotion,
        Width: 14,
        Height: 24,
        HealLife: 200,
        BuffType: VanillaBuffIds.None,
        BuffTimeTicks: 0);

    private static readonly VanillaBotItemDefinition1458 RegenerationPotion = Buff(
        VanillaItemIds.RegenerationPotion, VanillaBuffIds.Regeneration, 28_800);
    private static readonly VanillaBotItemDefinition1458 SwiftnessPotion = Buff(
        VanillaItemIds.SwiftnessPotion, VanillaBuffIds.Swiftness, 28_800);
    private static readonly VanillaBotItemDefinition1458 IronskinPotion = Buff(
        VanillaItemIds.IronskinPotion, VanillaBuffIds.Ironskin, 28_800);
    private static readonly VanillaBotItemDefinition1458 ArcheryPotion = Buff(
        VanillaItemIds.ArcheryPotion, VanillaBuffIds.Archery, 28_800);
    private static readonly VanillaBotItemDefinition1458 EndurancePotion = Buff(
        VanillaItemIds.EndurancePotion, VanillaBuffIds.Endurance, 14_400);
    private static readonly VanillaBotItemDefinition1458 RagePotion = Buff(
        VanillaItemIds.RagePotion, VanillaBuffIds.Rage, 14_400);
    private static readonly VanillaBotItemDefinition1458 WrathPotion = Buff(
        VanillaItemIds.WrathPotion, VanillaBuffIds.Wrath, 14_400);

    public static bool TryGet(ItemTypeId itemType, out VanillaBotItemDefinition1458 definition)
    {
        if (itemType == VanillaItemIds.WoodenArrow) definition = WoodenArrow;
        else if (itemType == VanillaItemIds.MusketBall) definition = MusketBall;
        else if (itemType == VanillaWallOfFleshItemIds.HealingPotion) definition = HealingPotion;
        else if (itemType == VanillaItemIds.GreaterHealingPotion) definition = GreaterHealingPotion;
        else if (itemType == VanillaItemIds.SuperHealingPotion) definition = SuperHealingPotion;
        else if (itemType == VanillaItemIds.RegenerationPotion) definition = RegenerationPotion;
        else if (itemType == VanillaItemIds.SwiftnessPotion) definition = SwiftnessPotion;
        else if (itemType == VanillaItemIds.IronskinPotion) definition = IronskinPotion;
        else if (itemType == VanillaItemIds.ArcheryPotion) definition = ArcheryPotion;
        else if (itemType == VanillaItemIds.EndurancePotion) definition = EndurancePotion;
        else if (itemType == VanillaItemIds.RagePotion) definition = RagePotion;
        else if (itemType == VanillaItemIds.WrathPotion) definition = WrathPotion;
        else
        {
            definition = default;
            return false;
        }

        return definition.IsValid;
    }


    /// <summary>
    /// Buffs whose outgoing combat effect is reproduced by the bot controller from the pinned Player.UpdateBuffs /
    /// Player.PickAmmo source. Other potion buffs remain catalogued for provenance but are fail-closed for pickup/use.
    /// </summary>
    public static bool IsSupportedCombatBuff(BuffTypeId buffType) =>
        buffType == VanillaBuffIds.Archery || buffType == VanillaBuffIds.Wrath;

    /// <summary>
    /// Vanilla QuickHeal choice for the source-backed healing subset: prefer the closest non-overheal amount;
    /// while every candidate under-heals, prefer the largest heal. Special restoration-potion case 227 is absent.
    /// </summary>
    public static bool IsBetterQuickHealCandidate(
        int missingLife,
        in VanillaBotItemDefinition1458 candidate,
        int currentDifference,
        out int candidateDifference)
    {
        candidateDifference = int.MinValue;
        if (missingLife <= 0 || candidate.Kind != VanillaBotItemKind.HealingPotion || candidate.HealLife <= 0)
            return false;

        candidateDifference = candidate.HealLife - missingLife;
        if (currentDifference < 0)
            return candidateDifference > currentDifference;
        return candidateDifference >= 0 && candidateDifference < currentDifference;
    }

    private static VanillaBotItemDefinition1458 Buff(ItemTypeId itemType, BuffTypeId buffType, int timeTicks) => new(
        itemType,
        VanillaBotItemKind.UsefulBuffPotion,
        Width: 14,
        Height: 24,
        HealLife: 0,
        BuffType: buffType,
        BuffTimeTicks: timeTicks);
}
