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
    private static readonly VanillaProjectileExplosionDefinition BombExplosion = new(128, 128, 8f, 100);
    private static readonly VanillaProjectileExplosionDefinition DynamiteExplosion = new(250, 250, 10f, 250);
    private static readonly VanillaProjectileExplosionDefinition CelebrationSmallExplosion = new(128, 128, 0f, PreserveKnockBack: true);
    private static readonly VanillaProjectileExplosionDefinition CelebrationLargeExplosion = new(240, 240, 0f, PreserveKnockBack: true);
    private static readonly VanillaProjectileExplosionDefinition MiniNukeExplosion = new(250, 250, 12f);

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

        if (type.Value is 28 or 37 or 516 or 519)
        {
            definition = BombExplosion;
            return true;
        }

        if (type.Value is 29 or 470 or 637)
        {
            definition = DynamiteExplosion;
            return true;
        }

        if (type.Value is 715 or 716)
        {
            definition = CelebrationSmallExplosion;
            return true;
        }

        if (type.Value is 717 or 718)
        {
            definition = CelebrationLargeExplosion;
            return true;
        }

        if (type.Value is 793 or 794 or 795 or 796 or 797 or 798 or 808 or 809)
        {
            definition = MiniNukeExplosion;
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
