using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

public readonly record struct VanillaProjectilePvpStatusEffect(
    BuffTypeId Buff,
    int DurationTicks)
{
    public bool IsPresent => Buff != VanillaBuffIds.None && DurationTicks > 0;
}


/// <summary>Source-backed PvP-only projectile hit facts from TerrariaServer 1.4.5.8 Projectile.Damage_PVP.</summary>
public static class VanillaProjectilePvpCombatFacts
{
    /// <summary>Projectile.playerImmune[target] written after a collided PvP projectile is processed.</summary>
    public const int PerProjectilePlayerImmunityTicks = 40;

    public static bool IsDamageDodgeable(ProjectileTypeId type, int damage)
    {
        int raw = type.Value;
        return !(((uint)(raw - 871) <= 3u || raw == 919 || (uint)(raw - 923) <= 1u) && damage == 9999);
    }

    /// <summary>
    /// Source-ordered type-specific StatusPvP effects for projectile types already admitted by TerraRuntime.
    /// A false return means the projectile has no type-specific status in the admitted slice; a true return with
    /// effect.IsPresent == false means the source-backed chance was rolled and did not proc. Attacker enchantment
    /// effects are a separate equipment/buff provenance slice and are deliberately not inferred here.
    /// </summary>
    public static bool TryRollAdmittedStatus(
        ProjectileTypeId type,
        Random random,
        out VanillaProjectilePvpStatusEffect effect)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (type == VanillaProjectileIds.FireArrow)
        {
            effect = random.Next(3) == 0
                ? new VanillaProjectilePvpStatusEffect(VanillaBuffIds.OnFire, 180)
                : default;
            return true;
        }

        if (type == VanillaProjectileIds.PoisonedKnife)
        {
            effect = random.Next(2) == 0
                ? new VanillaProjectilePvpStatusEffect(VanillaBuffIds.Poisoned, 600)
                : default;
            return true;
        }

        effect = default;
        return false;
    }



    /// <summary>Player.StatusToPlayerPvP / Projectile.StatusPvP weapon-imbue projection.</summary>
    public static bool TryRollMeleeEnchantStatus(
        byte meleeEnchant,
        Random random,
        out VanillaProjectilePvpStatusEffect effect)
    {
        ArgumentNullException.ThrowIfNull(random);
        effect = meleeEnchant switch
        {
            1 => new VanillaProjectilePvpStatusEffect(VanillaBuffIds.Venom, 60 * random.Next(5, 10)),
            2 => new VanillaProjectilePvpStatusEffect(VanillaBuffIds.CursedInferno, 60 * random.Next(3, 7)),
            3 => new VanillaProjectilePvpStatusEffect(VanillaBuffIds.OnFire, 60 * random.Next(3, 7)),
            5 => new VanillaProjectilePvpStatusEffect(VanillaBuffIds.Ichor, 60 * random.Next(10, 20)),
            6 => new VanillaProjectilePvpStatusEffect(VanillaBuffIds.Confused, 60 * random.Next(1, 4)),
            8 => new VanillaProjectilePvpStatusEffect(VanillaBuffIds.Poisoned, 60 * random.Next(5, 10)),
            _ => default
        };
        // Gold (4) and Confetti (7) have no StatusToPlayerPvP debuff. Confetti's child projectile is a separate
        // spawn-ordering slice and is deliberately not fabricated here.
        return meleeEnchant is >= 1 and <= 8;
    }

    /// <summary>Frost armor set bonus branch shared by direct melee and melee/ranged projectile PvP hits.</summary>
    public static bool TryRollFrostBurnStatus(
        bool frostBurn,
        Random random,
        out VanillaProjectilePvpStatusEffect effect)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!frostBurn)
        {
            effect = default;
            return false;
        }

        effect = new VanillaProjectilePvpStatusEffect(VanillaBuffIds.Frostburn2, 60 * random.Next(1, 8));
        return true;
    }

    /// <summary>ProjectileID.Sets.CanHitPastShimmer in TerrariaServer 1.4.5.8.</summary>
    public static bool CanHitPastShimmer(ProjectileTypeId type) => type.Value is
        605 or 270 or 719 or 961 or 962 or 926 or 922 or 100 or 84 or 83 or 96 or 101 or 102 or
        275 or 276 or 277 or 258 or 259 or 384 or 385 or 386 or 874 or 872 or 873 or 871 or 683 or
        676 or 670 or 675 or 686 or 687 or 467 or 468 or 464 or 465 or 466 or 526 or 456 or 462 or
        455 or 452 or 454 or 949 or 1041 or 1125;

    /// <summary>SetDefaults damage-class facts for the admitted projectile subset.</summary>
    public static bool IsAdmittedMeleeProjectile(ProjectileTypeId type) =>
        type == VanillaProjectileIds.EnchantedBoomerang ||
        type == VanillaProjectileIds.Waffle ||
        type == VanillaProjectileIds.MeleeBone;

    public static bool IsAdmittedRangedProjectile(ProjectileTypeId type) =>
        type == VanillaProjectileIds.WoodenArrowFriendly ||
        type == VanillaProjectileIds.FireArrow ||
        type == VanillaProjectileIds.Shuriken ||
        type == VanillaProjectileIds.UnholyArrow ||
        type == VanillaProjectileIds.JestersArrow ||
        type == VanillaProjectileIds.Bullet ||
        type == VanillaProjectileIds.Bone ||
        type == VanillaProjectileIds.ThrowingKnife ||
        type == VanillaProjectileIds.Seed ||
        type == VanillaProjectileIds.PoisonedKnife ||
        type == VanillaProjectileIds.RottenEgg ||
        type == VanillaProjectileIds.StarAnise ||
        type == VanillaProjectileIds.BoneArrowFromMerchant ||
        type == VanillaProjectileIds.BoneDagger ||
        type == VanillaProjectileIds.BoneShard;

    public static bool CanCarryMeleeEnchantStatus(ProjectileTypeId type) => IsAdmittedMeleeProjectile(type);

    public static bool CanCarryFrostBurnStatus(ProjectileTypeId type) =>
        IsAdmittedMeleeProjectile(type) || IsAdmittedRangedProjectile(type);

    /// <summary>Projectile.StatusPvP magmaStone branch for admitted melee projectiles (noEnchantments=false).</summary>
    public static bool TryRollMagmaStoneStatus(
        ProjectileTypeId type,
        bool magmaStone,
        Random random,
        out VanillaProjectilePvpStatusEffect effect)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (!magmaStone || !IsAdmittedMeleeProjectile(type))
        {
            effect = default;
            return false;
        }

        int duration = random.Next(4) == 0
            ? 360
            : random.Next(2) == 0 ? 240 : 120;
        effect = new VanillaProjectilePvpStatusEffect(VanillaBuffIds.OnFire, duration);
        return true;
    }

    /// <summary>
    /// Type-specific Colliding gates needed by projectile families currently admitted by the runtime. Ordinary
    /// admitted projectiles use their definition AABB; Bone Shard cannot hit until ai[0] reaches 15.
    /// </summary>
    public static bool CanUseDefinitionAabb(ProjectileTypeId type, float ai0) =>
        type != VanillaProjectileIds.BoneShard || ai0 >= 15f;
}
