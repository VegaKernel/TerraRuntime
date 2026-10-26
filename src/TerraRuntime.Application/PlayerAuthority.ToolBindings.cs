namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private WorldTileAuthority? toolWorld;
    private ProjectileAuthority? toolProjectiles;

    internal void BindToolActions(WorldTileAuthority world, ProjectileAuthority shots)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(shots);
        if (!ReferenceEquals(world.ToolPlayerOwner, this) || !ReferenceEquals(shots.ToolPlayerOwner, this) ||
            toolWorld is not null && !ReferenceEquals(toolWorld, world) ||
            toolProjectiles is not null && !ReferenceEquals(toolProjectiles, shots))
            throw new InvalidOperationException("Tool actions belong to another player authority.");
        toolWorld = world;
        toolProjectiles = shots;
        world.BindToolProjectiles(shots);
        shots.BindToolActorWorld(world);
    }

    internal bool TryCaptureToolBuildingBeforeMovement(float centerX, float centerY,
        out RuntimeToolBuildingCapture1458 capture)
    {
        capture = default;
        return toolWorld?.TryCaptureToolBuildingPermission(centerX, centerY, out capture) == true;
    }
}
