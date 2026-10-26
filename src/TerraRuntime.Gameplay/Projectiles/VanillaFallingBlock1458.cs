using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>WorldGen.GetSandfallProjData / Projectile.SetDefaults / Kill, official 1.4.5.8.</summary>
public static class VanillaFallingBlock1458
{
    public static bool TryGetProjectile(TileTypeId tile, out ProjectileTypeId projectile)
    {
        int type = tile.Value switch { 53 => 31, 112 => 56, 116 => 67, 123 => 71, 224 => 179, 234 => 241, 495 => 812, 330 => 411, 331 => 412, 332 => 413, 333 => 414, _ => 0 };
        projectile = new(type);
        return type != 0;
    }
    public static bool TryGetTile(ProjectileTypeId projectile, out TileTypeId tile)
    {
        int type = projectile.Value switch { 31 => 53, 56 => 112, 67 => 116, 71 => 123, 179 => 224, 241 => 234, 812 => 495, 411 => 330, 412 => 331, 413 => 332, 414 => 333, _ => -1 };
        tile = type < 0 ? default : new(type);
        return type >= 0;
    }
    /// <summary>WorldGen.GetSandfallProjData ordinary falling tile producer.</summary>
    public static bool TryGetSourceDamage(ProjectileTypeId projectile, out short damage)
    {
        damage = 0;
        if (!TryGetTile(projectile, out _)) return false;
        damage = projectile.Value is >= 411 and <= 414 ? (short)0 : (short)10;
        return true;
    }

    /// <summary>AI010's caller draw survives dedicated Dust.NewDust suppression.</summary>
    public static bool TryGetDedicatedAiRandomBound(ProjectileTypeId projectile, out int bound)
    {
        bound = 0;
        if (!TryGetTile(projectile, out _)) return false;
        bound = projectile.Value is >= 411 and <= 414 ? 3 : projectile.Value == 812 ? 20 : 2;
        return true;
    }

    public static bool PreventsFallAbove(TileTypeId tile) => tile.Value is 21 or 467 or 441 or 468 or 323 or 88 or 80 or 77 or 26 or 475 or 470 or 597;
    public static bool ConvertsLandingToItem(TileTypeId tile) => tile.Value is 314 or 421 or 422;
}
