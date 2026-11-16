using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>Cardinal trap frames in Wiring.HitWireSingle(137), TerrariaServer 1.4.5.8.</summary>
public static class VanillaWiringTrap1458
{
    public static bool IsDart(ProjectileTypeId type) => type.Value is 98 or 184 or 980;
    public static bool IsContact(ProjectileTypeId type) => IsDart(type) || type.Value is 185 or 186 or 188;
    public static int Damage(ProjectileTypeId type) => type.Value switch
    { 98 => 20, 184 or 185 or 187 or 188 => 40, 186 => 60, 980 => 30, _ => 0 };

    public static bool TryResolve(short frameX, short frameY, out WiringTrapLaunch1458 launch)
    {
        launch = default;
        if (frameX is < 0 or > 72 || frameX % 18 != 0 || frameY % 18 != 0) return false;
        int style = frameY / 18;
        if (style is < 0 or > 5) return false;
        int frame = frameX / 18;
        int dx, dy;
        if (style is 3 or 4)
        {
            dx = frame == 3 ? -1 : frame == 4 ? 1 : 0;
            dy = frame <= 1 ? 1 : frame == 2 ? -1 : 0;
        }
        else
        {
            dx = frame == 0 ? -1 : frame == 1 ? 1 : 0;
            dy = frame >= 4 ? 1 : frame >= 2 ? -1 : 0;
        }
        var type = new ProjectileTypeId(style switch { 0 => 98, 1 => 184, 2 => 187, 3 => 185, 4 => 186, _ => 980 });
        launch = new(type, Damage(type), dx, dy, style switch { 2 => 5, 3 => 4, 4 => 8, _ => 12 },
            style switch { 3 => 14, 4 => 18, _ => 10 }, style switch { 3 => 300, 4 => 90, _ => 200 });
        return true;
    }
}

public readonly record struct WiringTrapLaunch1458(ProjectileTypeId Type, int Damage,
    int DirectionX, int DirectionY, int Speed, int MuzzleOffset, int Cooldown);
