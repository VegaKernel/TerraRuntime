using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>Production tile LOS adapter for source-backed ordinary NPC projectile attacks.</summary>
internal sealed class VanillaNpcProjectileWorldEnvironment : IVanillaNpcProjectileEnvironment
{
    private readonly WorldTileStore tiles;

    public VanillaNpcProjectileWorldEnvironment(WorldTileStore tiles) =>
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public bool CanHit(
        float sourcePositionX,
        float sourcePositionY,
        int sourceWidth,
        int sourceHeight,
        float targetPositionX,
        float targetPositionY,
        int targetWidth,
        int targetHeight) =>
        VanillaWorldCanHit.HasLineOfSight(
            tiles,
            sourcePositionX,
            sourcePositionY,
            sourceWidth,
            sourceHeight,
            targetPositionX,
            targetPositionY,
            targetWidth,
            targetHeight);
}
