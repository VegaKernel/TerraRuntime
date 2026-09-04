using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>
/// Source-backed Projectile.PrepareBombToBlow facts for the first vanilla launcher family admitted by the
/// authoritative runtime. TerrariaServer 1.4.5.8 expands 133..138 to a 128x128 damage box with knockback 8,
/// and 139..144 to 200x200 with knockback 10 immediately before Projectile.Damage() runs from Kill().
/// World/tile destruction and self-hurt side effects are intentionally not represented by this damage fact.
/// </summary>
public readonly record struct VanillaProjectileExplosionDefinition(
    int Width,
    int Height,
    float KnockBack);

public static class VanillaProjectileExplosionFacts
{
    private static readonly VanillaProjectileExplosionDefinition SmallLauncherExplosion = new(128, 128, 8f);
    private static readonly VanillaProjectileExplosionDefinition LargeLauncherExplosion = new(200, 200, 10f);

    public static bool TryGetOnKillExplosion(
        ProjectileTypeId type,
        out VanillaProjectileExplosionDefinition definition)
    {
        if (type.Value is >= 133 and <= 138)
        {
            definition = SmallLauncherExplosion;
            return true;
        }

        if (type.Value is >= 139 and <= 144)
        {
            definition = LargeLauncherExplosion;
            return true;
        }

        definition = default;
        return false;
    }
}
