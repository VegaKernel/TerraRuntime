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

    // TerrariaServer 1.4.5.8 Main.debuff after Initialize_TileAndNPCData1/2.
    // Player.AddBuff preserves these slots when a full buff array needs space.
    public static bool IsDebuff(BuffTypeId type) => type.Value is
        20 or 21 or 22 or 23 or 24 or 25 or 28 or 30 or 31 or 32 or 33 or 34 or 35 or 36 or 37 or
        38 or 39 or 43 or 44 or 46 or 47 or 67 or 68 or 69 or 70 or 72 or 80 or 86 or 87 or 88 or
        89 or 94 or 103 or 119 or 120 or 137 or 144 or 145 or 146 or 147 or 148 or 149 or 153 or
        156 or 157 or 158 or 160 or 163 or 164 or 169 or 183 or 186 or 189 or 194 or 195 or 196 or
        197 or 199 or 203 or 204 or 215 or 320 or 321 or 323 or 324 or 332 or 333 or 334 or 344 or
        350 or 353 or 395 or 397 or 398 or 399 or 400;

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
