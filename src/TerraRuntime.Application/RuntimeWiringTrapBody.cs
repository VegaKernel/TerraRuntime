using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal static class RuntimeWiringTrapBody
{
    internal static bool TryGet(ProjectileTypeId type, in ProjectileLifecycleState life, out VanillaProjectileDefinition body)
    {
        if (type.Value != VanillaWiringTrap1458.GeyserProjectileType)
            return VanillaDefinitionCatalog.TryGet(type, out body);

        int height = life.GeyserHeight == 0 ? VanillaWiringTrap1458.GeyserInitialHeight : life.GeyserHeight;
        body = new(VanillaWiringTrap1458.GeyserWidth, height, new(126), TileCollide: false,
            IgnoreWater: true, CanCutTiles: true, VanillaWiringTrap1458.GeyserWidth, height);
        return true;
    }
}
