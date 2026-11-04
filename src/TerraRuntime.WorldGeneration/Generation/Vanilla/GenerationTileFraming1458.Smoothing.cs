using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal sealed partial class GenerationTileFraming1458
{
    /// <summary>Source Tile.SmoothSlope, with its default neighbours-first order and no network synchronization.</summary>
    internal void SmoothSourceSlope(int x, int y, bool neighbours = true)
    {
        if (neighbours)
        {
            SmoothSourceSlope(x + 1, y, false);
            SmoothSourceSlope(x - 1, y, false);
            SmoothSourceSlope(x, y + 1, false);
            SmoothSourceSlope(x, y - 1, false);
        }
        if (!Contains(x, y - 1) || !Contains(x, y + 1) || !SolidOrSloped(x, y)) return;
        ref WorldTile cell = ref At(x, y);
        WorldTile above = At(x, y - 1);
        if (!WorldSmoothingCatalog1458.CanBePounded(cell.TileType) || !CanRemoveTile(x, y) ||
            above.IsActive && WorldSmoothingCatalog1458.ForbidsSlopingBelow(above.TileType)) return;
        bool topOccupied = above.IsActive && !above.IsActuated;
        int mask = (topOccupied ? 8 : 0) | (SolidOrSloped(x, y + 1) ? 4 : 0) |
            (SolidOrSloped(x - 1, y) ? 2 : 0) | (SolidOrSloped(x + 1, y) ? 1 : 0);
        if (mask is 9 or 10 && topOccupied && !SolidOrSloped(x, y - 1)) return;
        byte slope = mask switch { 10 => 3, 9 => 4, 6 => 1, 5 => 2, _ => 0 };
        if (!cell.TrySetSourceShape(mask == 4, slope)) throw new InvalidOperationException("Invalid source slope.");
    }

    internal bool SolidOrSloped(int x, int y)
    {
        if (!Contains(x, y)) return false;
        WorldTile cell = At(x, y);
        return cell.IsActive && !cell.IsActuated && IsFramingSolid(cell.TileType) &&
            !VanillaTileCollisionCatalog.IsSolidTop(cell.TileType);
    }

}
