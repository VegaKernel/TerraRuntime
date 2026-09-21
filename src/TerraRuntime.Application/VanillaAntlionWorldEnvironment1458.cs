using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>Production LOS and tile projection for TerrariaServer 1.4.5.8 NPC AI_019 Antlion.</summary>
internal sealed class VanillaAntlionWorldEnvironment1458 : IVanillaAntlionEnvironment
{
    private const int TileSize = 16;
    private readonly WorldTileStore tiles;

    public VanillaAntlionWorldEnvironment1458(WorldTileStore tiles) =>
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public bool CanHit(float sourcePositionX, float sourcePositionY, int sourceWidth, int sourceHeight,
        float targetPositionX, float targetPositionY, int targetWidth, int targetHeight) =>
        VanillaWorldCanHit.HasLineOfSight(tiles, sourcePositionX, sourcePositionY, sourceWidth, sourceHeight,
            targetPositionX, targetPositionY, targetWidth, targetHeight);

    public bool HasSolidFloor(float positionX, float positionY, int width, int height)
    {
        if (!HasValidGeometry(positionX, positionY, width, height))
            return false;

        int y = (int)(positionY + height) / TileSize;
        return IsSolid((int)positionX / TileSize, y) ||
               IsSolid((int)(positionX + width * .5f) / TileSize, y) ||
               IsSolid((int)(positionX + width) / TileSize, y);
    }

    public bool HasConveyorBelow(float positionX, float positionY, int width, int height)
    {
        if (!HasValidGeometry(positionX, positionY, width, height))
            return false;

        int x = (int)(positionX + width * .5f) / TileSize;
        int y = (int)(positionY + height + 8f) / TileSize;
        if (!TryGet(x, y, out WorldTile tile))
            return false;

        return tile.IsActive && !tile.IsActuated &&
            (tile.TileType == VanillaTileIds.ConveyorBeltLeft || tile.TileType == VanillaTileIds.ConveyorBeltRight);
    }

    private bool IsSolid(int x, int y) =>
        TryGet(x, y, out WorldTile tile) &&
        tile.IsActive &&
        !tile.IsActuated &&
        VanillaTileCollisionCatalog.IsSolid(tile.TileType);

    private bool TryGet(int x, int y, out WorldTile tile)
    {
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles)
        {
            tile = default;
            return false;
        }

        tile = tiles.Get(x, y);
        return true;
    }

    private static bool HasValidGeometry(float x, float y, int width, int height) =>
        float.IsFinite(x) && float.IsFinite(y) && width > 0 && height > 0;
}
