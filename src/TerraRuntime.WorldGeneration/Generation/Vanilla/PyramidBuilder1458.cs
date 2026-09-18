using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>The treasure chamber a built pyramid produced, so the chest owner can fill it.</summary>
internal readonly record struct VanillaPyramidChamber1458(int ChestX, int ChestY, int PrimaryItemType);

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>WorldGen.Pyramid</c>.
/// </summary>
/// <remarks>
/// The visible shape is only the first third of this method. What makes a pyramid read as a pyramid rather than
/// a solid wedge is what follows: a sand-capped entry cut through one flank, a staircase that switches direction
/// every few tens of rows as it descends, one treasure chamber opened part-way down, and a wandering exit tunnel
/// that keeps going until it breaks into open ground. The runtime previously built the wedge and none of the
/// rest, which is exactly the "unfinished structure with no descent" a tester sees.
/// </remarks>
internal sealed class PyramidBuilder1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    CancellationToken cancellation)
{
    private const ushort SandstoneBrick = 151;
    private const ushort Sand = 53;
    private const ushort SandstoneBrickWall = 34;

    /// <summary>Source defaults for the registered Pyramids pass.</summary>
    internal const int DefaultMinimumDepth1458 = 75;
    internal const int DefaultMaximumDepth1458 = 125;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    /// <summary>
    /// Builds one pyramid at the accepted anchor. Returns the treasure chamber when the descent reached one, or
    /// <c>null</c> when the site was refused before any cell was written.
    /// </summary>
    public VanillaPyramidChamber1458? TryBuild(
        int i,
        int j,
        int minimumDepth = DefaultMinimumDepth1458,
        int maximumDepth = DefaultMaximumDepth1458,
        bool noTunnel = false)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Contains(i, j))
            return null;

        WorldTile anchor = At(i, j);
        if (anchor.IsActive && (anchor.Type == SandstoneBrick || anchor.Wall == SandstoneBrick))
            return null;

        int apex = j - random.Next(0, 7);
        int entryOffset = random.Next(9, 13);
        int halfWidth = 1;
        int baseRow = j + random.Next(minimumDepth, maximumDepth);

        for (int row = apex; row < baseRow; row++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int column = i - halfWidth; column < i + halfWidth - 1; column++)
                WriteBrick(column, row);
            halfWidth++;
        }

        FillInteriorWalls(i, j, halfWidth, baseRow);

        int direction = random.Next(2) == 0 ? -1 : 1;
        int cursorX = i - entryOffset * direction;
        int cursorY = j + entryOffset;
        int corridorHeight = random.Next(5, 8);
        int runLength = random.Next(20, 30);

        CutEntry(ref cursorX, cursorY, direction, corridorHeight);

        cursorX = i - entryOffset * direction;
        VanillaPyramidChamber1458? chamber = Descend(
            ref cursorX,
            ref cursorY,
            ref direction,
            ref runLength,
            corridorHeight,
            baseRow,
            noTunnel);

        if (!noTunnel)
            DigExitTunnel(cursorX, cursorY, direction, corridorHeight);

        return chamber;
    }

    /// <summary>
    /// Backs the solid wedge with Sandstone Brick Wall. Only cells whose entire three-by-three neighbourhood is
    /// brick get a wall, which is what leaves the pyramid's outer face bare and its inside papered.
    /// </summary>
    private void FillInteriorWalls(int i, int j, int halfWidth, int baseRow)
    {
        for (int column = i - halfWidth - 5; column <= i + halfWidth + 5; column++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int row = j - 1; row <= baseRow + 1; row++)
            {
                bool enclosed = true;
                for (int nx = column - 1; enclosed && nx <= column + 1; nx++)
                {
                    for (int ny = row - 1; ny <= row + 1; ny++)
                    {
                        if (!Contains(nx, ny) || !At(nx, ny).IsActive || At(nx, ny).Type != SandstoneBrick)
                        {
                            enclosed = false;
                            break;
                        }
                    }
                }

                if (!enclosed || !Contains(column, row))
                    continue;

                At(column, row).Wall = SandstoneBrickWall;
                SquareWallFrame(column, row);
            }
        }
    }

    /// <summary>
    /// Cuts the entry corridor inward from the flank until it stops meeting brick, walling the cells it opens.
    /// Any sand found directly above the corridor is pulled down into it, which is what caps the entrance with
    /// loose sand instead of leaving a clean brick hole in the pyramid's face.
    /// </summary>
    private void CutEntry(ref int cursorX, int cursorY, int direction, int corridorHeight)
    {
        bool cutting = true;
        while (cutting)
        {
            cancellation.ThrowIfCancellationRequested();
            cutting = false;
            bool sandAbove = false;
            for (int row = cursorY; row <= cursorY + corridorHeight; row++)
            {
                if (!Contains(cursorX, row) || !Contains(cursorX, row - 1))
                    continue;

                if (At(cursorX, row - 1).IsActive && At(cursorX, row - 1).Type == Sand)
                    sandAbove = true;

                if (At(cursorX, row).IsActive && At(cursorX, row).Type == SandstoneBrick)
                {
                    if (Contains(cursorX, row + 1))
                        At(cursorX, row + 1).Wall = SandstoneBrickWall;
                    if (Contains(cursorX + direction, row))
                        At(cursorX + direction, row).Wall = SandstoneBrickWall;
                    At(cursorX, row).Flags &= ~WorldTileFlags.Active;
                    cutting = true;
                }

                if (sandAbove)
                {
                    ref WorldTile cell = ref At(cursorX, row);
                    cell.Type = Sand;
                    cell.Flags |= WorldTileFlags.Active;
                    cell.Shape = 0;
                }
            }

            cursorX -= direction;
        }
    }

    /// <summary>
    /// Walks the staircase down through the wedge. The run length between direction changes is re-rolled at
    /// every turn, and the first turn, the chamber and every later turn each use a different range. Reaching
    /// two corridor heights above the base forces short runs so the descent finishes inside the pyramid.
    /// </summary>
    private VanillaPyramidChamber1458? Descend(
        ref int cursorX,
        ref int cursorY,
        ref int direction,
        ref int runLength,
        int corridorHeight,
        int baseRow,
        bool noTunnel)
    {
        VanillaPyramidChamber1458? chamber = null;
        bool firstTurnPending = true;
        bool chamberBuilt = false;
        bool descending = true;

        while (descending)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int row = cursorY; row <= cursorY + corridorHeight; row++)
            {
                if (Contains(cursorX, row))
                    At(cursorX, row).Flags &= ~WorldTileFlags.Active;
            }

            cursorX += direction;
            cursorY++;
            runLength--;
            if (cursorY >= baseRow - corridorHeight * 2)
                runLength = 10;

            if (runLength <= 0)
            {
                bool builtChamberNow = false;
                if (!firstTurnPending && !chamberBuilt)
                {
                    if (noTunnel)
                        descending = false;
                    chamberBuilt = true;
                    builtChamberNow = true;
                    chamber = OpenChamber(ref cursorX, cursorY, direction, corridorHeight);
                }

                if (firstTurnPending)
                {
                    firstTurnPending = false;
                    direction *= -1;
                    runLength = random.Next(15, 20);
                }
                else if (builtChamberNow)
                {
                    runLength = random.Next(10, 15);
                }
                else
                {
                    direction *= -1;
                    runLength = random.Next(20, 40);
                }
            }

            if (cursorY >= baseRow - corridorHeight)
                descending = false;
        }

        return chamber;
    }

    /// <summary>
    /// Opens the treasure chamber and decorates it. The room is carved column by column with its first three and
    /// last three columns stepped in, which rounds the corners; then the source rolls the chest's signature item,
    /// scatters one to nine sand piles, hangs four banners and lines the floor with pots.
    /// </summary>
    private VanillaPyramidChamber1458 OpenChamber(
        ref int cursorX,
        int cursorY,
        int direction,
        int corridorHeight)
    {
        int roomHeight = random.Next(7, 13);
        int remaining = random.Next(23, 28);
        int roomWidth = remaining;
        int startX = cursorX;
        int ceiling = cursorY - roomHeight + corridorHeight;

        while (remaining > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int row = ceiling; row <= cursorY + corridorHeight; row++)
            {
                bool outermost = remaining == roomWidth || remaining == 1;
                bool nextIn = remaining == roomWidth - 1 || remaining == 2 ||
                    remaining == roomWidth - 2 || remaining == 3;
                int minimumRow = outermost ? ceiling + 2 : nextIn ? ceiling + 1 : ceiling;
                if (row >= minimumRow && Contains(cursorX, row))
                    At(cursorX, row).Flags &= ~WorldTileFlags.Active;
            }

            remaining--;
            cursorX += direction;
        }

        int endX = cursorX - direction;
        int left = endX;
        int right = startX;
        if (endX > startX)
        {
            left = startX;
            right = endX;
        }

        // The signature item is rolled twice when the first roll picks the first entry, which biases the
        // pyramid away from that item without ever excluding it.
        int roll = random.Next(3);
        if (roll == 0)
            roll = random.Next(3);
        int primary = roll switch
        {
            0 => 848,
            1 => 857,
            _ => 934,
        };

        int piles = random.Next(1, 10);
        for (int pile = 0; pile < piles; pile++)
        {
            int pileX = random.Next(left, right);
            GenerationDecorationPlacement1458.TryPlaceSmallPile(
                store, pileX, cursorY + corridorHeight, random.Next(16, 19));
        }

        PlaceBanner(left + 2, cursorY - roomHeight + corridorHeight + 1);
        PlaceBanner(left + 3, cursorY - roomHeight + corridorHeight);
        PlaceBanner(right - 2, cursorY - roomHeight + corridorHeight + 1);
        PlaceBanner(right - 3, cursorY - roomHeight + corridorHeight);

        for (int potX = left; potX <= right; potX++)
        {
            GenerationDecorationPlacement1458.TryPlacePot(
                store, random, potX, cursorY + corridorHeight, random.Next(25, 28));
        }

        return new VanillaPyramidChamber1458((left + right) / 2, cursorY, primary);
    }

    /// <summary>
    /// The pyramid's banners are the only <c>PlaceTile</c> the chamber needs: a one-by-three hanging object that
    /// requires a solid ceiling and two clear cells below it.
    /// </summary>
    private void PlaceBanner(int x, int y)
    {
        int style = random.Next(4, 7);
        if (!Contains(x, y - 1) || !Contains(x, y + 2))
            return;

        WorldTile ceiling = At(x, y - 1);
        if (!ceiling.IsActive || ceiling.IsActuated || ceiling.Shape != 0 ||
            !VanillaTileCollisionCatalog.IsSolid(ceiling.TileType))
        {
            return;
        }

        for (int row = y; row < y + 3; row++)
        {
            if (At(x, row).IsActive)
                return;
        }

        for (int row = 0; row < 3; row++)
        {
            ref WorldTile cell = ref At(x, y + row);
            cell.Flags |= WorldTileFlags.Active;
            cell.Type = 91;
            cell.FrameX = checked((short)(style * 18));
            cell.FrameY = checked((short)(row * 18));
            cell.Shape = 0;
        }
    }

    /// <summary>
    /// Digs the wandering exit tunnel away from the pyramid. It runs for at least a hundred rows and at most
    /// eight hundred, switching direction every ten to fifty rows, and stops early only once the corridor's own
    /// width has broken into already-open ground. Dungeon walls are never overwritten.
    /// </summary>
    private void DigExitTunnel(int cursorX, int cursorY, int direction, int corridorHeight)
    {
        int minimumRun = random.Next(100, 200);
        int maximumRun = random.Next(500, 800);
        int runLength = random.Next(10, 50);
        int tunnelHeight = corridorHeight;
        if (direction == 1)
            cursorX -= tunnelHeight;
        int flare = random.Next(5, 10);
        bool digging = true;

        while (digging)
        {
            cancellation.ThrowIfCancellationRequested();
            minimumRun--;
            maximumRun--;
            runLength--;

            // The source writes this loop with a draw in BOTH the initializer and the condition, and a C# for
            // condition is re-evaluated every iteration. The right-hand draw therefore happens once per column,
            // not once per row, and it is the single largest consumer of shared RNG in the whole method.
            for (int column = cursorX - flare - random.Next(0, 2);
                 column <= cursorX + tunnelHeight + flare + random.Next(0, 2);
                 column++)
            {
                if (!Contains(column, cursorY))
                    continue;

                ref WorldTile cell = ref At(column, cursorY);
                if (column >= cursorX && column <= cursorX + tunnelHeight)
                {
                    cell.Flags &= ~WorldTileFlags.Active;
                }
                else if (!IsDungeonWall(cell.Wall))
                {
                    cell.Type = SandstoneBrick;
                    cell.Flags |= WorldTileFlags.Active;
                    cell.Shape = 0;
                }

                if (column >= cursorX - 1 && column <= cursorX + 1 + tunnelHeight && !IsDungeonWall(cell.Wall))
                    cell.Wall = SandstoneBrickWall;
            }

            cursorY++;
            cursorX += direction;

            if (minimumRun <= 0)
            {
                digging = false;
                for (int column = cursorX + 1; column <= cursorX + tunnelHeight - 1; column++)
                {
                    if (Contains(column, cursorY) && At(column, cursorY).IsActive)
                        digging = true;
                }
            }

            if (runLength < 0)
            {
                runLength = random.Next(10, 50);
                direction *= -1;
            }

            if (maximumRun <= 0)
                digging = false;
            if (!Contains(cursorX, cursorY))
                digging = false;
        }
    }

    /// <summary>
    /// Source <c>SquareWallFrame</c>, which the wall pass calls for every cell it papers and which is by far the
    /// largest consumer of shared RNG in the whole method - one draw per walled cell, hundreds per pyramid.
    /// It resets paint and coating on empty neighbours and draws a frame variant only for the centre.
    /// </summary>
    private void SquareWallFrame(int x, int y)
    {
        for (int nx = x - 1; nx <= x + 1; nx++)
        {
            for (int ny = y - 1; ny <= y + 1; ny++)
            {
                if (!Contains(nx, ny))
                    continue;

                ref WorldTile cell = ref At(nx, ny);
                if (cell.Wall == 0)
                {
                    cell.WallColor = 0;
                    cell.Flags &= ~(WorldTileFlags.InvisibleWall | WorldTileFlags.FullbrightWall);
                }
                else if (nx == x && ny == y)
                {
                    _ = random.Next(0, 3);
                }
            }
        }
    }

    private static bool IsDungeonWall(ushort wall) => wall is 7 or 8 or 9 or 94 or 95 or 96 or 97 or 98 or 99;

    private void WriteBrick(int x, int y)
    {
        if (!Contains(x, y))
            return;

        ref WorldTile cell = ref At(x, y);
        cell.Type = SandstoneBrick;
        cell.Flags |= WorldTileFlags.Active;
        cell.Shape = 0;
    }

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
