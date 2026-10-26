using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>Source caller/RNG facts for the two WorldGen tree-bomb producers; not general AI16 admission.</summary>
public static class VanillaWorldToolBomb1458
{
    public const int PhysicalBody = 22;
    public const int ExplosionBody = 128;
    public const int FuseThreshold = 3;
    public const int BombDamage = 100;
    public const int HappyBombDamage = 60;
    public const float ExplosionKnockBack = 8f;
    public const int SuppressedTrackedSoundBits = ushort.MaxValue;
    public static bool IsSupported(ProjectileTypeId type) => type.Value is 28 or 75;

    /// <summary>Dedicated AI_016, before motion. Dust allocation returns its sentinel; caller argument draws remain.</summary>
    public static bool TryAdvanceFuseEffects(ProjectileTypeId type, int timeLeft, Func<int, int, int> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (!IsSupported(type) || timeLeft <= 0) return false;
        if (timeLeft <= FuseThreshold) return true;
        if (next(0, 2) == 0)
        { _ = next(0, 5); _ = next(0, 5); _ = next(0, 5); }
        return true;
    }

    /// <summary>Kill_Bombs_DoUsualKillCode invokes four random Gore type arguments even on dedicated server.</summary>
    public static bool TryAdvanceKillEffects(ProjectileTypeId type, Func<int, int, int> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (!IsSupported(type)) return false;
        for (int index = 0; index < 4; index++) _ = next(61, 64);
        return true;
    }
}
