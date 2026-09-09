using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>DesertHive.AddTileVariance and its ordinary generation-only placement slice.</summary>
internal sealed class DesertDecoration1458(WorldTileStore tiles, IWorldGenerationVanillaRandom random)
{
    public void Apply(DesertSurface1458 description, CancellationToken cancellation = default)
    {
        WorldTileRegion hive = description.Hive;
        for (int x = hive.X - 20; x < hive.ExclusiveRight + 20; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = hive.Y - 20; y < hive.ExclusiveBottom + 20; y++)
            {
                if (!Inside(x, y, 1)) continue;
                ref WorldTile tile = ref At(x, y);
                if (tile.Type == 53 && (!Flat(x, y + 1) || !Flat(x, y + 2))) tile.Type = 397;
            }
        }
        for (int x = hive.X - 20; x < hive.ExclusiveRight + 20; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = hive.Y - 20; y < hive.ExclusiveBottom + 20; y++)
            {
                if (!Inside(x, y, 5) || !At(x, y).IsActive || At(x, y).Type != 396) continue;
                bool up = ClearSide(-1), down = ClearSide(1);
                if (up && random.Next(20) == 0)
                {
                    ushort type = 485;
                    int style = random.Next(4);
                    if (random.Next(30) == 0) { type = 751; style = 0; }
                    Place(x, y - 1, type, style);
                }
                else if (up && random.Next(5) == 0) Place(x, y - 1, 484, 0);
                else if ((up ^ down) && random.Next(5) == 0) Place(x, y + (up ? -1 : 1), 165, 0);
                else if (up && random.Next(5) == 0) Place(x, y - 1, 187, 29 + random.Next(6));

                bool ClearSide(int direction)
                {
                    for (int step = 1; step <= 3; step++)
                        if (At(x, y + step * direction).IsActive || At(x + 1, y + step * direction).IsActive) return false;
                    return true;
                }
            }
        }
    }

    private void Place(int x, int y, ushort type, int style)
    {
        // PlaceTile clears only the inactive anchor before trying the footprint.
        ref WorldTile anchor = ref At(x, y);
        if (!anchor.IsActive)
        {
            anchor.Type = 0; anchor.FrameX = anchor.FrameY = 0;
            anchor.Shape = anchor.TileColor = 0;
            anchor.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
        }
        if (type == 165) Stalactite(x, y);
        else if (type == 485) AntlionLarva(x, y, style);
        else if (type is 484 or 751 or 187) Furniture(x, y, type, style);
        else throw new InvalidOperationException("Unverified desert decoration.");
        // Source has an explicit square even on failed PlaceTight/Place3x2.
        if (type is 165 or 187 || At(x, y).IsActive) FrameSquare(x, y);
    }

    private void Furniture(int x, int y, ushort type, int style)
    {
        int width = type == 187 ? 3 : 2;
        for (int col = x - 1; col < x - 1 + width; col++)
        {
            if (At(col, y - 1).IsActive || At(col, y).IsActive || !Flat(col, y + 1)) return;
        }
        for (int col = 0; col < width; col++)
        for (int row = 0; row < 2; row++)
        {
            ref WorldTile cell = ref At(x - 1 + col, y - 1 + row);
            cell.Type = type; cell.Flags |= WorldTileFlags.Active;
            cell.FrameX = (short)(col * 18 + (type == 187 ? style * 54 : 0));
            cell.FrameY = (short)(row * 18 + (type == 187 ? 0 : style * 36));
        }
        if (type == 187)
        {
            bool supported = true;
            for (int col = x - 1; col <= x + 1; col++)
                supported &= At(col, y + 1).Type is 53 or 396 or 397;
            // Check3x2 enforces the material-specific rule AFTER Place3x2. Skipping
            // placement would leave the wrong inactive type/frames at a rejected pile.
            if (!supported)
                for (int col = x - 1; col <= x + 1; col++)
                for (int row = y - 1; row <= y; row++)
                {
                    ref WorldTile cell = ref At(col, row);
                    cell.Type = 0; cell.FrameX = cell.FrameY = -1;
                    cell.Shape = cell.TileColor = 0;
                    cell.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive |
                        WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
                    FrameSquare(col, row);
                }
        }
    }

    private void AntlionLarva(int x, int y, int style)
    {
        bool allowed = true;
        for (int col = x; col < x + 2; col++)
        {
            if (!Flat(col, y + 1)) allowed = false;
            for (int row = y - 1; row <= y; row++)
            {
                WorldTile tile = At(col, row);
                if (tile.IsActive || tile.LiquidAmount > 0 && tile.LiquidKind == WorldLiquidKind.Lava) allowed = false;
            }
        }
        // CanPlace rolls even on rejected anchors. PlaceObject then overrides random
        // with its default -1, so this draw does not randomize the placed style.
        _ = random.Next(4);
        if (!allowed) return;
        for (int col = 0; col < 2; col++)
        for (int row = 0; row < 2; row++)
        {
            ref WorldTile cell = ref At(x + col, y - 1 + row);
            cell.Type = 485; cell.Flags |= WorldTileFlags.Active;
            cell.FrameX = (short)(style * 36 + col * 18); cell.FrameY = (short)(row * 18);
        }
    }

    private void Stalactite(int x, int y)
    {
        if (At(x, y).LiquidAmount > 0 && At(x, y).LiquidKind == WorldLiquidKind.Shimmer) return;
        bool small = random.Next(2) == 0;
        int variation = random.Next(3);
        bool ceiling = Flat(x, y - 1) && !At(x, y).IsActive && !At(x, y + 1).IsActive;
        if (!ceiling && (!Flat(x, y + 1) || At(x, y).IsActive || At(x, y - 1).IsActive)) return;
        WorldTile support = At(x, y + (ceiling ? -1 : 1));
        int frameBase = support.Type switch { 396 or 397 => 378, 1 => 54, _ => -1 };
        if (frameBase < 0) throw new InvalidOperationException("Unverified desert speleothem support.");
        if (small) SetPart(y, ceiling ? 72 : 90);
        else if (ceiling) { SetPart(y, 0); SetPart(y + 1, 18); }
        else { SetPart(y - 1, 36); SetPart(y, 54); }

        void SetPart(int row, int frameY)
        {
            ref WorldTile cell = ref At(x, row);
            cell.Type = 165; cell.Shape = 0; cell.TileColor = support.TileColor;
            cell.Flags = (cell.Flags & ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock)) |
                WorldTileFlags.Active | (support.Flags & (WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock));
            cell.FrameX = (short)(frameBase + variation * 18); cell.FrameY = (short)frameY;
        }
    }

    internal void FrameSquare(int x, int y)
    {
        for (int col = x - 1; col <= x + 1; col++)
        for (int row = y - 1; row <= y + 1; row++)
            FrameCell(col, row);
    }

    internal void FrameNeighbours(int x, int y)
    {
        FrameCell(x, y); FrameCell(x + 1, y); FrameCell(x - 1, y); FrameCell(x, y + 1); FrameCell(x, y - 1);
    }

    private void FrameCell(int x, int y)
    {
        if (x <= 5 || y <= 5 || x >= tiles.Dimensions.WidthTiles - 5 || y >= tiles.Dimensions.HeightTiles - 5) return;
        ref WorldTile cell = ref At(x, y);
        if (cell.IsActive) return; // geometry is unchanged after complete admitted placements
        cell.Shape = cell.TileColor = 0;
        cell.Flags &= ~(WorldTileFlags.InvisibleBlock | WorldTileFlags.FullbrightBlock);
    }

    private bool Flat(int x, int y)
    {
        WorldTile cell = At(x, y);
        return cell.Shape == 0 && DesertHive1458.Solid(cell);
    }
    private bool Inside(int x, int y, int border) => x >= border && y >= border &&
        x < tiles.Dimensions.WidthTiles - border && y < tiles.Dimensions.HeightTiles - border;
    private ref WorldTile At(int x, int y) => ref tiles.Tiles[tiles.GetUncheckedIndex(x, y)];
}
