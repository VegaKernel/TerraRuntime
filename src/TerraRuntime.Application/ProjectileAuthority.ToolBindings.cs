using TerraRuntime.Core.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class ProjectileAuthority
{
    internal TerraRuntime.Core.VanillaUnifiedRandom1458 ToolRandom => projectileRandom;
    internal PlayerAuthority ToolPlayerOwner => players;
    internal RuntimeProjectileStore ToolProjectileOwner => projectiles;
}
