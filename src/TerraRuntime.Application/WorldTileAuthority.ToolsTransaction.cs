using TerraRuntime.Core.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class WorldTileAuthority
{
    private RuntimeToolBuildingPermission1458? toolBuildingPermission;
    private RuntimeWorldClock? toolClock;
    private RuntimeProjectileStore? toolProjectileStore;
    private RuntimeFallingBlockProjectiles? toolFallingBlocks;
    private ProjectileAuthority? toolProjectileAuthority;

    internal PlayerAuthority ToolPlayerOwner => players;
    internal RuntimeProjectileStore? ToolProjectileOwner => toolProjectileStore;
    internal RuntimeFallingBlockProjectiles? ToolFallingBlocks => toolFallingBlocks;

    internal bool TryPrepareWorldProjectileRandom(out ItemDropRandomPreview? preparation)
    {
        preparation = null;
        if (worldItemSpawnRandom is not SystemWorldItemSpawnRandom) return false;
        var candidate = CreateItemDropRandomPreview();
        if (!candidate.IsCurrent) return false;
        preparation = candidate;
        return true;
    }

    internal void BindToolProjectiles(ProjectileAuthority shots)
    {
        ArgumentNullException.ThrowIfNull(shots);
        if (!ReferenceEquals(shots.ToolPlayerOwner, players) ||
            toolProjectileStore is not null && !ReferenceEquals(toolProjectileStore, shots.ToolProjectileOwner) ||
            toolFallingBlocks is not null && !ReferenceEquals(toolFallingBlocks, shots.FallingBlocks))
            throw new InvalidOperationException("Tool world effects belong to another projectile authority.");
        toolProjectileStore = shots.ToolProjectileOwner;
        toolFallingBlocks = shots.FallingBlocks;
        toolProjectileAuthority = shots;
    }

    internal void BindToolClock(RuntimeWorldClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (toolClock is not null && !ReferenceEquals(toolClock, clock))
            throw new InvalidOperationException("Tool terrain already belongs to another world clock.");
        toolClock = clock;
    }

    internal void BindToolBuildingPermission(RuntimeToolBuildingPermission1458 permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        if (!ReferenceEquals(permission.Tiles, tiles) ||
            toolBuildingPermission is not null && !ReferenceEquals(toolBuildingPermission, permission))
            throw new InvalidOperationException("Tool building permission belongs to another world owner.");
        toolBuildingPermission = permission;
    }

    internal bool TryCaptureToolBuildingPermission(float centerX, float centerY,
        out RuntimeToolBuildingCapture1458 capture)
    {
        capture = default;
        return toolBuildingPermission?.TryCapture(centerX, centerY, out capture) == true;
    }
}
