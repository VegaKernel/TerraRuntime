using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>Production world queries for source-backed AI_002 daylight and Pigron phasing state.</summary>
internal sealed class VanillaFlyingEyeWorldEnvironment : IVanillaFlyingEyeEnvironment
{
    private readonly WorldTileStore _tiles;

    public VanillaFlyingEyeWorldEnvironment(WorldTileStore tiles) =>
        _tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public bool IsGraveyardAt(float centerX, float centerY) =>
        VanillaWorldGraveyardScene.IsFunctionalAt(_tiles, centerX, centerY);

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
            _tiles,
            sourcePositionX,
            sourcePositionY,
            sourceWidth,
            sourceHeight,
            targetPositionX,
            targetPositionY,
            targetWidth,
            targetHeight);

    public bool SolidCollision(float positionX, float positionY, int width, int height) =>
        VanillaWorldSolidCollision.Intersects(_tiles, positionX, positionY, width, height);
}
