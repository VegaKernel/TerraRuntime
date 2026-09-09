using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary DungeonGlobalDoors search and masonry on unpublished generation tiles.</summary>
internal sealed class DungeonDoors1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random,
    CancellationToken cancellationToken)
{
    public int Place(IReadOnlyList<DungeonDoorCandidate1458> candidates, ushort brick, ushort wall, int color)
    {
        if ((brick, wall, color) is not ((41, 7, 0) or (43, 8, 1) or (44, 9, 2)))
            throw new InvalidOperationException("Unsupported ordinary dungeon door palette.");
        int placed = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int px = candidate.Position.X, py = candidate.Position.Y;
            if (px < 30 || py < 30 || px >= tiles.Dimensions.WidthTiles - 30 || py >= tiles.Dimensions.HeightTiles - 30) continue;
            if (candidate.WidthFluff is not (3 or 10) || candidate.Direction is < -1 or > 1)
                throw new InvalidOperationException("Unsupported dungeon door candidate.");
            // Source consumes the style draw even when every search column fails.
            int style = random.Next(3) == 0 ? 16 + color : 13;
            int left = Math.Clamp(px - candidate.WidthFluff, 25, tiles.Dimensions.WidthTiles - 25);
            int right = Math.Clamp(Math.Max(left, px + candidate.WidthFluff - 1), 25, tiles.Dimensions.WidthTiles - 25);
            int chosenX = 0, chosenGap = 100, chosenCeiling = 0, chosenFloor = 0;
            for (int x = left; x <= right; x++)
            {
                int ceiling = Ceiling(x, py), floor = Floor(x, py);
                if (floor < 0 || floor + 10 > tiles.Dimensions.HeightTiles) continue;
                int gap = floor - ceiling;
                if (gap < 3 || gap >= 20 || !DungeonGenerationTiles1458.IsDungeonTile(At(x, ceiling).Type) ||
                    !DungeonGenerationTiles1458.IsDungeonTile(At(x, floor).Type)) continue;
                bool valid = true;
                for (int tx = x - 20; tx < x + 20; tx++)
                for (int ty = floor - 10; ty < floor + 10; ty++)
                    if (At(tx, ty) is { IsActive: true, Type: 10 }) valid = false;
                if (valid)
                    for (int ty = floor - 3; ty < floor; ty++)
                    for (int tx = x - 3; tx <= x + 3; tx++)
                        if (At(tx, ty).IsActive) valid = false;
                bool better = candidate.Direction switch { -1 => x > chosenX, 1 => chosenX == 0 || x < chosenX, _ => gap < chosenGap };
                if (valid && better) { chosenX = x; chosenGap = gap; chosenCeiling = ceiling; chosenFloor = floor; }
            }
            if (chosenGap >= 20) continue;
            int doorBottom = chosenFloor - 1, firstAir = chosenCeiling + 1;
            for (int y = firstAir; y < doorBottom - 2; y++)
            {
                Brick(chosenX, y);
                ClearKillable(chosenX - 1, y); ClearKillable(chosenX - 2, y);
                ClearKillable(chosenX + 1, y); ClearKillable(chosenX + 2, y);
            }
            DungeonGenerationTiles1458.PlaceEntranceDoor(tiles, random, chosenX, doorBottom, style);
            if (At(chosenX, doorBottom) is { IsActive: true, Type: 10 }) placed++;
            Reinforce(-1); Reinforce(1);
            for (int y = chosenFloor - 8; y < chosenFloor; y++)
            {
                ClearSide(chosenX + 2, y); ClearSide(chosenX + 3, y);
                ClearSide(chosenX - 2, y); ClearSide(chosenX - 3, y);
            }
            Brick(chosenX - 1, chosenFloor); Brick(chosenX + 1, chosenFloor);

            void Reinforce(int side)
            {
                int x = chosenX + side, ceiling = Ceiling(x, doorBottom - 3);
                bool reinforce = doorBottom - ceiling < doorBottom - firstAir + 5 && DungeonGenerationTiles1458.IsDungeonTile(At(x, ceiling).Type);
                if (!candidate.AlwaysClearArea && !reinforce) return;
                for (int y = doorBottom - 4 - random.Next(3); y > ceiling; y--)
                {
                    if (reinforce) Brick(x, y);
                    ClearSide(x + side, y); ClearSide(x + side * 2, y);
                }
            }
            void ClearSide(int x, int y)
            {
                if (candidate.AlwaysClearArea || At(x, y).Type == brick) Clear(x, y);
            }
        }
        return placed;

        void Brick(int x, int y) => DungeonGenerationTiles1458.SetBrick(ref At(x, y), brick, false);
        void Clear(int x, int y) { At(x, y) = new WorldTile { Wall = wall }; }
        void ClearKillable(int x, int y)
        {
            ref WorldTile cell = ref At(x, y);
            if (!cell.IsActive || cell.Wall == 350 || !WorldSmoothingCatalog1458.CanRemoveTileBelow(At(x, y - 1), cell.TileType)) return;
            // WorldGen.CanKillTile's chest/boulder/locked-door branches need object state. They are
            // not admitted by this ordinary pre-furniture generation slice; never guess destruction.
            if (!VanillaTileDefinitionCatalog.TryGet(cell.TileType, out var definition) || definition.IsFrameImportant) return;
            Clear(x, y);
        }
    }

    private int Ceiling(int x, int y)
    {
        while (y > 10 && !At(x, y).IsActive) y--;
        return y;
    }
    private int Floor(int x, int y)
    {
        // Vanilla assumes a floor exists. A malformed candidate must not read past an isolated workspace.
        while (y < tiles.Dimensions.HeightTiles && !At(x, y).IsActive) y++;
        return y == tiles.Dimensions.HeightTiles ? -1 : y;
    }
    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
