using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DungeonGlobalSpikes, before doors and wall variants, on an unpublished workspace.</summary>
internal sealed class DungeonSpikes1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken,
    IReadOnlyList<DungeonPitTraps1458.Pit>? pits = null)
{
    public int Place(DungeonBounds1458 bounds, ushort wall, ushort crackedBrick, double worldSurface,
        DungeonBounds1458? protectedEntrance = null)
    {
        if ((wall, crackedBrick) is not ((7, 481) or (8, 482) or (9, 483)))
            throw new InvalidOperationException("Unsupported ordinary dungeon spike palette.");
        int minY = checked((int)worldSurface + 25);
        if (bounds.Left < 2 || bounds.Right >= tiles.Dimensions.WidthTiles - 1 || minY < 2 || bounds.Bottom >= tiles.Dimensions.HeightTiles - 1 ||
            bounds.Left >= bounds.Right || minY > bounds.Bottom) throw new InvalidOperationException("Invalid dungeon spike sampling bounds.");
        int target = (int)(42f * ((float)tiles.Dimensions.WidthTiles / 4200f)), placed = 0;
        for (int orientation = 0; orientation < 2; orientation++)
        {
            int attempts = 0, completed = 0;
            while (completed < target)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempts++;
                int x = random.Next(bounds.Left, bounds.Right), y = random.Next(minY, bounds.Bottom);
                if (!At(x, y).IsActive && At(x, y).Wall == wall)
                {
                    int sign = random.Next(2) == 0 ? -1 : 1;
                    int dx = orientation == 0 ? 0 : sign, dy = orientation == 0 ? sign : 0;
                    int ux = orientation == 0 ? 1 : 0, uy = orientation == 0 ? 0 : 1;
                    while (!At(x, y).IsActive && (orientation == 0 || x > 5 && x < tiles.Dimensions.WidthTiles - 5))
                    {
                        x += dx; y += dy;
                        if (y < 2 || y >= tiles.Dimensions.HeightTiles - 2) break;
                    }
                    if (y >= 2 && y < tiles.Dimensions.HeightTiles - 2 &&
                        Supports(x - ux, y - uy) && At(x + ux, y + uy).IsActive &&
                        !At(x - ux - dx, y - uy - dy).IsActive && !At(x + ux - dx, y + uy - dy).IsActive)
                    {
                        completed++; placed++;
                        Extend(x, y, -1, random.Next(5, 13));
                        Extend(x + ux, y + uy, 1, random.Next(5, 13));
                    }

                    void Extend(int tx, int ty, int side, int remaining)
                    {
                        while (remaining > 0 && Safe(tx, ty) && Supports(tx + ux * side, ty + uy * side) &&
                            At(tx + dx, ty + dy).IsActive && At(tx, ty).IsActive && !At(tx - dx, ty - dy).IsActive)
                        {
                            if (!Allowed(tx, ty) || !Allowed(tx - dx, ty - dy)) break;
                            At(tx, ty).Type = 48; // Source does NOT clear the supporting cell's slope/liquid/frames.
                            if (!At(tx - ux - dx, ty - uy - dy).IsActive && !At(tx + ux - dx, ty + uy - dy).IsActive)
                            {
                                Tip(tx - dx, ty - dy);
                                Tip(tx - dx * 2, ty - dy * 2);
                            }
                            tx += ux * side; ty += uy * side; remaining--;
                        }
                    }
                }
                // Source counts attempts across successful runs too, and checks AFTER the attempt.
                if (attempts > 1000) { attempts = 0; completed++; }
            }
        }
        return placed;

        bool Supports(int x, int y) => At(x, y).IsActive && At(x, y).Type != crackedBrick &&
            VanillaTileDefinitionCatalog.TryGet(At(x, y).TileType, out var definition) &&
            !definition.IsFrameImportant && !VanillaProjectileTileCutFacts.IsCuttable(At(x, y).TileType);
        bool Safe(int x, int y) => x >= 2 && y >= 2 && x < tiles.Dimensions.WidthTiles - 2 && y < tiles.Dimensions.HeightTiles - 2;
        bool Allowed(int x, int y) => x >= 5 && y >= 5 && x < tiles.Dimensions.WidthTiles - 5 && y < tiles.Dimensions.HeightTiles - 5 &&
            At(x, y).Wall != 350 && !DungeonPitTraps1458.Contains(pits, x, y) &&
            !(protectedEntrance is { } area && x >= area.Left && x <= area.Right && y >= area.Top && y <= area.Bottom);
        void Tip(int x, int y) { ref WorldTile t = ref At(x, y); t.Type = 48; t.Shape = 0; t.Flags |= WorldTileFlags.Active; }
    }
    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
