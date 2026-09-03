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


    /// <summary>SetDefaults damage-class facts for the admitted projectile subset that can carry magmaStone.</summary>
    public static bool IsAdmittedMeleeProjectile(ProjectileTypeId type) =>
        type == VanillaProjectileIds.EnchantedBoomerang ||
        type == VanillaProjectileIds.Waffle ||
        type == VanillaProjectileIds.MeleeBone;

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
