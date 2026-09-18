using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>WorldGen.makeTemple</c> together with the helpers only it uses:
/// <c>makeTemple_GenerateBricks</c> and its three line routines, <c>templePather</c>, <c>outerTempled</c>,
/// <c>templeCleaner</c> and the <c>DungeonBounds</c> the brick pass keeps its per-depth extents in.
/// </summary>
/// <remarks>
/// <para>
/// The temple is built in six movements and every one of them writes Lihzahrd Brick over everything it touches,
/// which is why a faithful temple is a solid mass with rooms cut out of it rather than an outline. First a
/// chain of rectangles is walked outward and downward from the entrance, each one re-rolled until it misses
/// every room already placed, the last one deliberately oversized. Then the brick pass unions the rooms at each
/// depth into one box, inflates it by ten, fills it solid, and joins consecutive depths with four thick lines,
/// one per corner. Then each room is filled solid and a ragged interior is eroded out of it twice, forwards and
/// backwards, which is what gives temple rooms their uneven edges. Then a wandering path is dug from room to
/// room. Then the outside is sealed by two sweeps of <c>outerTempled</c>, which turns any cell within six of a
/// temple wall into brick. Finally the entrance is cut, the walls are smoothed by <c>templeCleaner</c>, the
/// altar is placed and spike traps are scattered until a budget derived from the room count runs out.
/// </para>
/// <para>
/// The brick lines are the one part that is not integer arithmetic: the source steps a single-precision vector
/// along the line and rounds each position, so the port uses <see langword="float"/> throughout and rounds the
/// same way. Using <see langword="double"/> there moves cells.
/// </para>
/// </remarks>
internal sealed class JungleTempleBuilder1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    int underworldTop,
    CancellationToken cancellation)
{
    private const ushort LihzahrdBrick = 226;
    private const ushort LihzahrdBrickWall = 87;
    private const ushort SpikyLihzahrdBrick = 232;
    private const ushort Door = 10;
    private const ushort LihzahrdAltar = 237;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    /// <summary>The temple's outer extents and room count, which later passes read the way the source reads GenVars.</summary>
    public int Left { get; private set; }

    public int Right { get; private set; }

    public int Top { get; private set; }

    public int Bottom { get; private set; }

    public int Rooms { get; private set; }

    public int AltarX { get; private set; }

    public int AltarY { get; private set; }

    public void Build(int x, int y)
    {
        double scale = width / 4200.0;
        int rooms = random.Next((int)(scale * 10.0), (int)(scale * 16.0));

        var roomRects = new TempleRect[rooms + 10];
        var roomDepths = new int[rooms + 10];

        int direction = 1;
        if (random.Next(2) == 0)
            direction = -1;

        int entryDirection = direction;
        int entryX = x;
        int entryY = y;
        int cursorX = x;
        int cursorY = y;
        int runLength = random.Next(1, 3);
        int placedInRun = 0;
        int depth = 0;

        for (int room = 0; room < rooms; room++)
        {
            cancellation.ThrowIfCancellationRequested();
            placedInRun++;
            int nextDirection = direction;
            int centreX = cursorX;
            int centreY = cursorY;
            bool retry = true;
            int roomWidth = 0;
            int roomHeight = 0;
            int overlap = -10;
            var rect = new TempleRect(centreX, centreY, 0, 0);

            while (retry)
            {
                cancellation.ThrowIfCancellationRequested();
                centreX = cursorX;
                centreY = cursorY;
                roomWidth = random.Next(25, 50);
                roomHeight = random.Next(20, 35);
                if (roomHeight > roomWidth)
                    roomHeight = roomWidth;

                bool last = centreY + 70 >= underworldTop - 10;
                if (room == rooms - 1 || last)
                {
                    rooms = room + 1;
                    roomWidth = random.Next(55, 65);
                    roomHeight = random.Next(45, 50);
                    if (roomHeight > roomWidth)
                        roomHeight = roomWidth;
                    roomWidth = (int)(roomWidth * 1.6);
                    roomHeight = (int)(roomHeight * 1.35);
                    centreY += random.Next(5, 10);
                }

                if (placedInRun > runLength)
                {
                    centreY += random.Next(roomHeight + 1, roomHeight + 3) + overlap;
                    centreX += random.Next(-5, 6);
                    nextDirection = direction * -1;
                }
                else
                {
                    centreX += (random.Next(roomWidth + 1, roomWidth + 3) + overlap) * nextDirection;
                    centreY += random.Next(-5, 6);
                }

                retry = false;
                rect = new TempleRect(centreX - roomWidth / 2, centreY - roomHeight / 2, roomWidth, roomHeight);
                if (last)
                    break;

                for (int other = 0; other < room; other++)
                {
                    if (rect.Intersects(roomRects[other]))
                        retry = true;
                    if (random.Next(100) == 0)
                        overlap++;
                }
            }

            roomDepths[room] = room == rooms ? depth + 1 : depth;
            if (placedInRun > runLength)
            {
                runLength++;
                placedInRun = 1;
                depth++;
            }

            roomRects[room] = rect;
            direction = nextDirection;
            cursorX = centreX;
            cursorY = centreY;
        }

        GenerateBricks(rooms, roomRects, roomDepths);
        CarveRooms(rooms, roomRects);
        DigCorridors(rooms, roomRects, entryX, entryY, direction);

        ComputeExtents(rooms, roomRects);
        SealOutside();
        int entranceFloor = CutEntranceShaft(entryX, entryY, -entryDirection);
        CutEntrance(entryX, entryY, entranceFloor);
        SmoothWalls();
        PaperInteriorWalls();
        PlaceAltar(roomRects[rooms - 1]);
        ScatterTraps(rooms, roomRects);

        Rooms = rooms;
    }

    /// <summary>
    /// Source <c>makeTemple_GenerateBricks</c>. Rooms at the same depth are unioned into one box, inflated by
    /// ten and filled solid; consecutive boxes are then joined by four thick lines, one per corner, each of
    /// which fills behind itself until it meets brick.
    /// </summary>
    private void GenerateBricks(int rooms, TempleRect[] roomRects, int[] roomDepths)
    {
        var bounds = new TempleBounds?[roomDepths.Length];
        for (int room = 0; room < rooms; room++)
        {
            TempleRect rect = roomRects[room];
            int depth = roomDepths[room];
            if (bounds[depth] is null)
            {
                bounds[depth] = new TempleBounds(width, height);
                bounds[depth]!.SetBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
            }
            else
            {
                bounds[depth]!.UpdateBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
            }
        }

        foreach (TempleBounds? box in bounds)
        {
            cancellation.ThrowIfCancellationRequested();
            if (box is null)
                continue;

            box.Inflate(10);
            box.CalculateHitbox();
            for (int column = box.Left; column <= box.Right; column++)
            {
                for (int row = box.Top; row <= box.Bottom; row++)
                    WriteBrick(column, row);
            }
        }

        for (int i = 0; i < bounds.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            TempleBounds? first = bounds[i];
            if (first is null)
                continue;

            for (int j = i + 1; j < bounds.Length; j++)
            {
                TempleBounds? second = bounds[j];
                if (second is null)
                    continue;

                BrickLineStrictAngles(first.Left, first.Top, second.Left, second.Top, false, false);
                BrickLineStrictAngles(first.Right, first.Top, second.Right, second.Top, true, false);
                BrickLineStrictAngles(first.Left, first.Bottom, second.Left, second.Bottom, false, true);
                BrickLineStrictAngles(first.Right, first.Bottom, second.Right, second.Bottom, true, true);
            }
        }
    }

    /// <summary>Source <c>makeTemple_GenerateBricks_Line_StrictAngles</c>: a diagonal is split into two runs.</summary>
    private void BrickLineStrictAngles(int startX, int startY, int endX, int endY, bool fillLeft, bool fillUpwards)
    {
        if (startX == endX || startY == endY)
        {
            BrickLine(startX, startY, endX, endY, fillLeft, fillUpwards);
            return;
        }

        float originX = fillUpwards ? endX : startX;
        float originY = fillUpwards ? startY : endY;
        float toStart = Distance(originX, originY, startX, startY);
        float toEnd = Distance(originX, originY, endX, endY);

        float fromX = startX;
        float fromY = startY;
        float toX = endX;
        float toY = endY;
        if (toEnd > toStart)
        {
            fromX = endX;
            fromY = endY;
            toX = startX;
            toY = startY;
        }

        float walkX = fromX;
        float walkY = fromY;
        int stepX = toX > fromX ? 1 : -1;
        int stepY = toY > fromY ? 1 : -1;
        int guard = 500;
        while (walkX != toX && walkY != toY)
        {
            guard--;
            if (guard < 0)
                break;
            walkX += stepX;
            walkY += stepY;
        }

        if (guard < 0)
        {
            BrickLine(startX, startY, endX, endY, fillLeft, fillUpwards);
            return;
        }

        int cornerX = (int)walkX;
        int cornerY = (int)walkY;
        BrickLine(startX, startY, cornerX, cornerY, fillLeft, fillUpwards);
        BrickLine(cornerX, cornerY, endX, endY, fillLeft, fillUpwards);
    }

    /// <summary>
    /// Source <c>makeTemple_GenerateBricks_Line</c>. The walk is single-precision and each step rounds to the
    /// nearest cell, so the line's cells depend on float rounding rather than on integer stepping.
    /// </summary>
    private void BrickLine(int startX, int startY, int endX, int endY, bool fillLeft, bool fillUpwards)
    {
        float positionX = startX;
        float positionY = startY;
        float deltaX = endX - positionX;
        float deltaY = endY - positionY;
        if (deltaX == 0f && deltaY == 0f)
            return;

        SafeNormalize(deltaX, deltaY, out float stepX, out float stepY);
        float remaining = Length(deltaX, deltaY);
        float stepLength = Length(stepX, stepY);
        int guard = 2000;
        int lastX = -1;
        int lastY = -1;

        while (remaining > 0f)
        {
            guard--;
            if (guard <= 0)
                break;

            int cellX = (int)Math.Round(positionX);
            int cellY = (int)Math.Round(positionY);
            if (!InWorld(cellX, cellY, 5))
                break;

            WriteBrick(cellX, cellY);
            BrickLineFillY(fillUpwards, lastX, cellX, cellY);
            BrickLineFillX(fillLeft, lastY, cellX, cellY);
            lastX = cellX;
            lastY = cellY;
            positionX += stepX;
            positionY += stepY;
            remaining -= stepLength;
        }
    }

    /// <summary>
    /// Source <c>makeTemple_GenerateBricks_Line_FillY</c>: probe vertically for the nearest brick and, if it is
    /// within three hundred cells and inside the world, fill everything up to it.
    /// </summary>
    private void BrickLineFillY(bool fillUpwards, int lastX, int currentX, int currentY)
    {
        if (currentX == lastX)
            return;

        int step = fillUpwards ? -1 : 1;
        int row = currentY + step;
        int distance = 0;
        while (!IsBrick(currentX, row))
        {
            distance++;
            row += step;
            if (!InWorld(currentX, row, 5))
                break;
        }

        if (distance >= 300 || row <= 10 || row >= height - 10)
            return;

        row = currentY + step;
        while (!IsBrick(currentX, row))
        {
            WriteBrick(currentX, row);
            row += step;
            if (!InWorld(currentX, row, 5))
                break;
        }
    }

    /// <summary>Source <c>makeTemple_GenerateBricks_Line_FillX</c>, the horizontal twin of the above.</summary>
    private void BrickLineFillX(bool fillLeft, int lastY, int currentX, int currentY)
    {
        if (currentY == lastY)
            return;

        int step = fillLeft ? -1 : 1;
        int column = currentX + step;
        int distance = 0;
        while (!IsBrick(column, currentY))
        {
            distance++;
            column += step;
            if (!InWorld(column, currentY, 5))
                break;
        }

        if (distance >= 300 || column <= 10 || column >= width - 10)
            return;

        column = currentX + step;
        while (!IsBrick(column, currentY))
        {
            WriteBrick(column, currentY);
            column += step;
            if (!InWorld(column, currentY, 5))
                break;
        }
    }

    /// <summary>
    /// Fills each room solid and then erodes a ragged interior out of it twice - once forwards, once backwards.
    /// The four edges wander by one cell on a one-in-twenty draw each, clamped to the room and to its centre,
    /// and the draws happen for every cell of the room whether or not the cell is carved.
    /// </summary>
    private void CarveRooms(int rooms, TempleRect[] roomRects)
    {
        for (int room = 0; room < rooms; room++)
        {
            cancellation.ThrowIfCancellationRequested();
            TempleRect rect = roomRects[room];
            for (int column = rect.X; column < rect.X + rect.Width; column++)
            {
                for (int row = rect.Y; row < rect.Y + rect.Height; row++)
                    WriteBrickClearingLiquid(column, row);
            }

            int left = rect.X + random.Next(3, 8);
            int right = rect.X + rect.Width - random.Next(3, 8);
            int top = rect.Y + random.Next(3, 8);
            int bottom = rect.Y + rect.Height - random.Next(3, 8);

            int wanderLeft = left;
            int wanderRight = right;
            int wanderTop = top;
            int wanderBottom = bottom;
            int midX = (left + right) / 2;
            int midY = (top + bottom) / 2;

            for (int column = left; column < right; column++)
            {
                for (int row = top; row < bottom; row++)
                {
                    WanderEdges(
                        ref wanderLeft, ref wanderRight, ref wanderTop, ref wanderBottom,
                        left, right, top, bottom, midX, midY);
                    if (column >= wanderLeft && column < wanderRight && row >= wanderTop && row <= wanderBottom)
                        CarveInterior(column, row);
                }
            }

            for (int row = bottom; row > top; row--)
            {
                for (int column = right; column > left; column--)
                {
                    WanderEdges(
                        ref wanderLeft, ref wanderRight, ref wanderTop, ref wanderBottom,
                        left, right, top, bottom, midX, midY);
                    if (column >= wanderLeft && column < wanderRight && row >= wanderTop && row <= wanderBottom)
                        CarveInterior(column, row);
                }
            }
        }
    }

    /// <summary>The four one-in-twenty edge nudges, in the source's order and with the source's clamps.</summary>
    private void WanderEdges(
        ref int wanderLeft,
        ref int wanderRight,
        ref int wanderTop,
        ref int wanderBottom,
        int left,
        int right,
        int top,
        int bottom,
        int midX,
        int midY)
    {
        if (random.Next(20) == 0)
            wanderTop += random.Next(-1, 2);
        if (random.Next(20) == 0)
            wanderBottom += random.Next(-1, 2);
        if (random.Next(20) == 0)
            wanderLeft += random.Next(-1, 2);
        if (random.Next(20) == 0)
            wanderRight += random.Next(-1, 2);

        if (wanderLeft < left)
            wanderLeft = left;
        if (wanderRight > right)
            wanderRight = right;
        if (wanderTop < top)
            wanderTop = top;
        if (wanderBottom > bottom)
            wanderBottom = bottom;
        if (wanderLeft > midX)
            wanderLeft = midX;
        if (wanderRight < midX)
            wanderRight = midX;
        if (wanderTop > midY)
            wanderTop = midY;
        if (wanderBottom < midY)
            wanderBottom = midY;
    }

    /// <summary>
    /// Digs the path that links the rooms. Every room gets a wandering walk to a random point inside it, and
    /// between rooms the source either aims at a doorway on the next room's edge or at the midpoint of the two,
    /// on a two-in-three draw.
    /// </summary>
    private void DigCorridors(int rooms, TempleRect[] roomRects, int entryX, int entryY, int direction)
    {
        int pathX = entryX;
        int pathY = entryY;

        for (int room = 0; room < rooms; room++)
        {
            cancellation.ThrowIfCancellationRequested();
            TempleRect inner = roomRects[room];
            inner = new TempleRect(inner.X + 8, inner.Y + 8, inner.Width - 16, inner.Height - 16);

            bool walking = true;
            while (walking)
            {
                cancellation.ThrowIfCancellationRequested();
                int targetX = random.Next(inner.X, inner.X + inner.Width);
                int targetY = random.Next(inner.Y, inner.Y + inner.Height);
                if (room == rooms - 1)
                {
                    targetX = inner.X + inner.Width / 2 + random.Next(-10, 10);
                    targetY = inner.Y + inner.Height / 2 + random.Next(-10, 10);
                }

                Pather(ref pathX, ref pathY, targetX, targetY);
                if (pathX == targetX && pathY == targetY)
                    walking = false;
            }

            if (room >= rooms - 1)
                continue;

            if (random.Next(3) != 0)
            {
                int next = room + 1;
                int doorX;
                int doorY;
                if (roomRects[next].Y >= roomRects[room].Y + roomRects[room].Height)
                {
                    doorX = roomRects[next].X;
                    if (room == 0)
                    {
                        doorX += direction > 0
                            ? (int)(roomRects[next].Width * 0.8)
                            : (int)(roomRects[next].Width * 0.2);
                    }
                    else if (roomRects[next].X < roomRects[room].X)
                    {
                        doorX += (int)(roomRects[next].Width * 0.2);
                    }
                    else
                    {
                        doorX += (int)(roomRects[next].Width * 0.8);
                    }

                    doorY = roomRects[next].Y;
                }
                else
                {
                    doorX = (roomRects[room].X + roomRects[room].Width / 2 +
                        roomRects[next].X + roomRects[next].Width / 2) / 2;
                    doorY = (int)(roomRects[next].Y + roomRects[next].Height * 0.8);
                }

                walking = true;
                while (walking)
                {
                    cancellation.ThrowIfCancellationRequested();
                    int targetX = random.Next(doorX - 6, doorX + 7);
                    int targetY = random.Next(doorY - 6, doorY + 7);
                    Pather(ref pathX, ref pathY, targetX, targetY);
                    if (pathX == targetX && pathY == targetY)
                        walking = false;
                }

                continue;
            }

            int between = room + 1;
            int midX = (roomRects[room].X + roomRects[room].Width / 2 +
                roomRects[between].X + roomRects[between].Width / 2) / 2;
            int midY = (roomRects[room].Y + roomRects[room].Height / 2 +
                roomRects[between].Y + roomRects[between].Height / 2) / 2;

            walking = true;
            while (walking)
            {
                cancellation.ThrowIfCancellationRequested();
                int targetX = random.Next(midX - 6, midX + 7);
                int targetY = random.Next(midY - 6, midY + 7);
                Pather(ref pathX, ref pathY, targetX, targetY);
                if (pathX == targetX && pathY == targetY)
                    walking = false;
            }
        }
    }

    /// <summary>
    /// Source <c>WorldGen.templePather</c>. It walks at most nineteen cells toward the target, clearing a
    /// square of its own rolled radius around every step, and returns where it stopped; the caller keeps asking
    /// until it arrives.
    /// </summary>
    private void Pather(ref int pathX, ref int pathY, int destX, int destY)
    {
        int steps = random.Next(5, 20);
        int radius = random.Next(2, 5);
        while (steps > 0 && (pathX != destX || pathY != destY))
        {
            steps--;
            if (pathX > destX)
                pathX--;
            if (pathX < destX)
                pathX++;
            if (pathY > destY)
                pathY--;
            if (pathY < destY)
                pathY++;

            for (int column = pathX - radius; column < pathX + radius; column++)
            {
                for (int row = pathY - radius; row < pathY + radius; row++)
                    CarveInterior(column, row);
            }
        }
    }

    private void ComputeExtents(int rooms, TempleRect[] roomRects)
    {
        int left = width - 20;
        int right = 20;
        int top = height - 20;
        int bottom = 20;

        for (int room = 0; room < rooms; room++)
        {
            TempleRect rect = roomRects[room];
            if (rect.X == 0 || rect.Y == 0 || rect.Width == 0 || rect.Height == 0)
                continue;

            if (rect.X < left)
                left = rect.X;
            if (rect.X + rect.Width > right)
                right = rect.X + rect.Width;
            if (rect.Y < top)
                top = rect.Y;
            if (rect.Y + rect.Height > bottom)
                bottom = rect.Y + rect.Height;
        }

        Left = left - 10;
        Right = right + 10;
        Top = top - 10;
        Bottom = bottom + 10;
    }

    /// <summary>
    /// The four sweeps of <c>outerTempled</c>. Each direction matters: the routine turns a cell into brick when
    /// any cell within six of it is already temple interior, so sweeping left to right then right to left then
    /// top to bottom then bottom to top grows the shell one layer at a time in every direction.
    /// </summary>
    private void SealOutside()
    {
        for (int column = Left; column < Right; column++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int row = Top; row < Bottom; row++)
                OuterTempled(column, row);
        }

        for (int column = Right; column >= Left; column--)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int row = Top; row < Bottom; row++)
                OuterTempled(column, row);
        }

        for (int row = Top; row < Bottom; row++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int column = Left; column < Right; column++)
                OuterTempled(column, row);
        }

        for (int row = Bottom; row >= Top; row--)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int column = Left; column < Right; column++)
                OuterTempled(column, row);
        }
    }

    /// <summary>Source <c>WorldGen.outerTempled</c>.</summary>
    private void OuterTempled(int x, int y)
    {
        if (!Contains(x, y))
            return;
        if (IsBrick(x, y) || WallAt(x, y) == LihzahrdBrickWall)
            return;

        for (int column = x - 6; column <= x + 6; column++)
        {
            for (int row = y - 6; row <= y + 6; row++)
            {
                if (!Contains(column, row))
                    continue;
                if (At(column, row).IsActive || At(column, row).Wall != LihzahrdBrickWall)
                    continue;

                WriteBrickClearingLiquid(x, y);
                return;
            }
        }
    }

    /// <summary>
    /// Bores the entrance shaft outward from the entry point until it leaves the temple, rising one row every
    /// nine to thirteen columns, and reports the deepest brick row it opened.
    /// </summary>
    private int CutEntranceShaft(int entryX, int entryY, int direction)
    {
        int deepest = entryY;
        double shaftX = entryX;
        double shaftY = entryY;
        int halfHeight = random.Next(2, 5);
        int rise = 0;
        int riseEvery = random.Next(9, 14);
        bool inside = true;

        while (inside)
        {
            cancellation.ThrowIfCancellationRequested();
            rise++;
            if (rise >= riseEvery)
            {
                rise = 0;
                shaftY -= 1.0;
            }

            shaftX += direction;
            int column = (int)shaftX;
            inside = false;
            for (int row = (int)shaftY - halfHeight; row < shaftY + halfHeight; row++)
            {
                if (!Contains(column, row))
                    continue;
                if (At(column, row).Wall == LihzahrdBrickWall || IsBrick(column, row))
                    inside = true;
                if (!IsBrick(column, row))
                    continue;

                if (row > deepest)
                    deepest = row;
                CarveInterior(column, row);
            }
        }

        return deepest + 2;
    }

    /// <summary>
    /// Cuts the doorway itself: a brick plug is driven down to the floor, the surrounding block is hollowed and
    /// re-bricked, and the door is placed in the gap.
    /// </summary>
    private void CutEntrance(int entryX, int entryY, int floorLimit)
    {
        int doorX = entryX;
        int doorY = entryY;
        while (Contains(doorX, doorY) && !At(doorX, doorY).IsActive)
        {
            doorY++;
            if (doorY >= floorLimit)
            {
                doorY = floorLimit;
                ClearEverything(doorX, doorY);
                WriteBrick(doorX, doorY);
                break;
            }
        }

        doorY -= 4;
        int ceiling = doorY;
        while (Contains(doorX, ceiling) &&
               (IsBrick(doorX, ceiling) || At(doorX, ceiling).Wall == LihzahrdBrickWall))
        {
            ceiling--;
        }

        ceiling += 2;
        for (int column = doorX - 1; column <= doorX + 1; column++)
        {
            for (int row = ceiling; row <= doorY; row++)
                WriteBrickClearingLiquid(column, row);
        }

        for (int column = doorX - 4; column <= doorX + 4; column++)
        {
            for (int row = doorY - 1; row < doorY + 3; row++)
                CarveInterior(column, row);
        }

        for (int column = doorX - 1; column <= doorX + 1; column++)
        {
            for (int row = doorY - 5; row <= doorY + 8; row++)
                WriteBrickClearingLiquid(column, row);
        }

        for (int column = doorX - 3; column <= doorX + 3; column++)
        {
            for (int row = doorY - 2; row < doorY + 3; row++)
            {
                if (row >= doorY || column < entryX - 1 || column > entryX + 1)
                    CarveInterior(column, row);
            }
        }

        PlaceDoor(doorX, doorY, style: 11);
    }

    /// <summary>
    /// Source <c>WorldGen.PlaceTile</c> for a door reduced to what the temple needs, and <c>PlaceDoor</c>
    /// behind it. Three frame draws happen, one per cell, and only once the opening has been accepted.
    /// </summary>
    private void PlaceDoor(int x, int y, int style)
    {
        if (!Contains(x, y - 3) || !Contains(x, y + 3))
            return;

        int anchor;
        if (!At(x, y - 1).IsActive && !At(x, y - 2).IsActive && IsSolid(x, y - 3))
            anchor = y - 1;
        else if (!At(x, y + 1).IsActive && !At(x, y + 2).IsActive && IsSolid(x, y + 3))
            anchor = y + 1;
        else
            return;

        if (!Contains(x, anchor - 2) || !Contains(x, anchor + 2))
            return;
        if (!IsSolid(x, anchor - 2) || At(x, anchor - 2).IsActuated || !IsSolidUnsloped(x, anchor + 2))
            return;

        int frameBase = 54 * (style / 36);
        int frameRow = 54 * (style % 36);
        for (int offset = -1; offset <= 1; offset++)
        {
            ref WorldTile cell = ref At(x, anchor + offset);
            cell.Flags |= WorldTileFlags.Active;
            cell.Type = Door;
            cell.FrameY = checked((short)(frameRow + (offset + 1) * 18));
            cell.FrameX = checked((short)(frameBase + random.Next(3) * 18));
        }
    }

    /// <summary>The two sweeps of <c>templeCleaner</c>, forwards then backwards.</summary>
    private void SmoothWalls()
    {
        for (int column = Left; column < Right; column++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int row = Top; row < Bottom; row++)
                TempleCleaner(column, row);
        }

        for (int row = Bottom; row >= Top; row--)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int column = Right; column >= Left; column--)
                TempleCleaner(column, row);
        }
    }

    /// <summary>
    /// Source <c>WorldGen.templeCleaner</c>: a brick with at most one brick neighbour is dissolved into wall,
    /// and an open cell with exactly three brick neighbours is filled in.
    /// </summary>
    private void TempleCleaner(int x, int y)
    {
        if (!Contains(x - 1, y - 1) || !Contains(x + 1, y + 1))
            return;

        int neighbours = 0;
        if (IsBrick(x + 1, y))
            neighbours++;
        if (IsBrick(x - 1, y))
            neighbours++;
        if (IsBrick(x, y + 1))
            neighbours++;
        if (IsBrick(x, y - 1))
            neighbours++;

        if (IsBrick(x, y))
        {
            if (neighbours <= 1)
                CarveInterior(x, y);
        }
        else if (!At(x, y).IsActive && neighbours == 3)
        {
            WriteBrickClearingLiquid(x, y);
        }
    }

    /// <summary>Papers every cell whose whole three-by-three neighbourhood is already temple.</summary>
    private void PaperInteriorWalls()
    {
        for (int column = Left; column < Right; column++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int row = Top; row < Bottom; row++)
            {
                if (!Contains(column - 1, row - 1) || !Contains(column + 1, row + 1))
                    continue;

                bool enclosed = true;
                for (int nx = column - 1; nx <= column + 1 && enclosed; nx++)
                {
                    for (int ny = row - 1; ny <= row + 1; ny++)
                    {
                        if (!IsBrick(nx, ny) && At(nx, ny).Wall != LihzahrdBrickWall)
                        {
                            enclosed = false;
                            break;
                        }
                    }
                }

                if (enclosed)
                    At(column, row).Wall = LihzahrdBrickWall;
            }
        }
    }

    /// <summary>
    /// Places the Lihzahrd Altar in the last room. The source retries a random spot up to a thousand times and
    /// only then falls back to clearing a shelf and writing the altar's six cells directly.
    /// </summary>
    private void PlaceAltar(TempleRect lastRoom)
    {
        int attempts = 0;
        int halfWidth = lastRoom.Width / 2;
        int halfHeight = lastRoom.Height / 2;

        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            attempts++;
            int x = lastRoom.X + halfWidth + 15 - random.Next(30);
            int y = lastRoom.Y + halfHeight + 15 - random.Next(30);

            if (TryPlaceAltar3x2(x, y))
            {
                AltarX = x - At(x, y).FrameX / 18;
                AltarY = y - At(x, y).FrameY / 18;
                return;
            }

            if (attempts < 1000)
                continue;

            int fallbackX = lastRoom.X + halfWidth + random.Next(-10, 11);
            int fallbackY = lastRoom.Y + halfHeight + random.Next(-10, 11);
            while (fallbackY < lastRoom.Bottom - 2 && Contains(fallbackX, fallbackY) &&
                   At(fallbackX, fallbackY).IsActive)
            {
                fallbackY++;
            }

            while (Contains(fallbackX, fallbackY) && !At(fallbackX, fallbackY).IsActive)
                fallbackY++;

            for (int offset = -1; offset <= 1; offset++)
                WriteBrick(fallbackX + offset, fallbackY);

            fallbackY -= 2;
            fallbackX--;
            for (int dx = -1; dx <= 3; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (Contains(fallbackX + dx, fallbackY + dy))
                        At(fallbackX + dx, fallbackY + dy).Flags &= ~WorldTileFlags.Active;
                }
            }

            AltarX = fallbackX;
            AltarY = fallbackY;
            for (int dx = 0; dx <= 2; dx++)
            {
                for (int dy = 0; dy <= 1; dy++)
                {
                    ref WorldTile cell = ref At(fallbackX + dx, fallbackY + dy);
                    cell.Flags |= WorldTileFlags.Active;
                    cell.Type = LihzahrdAltar;
                    cell.FrameX = checked((short)(dx * 18));
                    cell.FrameY = checked((short)(dy * 18));
                }
            }

            return;
        }
    }

    /// <summary>The <c>Place3x2</c> path <c>PlaceTile(x, y, 237)</c> takes, with the altar's own ground rules.</summary>
    private bool TryPlaceAltar3x2(int x, int y)
    {
        if (x < 5 || x > width - 5 || y < 5 || y > height - 5)
            return false;

        bool fits = true;
        for (int column = x - 1; column < x + 2; column++)
        {
            for (int row = y - 1; row < y + 1; row++)
            {
                if (At(column, row).IsActive)
                    fits = false;
            }

            if (!IsSolidUnsloped(column, y + 1))
                fits = false;
        }

        if (!fits)
            return false;

        for (int dx = 0; dx < 3; dx++)
        {
            for (int dy = 0; dy < 2; dy++)
            {
                ref WorldTile cell = ref At(x - 1 + dx, y - 1 + dy);
                cell.Flags |= WorldTileFlags.Active;
                cell.Type = LihzahrdAltar;
                cell.FrameX = checked((short)(dx * 18));
                cell.FrameY = checked((short)(dy * 18));
            }
        }

        return true;
    }

    /// <summary>
    /// Scatters spike traps until a budget of about one and a tenth per room, jittered by a quarter, runs out.
    /// Each attempt picks a room, picks an open walled cell in it, falls to a surface in one of four directions
    /// and converts a square of exposed brick into Spiky Lihzahrd Brick with a spike on alternating sides.
    /// </summary>
    private void ScatterTraps(int rooms, TempleRect[] roomRects)
    {
        double budget = rooms * 1.1;
        budget *= 1.0 + random.Next(-25, 26) * 0.01;
        int stale = 0;

        while (budget > 0.0)
        {
            cancellation.ThrowIfCancellationRequested();
            stale++;

            int room = random.Next(rooms);
            int x = random.Next(roomRects[room].X, roomRects[room].X + roomRects[room].Width);
            int y = random.Next(roomRects[room].Y, roomRects[room].Y + roomRects[room].Height);

            if (Contains(x, y) && At(x, y).Wall == LihzahrdBrickWall && !At(x, y).IsActive)
            {
                bool placed = random.Next(2) == 0
                    ? TryPlaceVerticalTrap(x, y)
                    : TryPlaceHorizontalTrap(x, y);
                if (placed)
                {
                    stale = 0;
                    budget -= 1.0;
                }
            }

            if (stale > 1000)
            {
                stale = 0;
                budget -= 1.0;
            }
        }
    }

    private bool TryPlaceVerticalTrap(int x, int y)
    {
        int step = random.Next(2) == 0 ? -1 : 1;
        while (Contains(x, y) && !At(x, y).IsActive)
            y += step;
        y -= step;

        int side = random.Next(2);
        int reach = random.Next(3, 10);
        for (int column = x - reach; column < x + reach; column++)
        {
            for (int row = y - reach; row < y + reach; row++)
            {
                if (Contains(column, row) && At(column, row).IsActive &&
                    At(column, row).Type is Door or LihzahrdAltar)
                {
                    return false;
                }
            }
        }

        bool placed = false;
        for (int column = x - reach; column < x + reach; column++)
        {
            for (int row = y - reach; row < y + reach; row++)
            {
                if (!IsSolid(column, row) || TypeAt(column, row) == SpikyLihzahrdBrick ||
                    IsSolid(column, row - step))
                {
                    continue;
                }

                At(column, row).Type = SpikyLihzahrdBrick;
                placed = true;
                int spikeRow = side == 0 ? row - 1 : row + 1;
                if (Contains(column, spikeRow))
                {
                    ref WorldTile spike = ref At(column, spikeRow);
                    spike.Type = SpikyLihzahrdBrick;
                    spike.Flags |= WorldTileFlags.Active;
                }

                side++;
                if (side > 1)
                    side = 0;
            }
        }

        return placed;
    }

    private bool TryPlaceHorizontalTrap(int x, int y)
    {
        int step = random.Next(2) == 0 ? -1 : 1;
        while (Contains(x, y) && !At(x, y).IsActive)
            x += step;
        x -= step;

        int side = random.Next(2);
        int reach = random.Next(3, 10);
        for (int column = x - reach; column < x + reach; column++)
        {
            for (int row = y - reach; row < y + reach; row++)
            {
                if (Contains(column, row) && At(column, row).IsActive && At(column, row).Type == Door)
                    return false;
            }
        }

        bool placed = false;
        for (int column = x - reach; column < x + reach; column++)
        {
            for (int row = y - reach; row < y + reach; row++)
            {
                if (!IsSolid(column, row) || TypeAt(column, row) == SpikyLihzahrdBrick ||
                    IsSolid(column - step, row))
                {
                    continue;
                }

                At(column, row).Type = SpikyLihzahrdBrick;
                placed = true;

                // The source writes the left neighbour in both arms of this branch; only the non-drunk half of
                // the second arm differs, and it too reaches for the left cell. Reproduced as written.
                int spikeColumn = side == 0 ? column - 1 : column + 1;
                if (Contains(spikeColumn, row))
                {
                    ref WorldTile spike = ref At(spikeColumn, row);
                    spike.Type = SpikyLihzahrdBrick;
                    spike.Flags |= WorldTileFlags.Active;
                }

                side++;
                if (side > 1)
                    side = 0;
            }
        }

        return placed;
    }

    private void WriteBrick(int x, int y)
    {
        if (!Contains(x, y))
            return;

        ref WorldTile cell = ref At(x, y);
        cell.Flags |= WorldTileFlags.Active;
        cell.Type = LihzahrdBrick;
    }

    private void WriteBrickClearingLiquid(int x, int y)
    {
        if (!Contains(x, y))
            return;

        ref WorldTile cell = ref At(x, y);
        cell.Flags |= WorldTileFlags.Active;
        cell.Type = LihzahrdBrick;
        cell.LiquidAmount = 0;
        cell.Shape = 0;
    }

    private void CarveInterior(int x, int y)
    {
        if (!Contains(x, y))
            return;

        ref WorldTile cell = ref At(x, y);
        cell.Flags &= ~WorldTileFlags.Active;
        cell.Wall = LihzahrdBrickWall;
    }

    private void ClearEverything(int x, int y)
    {
        if (!Contains(x, y))
            return;

        At(x, y) = default;
    }

    private bool IsBrick(int x, int y) =>
        Contains(x, y) && At(x, y).IsActive && At(x, y).Type == LihzahrdBrick;

    private ushort TypeAt(int x, int y) => Contains(x, y) ? At(x, y).Type : (ushort)0;

    private ushort WallAt(int x, int y) => Contains(x, y) ? At(x, y).Wall : (ushort)0;

    /// <summary>Source <c>SolidTile</c>.</summary>
    private bool IsSolid(int x, int y)
    {
        if (!Contains(x, y))
            return true;

        WorldTile tile = At(x, y);
        return tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !tile.IsActuated && tile.Shape != 1 && tile.Shape < 2;
    }

    private bool IsSolidUnsloped(int x, int y) => IsSolid(x, y);

    private bool InWorld(int x, int y, int fluff) =>
        x >= fluff && x < width - fluff && y >= fluff && y < height - fluff;

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];

    private static float Length(float x, float y) => (float)Math.Sqrt(x * x + y * y);

    private static float Distance(float ax, float ay, float bx, float by) => Length(bx - ax, by - ay);

    /// <summary>Source <c>Vector2.SafeNormalize(Vector2.UnitX)</c>.</summary>
    private static void SafeNormalize(float x, float y, out float nx, out float ny)
    {
        if ((x == 0f && y == 0f) || float.IsNaN(x) || float.IsNaN(y))
        {
            nx = 1f;
            ny = 0f;
            return;
        }

        float scale = 1f / (float)Math.Sqrt(x * x + y * y);
        nx = x * scale;
        ny = y * scale;
    }

    /// <summary>The source's <c>Rectangle</c> with XNA's intersection rule.</summary>
    private readonly record struct TempleRect(int X, int Y, int Width, int Height)
    {
        public int Left => X;

        public int Top => Y;

        public int Right => X + Width;

        public int Bottom => Y + Height;

        public bool Intersects(TempleRect other) =>
            other.Left < Right && Left < other.Right && other.Top < Bottom && Top < other.Bottom;
    }

    /// <summary>
    /// Source <c>Terraria.GameContent.Generation.Dungeon.DungeonBounds</c> reduced to what the brick pass uses.
    /// Its setters clamp to ten cells inside the world, which is what keeps a temple near the world edge from
    /// bricking the border.
    /// </summary>
    private sealed class TempleBounds(int worldWidth, int worldHeight)
    {
        private int left;
        private int right;
        private int top;
        private int bottom;

        public int Left
        {
            get => left;
            set => left = Math.Clamp(value, 10, worldWidth - 10);
        }

        public int Right
        {
            get => right;
            set => right = Math.Clamp(value, 10, worldWidth - 10);
        }

        public int Top
        {
            get => top;
            set => top = Math.Clamp(value, 10, worldHeight - 10);
        }

        public int Bottom
        {
            get => bottom;
            set => bottom = Math.Clamp(value, 10, worldHeight - 10);
        }

        public void SetBounds(int minX, int minY, int maxX, int maxY)
        {
            Left = minX;
            Right = maxX;
            Top = minY;
            Bottom = maxY;
            CalculateHitbox();
        }

        public void UpdateBounds(int minX, int minY, int maxX, int maxY)
        {
            if (minX < left)
                Left = minX;
            if (maxX > right)
                Right = maxX;
            if (minY < top)
                Top = minY;
            if (maxY > bottom)
                Bottom = maxY;
        }

        public void Inflate(int amount) => SetBounds(Left - amount, Top - amount, Right + amount, Bottom + amount);

        public void CalculateHitbox()
        {
            if (Right <= Left)
                Right = Left + 1;
            if (Bottom <= Top)
                Bottom = Top + 1;
        }
    }
}
