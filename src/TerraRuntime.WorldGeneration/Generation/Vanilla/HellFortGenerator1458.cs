using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>Ordinary 1.4.5.8 AddHellHouses placement and HellFort structure/connection slice.
/// Lighting and furniture follow in their own phase helpers; paintings/banners and special seeds remain unadmitted.</summary>
internal static class HellFortGenerator1458
{
    internal const ushort ObsidianBrick = 75, HellstoneBrick = 76;
    internal const ushort ObsidianWall = 14, HellstoneWall = 13;
    private const int Columns = 5, Rows = 10;
    // Vanilla contains unbounded rejection loops. An exhausted candidate fails generation; no substitute layout.
    private const int RejectionLimit = 100_000;

    public static int Generate(WorldTileStore store, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
    {
        int width = store.Dimensions.WidthTiles, height = store.Dimensions.HeightTiles;
        int quarter = width / 4, count = 0;
        for (int x = 100; x < width - 100; x++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (x < quarter || x > width - quarter) continue;
            int y = height - 40;
            while (y > 0 && (At(store, x, y).IsActive || At(store, x, y).LiquidAmount > 0)) y--;
            if (y == 0 || !At(store, x, y + 1).IsActive) continue;
            ushort brick = (ushort)random.Next(75, 77);
            if (random.Next(5) > 0) brick = ObsidianBrick;
            ushort wall = brick == ObsidianBrick ? ObsidianWall : HellstoneWall;
            count += Build(store, x, y, brick, wall, random, cancellationToken) > 0 ? 1 : 0;
            x += random.Next(30, 130);
            if (random.Next(10) == 0) x += random.Next(0, 200);
        }
        return count;
    }

    internal static int Build(WorldTileStore store, int anchorX, int anchorY, ushort brick, ushort wall,
        IWorldGenerationVanillaRandom random, CancellationToken cancellationToken = default)
    {
        if ((brick, wall) is not ((ObsidianBrick, ObsidianWall) or (HellstoneBrick, HellstoneWall)))
            throw new ArgumentOutOfRangeException(nameof(brick));
        int[] left = new int[Columns], right = new int[Columns], top = new int[Rows], bottom = new int[Rows];
        left[2] = anchorX - random.Next(4, 10);
        right[2] = anchorX + random.Next(4, 10);
        for (int col = 1; col >= 0; col--) { right[col] = left[col + 1]; left[col] = right[col] - random.Next(8, 20); }
        for (int col = 3; col < Columns; col++) { left[col] = right[col - 1]; right[col] = left[col] + random.Next(8, 20); }
        top[3] = anchorY - random.Next(6, 12);
        bottom[3] = anchorY;
        for (int row = 4; row < Rows; row++) { top[row] = bottom[row - 1]; bottom[row] = top[row] + random.Next(6, 12); }
        for (int row = 2; row >= 0; row--) { bottom[row] = top[row + 1]; top[row] = bottom[row] - random.Next(6, 12); }

        var rooms = new bool[Columns, Rows];
        var leftDoors = new bool[Columns, Rows];
        var rightDoors = new bool[Columns, Rows];
        bool hasWing = false;
        for (int attempt = 0; attempt < 2; attempt++)
        for (int side = 0; side < 2; side++)
        {
            if (random.Next(3) != 0) continue;
            hasWing = true;
            _ = random.Next(10); // Source clamps this draw to the current [3,3] center interval.
            int row = 3, inner = side == 0 ? 1 : 3, outer = side == 0 ? 0 : 4;
            rooms[inner, row] = true;
            int column = inner;
            if (random.Next(2) == 0) { rooms[outer, row] = true; column = outer; }
            int direction = random.Next(2) == 0 ? -1 : 1;
            // The source's Next(10) is a zero/nonzero gate, NOT a decreasing room budget.
            if (random.Next(10) > 0)
                for (; row >= 0 && row < Rows; row += direction) rooms[column, row] = true;
        }

        int height = store.Dimensions.HeightTiles;
        for (int col = 0; col < Columns; col++)
        {
            bool blocked = left[col] < 10 || left[col] > store.Dimensions.WidthTiles - 10;
            if (!blocked)
                for (int y = height - 200; y < height; y++) blocked |= At(store, left[col], y).Wall > 0;
            if (blocked)
                for (int row = 0; row < Rows; row++) rooms[col, row] = false;
        }
        _ = random.Next(10); _ = random.Next(10); // Two overwritten draws in HellFort are still observable RNG.
        int first = 3, last = 3;
        for (int attempt = 0; !hasWing && last - first < 5; attempt++)
        {
            CheckBudget(attempt, cancellationToken);
            first = Math.Min(first, random.Next(10));
            last = Math.Max(last, random.Next(10));
        }
        for (int row = first; row <= last; row++) rooms[2, row] = true;

        int roomCount = 0;
        for (int col = 0; col < Columns; col++)
        for (int row = 0; row < Rows; row++)
        {
            if (top[row] < height - 200 || bottom[row] > height - 20) rooms[col, row] = false;
            if (!rooms[col, row]) continue;
            // Refuse out-of-world footprints rather than clipping a half-built structure.
            if (!InWorld(store, left[col], top[row], 10) || !InWorld(store, right[col], bottom[row], 10))
                throw new InvalidOperationException("HellFort footprint escaped the admitted world interior.");
            roomCount++;
            for (int x = left[col]; x <= right[col]; x++)
            for (int y = top[row]; y <= bottom[row]; y++)
            {
                ref WorldTile tile = ref At(store, x, y);
                tile.LiquidAmount = 0;
                if (x == left[col] || x == right[col] || y == top[row] || y == bottom[row])
                    SetTile(ref tile, brick, 0, 0);
                else { tile.Flags &= ~WorldTileFlags.Active; tile.Wall = wall; }
            }
        }

        for (int col = 0; col < Columns - 1; col++)
        {
            var candidates = new bool[Rows];
            for (int row = 0; row < Rows; row++) candidates[row] = rooms[col, row] && rooms[col + 1, row];
            SelectDoor(store, col, right, bottom, candidates, rightDoors, wall, random, cancellationToken);
        }
        for (int col = 0; col < Columns; col++)
        for (int row = 0; row < Rows; row++)
        {
            if (!rooms[col, row]) continue;
            if (row > 0 && rooms[col, row - 1])
            {
                if (!TryPlatformSpan(left[col], right[col], random, out int start, out int end)) break;
                PlacePlatforms(store, start, end, top[row], wall);
            }
            if (col < Columns - 1 && rooms[col + 1, row] && random.Next(3) == 0)
                TryDoor(store, right[col], bottom[row], wall, random);
        }
        for (int side = 0; side < 2; side++)
        {
            int direction = side == 0 ? -1 : 1;
            for (int col = side == 0 ? 0 : Columns - 1; col >= 0 && col < Columns; col -= direction)
            {
                bool occupied = false;
                var candidates = new bool[Rows];
                int x = side == 0 ? left[col] : right[col];
                for (int row = 0; row < Rows; row++)
                {
                    occupied |= rooms[col, row];
                    candidates[row] = rooms[col, row] && IsDryOpen(store, x + direction, bottom[row] - 3, bottom[row] - 1);
                }
                if (!occupied) continue;
                SelectDoor(store, col, side == 0 ? left : right, bottom, candidates,
                    side == 0 ? leftDoors : rightDoors, 0, random, cancellationToken);
                break;
            }
        }
        for (int row = 0; row < Rows; row++)
        {
            bool occupied = false;
            for (int col = 0; col < Columns; col++) occupied |= rooms[col, row];
            if (!occupied) continue;
            int column = 0;
            for (int attempt = 0; ; attempt++)
            {
                CheckBudget(attempt, cancellationToken);
                column = random.Next(Columns);
                if (rooms[column, row]) break;
            }
            if (TryPlatformSpan(left[column], right[column], random, out int start, out int end))
            {
                bool clear = true;
                for (int x = start; x <= end; x++) clear &= IsDryOpen(store, x, top[row] - 1, top[row] - 1);
                if (clear) PlacePlatforms(store, start, end, top[row], null);
            }
            break;
        }
        for (int col = 0; col < Columns; col++)
        for (int row = 0; row < Rows; row++)
        {
            if (!rooms[col, row]) continue;
            if (!leftDoors[col, row]) Crumble(store, left[col], top[row], bottom[row], -1, random);
            if (!rightDoors[col, row]) Crumble(store, right[col], top[row], bottom[row], 1, random);
        }
        return roomCount;
    }

    private static void SelectDoor(WorldTileStore store, int column, int[] edge, int[] bottom, bool[] candidates,
        bool[,] doors, ushort wall, IWorldGenerationVanillaRandom random, CancellationToken cancellationToken)
    {
        if (!candidates.Contains(true)) return;
        for (int attempt = 0; ; attempt++)
        {
            CheckBudget(attempt, cancellationToken);
            int row = random.Next(Rows);
            if (!candidates[row]) continue;
            doors[column, row] = TryDoor(store, edge[column], bottom[row], wall, random);
            if (doors[column, row]) return;
        }
    }

    private static bool TryDoor(WorldTileStore store, int x, int floor, ushort wall, IWorldGenerationVanillaRandom random)
    {
        if (!InWorld(store, x, floor, 10) || !Solid(At(store, x, floor - 4)) || !Solid(At(store, x, floor))) return false;
        for (int dy = 0; dy < 3; dy++)
        {
            ref WorldTile tile = ref At(store, x, floor - 3 + dy);
            if (dy == 2) ClearPlacementAnchor(ref tile);
            // PlaceDoor consumes one appearance draw for EACH cell; Obsidian door is style 19, not frameX 19*54.
            SetTile(ref tile, 10, (short)(random.Next(3) * 18), (short)(19 * 54 + dy * 18));
            if (wall > 0) tile.Wall = wall;
        }
        return true;
    }

    private static bool TryPlatformSpan(int left, int right, IWorldGenerationVanillaRandom random, out int start, out int end)
    {
        start = random.Next(left + 2, right - 1); end = random.Next(left + 2, right - 1);
        for (int attempt = 0; end - start < 2 || end - start > 5; attempt++)
        {
            start = random.Next(left + 2, right - 1); end = random.Next(left + 2, right - 1);
            if (attempt >= 10_000) return false;
        }
        return true;
    }

    private static void PlacePlatforms(WorldTileStore store, int start, int end, int y, ushort? wall)
    {
        for (int x = start; x <= end; x++)
        {
            ref WorldTile tile = ref At(store, x, y);
            ClearPlacementAnchor(ref tile);
            SetTile(ref tile, 19, 0, 13 * 18);
            if (wall.HasValue) tile.Wall = wall.Value;
        }
    }

    private static void Crumble(WorldTileStore store, int x, int top, int bottom, int direction, IWorldGenerationVanillaRandom random)
    {
        if (random.Next(2) == 0) return;
        for (int y = top + 1; y < bottom; y++)
        {
            WorldTile outside = At(store, x + direction, y);
            if (outside.Wall > 0 || outside.LiquidAmount > 0 || Solid(outside, noDoors: false)) return;
        }
        int first = top + 1;
        int end = first + (int)((bottom - top - 3) * (float)random.NextDouble()) + random.Next(2);
        end = Math.Min(bottom - 1, Math.Max(first + 2, end));
        for (int y = first; y <= end; y++)
        {
            ref WorldTile tile = ref At(store, x, y);
            if (!tile.IsActive) continue;
            tile.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive);
            tile.Shape = 0; // Tile.ClearTile preserves inactive type, frames, paint and wiring.
        }
    }

    internal static bool Solid(in WorldTile tile, bool noDoors = true) => tile.IsActive && !tile.IsActuated && tile.Shape == 0 &&
        (!noDoors || tile.Type != 10) && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
        !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);

    private static bool IsDryOpen(WorldTileStore store, int x, int top, int bottom)
    {
        if (!InWorld(store, x, top, 1) || !InWorld(store, x, bottom, 1)) return false;
        for (int y = top; y <= bottom; y++) if (At(store, x, y).IsActive || At(store, x, y).LiquidAmount > 0) return false;
        return true;
    }
    private static void SetTile(ref WorldTile tile, ushort type, short frameX, short frameY)
    {
        tile.Flags |= WorldTileFlags.Active; tile.Type = type; tile.Shape = 0; tile.FrameX = frameX; tile.FrameY = frameY;
    }
    internal static void ClearPlacementAnchor(ref WorldTile tile)
    {
        // PlaceTile clears Tile|TilePaint|Slope on its inactive anchor, not all cells of a multi-tile object.
        tile.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        tile.Type = 0; tile.FrameX = 0; tile.FrameY = 0; tile.Shape = 0; tile.TileColor = 0;
    }
    private static bool InWorld(WorldTileStore store, int x, int y, int margin) => x >= margin && y >= margin &&
        x < store.Dimensions.WidthTiles - margin && y < store.Dimensions.HeightTiles - margin;
    private static void CheckBudget(int attempt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (attempt >= RejectionLimit) throw new InvalidOperationException("HellFort rejection search exhausted its safety budget.");
    }
    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
