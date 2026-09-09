using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal readonly record struct DungeonPlatformCandidate1458(DungeonPoint1458 Position)
{
    public int HeightFluff { get; init; } = 5;
    public bool ForcePlacement { get; init; }
    public double PotsChance { get; init; }
    public double BooksChance { get; init; }
    public double PotionsChance { get; init; }
    public bool NoWaterbolt { get; init; }
    public bool InAHallway { get; init; }
}

/// <summary>Ordinary DungeonGlobalPlatforms placement and ordered shelf objects on unpublished generation tiles.</summary>
internal sealed class DungeonPlatforms1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random,
    double worldSurface, double rockLayer, CancellationToken cancellationToken)
{
    public int Place(IReadOnlyList<DungeonPlatformCandidate1458> candidates, int dungeonColor)
    {
        var placement = new DungeonObjectPlacement1458(tiles, random);
        int style = dungeonColor switch { 0 => 6, 1 => 8, 2 => 7, _ => throw new InvalidOperationException("Unknown dungeon color.") };
        int placed = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = candidate.Position.X, y = candidate.Position.Y, fluff = candidate.HeightFluff;
            if (x < 30 || y < 30 || x >= tiles.Dimensions.WidthTiles - 30 || y >= tiles.Dimensions.HeightTiles - 30) continue;
            if (fluff is < 0 or > 5) throw new InvalidOperationException("Unsupported dungeon platform clearance.");
            int maxLength = y < worldSurface + 50 ? 20 : 10, selectedY = -1;
            for (int row = y - fluff; row <= y + fluff; row++)
            {
                int left = x, right = x;
                bool valid = candidate.ForcePlacement || !At(x, row).IsActive;
                if (valid)
                {
                    while (!At(left, row).IsActive)
                    {
                        left--;
                        if (!candidate.ForcePlacement && At(left, row).IsActive && !DungeonGenerationTiles1458.IsDungeonTile(At(left, row).Type)) { valid = false; break; }
                        if (left <= 10) break;
                    }
                    while (!At(right, row).IsActive)
                    {
                        right++;
                        if (!candidate.ForcePlacement && At(right, row).IsActive && !DungeonGenerationTiles1458.IsDungeonTile(At(right, row).Type)) { valid = false; break; }
                        if (right >= tiles.Dimensions.WidthTiles - 10) break;
                    }
                }
                if (!valid || !candidate.ForcePlacement && right - left > maxLength) continue;
                if (!candidate.ForcePlacement)
                {
                    for (int tx = x - maxLength / 2 - 2; tx <= x + maxLength / 2 + 2; tx++)
                    for (int ty = row - fluff; ty <= row + fluff; ty++)
                        if (At(tx, ty) is { IsActive: true, Type: 19 }) valid = false;
                    for (int ty = row + 3; ty >= row - 5; ty--) if (At(x, ty).IsActive) valid = false;
                }
                if (valid) { selectedY = row; break; }
            }
            if (selectedY < 0) continue;
            int platformStyle = At(x, y).Wall is >= 94 and <= 105 ? 54 : style;
            int begin = x, end = x + 1;
            while (!At(begin, selectedY).IsActive)
            {
                placement.SetPlatform(begin, selectedY, platformStyle);
                if (--begin <= 10) break;
            }
            while (!At(end, selectedY).IsActive)
            {
                placement.SetPlatform(end, selectedY, platformStyle);
                if (++end >= tiles.Dimensions.WidthTiles - 10) break;
            }
            placed++;
            for (int tx = begin; tx < end; tx++)
            {
                if (candidate.PotsChance > 0 && random.NextDouble() < candidate.PotsChance) placement.Pot(tx, selectedY - 1);
                else if (candidate.PotionsChance > 0 && random.NextDouble() < candidate.PotionsChance)
                {
                    placement.TableObject(tx, selectedY - 1, 13);
                    if (At(tx, selectedY - 1).Type == 13) At(tx, selectedY - 1).FrameX = random.Next(2) == 0 ? (short)18 : (short)36;
                }
                else if (candidate.BooksChance > 0 && random.NextDouble() < candidate.BooksChance)
                {
                    bool waterbolt = !candidate.NoWaterbolt && random.Next(50) == 0;
                    int bookY = selectedY - 1;
                    placement.TableObject(tx, bookY, 50);
                    // The pinned source checks [placeY,placeY], not [placeX,placeY]. Preserve that rule.
                    if (waterbolt && bookY > (worldSurface + rockLayer) / 2 && At(bookY, bookY).Type == 50) At(tx, bookY).FrameX = 90;
                }
            }
        }
        return placed;
    }


    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
