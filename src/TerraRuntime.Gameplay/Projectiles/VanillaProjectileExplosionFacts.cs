using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>
/// Source-backed Projectile.Kill damage-shape facts admitted by the authoritative runtime. TerrariaServer
/// 1.4.5.8 expands launcher explosions, selected Moon Lord projectiles and Cultist fireballs immediately before
/// Projectile.Damage() runs. Presentation, tile destruction and unrelated self-hurt effects are intentionally
/// not represented by this damage fact.
/// </summary>
public readonly record struct VanillaProjectileExplosionDefinition(
    int Width,
    int Height,
    float KnockBack,
    int? DamageOverride = null,
    bool PreserveKnockBack = false);

public static class VanillaProjectileExplosionFacts
{
    private static readonly VanillaProjectileExplosionDefinition SkeletronPrimeBombExplosion = new(128, 128, 8f, 40);
    private static readonly VanillaProjectileExplosionDefinition PhantasmalEyeExplosion = new(144, 144, 0f, PreserveKnockBack: true);
    private static readonly VanillaProjectileExplosionDefinition PhantasmalSphereExplosion = new(208, 208, 0f, PreserveKnockBack: true);
    private static readonly VanillaProjectileExplosionDefinition CultistFireballExplosion = new(176, 176, 0f, PreserveKnockBack: true);
    private static readonly VanillaProjectileExplosionDefinition SmallLauncherExplosion = new(128, 128, 8f);
    private static readonly VanillaProjectileExplosionDefinition LargeLauncherExplosion = new(200, 200, 10f);

    public static bool TryGetOnKillExplosion(
        ProjectileTypeId type,
        out VanillaProjectileExplosionDefinition definition)
    {
        if (type == VanillaProjectileIds.SkeletronPrimeBomb)
        {
            definition = SkeletronPrimeBombExplosion;
            return true;
        }

        if (type == VanillaProjectileIds.PhantasmalEye)
        {
            definition = PhantasmalEyeExplosion;
            return true;
        }

        if (type == VanillaProjectileIds.PhantasmalSphere)
        {
            definition = PhantasmalSphereExplosion;
            return true;
        }

        if (type == VanillaProjectileIds.CultistBossFireBall ||
            type == VanillaProjectileIds.CultistBossFireBallClone)
        {
            definition = CultistFireballExplosion;
            return true;
        }

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
