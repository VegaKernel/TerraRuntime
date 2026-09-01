using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Buffs;

/// <summary>Verified BuffID.Sets traits currently needed to classify vanilla 1.4.5.8 buff state.</summary>
public readonly record struct VanillaBuffDefinition(
    BuffTypeId Type,
    bool IsWellFed,
    bool IsFedState,
    bool IsFlaskBuff,
    bool TimeIsExtendedWithGameDifficulty)
{
    public bool IsPresent => Type != VanillaBuffIds.None;
}

/// <summary>
/// Dense identity catalog with selected source-backed BuffID.Sets metadata. Missing traits remain false rather
/// than being inferred from names; behavior support is a separate concern from a valid vanilla identity.
/// </summary>
public static class VanillaBuffDefinitionCatalog
{
    public const int Count = VanillaBuffIds.Count;

    public static bool TryGet(BuffTypeId type, out VanillaBuffDefinition definition)
    {
        if (!VanillaBuffIds.TryCreate(type.Value, out _))
        {
            definition = default;
            return false;
        }

        bool wellFed = IsWellFed(type);
        definition = new VanillaBuffDefinition(
            type,
            wellFed,
            IsFedState(type, wellFed),
            IsFlaskBuff(type),
            TimeIsExtendedWithGameDifficulty(type));
        return true;
    }

    private static bool IsWellFed(BuffTypeId type) =>
        type == VanillaBuffIds.WellFed ||
        type == VanillaBuffIds.WellFed2 ||
        type == VanillaBuffIds.WellFed3;

    private static bool IsFedState(BuffTypeId type, bool wellFed) =>
        wellFed ||
        type == VanillaBuffIds.NeutralHunger ||
        type == VanillaBuffIds.Hunger ||
        type == VanillaBuffIds.Starving;

    private static bool IsFlaskBuff(BuffTypeId type) =>
        type == VanillaBuffIds.WeaponImbueVenom ||
        type == VanillaBuffIds.Midas ||
        type == VanillaBuffIds.WeaponImbueCursedFlames ||
        type == VanillaBuffIds.WeaponImbueFire ||
        type == VanillaBuffIds.WeaponImbueGold ||
        type == VanillaBuffIds.WeaponImbueIchor ||
        type == VanillaBuffIds.WeaponImbueNanites ||
        type == VanillaBuffIds.WeaponImbueConfetti ||
        type == VanillaBuffIds.WeaponImbuePoison;

    private static bool TimeIsExtendedWithGameDifficulty(BuffTypeId type) =>
        type == VanillaBuffIds.Poisoned ||
        type == VanillaBuffIds.Darkness ||
        type == VanillaBuffIds.Cursed ||
        type == VanillaBuffIds.OnFire ||
        type == VanillaBuffIds.OnFire3 ||
        type == VanillaBuffIds.Bleeding ||
        type == VanillaBuffIds.Confused ||
        type == VanillaBuffIds.Slow ||
        type == VanillaBuffIds.Weak ||
        type == VanillaBuffIds.Silenced ||
        type == VanillaBuffIds.BrokenArmor ||
        type == VanillaBuffIds.CursedInferno ||
        type == VanillaBuffIds.Frostburn ||
        type == VanillaBuffIds.Frostburn2 ||
        type == VanillaBuffIds.Chilled ||
        type == VanillaBuffIds.Frozen ||
        type == VanillaBuffIds.Ichor ||
        type == VanillaBuffIds.Venom ||
        type == VanillaBuffIds.Blackout;
}
