using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

public enum VanillaProjectileTileExplosionCenter1458 : byte
{
    Position = 0,
    Center = 1
}

/// <summary>
/// TerrariaServer 1.4.5.8 Projectile.Kill_ExplodeTiles facts. These facts describe irreversible world-tile
/// destruction only; combat hitbox expansion remains owned by <see cref="VanillaProjectileExplosionFacts"/>.
/// Unknown projectile types fail closed.
/// </summary>
public readonly record struct VanillaProjectileTileExplosionDefinition1458(
    int RadiusTiles,
    VanillaProjectileTileExplosionCenter1458 Center,
    bool ExplodeHardmodeOres = false);

public static class VanillaProjectileTileExplosionFacts1458
{
    public static bool TryGet(
        ProjectileTypeId type,
        out VanillaProjectileTileExplosionDefinition1458 definition)
    {
        // Projectile.Kill_ExplodeTiles in TerrariaServer 1.4.5.8.
        int radius = type.Value switch
        {
            28 or 37 or 516 or 519 => 4,
            29 or 470 or 637 or 796 or 797 or 798 or 809 => 7,
            142 or 143 or 144 or 341 or 718 => 5,
            716 or 780 or 781 or 782 or 804 or 783 or 863 => 3,
            108 => 10,
            136 or 137 or 138 or 339 => 3,
            1086 or 1087 => 9,
            _ => 0
        };
        if (radius == 0)
        {
            definition = default;
            return false;
        }

        bool center = type.Value is 716 or 718 or 773 or 1086 or 1087;
        definition = new VanillaProjectileTileExplosionDefinition1458(
            radius,
            center ? VanillaProjectileTileExplosionCenter1458.Center : VanillaProjectileTileExplosionCenter1458.Position,
            ExplodeHardmodeOres: type.Value is 1086 or 1087);
        return true;
    }
}
