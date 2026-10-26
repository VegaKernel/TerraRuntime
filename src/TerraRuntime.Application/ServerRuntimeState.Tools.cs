namespace TerraRuntime.Application;

internal sealed partial class ServerRuntimeState
{
    internal void BindToolBuildingPermission(RuntimeToolBuildingPermission1458 permission) =>
        _runtime.WorldTileAuthority.BindToolBuildingPermission(permission);
}
