using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary AddHellHouses ground-furniture phase, TerrariaServer 1.4.5.8.
/// Placement is generation-only on an isolated Workspace, never client tile authority.</summary>
internal static class HellFortFurniture1458
{
    // Source clearance half-width and height above the sampled floor, not the object's footprint.
    private static ReadOnlySpan<int> ClearanceX => [5, 4, 3, 4, 3, 5, 5, 5, 5, 3, 5, 2, 3];
    private static ReadOnlySpan<int> ClearanceY => [4, 3, 5, 6, 3, 3, 4, 4, 4, 5, 3, 4, 3];

    internal static int AttemptCount(int width) => (int)Math.Ceiling(4_200_000d / width);

    public static void Generate(Workspace workspace, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
    {
        WorldTileStore store = workspace.TileStore;
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles, quarter = (int)(width * .25);
        for (int attempt = 0; attempt < AttemptCount(width); attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(quarter, width - quarter), y = random.Next(height - 250, height - 20), retries = 0;
            while (!IsHouseAir(At(store, x, y)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                x = random.Next(quarter, width - quarter); y = random.Next(height - 250, height - 20);
                if (++retries > 100_000) break;
            }
            if (retries > 100_000 || !IsHouseAir(At(store, x, y))) continue;
            TryPlaceCandidate(workspace, x, y, random);
        }
    }

    internal static bool TryPlaceCandidate(Workspace workspace, int x, int y, IWorldGenerationVanillaRandom random)
    {
        WorldTileStore store = workspace.TileStore;
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles;
        if (x < 6 || x >= width - 6 || y < 7 || y >= height - 20 || !IsHouseAir(At(store, x, y))) return false;
        while (!SolidFloor(At(store, x, y)) && y < height - 20) y++;
        y--;
        int left = x, right = x;
        while (left > 0 && !At(store, left, y).IsActive && SolidFloor(At(store, left, y + 1))) left--;
        while (right < width - 1 && !At(store, right, y).IsActive && SolidFloor(At(store, right, y + 1))) right++;
        if (left == 0 || right == width - 1) return false;
        left++; right--;
        int center = (left + right) / 2;
        if (!IsHouseAir(At(store, center, y)) || !SolidFloor(At(store, center, y + 1))) return false;
        int choice = random.Next(13), halfWidth = ClearanceX[choice], above = ClearanceY[choice];
        if (center - halfWidth < 0 || center + halfWidth >= width || y - above < 0) return false;
        for (int scanX = center - halfWidth; scanX <= center + halfWidth; scanX++)
        for (int scanY = y - above; scanY <= y; scanY++)
            if (At(store, scanX, scanY).IsActive) return false;
        if (right - left < halfWidth * 1.75) return false;
        PlaceArrangement(workspace, center, y, choice, random);
        return true; // Admitted arrangement, not a guarantee that every individual PlaceTile succeeded.
    }

    internal static void PlaceArrangement(Workspace workspace, int x, int y, int choice, IWorldGenerationVanillaRandom random)
    {
        WorldTileStore store = workspace.TileStore;
        switch (choice)
        {
            case 0:
                Place(workspace, x, y, 14);
                int candleOnTable = random.Next(6);
                if (candleOnTable < 3) Place(workspace, x + candleOnTable, y - 2, 33);
                if (!At(store, x, y).IsActive) break;
                Chair(workspace, x - 2, y, faceRight: true);
                Chair(workspace, x + 2, y, faceRight: false);
                break;
            case 1:
                Place(workspace, x, y, 18);
                int candleOnBench = random.Next(4);
                if (candleOnBench < 2) Place(workspace, x + candleOnBench, y - 1, 33);
                if (!At(store, x, y).IsActive) break;
                bool chairOnLeft = random.Next(2) == 0;
                Chair(workspace, x + (chairOnLeft ? -1 : 2), y, chairOnLeft);
                break;
            case 2: Place(workspace, x, y, 105); break;
            case 3: Place(workspace, x, y, 101); break;
            case 4:
                bool faceRight = random.Next(2) == 0;
                Place(workspace, x, y, 15);
                // Source applies this frame change even if placement rejected.
                if (faceRight) TurnChair(store, x, y);
                break;
            case 5: Place(workspace, x, y, 79, random.Next(2) == 0); break;
            case 6: Place(workspace, x, y, 87); break;
            case 7: Place(workspace, x, y, 88); break;
            case 8: Place(workspace, x, y, 89); break;
            case 9: Place(workspace, x, y, 104); break;
            case 10: Place(workspace, x, y, 90, random.Next(2) == 0); break;
            case 11: Place(workspace, x, y, 93); break;
            case 12: Place(workspace, x, y, 100); break;
            default: throw new ArgumentOutOfRangeException(nameof(choice));
        }
    }

    // Exact type/style/atlas facts from AddHellHouses and Place1x2/1xX/2x1/2xX/2x2/3x2/3x4/4x2/OnTable1x1.
    // The caller's cleared room admits only solid floors and its own table/workbench candle supports.
    internal static bool Place(Workspace workspace, int x, int bottom, ushort type, bool faceRight = false)
    {
        (int offset, int width, int height, int frameX, int frameY) = type switch
        {
            14 => (-1, 3, 2, 13 * 54, 0),
            15 => (0, 1, 2, 0, 16 * 40),
            18 => (0, 2, 1, 14 * 36, 0),
            33 => (0, 1, 1, 0, 25 * 22),
            79 => (-1, 4, 2, faceRight ? 72 : 0, 8 * 36),
            87 => (-1, 3, 2, 15 * 54, 0),
            88 => (-1, 3, 2, 9 * 54, 0),
            89 => (-1, 3, 2, 10 * 54, 0),
            90 => (-1, 4, 2, faceRight ? 72 : 0, 25 * 36),
            93 => (0, 1, 3, 0, 23 * 54),
            100 => (-1, 2, 2, 0, 25 * 36),
            101 => (-1, 3, 4, 4 * 54, 0),
            104 => (0, 2, 5, 17 * 36, 0),
            105 => (0, 2, 3, 49 * 36, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        WorldTileStore store = workspace.TileStore;
        if (x < 5 || x > store.Dimensions.WidthTiles - 5 || bottom < 6 || bottom > store.Dimensions.HeightTiles - 5) return false;
        // Beds/sofas call Place4x2 directly; all other choices go through PlaceTile's inactive-anchor cleanup.
        if (type is not (79 or 90) && !At(store, x, bottom).IsActive)
            HellFortGenerator1458.ClearPlacementAnchor(ref At(store, x, bottom));
        int left = x + offset, top = bottom - height + 1;
        for (int column = left; column < left + width; column++)
        {
            WorldTile support = At(store, column, bottom + 1);
            bool supported = type == 33
                ? support.IsActive && !support.IsActuated && support.Shape == 0 && support.Type is 14 or 18
                : SolidSupport(support);
            if (!supported) return false;
            for (int row = top; row <= bottom; row++)
                if (At(store, column, row).IsActive || (type == 93 && At(store, column, row).LiquidAmount > 0)) return false;
        }
        // Preserve the footprint if the shared chest registry refuses a dresser (capacity/duplicate).
        Span<WorldTile> dresserBefore = stackalloc WorldTile[6];
        if (type == 88)
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 3; dx++) dresserBefore[dy * 3 + dx] = At(store, left + dx, top + dy);
        for (int dy = 0; dy < height; dy++)
        for (int dx = 0; dx < width; dx++)
        {
            ref WorldTile tile = ref At(store, left + dx, top + dy);
            tile.Flags |= WorldTileFlags.Active; tile.Type = type;
            tile.FrameX = (short)(frameX + dx * 18); tile.FrameY = (short)(frameY + dy * 18);
        }
        if (type == 88 && !workspace.TryAddGeneratedChest(left, top, string.Empty, []))
        {
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 3; dx++) At(store, left + dx, top + dy) = dresserBefore[dy * 3 + dx];
            return false;
        }
        return true;
    }

    private static void Chair(Workspace workspace, int x, int y, bool faceRight)
    {
        WorldTileStore store = workspace.TileStore;
        if (At(store, x, y).IsActive) return;
        Place(workspace, x, y, 15);
        if (faceRight && At(store, x, y).IsActive) TurnChair(store, x, y);
    }
    private static void TurnChair(WorldTileStore store, int x, int y)
    {
        At(store, x, y).FrameX += 18; At(store, x, y - 1).FrameX += 18;
    }
    private static bool IsHouseAir(in WorldTile tile) => !tile.IsActive && tile.Wall is 13 or 14;
    private static bool SolidFloor(in WorldTile tile) => SolidSupport(tile) && !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    private static bool SolidSupport(in WorldTile tile) => tile.IsActive && !tile.IsActuated && tile.Shape == 0 && VanillaTileCollisionCatalog.IsSolid(tile.TileType);
    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
