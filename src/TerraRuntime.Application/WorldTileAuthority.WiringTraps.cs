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
            !VanillaWiringDartTrap1458.TryResolve(tile.FrameX, tile.FrameY, out var type,
                out int damage, out int dx, out int dy) ||
            !VanillaDefinitionCatalog.TryGet(type, out var definition)) return false;
        // CheckMech happens before NewProjectile, even if the source allocator
        // cannot create a physical projectile. All colors share this one clock.
        if (!wiring.Register(x, y, VanillaWiringDartTrap1458.CooldownUpdates)) return false;
        var state = new ProjectileStateUpdate(type, byte.MaxValue,
            x * 16 + 8 + dx * 10 - definition.Width * .5f,
            y * 16 + 8 + dy * 10 - definition.Height * .5f,
            dx * 12f, dy * 12f, default, 0, checked((short)damage), 2f, 0);
        using var prepared = shots.CreateWorldSpawnPreparation();
        if (!prepared.TryStage(in state, false, out _) || !prepared.TryAdoptUnpublished(out var publication)) return false;
        if (publication?.TryPublishNext() != true)
            throw new InvalidOperationException("Adopted wiring trap lost its source projectile publication.");
        return true;
    }
}
