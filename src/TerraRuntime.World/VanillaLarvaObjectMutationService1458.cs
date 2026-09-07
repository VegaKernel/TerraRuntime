using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public enum VanillaLarvaObjectMutationStatus1458 : byte
{
    Applied = 0,
    OutOfBounds = 1,
    NotLarva = 2,
    InvalidObjectState = 3
}

public readonly record struct VanillaLarvaObjectMutationResult1458(
    VanillaLarvaObjectMutationStatus1458 Status,
    WorldTileRegion Bounds,
    int ChangedTiles)
{
    public bool Applied => Status == VanillaLarvaObjectMutationStatus1458.Applied;
}

/// <summary>
/// Authoritative TerrariaServer 1.4.5.8 <c>WorldGen.Check3x3</c> slice for TileID 231 (Larva). The clicked
/// frame resolves to one 3x3 style instance, every frame is preflighted, then the complete object is cleared as one
/// single-writer transaction. NPC selection/spawn remains outside the world layer.
/// </summary>
public sealed class VanillaLarvaObjectMutationService1458
{
    public const int Width = 3;
    public const int Height = 3;
    public const short FrameCellSize = 18;
    public const short FrameStylePeriod = Width * FrameCellSize;

    private readonly WorldTileStore tiles;

    public VanillaLarvaObjectMutationService1458(WorldTileStore tiles) =>
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    public VanillaLarvaObjectMutationResult1458 TryBreakAt(int tileX, int tileY)
    {
        VanillaLarvaObjectMutationStatus1458 resolved = TryResolveAt(tileX, tileY, out WorldTileRegion bounds);
        if (resolved != VanillaLarvaObjectMutationStatus1458.Applied)
            return new VanillaLarvaObjectMutationResult1458(resolved, default, 0);

        int changed = 0;
        for (int y = bounds.Y; y < bounds.ExclusiveBottom; y++)
        {
            for (int x = bounds.X; x < bounds.ExclusiveRight; x++)
            {
                WorldTile cleared = tiles.Get(x, y);
                cleared.Type = 0;
                cleared.FrameX = 0;
                cleared.FrameY = 0;
                cleared.TileColor = 0;
                cleared.Shape = 0;
                cleared.Flags &= ~(
                    WorldTileFlags.Active |
                    WorldTileFlags.Actuator |
                    WorldTileFlags.Inactive |
                    WorldTileFlags.InvisibleBlock |
                    WorldTileFlags.FullbrightBlock);
                tiles.Set(x, y, in cleared);
                changed++;
            }
        }

        VanillaWorldLiquidWakeup1458.WakeRegion(tiles, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        MarkFrameNeighborhoodDirty(in bounds);
        return new VanillaLarvaObjectMutationResult1458(
            VanillaLarvaObjectMutationStatus1458.Applied,
            bounds,
            changed);
    }

    public VanillaLarvaObjectMutationStatus1458 TryResolveAt(
        int tileX,
        int tileY,
        out WorldTileRegion bounds)
    {
        bounds = default;
        if (!Contains(tileX, tileY))
            return VanillaLarvaObjectMutationStatus1458.OutOfBounds;

        WorldTile clicked = tiles.Get(tileX, tileY);
        if (!clicked.IsActive || clicked.TileType != VanillaTileIds.Larva)
            return VanillaLarvaObjectMutationStatus1458.NotLarva;
        if (!TryResolveFrame(in clicked, out int column, out int row, out int styleX, out int styleY))
            return VanillaLarvaObjectMutationStatus1458.InvalidObjectState;

        int topLeftX = tileX - column;
        int topLeftY = tileY - row;
        if (topLeftX < 0 || topLeftY < 0 ||
            (long)topLeftX + Width > tiles.Dimensions.WidthTiles ||
            (long)topLeftY + Height > tiles.Dimensions.HeightTiles)
        {
            return VanillaLarvaObjectMutationStatus1458.InvalidObjectState;
        }

        for (int objectY = 0; objectY < Height; objectY++)
        {
            for (int objectX = 0; objectX < Width; objectX++)
            {
                WorldTile cell = tiles.Get(topLeftX + objectX, topLeftY + objectY);
                if (!cell.IsActive ||
                    cell.TileType != VanillaTileIds.Larva ||
                    cell.FrameX != styleX + objectX * FrameCellSize ||
                    cell.FrameY != styleY + objectY * FrameCellSize)
                {
                    return VanillaLarvaObjectMutationStatus1458.InvalidObjectState;
                }
            }
        }

        bounds = new WorldTileRegion(topLeftX, topLeftY, Width, Height);
        return VanillaLarvaObjectMutationStatus1458.Applied;
    }

    private static bool TryResolveFrame(
        in WorldTile tile,
        out int column,
        out int row,
        out int styleX,
        out int styleY)
    {
        column = row = styleX = styleY = 0;
        if (tile.FrameX < 0 || tile.FrameY < 0 ||
            tile.FrameX % FrameCellSize != 0 ||
            tile.FrameY % FrameCellSize != 0)
        {
            return false;
        }

        column = tile.FrameX / FrameCellSize % Width;
        row = tile.FrameY / FrameCellSize % Height;
        styleX = tile.FrameX / FrameStylePeriod * FrameStylePeriod;
        styleY = tile.FrameY / FrameStylePeriod * FrameStylePeriod;
        return true;
    }

    private void MarkFrameNeighborhoodDirty(in WorldTileRegion bounds)
    {
        int minX = Math.Max(0, bounds.X - 1);
        int minY = Math.Max(0, bounds.Y - 1);
        int maxX = Math.Min(tiles.Dimensions.WidthTiles - 1, bounds.ExclusiveRight);
        int maxY = Math.Min(tiles.Dimensions.HeightTiles - 1, bounds.ExclusiveBottom);
        WorldSectionId first = TerrariaSectionGeometry.FromTile(tiles.Dimensions, minX, minY);
        WorldSectionId last = TerrariaSectionGeometry.FromTile(tiles.Dimensions, maxX, maxY);
        for (int sectionY = first.Y; sectionY <= last.Y; sectionY++)
        {
            for (int sectionX = first.X; sectionX <= last.X; sectionX++)
                tiles.DirtySections.MarkDirty(new WorldSectionId(sectionX, sectionY));
        }
    }

    private bool Contains(int x, int y) =>
        (uint)x < (uint)tiles.Dimensions.WidthTiles &&
        (uint)y < (uint)tiles.Dimensions.HeightTiles;
}
