using TerraRuntime.Core.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    internal PlayerAuthority ToolPlayerOwner => players;
    internal RuntimeProjectileStore ToolProjectileOwner => projectiles;
}
