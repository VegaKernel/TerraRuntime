using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private bool TryFireWiringTrap(int x, int y)
    {
        if (tiles is null || wiring is null || ToolProjectileOwner is not { } shots) return false;
        var tile = tiles.Get(x, y);
        if (!tile.IsActive || tile.Type != 137 ||
            !VanillaWiringTrap1458.TryResolve(tile.FrameX, tile.FrameY, out var launch) ||
            !VanillaDefinitionCatalog.TryGet(launch.Type, out var definition)) return false;
        var random = toolProjectileAuthority?.ToolRandom;
        if (launch.Type.Value == 185 && random is null) return false;
        if (!wiring.Register(x, y, launch.Cooldown)) return false;
        float vx = launch.DirectionX * launch.Speed, vy = launch.DirectionY * launch.Speed;
        if (launch.Type.Value == 185)
        {
            // The current source branch's density comparison still uses type0.
            // This store cannot contain active type0, so its budget remains200.
            // Source evaluates these arguments even if NewProjectile later fails.
            vx += random!.Next(-20 + (launch.DirectionX == 1 ? 20 : 0), 21 - (launch.DirectionX == -1 ? 20 : 0)) * .05f;
            vy += random.Next(-20 + (launch.DirectionY == 1 ? 20 : 0), 21 - (launch.DirectionY == -1 ? 20 : 0)) * .05f;
        }
        var state = new ProjectileStateUpdate(launch.Type, byte.MaxValue,
            x * 16 + 8 + launch.DirectionX * launch.MuzzleOffset - definition.Width * .5f,
            y * 16 + 8 + launch.DirectionY * launch.MuzzleOffset - definition.Height * .5f,
            vx, vy, default, 0, checked((short)launch.Damage), 2f, 0);
        using var prepared = shots.CreateWorldSpawnPreparation();
        if (!prepared.TryStage(in state, false, out _) || !prepared.TryAdoptUnpublished(out var publication)) return false;
        if (publication?.TryPublishNext() != true)
            throw new InvalidOperationException("Adopted wiring trap lost its source projectile publication.");
        return true;
    }
}
