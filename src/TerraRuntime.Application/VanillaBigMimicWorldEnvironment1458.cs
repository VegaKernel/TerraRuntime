using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed class VanillaBigMimicWorldEnvironment1458(WorldTileStore tiles) : IVanillaBigMimicEnvironment1458
{
    public bool CanHit(float sourceX, float sourceY, float targetX, float targetY) =>
        VanillaWorldCanHit.HasLineOfSight(tiles, sourceX, sourceY, 1, 1, targetX, targetY, 1, 1);

    public bool SolidCollision(float x, float y, int width, int height) =>
        VanillaWorldSolidCollision.Intersects(tiles, x, y, width, height);
}
