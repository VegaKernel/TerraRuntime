using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Items;

/// <summary>
/// Combat-relevant Player.UpdateBuffs projection for server-confirmed active buffs in TerrariaServer 1.4.5.8.
/// This catalog never decides buff provenance: callers must only pass buffs whose acquisition and lifetime are
/// authoritative server state. Unsupported combat modifiers fail closed at the grant boundary rather than being
/// inferred from packet 50.
/// </summary>
public static class VanillaPlayerCombatBuffCatalog
{
    public static bool IsSupported(BuffTypeId type) => type.Value is
        5 or 7 or 16 or 20 or 24 or 25 or 26 or 31 or 33 or 36 or 39 or 44 or 59 or 69 or 70 or
        71 or 73 or 74 or 75 or 76 or 77 or 78 or 79 or 112 or 114 or 115 or 117 or 206 or 207 or
        321 or 323 or 324 or 332 or 333 or 334 or 353;

    public static bool TryApply(BuffTypeId type, ref VanillaPlayerCombatSnapshot snapshot)
    {
        switch (type.Value)
        {
            case 5: // Ironskin
                snapshot = snapshot with { Defense = snapshot.Defense + 8 };
                return true;
            case 7: // Magic Power
                snapshot = snapshot with { MagicDamage = snapshot.MagicDamage + 0.2f };
                return true;
            case 16: // Archery
                snapshot = snapshot with
                {
                    Archery = true,
                    ArrowDamage = snapshot.ArrowDamage * 1.1f
                };
                return true;
            case 20: // Poisoned: server-owned DoT state, no direct combat-snapshot modifier.
            case 24: // On Fire!: server-owned DoT state, no direct combat-snapshot modifier.
            case 31: // Confused: movement semantics are tracked separately from damage projection.
            case 39: // Cursed Inferno: server-owned DoT state.
            case 44: // Frostburn: server-owned DoT state.
            case 70: // Venom: server-owned DoT state.
            case 323: // Hellfire/On Fire! 3: server-owned DoT state.
            case 324: // Frostburn2: server-owned DoT state.
                return true;
            case 25: // Tipsy
                snapshot = snapshot with
                {
                    Defense = snapshot.Defense - 4,
                    MeleeCrit = snapshot.MeleeCrit + 2,
                    MeleeDamage = snapshot.MeleeDamage + 0.1f,
                    MeleeAttackSpeed = snapshot.MeleeAttackSpeed + 0.1f
                };
                return true;
            case 26: // Well Fed
                ApplyFed(+2, +0.05f, +0.05f, ref snapshot);
                return true;
            case 206: // Plenty Satisfied / Well Fed 2
                ApplyFed(+3, +0.075f, +0.075f, ref snapshot);
                return true;
            case 207: // Exquisitely Stuffed / Well Fed 3
                ApplyFed(+4, +0.1f, +0.1f, ref snapshot);
                return true;
            case 33: // Weak
                snapshot = snapshot with
                {
                    Defense = snapshot.Defense - 4,
                    MeleeDamage = snapshot.MeleeDamage - 0.051f,
                    MeleeAttackSpeed = snapshot.MeleeAttackSpeed - 0.051f
                };
                return true;
            case 36: // Broken Armor is applied after UpdateBuffs; defer the actual halving to the span overload.
                return true;
            case 59: // Shadow Dodge
                snapshot = snapshot with { ShadowDodge = true };
                return true;
            case 69: // Ichor
                snapshot = snapshot with { Defense = snapshot.Defense - 15 };
                return true;
            case 71: // Weapon Imbue: Venom
                snapshot = snapshot with { MeleeEnchant = 1 };
                return true;
            case 73: // Weapon Imbue: Cursed Flames
                snapshot = snapshot with { MeleeEnchant = 2 };
                return true;
            case 74: // Weapon Imbue: Fire
                snapshot = snapshot with { MeleeEnchant = 3 };
                return true;
            case 75: // Weapon Imbue: Gold
                snapshot = snapshot with { MeleeEnchant = 4 };
                return true;
            case 76: // Weapon Imbue: Ichor
                snapshot = snapshot with { MeleeEnchant = 5 };
                return true;
            case 77: // Weapon Imbue: Nanites
                snapshot = snapshot with { MeleeEnchant = 6 };
                return true;
            case 78: // Weapon Imbue: Confetti
                snapshot = snapshot with { MeleeEnchant = 7 };
                return true;
            case 79: // Weapon Imbue: Poison
                snapshot = snapshot with { MeleeEnchant = 8 };
                return true;
            case 112: // Ammo Reservation
                snapshot = snapshot with { AmmoPotion = true };
                return true;
            case 114: // Endurance
                snapshot = snapshot with { Endurance = snapshot.Endurance + 0.1f };
                return true;
            case 115: // Rage
                snapshot = snapshot with
                {
                    MeleeCrit = snapshot.MeleeCrit + 10,
                    RangedCrit = snapshot.RangedCrit + 10,
                    MagicCrit = snapshot.MagicCrit + 10
                };
                return true;
            case 117: // Wrath
                snapshot = snapshot with
                {
                    MeleeDamage = snapshot.MeleeDamage + 0.1f,
                    RangedDamage = snapshot.RangedDamage + 0.1f,
                    MagicDamage = snapshot.MagicDamage + 0.1f
                };
                return true;
            case 321: // Brain of Confusion dodge cooldown
                snapshot = snapshot with { BrainOfConfusionCooldown = true };
                return true;
            case 332: // Neutral Hunger: replaces fed/hunger states but has no combat modifier.
                return true;
            case 333: // Hunger
                ApplyFed(-2, -0.05f, -0.05f, ref snapshot);
                return true;
            case 334: // Starving
                ApplyFed(-4, -0.1f, -0.1f, ref snapshot);
                return true;
            case 353: // Shimmer: Player.UpdateBuffs sets shimmering=true. Provenance must remain server-owned.
                snapshot = snapshot with { Shimmering = true };
                return true;
            default:
                return false;
        }
    }

    public static bool TryApply(ReadOnlySpan<BuffTypeId> buffs, ref VanillaPlayerCombatSnapshot snapshot)
    {
        bool brokenArmor = false;
        for (int i = 0; i < buffs.Length; i++)
        {
            if (buffs[i] == VanillaBuffIds.BrokenArmor)
                brokenArmor = true;
            if (!TryApply(buffs[i], ref snapshot))
                return false;
        }

        // Player.Update applies Broken Armor after ordinary buff/equipment defense changes. Integer division is
        // intentional and source-matched. Ichor therefore subtracts first and the result is then halved.
        if (brokenArmor)
            snapshot = snapshot with { Defense = snapshot.Defense / 2 };
        return true;
    }

    private static void ApplyFed(
        int defenseAndCrit,
        float damage,
        float meleeSpeed,
        ref VanillaPlayerCombatSnapshot snapshot)
    {
        snapshot = snapshot with
        {
            Defense = snapshot.Defense + defenseAndCrit,
            MeleeCrit = snapshot.MeleeCrit + defenseAndCrit,
            RangedCrit = snapshot.RangedCrit + defenseAndCrit,
            MagicCrit = snapshot.MagicCrit + defenseAndCrit,
            MeleeDamage = snapshot.MeleeDamage + damage,
            RangedDamage = snapshot.RangedDamage + damage,
            MagicDamage = snapshot.MagicDamage + damage,
            MeleeAttackSpeed = snapshot.MeleeAttackSpeed + meleeSpeed
        };
    }
}
