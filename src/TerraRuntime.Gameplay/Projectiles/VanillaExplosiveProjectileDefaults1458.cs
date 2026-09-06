using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

public enum VanillaAi016MotionKind1458 : byte
{
    OrdinaryFuse = 0,
    Grenade = 1,
    StraightRocket = 2,
    ProximityMine = 3
}

/// <summary>
/// TerrariaServer 1.4.5.8 <c>Projectile.SetDefaults</c> fields required by the admitted explosive runtime slice.
/// Damage is supplied by <c>NewProjectile</c>; aiStyle-16 <c>PrepareBombToBlow</c> retains that damage for the
/// launcher/Mini Nuke families represented here. Unknown types fail closed.
/// </summary>
public readonly record struct VanillaExplosiveProjectileDefaults1458(
    int Width,
    int Height,
    ProjectileAiStyleId AiStyle,
    int Penetrate,
    int TimeLeft,
    bool TileCollide,
    bool IgnoreWater,
    bool Friendly,
    bool Hostile,
    bool Ranged,
    int ExtraUpdates);

public static class VanillaExplosiveProjectileFacts1458
{
    public static bool TryGetDefaults(
        ProjectileTypeId type,
        out VanillaExplosiveProjectileDefaults1458 defaults)
    {
        if (type == VanillaProjectileIds.CelebrationMk2HeldProjectile)
        {
            defaults = new VanillaExplosiveProjectileDefaults1458(
                Width: 22,
                Height: 22,
                AiStyle: VanillaProjectileAiStyles.HeldProjectile,
                Penetrate: -1,
                TimeLeft: VanillaProjectileLifecycleFacts.DefaultTimeLeft,
                TileCollide: false,
                IgnoreWater: true,
                Friendly: false,
                Hostile: false,
                Ranged: true,
                ExtraUpdates: 0);
            return true;
        }

        if (type == VanillaProjectileIds.Bomb || type == VanillaProjectileIds.Dynamite)
        {
            bool bomb = type == VanillaProjectileIds.Bomb;
            defaults = new VanillaExplosiveProjectileDefaults1458(
                Width: bomb ? 22 : 10,
                Height: bomb ? 22 : 10,
                AiStyle: VanillaProjectileAiStyles.Bomb,
                Penetrate: -1,
                TimeLeft: VanillaProjectileLifecycleFacts.DefaultTimeLeft,
                TileCollide: true,
                IgnoreWater: false,
                Friendly: true,
                Hostile: false,
                Ranged: false,
                ExtraUpdates: 0);
            return true;
        }

        if (type.Value is >= 715 and <= 718)
        {
            defaults = new VanillaExplosiveProjectileDefaults1458(
                Width: 14,
                Height: 14,
                AiStyle: VanillaProjectileAiStyles.CelebrationRocket,
                Penetrate: 1,
                TimeLeft: 1_080,
                TileCollide: true,
                IgnoreWater: false,
                Friendly: true,
                Hostile: false,
                Ranged: true,
                ExtraUpdates: 2);
            return true;
        }

        if (type.Value is (>= 793 and <= 801) or (>= 803 and <= 810))
        {
            defaults = new VanillaExplosiveProjectileDefaults1458(
                Width: 14,
                Height: 14,
                AiStyle: VanillaProjectileAiStyles.Bomb,
                Penetrate: -1,
                TimeLeft: VanillaProjectileLifecycleFacts.DefaultTimeLeft,
                TileCollide: true,
                IgnoreWater: false,
                Friendly: true,
                Hostile: false,
                Ranged: true,
                ExtraUpdates: 0);
            return true;
        }

        defaults = default;
        return false;
    }

    /// <summary>
    /// Source grouping from TerrariaServer 1.4.5.8 <c>Projectile.AI_016_Bombs</c>. This is deliberately an exact
    /// type allow-list; sharing aiStyle 16 alone does not grant a projectile this runtime behavior.
    /// </summary>
    public static bool TryGetAi016MotionKind(ProjectileTypeId type, out VanillaAi016MotionKind1458 kind)
    {
        int raw = type.Value;
        if (raw is 28 or 29)
        {
            kind = VanillaAi016MotionKind1458.OrdinaryFuse;
            return true;
        }
        if (raw is >= 133 and <= 144)
        {
            kind = (VanillaAi016MotionKind1458)((raw - 133) % 3 + 1);
            return true;
        }

        if (raw is 793 or 796 or 799 or 803 or 804 or 805 or 806 or 807 or 808 or 809 or 810)
        {
            kind = VanillaAi016MotionKind1458.StraightRocket;
            return true;
        }

        if (raw is 794 or 797 or 800)
        {
            kind = VanillaAi016MotionKind1458.Grenade;
            return true;
        }

        if (raw is 795 or 798 or 801)
        {
            kind = VanillaAi016MotionKind1458.ProximityMine;
            return true;
        }

        kind = default;
        return false;
    }

    /// <summary>Owner-local assignments in TerrariaServer 1.4.5.8 <c>Projectile.NewProjectile</c>.</summary>
    public static bool TryGetPlayerOwnedSpawnTimeLeftOverride(ProjectileTypeId type, out int timeLeft)
    {
        timeLeft = type.Value switch
        {
            28 or 37 or 516 or 519 or 133 or 136 or 139 or 142 or 794 or 797 or 800 => 180,
            29 or 470 or 637 => 300,
            _ => 0
        };
        return timeLeft > 0;
    }

    public static int GetCelebrationVolleyCapacity(int pattern) => pattern switch
    {
        4 => 2,
        5 => 3,
        >= 0 and <= 6 => 1,
        _ => 0
    };

    public static float GetCelebrationLaunchSpeed(int pattern) => pattern == 3 ? 9f : 8f;
}
