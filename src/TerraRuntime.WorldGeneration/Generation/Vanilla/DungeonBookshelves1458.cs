using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DungeonGlobalBookshelves search, retry accounting and ordered shelf decoration.</summary>
internal sealed class DungeonBookshelves1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random,
    double worldSurface, double rockLayer, CancellationToken cancellationToken, IReadOnlyList<DungeonPitTraps1458.Pit>? pits = null)
{
    public int Place(DungeonBounds1458 bounds, IReadOnlyList<int> wallVariants,
        DungeonDecorationProfile1458 decoration, DungeonBounds1458? protectedEntrance = null)
    {
        int width = tiles.Dimensions.WidthTiles, height = tiles.Dimensions.HeightTiles;
        if (bounds.Left < 5 || bounds.Right >= width - 5 || bounds.Top < 5 || bounds.Bottom >= height - 5 ||
            bounds.Left >= bounds.Right || bounds.Top >= bounds.Bottom || wallVariants.Count != 3)
            throw new InvalidOperationException("Invalid dungeon bookshelf sampling bounds.");
        var placement = new DungeonObjectPlacement1458(tiles, random);
        int completed = 0, failures = 0, placed = 0;
        while (completed < width / 20)
        {
            cancellationToken.ThrowIfCancellationRequested();
            failures++;
            int x = random.Next(bounds.Left, bounds.Right), y = random.Next(bounds.Top, bounds.Bottom);
            if (!At(x, y).IsActive && DungeonGenerationTiles1458.IsDungeonWall(At(x, y).Wall))
            {
                int direction = random.Next(2) == 0 ? -1 : 1;
                while (x >= 5 && x <= width - 5 && !At(x, y).IsActive) x -= direction;
                if (x >= 5 && x <= width - 5 && Support(x, y) && Support(x, y - 1) && Support(x, y + 1))
                {
                    x += direction;
                    bool clear = !At(x, y - 1).IsActive && !At(x, y - 2).IsActive && !At(x, y - 3).IsActive;
                    for (int tx = x - 3; tx <= x + 3; tx++)
                    for (int ty = y - 3; ty <= y + 3; ty++)
                        if (At(tx, ty) is { IsActive: true, Type: 19 }) clear = false;
                    if (clear)
                    {
                        // The source's feature-gate continue intentionally bypasses retry accounting below.
                        if (x < 5 || x >= width - 5 || At(x, y).Wall == 350 || DungeonPitTraps1458.Contains(pits, x, y) ||
                            protectedEntrance is { } area && x >= area.Left && x <= area.Right && y >= area.Top && y <= area.Bottom)
                            continue;
                        int end = x;
                        while (end > bounds.Left && end < bounds.Right && !At(end, y).IsActive &&
                            !At(end, y - 1).IsActive && !At(end, y + 1).IsActive) end += direction;
                        // This draw occurs even for a corridor too short to receive a shelf.
                        bool books = random.Next(2) == 0;
                        if (Math.Abs(end - x) > 5)
                        {
                            int start = x, length = random.Next(1, 4);
                            for (int step = 0; step < length; step++, x += direction)
                            {
                                int variant = At(x, y).Wall == wallVariants[2] ? 2 : At(x, y).Wall == wallVariants[1] ? 1 : 0;
                                placement.SetPlatform(x, y, decoration.GetShelfStyle(variant));
                                if (!books) continue;
                                placement.TableObject(x, y - 1, 50);
                                if (random.Next(50) == 0 && y > (worldSurface + rockLayer) / 2 && At(x, y - 1).Type == 50)
                                    At(x, y - 1).FrameX = 90;
                            }
                            failures = 0; completed++; placed++;
                            if (!books && random.Next(2) == 0)
                            {
                                placement.TableObject(start, y - 1, random.Next(4) == 0 ? (ushort)49 : (ushort)13);
                                if (At(start, y - 1).Type == 13) At(start, y - 1).FrameX = random.Next(2) == 0 ? (short)18 : (short)36;
                            }
                        }
                    }
                }
            }
            if (failures > 1000) { failures = 0; completed++; }
        }
        return placed;
    }

    private bool Support(int x, int y) => At(x, y).IsActive && DungeonGenerationTiles1458.IsDungeonTile(At(x, y).Type);
    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
