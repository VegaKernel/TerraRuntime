using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary Slush pass: retained snow bounds and stored material conversion, no RNG.</summary>
internal static class SnowMaterialConversion1458
{
    public static void Apply(Workspace workspace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rows = workspace.VanillaSnowRows;
        for (int y = workspace.VanillaSnowTop; y < workspace.VanillaSnowBottom; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = rows[y].Left; x < rows[y].Right; x++)
            {
                ref WorldTile tile = ref At(workspace.TileStore,x,y);
                // WorldGen's StoneToIceAndSiltPlusMudIntoSlush delegate does not gate the center on activity.
                if (tile.Type == 123) tile.Type = 224;
                else if (tile.Type == 1) tile.Type = 161;
                else if (tile.Type == 59 && !HasJungleNeighbour(workspace.TileStore,x,y)) tile.Type = 224;
            }
        }
    }

    private static bool HasJungleNeighbour(WorldTileStore tiles, int x, int y)
    {
        for (int tx = x - 3; tx <= x + 3; tx++)
        for (int ty = y - 3; ty <= y + 3; ty++)
        {
            WorldTile tile = At(tiles,tx,ty);
            if (tile.IsActive && tile.Type is 60 or 70 or 71 or 72) return true;
        }
        return false;
    }

    private static ref WorldTile At(WorldTileStore tiles, int x, int y)
    {
        if ((uint)x >= (uint)tiles.Dimensions.WidthTiles || (uint)y >= (uint)tiles.Dimensions.HeightTiles)
            throw new InvalidOperationException("Snow conversion left the candidate terrain.");
        return ref tiles.Tiles[tiles.GetUncheckedIndex(x,y)];
    }
}
