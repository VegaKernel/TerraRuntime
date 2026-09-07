using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>
/// TerrariaServer 1.4.5.8 Projectile.StatusPvP projectile-type rules for the currently admitted
/// authoritative projectile slice. Equipment/enchantment-derived PvP status effects are deliberately
/// excluded until their source state is server-owned.
/// </summary>
public static class VanillaProjectilePvpStatusFacts1458
{
    public readonly record struct Rule(BuffTypeId BuffType, int DurationTicks, int ChanceDenominator)
    {
        public bool IsValid =>
            BuffType != VanillaBuffIds.None &&
            DurationTicks > 0 &&
            ChanceDenominator > 0;
    }

    public static bool TryGetTypeSpecificRule(ProjectileTypeId type, out Rule rule)
    {
        if (type == VanillaProjectileIds.FireArrow)
        {
            rule = new Rule(VanillaBuffIds.OnFire, DurationTicks: 180, ChanceDenominator: 3);
            return true;
        }
        if (type == VanillaProjectileIds.Flamelash)
        {
            rule = new Rule(VanillaBuffIds.OnFire, DurationTicks: 240, ChanceDenominator: 2);
            return true;
        }
        if (type == VanillaProjectileIds.PoisonedKnife)
        {
            rule = new Rule(VanillaBuffIds.Poisoned, DurationTicks: 600, ChanceDenominator: 2);
            return true;
        }

        rule = default;
        return false;
    }
}
