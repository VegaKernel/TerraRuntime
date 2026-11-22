using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private bool TryFireWiringTrap(int x, int y)
    {
        if (tiles is null || wiring is null || ToolProjectileOwner is not { } shots) return false;
        var tile = tiles.Get(x, y);
        if (tile.Type == WorldWiringDeviceExecutor1458.GeyserTileType)
            return TryFireWiringGeyser(x, y, shots);
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
        return TryPublishWiringTrapBirth(shots, in state);
    }

    private bool TryFireWiringGeyser(int x, int y, RuntimeProjectileStore shots)
    {
        if (tiles is null || wiring is null ||
            !WorldWiringDeviceExecutor1458.TryResolveGeyser(tiles, x, y, out int left, out int direction) ||
            !wiring.Register(left, y, 200)) return false;
        var type = new TerraRuntime.Contracts.Gameplay.ProjectileTypeId(VanillaWiringTrap1458.GeyserProjectileType);
        var state = new ProjectileStateUpdate(type, byte.MaxValue,
            (left + 1) * 16f - VanillaWiringTrap1458.GeyserWidth * .5f,
            (y + (direction > 0 ? 1 : 0)) * 16f - VanillaWiringTrap1458.GeyserInitialHeight * .5f,
            0, direction * 8f, default, 0, (short)VanillaWiringTrap1458.Damage(type), 2f, 0);
        return TryPublishWiringTrapBirth(shots, in state);
    }

    internal void ApplyGeyserTileCuts(in ProjectileSnapshot shot, int height)
    {
        if (tiles is null) return;
        var bounds = VanillaWorldProjectileTileCut.GetCutBounds(tiles.Dimensions, shot.PositionX, shot.PositionY,
            VanillaWiringTrap1458.GeyserWidth, height);
        // CutTilesAt reads the live rectangle in column order. Keep the existing tile owner
        // for each cut so framing and drops stay on the authoritative world stream.
        for (int x = bounds.X; x < bounds.ExclusiveRight; x++)
            for (int y = bounds.Y; y < bounds.ExclusiveBottom; y++)
            {
                if (ToolProjectileOwner?.TryGet(shot.Handle, out var current) != true || current != shot) return;
                if (VanillaWorldProjectileTileCut.IsCutCandidate(tiles, x, y)) TryCutTile(x, y);
            }
    }

    private bool TryPublishWiringTrapBirth(RuntimeProjectileStore shots, in ProjectileStateUpdate state)
    {
        using var prepared = shots.CreateWorldSpawnPreparation();
        if (!prepared.TryStage(in state, false, out var birth) || !prepared.TryAdoptUnpublished(out var publication)) return false;
        if (toolProjectileAuthority?.TryScheduleWiringTrapBirth(in birth) == false || publication?.TryPublishNext() != true)
            throw new InvalidOperationException("Adopted wiring trap lost its source projectile publication.");
        return true;
    }
}
