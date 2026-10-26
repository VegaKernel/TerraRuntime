using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Gameplay.Projectiles;

/// <summary>Source SetDefaults world geometry for all24 retained tool shots; not AI or use admission.</summary>
public static class VanillaToolProjectileDefinition1458
{
    public static bool TryGet(ProjectileTypeId type, out VanillaProjectileDefinition definition)
    {
        // Projectile.CanCutTiles and ProjectileID.Sets.IsAGolfBall (1458):
        // none of these types/styles belongs to the cut exclusion set.
        // GetCollisionParams (1458) gives1052 a10x10 terrain box inside its22x22 body.
        definition = type.Value switch
        {
            57 => new(18, 18, new(20), false, false, true, 18, 18),
            58 => new(19, 19, new(20), false, false, true, 19, 19),
            59 => new(19, 19, new(20), false, false, true, 19, 19),
            60 => new(19, 19, new(20), false, false, true, 19, 19),
            61 => new(20, 20, new(20), false, false, true, 20, 20),
            62 => new(19, 19, new(20), false, false, true, 19, 19),
            107 => new(24, 24, new(20), false, false, true, 24, 24),
            213 => new(20, 20, new(20), false, false, true, 20, 20),
            214 => new(18, 18, new(20), false, false, true, 18, 18),
            216 => new(20, 20, new(20), false, false, true, 20, 20),
            217 => new(20, 20, new(20), false, false, true, 20, 20),
            219 => new(20, 20, new(20), false, false, true, 20, 20),
            220 => new(21, 21, new(20), false, false, true, 21, 21),
            223 => new(22, 22, new(20), false, false, true, 22, 22),
            224 => new(19, 19, new(20), false, false, true, 19, 19),
            252 => new(19, 19, new(20), false, false, true, 19, 19),
            369 => new(22, 22, new(20), false, false, true, 22, 22),
            428 => new(26, 54, new(20), false, false, true, 26, 54),
            430 => new(30, 54, new(20), false, false, true, 30, 54),
            432 => new(30, 54, new(20), false, false, true, 30, 54),
            445 => new(10, 10, new(75), false, true, true, 10, 10),
            509 => new(26, 26, new(20), false, false, true, 26, 26),
            609 => new(30, 54, new(20), false, false, true, 30, 54),
            1052 => new(22, 22, new(3), true, false, true, 10, 10),
            _ => default
        };
        return definition.Width != 0;
    }
}
