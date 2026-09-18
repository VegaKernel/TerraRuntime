using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 <c>WorldGen.GrowLivingTree_MakePassage</c>,
/// <c>WorldGen.GrowLivingTreePassageRoom</c> and <c>WorldGen.GrowLivingTree_HorizontalTunnel</c>: the shaft a
/// wide living tree drives into the ground, the secret room it opens part way down, and the side tunnels a
/// patch tree bores instead of a room.
/// </summary>
/// <remarks>
/// <para>
/// This is where most of a living tree's Living Wood actually goes. The shaft descends for four hundred to
/// seven hundred rows, and on each row it writes wood across the full trunk width EXCEPT the four-cell corridor
/// down the middle, which is cleared and papered with Living Wood Wall. Every sixth row the shaft either shifts
/// one cell sideways or, on a one-in-three draw, lays a platform across the corridor; the sideways shift is
/// refused toward a side that already carries the tree's own wall. The descent stops when it runs out of rows,
/// leaves the world's lower bound, meets another living tree's wall, or breaks into open ground - the last test
/// looking four rows ahead normally and two once a dungeon wall has been seen.
/// </para>
/// <para>
/// A main tree opens the secret room once its head start of fifty rows is spent: a corridor fifteen to thirty
/// cells long ending in a chamber, doors at both ends, a chair, a Living Loom and a chest. A patch tree opens
/// no room and instead tries a horizontal tunnel every few shifts, which bores left or right until it meets
/// open space, another living tree's trunk or eighty cells of nothing, and hangs a door at each end on a
/// one-in-three draw.
/// </para>
/// <para>
/// Bounded slice: the room's chest is placed from this runtime's buried-chest loot rather than through
/// <c>WorldGen.AddBuriedChest</c>, whose site scan and loot cascade are a separate sixteen-hundred-line
/// primitive that is not ported. The chest's signature item is the source's - a Leaf Wand, or a Living Wood Wand
/// on a one-in-three draw - but the shared RNG position after a room differs from the source's for that reason.
/// </para>
/// </remarks>
internal sealed class LivingTreePassage1458(
    WorldTileStore store,
    IWorldGenerationVanillaRandom random,
    double worldSurface,
    int underworldTop,
    CancellationToken cancellation)
{
    private const ushort LivingWood = 191;
    private const ushort Sandstone = 40;
    private const ushort Platform = 19;
    private const ushort Chair = 15;
    private const ushort LivingLoom = 304;
    private const ushort Chest = 21;
    private const ushort Door = 10;
    private const ushort Cobweb = 48;
    private const ushort LivingWoodWall = 244;
    private const ushort SpiderWall = 34;

    private readonly int width = store.Dimensions.WidthTiles;
    private readonly int height = store.Dimensions.HeightTiles;

    /// <summary>Where the secret room wants a chest, if it opened one. The caller owns the loot.</summary>
    public (int X, int Y, int SignatureItem)? ChestRequest { get; private set; }

    /// <summary>Source <c>GrowLivingTree_MakePassage</c>.</summary>
    public void MakePassage(int j, int trunkWidth, ref int minl, ref int minr, bool noSecretRoom)
    {
        bool roomOpened = noSecretRoom;
        int originalLeft = minl;
        int originalRight = minr;
        bool sawDungeonWall = false;

        int row = j - 6;
        int headStart = 50;
        int remaining = random.Next(400, 700);
        int sinceShift = 0;
        bool firstShiftIsPlatform = true;
        int untilTunnel = random.Next(5, 16);

        while (remaining > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (row > underworldTop + random.Next(15, 31))
                remaining = 0;

            row++;
            remaining--;
            headStart--;

            int centre = (minl + minr) / 2;
            if (!IsActive(minl, row) && WallAt(minl, row) == LivingWoodWall &&
                !IsActive(minr, row) && WallAt(minr, row) == LivingWoodWall)
            {
                break;
            }

            int flare = 1;
            if (row > j && trunkWidth <= 4)
                flare++;

            for (int column = minl - flare; column <= minr + flare; column++)
            {
                if (!Contains(column, row))
                    continue;

                if (IsDungeonWall(WallAt(column, row)))
                {
                    roomOpened = true;
                    sawDungeonWall = true;
                }

                if (column > centre - 2 && column <= centre + 1)
                {
                    if (row <= j - 4)
                        continue;

                    bool platformAllowed = true;
                    if (!IsActive(column, row + 1) && WallAt(column, row + 1) == SpiderWall)
                        platformAllowed = false;

                    if (!IsFurniture(column, row) && !IsFurnitureAbove(column, row - 1) &&
                        TypeAt(column, row + 1) != Door)
                    {
                        At(column, row).Flags &= ~WorldTileFlags.Active;
                    }

                    PaperCorridor(column, row);
                    PaperNeighbour(column - 1, row);
                    PaperNeighbour(column + 1, row);

                    if (row == j && platformAllowed)
                    {
                        At(column, row + 1).Flags &= ~WorldTileFlags.Active;
                        PlacePlatform(column, row + 1, 23);
                    }

                    continue;
                }

                if (!IsFurnitureWall(column, row) && TypeAt(column - 1, row) != Door &&
                    TypeAt(column + 1, row) != Door)
                {
                    ushort wall = WallAt(column, row);
                    if (!IsDungeonWall(wall) && wall != 3 && wall != 83 &&
                        (IsActive(column, row) || wall != SpiderWall))
                    {
                        ref WorldTile cell = ref At(column, row);
                        cell.Type = LivingWood;
                        cell.Flags |= WorldTileFlags.Active;
                        cell.Shape = 0;
                    }

                    if (TypeAt(column - 1, row) == Sandstone)
                        At(column - 1, row).Type = 0;
                    if (TypeAt(column + 1, row) == Sandstone)
                        At(column + 1, row).Type = 0;
                }

                if (row <= j && row > j - 4 && column > minl - flare && column <= minr + flare - 1)
                    At(column, row).Wall = LivingWoodWall;
            }

            sinceShift++;
            if (sinceShift >= 6)
            {
                sinceShift = 0;
                int shift = random.Next(3);
                if (shift == 0)
                    shift = -1;
                if (firstShiftIsPlatform)
                    shift = 2;

                if (shift == -1 && WallAt(minl - 5, row) == LivingWoodWall)
                    shift = 1;
                else if (shift == 1 && WallAt(minr + 5, row) == LivingWoodWall)
                    shift = -1;

                if (shift == 2)
                {
                    firstShiftIsPlatform = false;
                    int style = 23;
                    if (IsDungeonWall(WallAt(minl, row + 1)) || IsDungeonWall(WallAt(minl + 1, row + 1)) ||
                        IsDungeonWall(WallAt(minl + 2, row + 1)))
                    {
                        style = 12;
                    }

                    for (int column = minl; column <= minr; column++)
                    {
                        if (column <= centre - 2 || column > centre + 1)
                            continue;

                        if (Contains(column, row + 1))
                            At(column, row + 1).Flags &= ~WorldTileFlags.Active;
                        PlacePlatform(column, row + 1, style);
                    }
                }
                else
                {
                    minl += shift;
                    minr += shift;
                }

                if (noSecretRoom)
                {
                    untilTunnel--;
                    if (untilTunnel <= 0)
                        untilTunnel = HorizontalTunnel(centre, row) ? random.Next(5, 21) : random.Next(2, 11);
                }

                if (headStart <= 0 && !roomOpened)
                {
                    roomOpened = true;
                    PassageRoom(minl, minr, row);
                }
            }

            if (sawDungeonWall)
            {
                bool open = true;
                for (int column = minl; column <= minr; column++)
                {
                    for (int probe = row + 1; probe <= row + 2; probe++)
                    {
                        if (IsSolid(column, probe))
                            open = false;
                    }
                }

                if (open)
                    remaining = 0;

                continue;
            }

            if (headStart > 0)
                continue;

            bool clear = true;
            for (int column = minl; column <= minr; column++)
            {
                for (int probe = row + 1; probe <= row + 4; probe++)
                {
                    if (IsSolid(column, probe))
                        clear = false;
                }
            }

            if (clear)
                remaining = 0;
        }

        minl = originalLeft;
        minr = originalRight;

        // The mouth of the shaft: four rows under the trunk are opened, and any of them fully enclosed by
        // occupied or walled neighbours takes the tree's own wall.
        for (int column = minl; column <= minr; column++)
        {
            for (int row2 = j - 3; row2 <= j; row2++)
            {
                if (!Contains(column, row2))
                    continue;

                At(column, row2).Flags &= ~WorldTileFlags.Active;
                bool enclosed = true;
                for (int nx = column - 1; nx <= column + 1; nx++)
                {
                    for (int ny = row2 - 1; ny <= row2 + 1; ny++)
                    {
                        if (!IsActive(nx, ny) && WallAt(nx, ny) == 0)
                            enclosed = false;
                    }
                }

                if (enclosed && !IsDungeonWall(WallAt(column, row2)))
                    At(column, row2).Wall = LivingWoodWall;
            }
        }
    }

    /// <summary>
    /// Source <c>GrowLivingTreePassageRoom</c>: a corridor off one side of the shaft ending in a chamber, both
    /// hollowed out of solid Living Wood, with doors at the shaft end and the far end.
    /// </summary>
    private void PassageRoom(int minl, int minr, int y)
    {
        int side = random.Next(2);
        if (side == 0)
            side = -1;

        int top = y - 2;
        int near = (minl + minr) / 2;
        if (side < 0)
            near--;
        if (side > 0)
            near++;

        int length = random.Next(15, 30);
        int far = near + length;
        if (side < 0)
        {
            far = near;
            near -= length;
        }

        // The corridor refuses open sky: any unwalled empty cell above the surface aborts the whole room.
        for (int column = near; column < far; column++)
        {
            for (int row = y - 20; row < y + 10; row++)
            {
                if (Contains(column, row) && WallAt(column, row) == 0 && !IsActive(column, row) &&
                    row < worldSurface)
                {
                    return;
                }
            }
        }

        CarveWoodenCorridor(near, far, top, y);

        int doorX = ((minl + minr) / 2) + 3 * side;
        PlaceDoor(doorX, y, style: 7);

        int chamberHalf = random.Next(5, 9);
        int chamberHeight = random.Next(4, 6);
        if (side < 0)
        {
            far = near + chamberHalf;
            near -= chamberHalf;
        }
        else
        {
            near = far - chamberHalf;
            far += chamberHalf;
        }

        top = y - chamberHeight;
        CarveWoodenChamber(near, far, top, y);

        int farDoor = side < 0 ? far + 2 : near - 2;
        PlaceDoor(farDoor, y, style: 7);

        int anchor = side < 0 ? near : far;
        int chairChance = 2;
        if (random.Next(chairChance) == 0)
        {
            chairChance += 2;
            PlaceChair(anchor, y, style: 5);
            if (side < 0)
            {
                ShiftFrame(anchor, y - 1, 18);
                ShiftFrame(anchor, y, 18);
            }
        }

        anchor = side < 0 ? near + 2 : far - 2;
        PlaceLoom(anchor, y);

        anchor = side < 0 ? near + 4 : far - 4;
        if (random.Next(chairChance) == 0)
        {
            PlaceChair(anchor, y, style: 5);
            if (side > 0)
            {
                ShiftFrame(anchor, y - 1, 18);
                ShiftFrame(anchor, y, 18);
            }
        }

        anchor = side < 0 ? near + 8 : far - 7;
        int signature = 832;
        if (random.Next(3) == 0)
            signature = 4281;

        ChestRequest = (anchor, y, signature);
    }

    /// <summary>Fills the corridor's block with wood and hollows the three rows the player walks through.</summary>
    private void CarveWoodenCorridor(int near, int far, int top, int y)
    {
        for (int column = near; column <= far; column++)
        {
            for (int row = top - 2; row <= y + 2; row++)
            {
                if (!Contains(column, row))
                    continue;

                ClearSandstoneAround(column, row);
                if (WallAt(column, row) != LivingWoodWall && TypeAt(column, row) != Platform)
                {
                    ref WorldTile cell = ref At(column, row);
                    cell.Flags |= WorldTileFlags.Active;
                    cell.Type = LivingWood;
                    cell.Shape = 0;
                }

                if (row < top || row > y)
                    continue;

                ref WorldTile open = ref At(column, row);
                open.LiquidAmount = 0;
                open.Wall = LivingWoodWall;
                open.Flags &= ~WorldTileFlags.Active;
            }
        }
    }

    /// <summary>
    /// The chamber. Its frame is two cells wider on every side than the room it hollows, and the source's
    /// sandstone scrub in this second loop is a no-op that assigns Sandstone back onto itself; it is reproduced
    /// as written because removing it would be a change, not a fix.
    /// </summary>
    private void CarveWoodenChamber(int near, int far, int top, int y)
    {
        for (int column = near - 2; column <= far + 2; column++)
        {
            for (int row = top - 2; row <= y + 2; row++)
            {
                if (!Contains(column, row))
                    continue;

                if (WallAt(column, row) != LivingWoodWall && TypeAt(column, row) != Platform)
                {
                    ref WorldTile cell = ref At(column, row);
                    cell.Flags |= WorldTileFlags.Active;
                    cell.Type = LivingWood;
                    cell.Shape = 0;
                }

                if (row < top || row > y || column < near || column > far)
                    continue;

                ref WorldTile open = ref At(column, row);
                open.LiquidAmount = 0;
                open.Wall = LivingWoodWall;
                open.Flags &= ~WorldTileFlags.Active;
            }
        }
    }

    /// <summary>
    /// Source <c>GrowLivingTree_HorizontalTunnel</c>. It probes right then left, or left then right, for a
    /// place to break out - open space, another living tree's trunk, or a cobweb - and bores a corridor there
    /// with a door at each end on a one-in-three draw.
    /// </summary>
    public bool HorizontalTunnel(int i, int j)
    {
        int leftEnd = i;
        int rightEnd = i;
        const int reach = 80;
        int direction = random.Next(2) == 0 ? -1 : 1;

        for (int pass = 0; pass < 2; pass++)
        {
            cancellation.ThrowIfCancellationRequested();
            bool blocked = false;
            if (leftEnd == i && direction > 0)
            {
                for (int column = i + 5; column < i + reach; column++)
                {
                    if (!InWorld(column, j, 10))
                        return false;
                    if (TypeAt(column, j) == Cobweb)
                    {
                        blocked = true;
                        break;
                    }

                    if (TypeAt(column, j) == LivingWood)
                    {
                        for (int row = j - 2; row <= j; row++)
                        {
                            if (WallAt(column + 2, row) != LivingWoodWall)
                                blocked = true;
                        }

                        if (!blocked)
                        {
                            pass = 2;
                            rightEnd = column + 2;
                        }

                        break;
                    }

                    if (IsActive(column, j))
                        continue;

                    bool open = true;
                    for (int row = j - 2; row <= j; row++)
                    {
                        if (j < worldSurface + 3.0 &&
                            (WallAt(column + 1, row) == 0 || WallAt(column + 2, row) == 0 ||
                             WallAt(column + 3, row) == 0))
                        {
                            return false;
                        }

                        if (IsActive(column, row) || IsActive(column + 1, row) || IsActive(column + 2, row))
                            open = false;
                    }

                    if (open)
                    {
                        pass = 2;
                        rightEnd = column;
                        break;
                    }
                }
            }

            blocked = false;
            if (rightEnd == i && direction < 0)
            {
                for (int column = i - 5; column > i - reach; column--)
                {
                    if (!InWorld(column, j, 10))
                        return false;
                    if (TypeAt(column, j) == Cobweb)
                    {
                        blocked = true;
                        break;
                    }

                    if (TypeAt(column, j) == LivingWood)
                    {
                        for (int row = j - 2; row <= j; row++)
                        {
                            if (WallAt(column - 3, row) != LivingWoodWall)
                                blocked = true;
                        }

                        if (!blocked)
                        {
                            pass = 2;
                            leftEnd = column - 2;
                        }

                        break;
                    }

                    if (IsActive(column, j))
                        continue;

                    bool open = true;
                    for (int row = j - 2; row <= j; row++)
                    {
                        if (j < worldSurface + 3.0 &&
                            (WallAt(column - 1, row) == 0 || WallAt(column - 2, row) == 0 ||
                             WallAt(column - 3, row) == 0))
                        {
                            return false;
                        }

                        if (IsActive(column, row) || IsActive(column - 1, row) || IsActive(column - 2, row))
                            open = false;
                    }

                    if (open)
                    {
                        pass = 2;
                        leftEnd = column;
                        break;
                    }
                }
            }

            direction *= -1;
        }

        if (leftEnd == rightEnd)
            return false;

        bool nearDoorPlaced = false;
        bool farDoorPlaced = false;
        for (int row = j - 5; row <= j + 1; row++)
        {
            for (int column = leftEnd; column <= rightEnd; column++)
            {
                if (!Contains(column, row))
                    continue;

                int headroom = 2;
                if (Math.Abs(column - rightEnd) > 3 && Math.Abs(column - leftEnd) > 3)
                    headroom = 4;

                if (WallAt(column, row) != LivingWoodWall && !IsFurniture(column, row))
                {
                    ushort wall = WallAt(column, row);
                    if (!IsDungeonWall(wall) &&
                        (!IsActive(column, row) ||
                         (!IsDungeonWall(WallAt(column, row - 1)) && !IsDungeonWall(WallAt(column, row + 1)))) &&
                        (IsActive(column, row) || wall != SpiderWall))
                    {
                        ref WorldTile cell = ref At(column, row);
                        cell.Flags |= WorldTileFlags.Active;
                        cell.Type = LivingWood;
                        cell.Shape = 0;
                    }

                    if (TypeAt(column, row - 1) == Sandstone)
                        At(column, row - 1).Type = 0;
                    if (TypeAt(column, row + 1) == Sandstone)
                        At(column, row + 1).Type = 0;
                }

                if (row >= j - headroom && row <= j && !IsFurniture(column, row) &&
                    !IsFurnitureAbove(column, row - 1) && TypeAt(column, row + 1) != Door)
                {
                    ref WorldTile cell = ref At(column, row);
                    if (!IsDungeonWall(cell.Wall))
                        cell.Wall = LivingWoodWall;
                    cell.LiquidAmount = 0;
                    cell.Flags &= ~WorldTileFlags.Active;
                }

                if (row != j)
                    continue;

                int style = 7;
                if (IsDungeonWall(WallAt(column, row)) || IsDungeonWall(WallAt(column, row - 1)) ||
                    IsDungeonWall(WallAt(column, row - 2)))
                {
                    style = 13;
                }

                if (column <= leftEnd + 4 && !nearDoorPlaced)
                {
                    if (TypeAt(column - 1, row) == Door || TypeAt(column + 1, row) == Door)
                    {
                        nearDoorPlaced = true;
                    }
                    else if (random.Next(3) == 0)
                    {
                        PlaceDoor(column, row, style);
                        if (TypeAt(column, row) == Door)
                            nearDoorPlaced = true;
                    }
                }

                if (column < rightEnd - 4 || farDoorPlaced)
                    continue;

                if (TypeAt(column - 1, row) == Door || TypeAt(column + 1, row) == Door)
                {
                    farDoorPlaced = true;
                }
                else if (random.Next(3) == 0)
                {
                    PlaceDoor(column, row, style);
                    if (TypeAt(column, row) == Door)
                        farDoorPlaced = true;
                }
            }
        }

        return true;
    }

    /// <summary>The corridor cell: cleared of its own identity and papered unless it already carries a claim.</summary>
    private void PaperCorridor(int x, int y)
    {
        ushort wall = WallAt(x, y);
        if (!IsDungeonWall(wall) && wall != 3 && wall != 83)
            At(x, y).Wall = LivingWoodWall;
    }

    /// <summary>A corridor's shoulder takes the wall only where something already stands or below the surface.</summary>
    private void PaperNeighbour(int x, int y)
    {
        if (!Contains(x, y))
            return;

        ushort wall = WallAt(x, y);
        if (!IsDungeonWall(wall) && (wall > 0 || y >= worldSurface))
            At(x, y).Wall = LivingWoodWall;
    }

    private void ClearSandstoneAround(int x, int y)
    {
        if (TypeAt(x - 1, y) == Sandstone)
            At(x - 1, y).Type = 0;
        if (TypeAt(x + 1, y) == Sandstone)
            At(x + 1, y).Type = 0;
        if (TypeAt(x, y - 1) == Sandstone)
            At(x, y - 1).Type = 0;
        if (TypeAt(x, y + 1) == Sandstone)
            At(x, y + 1).Type = 0;
    }

    /// <summary>Source <c>PlaceTile</c> for a platform: a single cell with the style in its vertical frame.</summary>
    private void PlacePlatform(int x, int y, int style)
    {
        if (!Contains(x, y))
            return;

        ref WorldTile cell = ref At(x, y);
        if (!cell.IsActive)
        {
            cell.Type = 0;
            cell.FrameX = 0;
            cell.FrameY = 0;
            cell.Shape = 0;
            cell.TileColor = 0;
        }

        cell.FrameY = checked((short)(18 * style));
        cell.Flags |= WorldTileFlags.Active;
        cell.Type = Platform;
        new GenerationTileFraming1458(store).SquareTileFrame(x, y);
    }

    /// <summary>Source <c>PlaceTile</c> plus <c>PlaceDoor</c>, whose three frame draws happen only on success.</summary>
    private void PlaceDoor(int x, int y, int style)
    {
        if (!Contains(x, y - 3) || !Contains(x, y + 3))
            return;

        int anchor;
        if (!IsActive(x, y - 1) && !IsActive(x, y - 2) && IsSolid(x, y - 3))
            anchor = y - 1;
        else if (!IsActive(x, y + 1) && !IsActive(x, y + 2) && IsSolid(x, y + 3))
            anchor = y + 1;
        else
            return;

        if (!Contains(x, anchor - 2) || !Contains(x, anchor + 2))
            return;
        if (!IsActive(x, anchor - 2) || At(x, anchor - 2).IsActuated ||
            !VanillaTileCollisionCatalog.IsSolid(At(x, anchor - 2).TileType) || !IsSolid(x, anchor + 2))
        {
            return;
        }

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

    /// <summary>Source <c>Place1x2</c> for a chair, whose style stride is forty pixels rather than thirty-six.</summary>
    private void PlaceChair(int x, int y, int style)
    {
        if (!Contains(x, y - 1) || !Contains(x, y + 1))
            return;
        if (!IsFlatSolidFloor(x, y + 1) || IsActive(x, y - 1))
            return;

        short frameY = checked((short)(style * 40));
        for (int offset = -1; offset <= 0; offset++)
        {
            ref WorldTile cell = ref At(x, y + offset);
            cell.Flags |= WorldTileFlags.Active;
            cell.FrameY = checked((short)(frameY + (offset + 1) * 18));
            cell.FrameX = 0;
            cell.Type = Chair;
        }
    }

    /// <summary>Source <c>Place3x3</c> for the Living Loom, which hangs its three rows ABOVE the anchor.</summary>
    private void PlaceLoom(int x, int y)
    {
        if (!Contains(x - 1, y - 2) || !Contains(x + 1, y + 1))
            return;

        for (int column = x - 1; column < x + 2; column++)
        {
            for (int row = y - 2; row < y + 1; row++)
            {
                if (IsActive(column, row))
                    return;
            }
        }

        for (int column = x - 1; column < x + 2; column++)
        {
            if (!IsFlatSolidFloor(column, y + 1))
                return;
        }

        for (int dx = 0; dx < 3; dx++)
        {
            for (int dy = 0; dy < 3; dy++)
            {
                ref WorldTile cell = ref At(x - 1 + dx, y - 2 + dy);
                cell.Flags |= WorldTileFlags.Active;
                cell.FrameY = checked((short)(dy * 18));
                cell.FrameX = checked((short)(dx * 18));
                cell.Type = LivingLoom;
            }
        }
    }

    private void ShiftFrame(int x, int y, short amount)
    {
        if (Contains(x, y))
            At(x, y).FrameX = checked((short)(At(x, y).FrameX + amount));
    }

    /// <summary>The identities the shaft refuses to overwrite: platforms, chairs, looms, chests and doors.</summary>
    private bool IsFurniture(int x, int y) =>
        TypeAt(x, y) is Platform or Chair or LivingLoom or Chest or Door;

    private bool IsFurnitureWall(int x, int y) =>
        TypeAt(x, y) is Chair or LivingLoom or Chest or Door;

    /// <summary>
    /// The same set minus the platform: the source's checks on the cell ABOVE never list type nineteen, so a
    /// platform overhead does not stop the cell below being cleared.
    /// </summary>
    private bool IsFurnitureAbove(int x, int y) =>
        TypeAt(x, y) is Chair or LivingLoom or Chest or Door;

    private bool IsFlatSolidFloor(int x, int y)
    {
        if (!Contains(x, y))
            return true;

        WorldTile tile = At(x, y);
        return tile.IsActive && !tile.IsActuated && tile.Shape == 0 &&
            VanillaTileCollisionCatalog.IsSolid(tile.TileType);
    }

    private bool IsSolid(int x, int y)
    {
        if (!Contains(x, y))
            return true;

        WorldTile tile = At(x, y);
        return tile.IsActive && VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
            !tile.IsActuated && tile.Shape < 2 && tile.Shape != 1;
    }

    private static bool IsDungeonWall(ushort wall) => wall is 7 or 8 or 9 or 94 or 95 or 96 or 97 or 98 or 99;

    private bool IsActive(int x, int y) => Contains(x, y) && At(x, y).IsActive;

    private ushort TypeAt(int x, int y) => Contains(x, y) && At(x, y).IsActive ? At(x, y).Type : (ushort)0;

    private ushort WallAt(int x, int y) => Contains(x, y) ? At(x, y).Wall : (ushort)0;

    private bool InWorld(int x, int y, int fluff) =>
        x >= fluff && x < width - fluff && y >= fluff && y < height - fluff;

    private bool Contains(int x, int y) => (uint)x < (uint)width && (uint)y < (uint)height;

    private ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
}
