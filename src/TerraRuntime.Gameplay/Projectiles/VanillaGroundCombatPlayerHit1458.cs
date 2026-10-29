using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>Projectile.Damage_EVP / StatusPlayer, TerrariaServer 1.4.5.8.</summary>
public static class VanillaGroundCombatPlayerHit1458
{
    public static bool IsSupported(ProjectileTypeId type) => type.Value is 81 or 435 or 436 or 437 or 438 or 471 or 508 or 592 or 909;

    public static bool ConsumesPlayerPenetration(ProjectileTypeId type) => type.Value is 435 or 436 or 437;

    // Hurt evaluates the random velocity argument before dedicated-server Dust returns.
    // The source loop compares its integral index with this double-valued percentage.
    public static int SurvivingBloodSamples(int damage, int maximumLife) =>
        checked((int)Math.Ceiling(damage / (double)maximumLife * 100.0));
}
