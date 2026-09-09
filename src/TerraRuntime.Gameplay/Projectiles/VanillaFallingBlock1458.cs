using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>WorldGen.GetSandfallProjData / Projectile.SetDefaults / Kill, official 1.4.5.8.</summary>
public static class VanillaFallingBlock1458
{
    public static bool TryGetProjectile(TileTypeId tile, out ProjectileTypeId projectile)
    {
        int type = tile.Value switch { 53 => 31, 112 => 56, 116 => 67, 123 => 71, 224 => 179, 234 => 241, 495 => 812, _ => 0 };
        projectile = new(type);
        return type != 0;
    }
    public static bool TryGetTile(ProjectileTypeId projectile, out TileTypeId tile)
    {
        int type = projectile.Value switch { 31 => 53, 56 => 112, 67 => 116, 71 => 123, 179 => 224, 241 => 234, 812 => 495, _ => -1 };
        tile = type < 0 ? default : new(type);
        return type >= 0;
    }
    public static bool PreventsFallAbove(TileTypeId tile) => tile.Value is 21 or 467 or 441 or 468 or 323 or 88 or 80 or 77 or 26 or 475 or 470 or 597;
    public static bool ConvertsLandingToItem(TileTypeId tile) => tile.Value is 314 or 421 or 422;
}
