using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>Wiring.HitWireSingle(137), TerrariaServer 1.4.5.8 dart styles.</summary>
public static class VanillaWiringDartTrap1458
{
    public const int CooldownUpdates = 200;
    public static bool IsDart(ProjectileTypeId type) => type.Value is 98 or 184 or 980;

    public static bool TryResolve(short frameX, short frameY, out ProjectileTypeId type,
        out int damage, out int directionX, out int directionY)
    {
        type = default; damage = directionX = directionY = 0;
        // TileObjectData's represented cardinal frames; malformed world frames
        // and the flame/spiky/spear families need separate source participants.
        if (frameX is < 0 or > 72 || frameX % 18 != 0 || frameY % 18 != 0) return false;
        int style = frameY / 18;
        if (style is not (0 or 1 or 5)) return false;
        type = new(style == 0 ? 98 : style == 1 ? 184 : 980);
        damage = style == 0 ? 20 : style == 1 ? 40 : 30;
        directionX = frameX == 0 ? -1 : frameX == 18 ? 1 : 0;
        directionY = frameX >= 36 ? frameX >= 72 ? 1 : -1 : 0;
        return true;
    }
}
