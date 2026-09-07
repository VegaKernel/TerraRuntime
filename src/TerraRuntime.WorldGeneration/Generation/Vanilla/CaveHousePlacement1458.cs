using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal enum CaveHouseType1458 : byte
{
    Wood,
    Ice,
    Desert,
    Jungle,
    Mushroom,
    Granite,
    Marble
}

internal readonly record struct CaveHouseRoom1458(int X, int Y, int Width, int Height)
{
    public int Left => X;
    public int Top => Y;
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public CaveHouseRoom1458 Inflate(int amount) =>
        new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

    public bool Intersects(in CaveHouseRoom1458 other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
}

internal readonly record struct CaveHousePlacementResult1458(
    CaveHouseType1458 Type,
    CaveHouseRoom1458[] Rooms,
    int ChestX,
    int ChestY,
    ushort ChestTileType,
    int ChestStyle);

/// <summary>
/// Ordinary-seed structural slice of TerrariaServer 1.4.5.8 CaveHouseBiome/HouseUtils/HouseBuilder. Room discovery,
/// biome palette selection, shells, interior walls, connecting stairs, exits, support beams, protected-room spacing,
/// and the guaranteed configured chest are owned here. Decorative furniture and room aging stay fail-closed until
/// their multi-tile placement and framing paths are ported; this class never emits orphan decorative object tiles.
/// </summary>
internal static class CaveHousePlacement1458
{
    private const ushort Chest = 21;
    private const ushort Containers2 = 467;
    private const ushort Platform = 19;
    private const ushort ClosedDoor = 10;
    private const int MaximumDownSearch = 200;

    private static readonly ushort[] InvalidStructureTiles = [225, 41, 43, 44, 226, 203, 112, 25, 151, 21, 467];

    public static bool TryPlace(
        Workspace workspace,
        ChestPlacementState1458 state,
        IWorldGenerationVanillaRandom random,
        int originX,
        int originY,
        out CaveHousePlacementResult1458 result)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(random);
        result = default;

        var grid = new Grid(workspace.TileStore);
        if (!grid.InWorld(originX, originY, 1, 1, 30) || HasWireOrChestNearby(grid, originX, originY, 25) ||
            !TryCreateRooms(grid, random, originX, originY, out CaveHouseRoom1458[] rooms))
        {
            return false;
        }

        CaveHouseType1458 type = ResolveHouseType(grid, rooms);
        if (!AreRoomsValid(grid, state, rooms, type))
            return false;

        CaveHousePalette1458 palette = ResolvePalette(type);
        Array.Sort(rooms, static (left, right) => left.Top.CompareTo(right.Top));
        PlaceEmptyRooms(grid, rooms, in palette);
        PlaceStairs(grid, rooms, palette.PlatformStyle);
        PlaceDoors(grid, rooms, palette.DoorStyle, random);
        PlacePlatforms(grid, rooms, palette.PlatformStyle);
        PlaceSupportBeams(grid, rooms, palette.BeamType);

        if (!TryPlaceChest(workspace, grid, state, random, rooms, in palette,
                out int chestX, out int chestY))
        {
            // CaveHouseBiome config pins every ordinary house chest chance to 1.0. A house without its side-table
            // backed chest is not an admissible partial structure. Room width, the cleared interior, protected-chest
            // rejection, and the 1000-slot workspace ceiling make this an internal invariant, not a retry fallback.
            throw new InvalidOperationException("Cave house geometry did not retain its source-guaranteed chest location.");
        }

        foreach (CaveHouseRoom1458 room in rooms)
            state.ProtectedHouseRooms.Add(room.Inflate(8));
        result = new CaveHousePlacementResult1458(type, rooms, chestX, chestY, palette.ChestTileType, palette.ChestStyle);
        return true;
    }

    private static bool TryCreateRooms(
        Grid grid,
        IWorldGenerationVanillaRandom random,
        int originX,
        int originY,
        out CaveHouseRoom1458[] rooms)
    {
        rooms = [];
        if (!TryFindSolidDown(grid, originX, originY, MaximumDownSearch, out int floorY) || floorY == originY)
            return false;

        CaveHouseRoom1458 middle = FindRoom(grid, originX, floorY);
        CaveHouseRoom1458 upper = FindRoom(grid, middle.X + middle.Width / 2, middle.Y + 1);
        CaveHouseRoom1458 lower = FindRoom(grid, middle.X + middle.Width / 2, middle.Y + middle.Height + 10);
        lower = lower with { Y = middle.Y + middle.Height - 1 };
        double upperSolid = GetSolidPercentage(grid, upper);
        double lowerSolid = GetSolidPercentage(grid, lower);
        middle = middle with { Y = middle.Y + 3 };
        upper = upper with { Y = upper.Y + 3 };
        lower = lower with { Y = lower.Y + 3 };

        var selected = new List<CaveHouseRoom1458>(3);
        if (random.NextDouble() > upperSolid + 0.2d)
            selected.Add(upper);
        selected.Add(middle);
        if (random.NextDouble() > lowerSolid + 0.2d)
            selected.Add(lower);
        if (selected.Count == 0)
            return false;

        foreach (CaveHouseRoom1458 room in selected)
        {
            if (!grid.InWorld(room.X, room.Y, room.Width, room.Height, 10) ||
                room.Bottom > grid.Height - 220)
            {
                return false;
            }
        }
        rooms = selected.ToArray();
        return true;
    }

    private static CaveHouseRoom1458 FindRoom(Grid grid, int originX, int originY)
    {
        int left = TryFindSolidHorizontal(grid, originX, originY, -1, 25, out int foundLeft)
            ? foundLeft
            : originX - 25;
        int right = TryFindSolidHorizontal(grid, originX, originY, 1, 25, out int foundRight)
            ? foundRight
            : originX + 25;
        int width = Math.Clamp(right - left, 15, 30);
        int x = originX - left > right - originX ? left : right - width;
        int upperLeft = TryFindSolidVertical(grid, left, originY, -1, 10, out int foundUpperLeft)
            ? foundUpperLeft
            : originY - 10;
        int upperRight = TryFindSolidVertical(grid, right, originY, -1, 10, out int foundUpperRight)
            ? foundUpperRight
            : originY - 10;
        int height = Math.Clamp(Math.Max(originY - upperLeft, originY - upperRight), 8, 12);
        return new CaveHouseRoom1458(x, originY - height, width, height);
    }

    private static bool AreRoomsValid(
        Grid grid,
        ChestPlacementState1458 state,
        IReadOnlyList<CaveHouseRoom1458> rooms,
        CaveHouseType1458 type)
    {
        foreach (CaveHouseRoom1458 room in rooms)
        {
            CaveHouseRoom1458 padded = room.Inflate(5);
            foreach (CaveHouseRoom1458 reserved in state.ProtectedHouseRooms)
            {
                if (padded.Intersects(in reserved))
                    return false;
            }

            for (int x = padded.Left; x < padded.Right; x++)
            for (int y = padded.Top; y < padded.Bottom; y++)
            {
                if (!grid.InWorld(x, y))
                    return false;
                WorldTile tile = grid.At(x, y);
                if (tile.IsActive && Array.IndexOf(InvalidStructureTiles, tile.Type) >= 0)
                    return false;
            }

            if (type == CaveHouseType1458.Granite)
                continue;
            CaveHouseRoom1458 lavaSearch = room.Inflate(2);
            for (int x = lavaSearch.Left; x < lavaSearch.Right; x++)
            for (int y = lavaSearch.Top; y < lavaSearch.Bottom; y++)
            {
                WorldTile tile = grid.At(x, y);
                if (tile.LiquidAmount > 0 && tile.LiquidKind == WorldLiquidKind.Lava)
                    return false;
            }
        }
        return true;
    }

    private static CaveHouseType1458 ResolveHouseType(Grid grid, IReadOnlyList<CaveHouseRoom1458> rooms)
    {
        int wood = 0;
        int jungle = 0;
        int mushroom = 0;
        int ice = 0;
        int desert = 0;
        int granite = 0;
        int marble = 0;
        foreach (CaveHouseRoom1458 room in rooms)
        {
            CaveHouseRoom1458 scan = room.Inflate(10);
            for (int x = scan.Left; x < scan.Right; x++)
            for (int y = scan.Top; y < scan.Bottom; y++)
            {
                WorldTile tile = grid.At(x, y);
                if (!tile.IsActive)
                    continue;
                switch (tile.Type)
                {
                    case 0:
                    case 1:
                        wood++;
                        break;
                    case 59:
                        jungle++;
                        mushroom++;
                        break;
                    case 60:
                        jungle += 10;
                        break;
                    case 70:
                        mushroom += 10;
                        break;
                    case 147:
                    case 161:
                        ice++;
                        break;
                    case 53:
                    case 396:
                    case 397:
                        desert++;
                        break;
                    case 368:
                        granite++;
                        break;
                    case 367:
                        marble++;
                        break;
                }
            }
        }

        CaveHouseType1458 result = CaveHouseType1458.Wood;
        int score = wood;
        Promote(CaveHouseType1458.Jungle, jungle, ref result, ref score);
        Promote(CaveHouseType1458.Mushroom, mushroom, ref result, ref score);
        Promote(CaveHouseType1458.Ice, ice, ref result, ref score);
        Promote(CaveHouseType1458.Desert, desert, ref result, ref score);
        Promote(CaveHouseType1458.Granite, granite, ref result, ref score);
        Promote(CaveHouseType1458.Marble, marble, ref result, ref score);
        return result;
    }

    private static void Promote(CaveHouseType1458 candidate, int candidateScore,
        ref CaveHouseType1458 result, ref int score)
    {
        if (candidateScore <= score)
            return;
        result = candidate;
        score = candidateScore;
    }

    private static void PlaceEmptyRooms(Grid grid, IReadOnlyList<CaveHouseRoom1458> rooms,
        in CaveHousePalette1458 palette)
    {
        foreach (CaveHouseRoom1458 room in rooms)
        {
            for (int x = room.Left; x < room.Right; x++)
            for (int y = room.Top; y < room.Bottom; y++)
                SetBlock(ref grid.At(x, y), palette.TileType);
            for (int x = room.Left + 1; x < room.Right - 1; x++)
            for (int y = room.Top + 1; y < room.Bottom - 1; y++)
            {
                ref WorldTile tile = ref grid.At(x, y);
                ClearTile(ref tile);
                tile.Wall = palette.WallType;
            }
        }
    }

    private static void PlaceStairs(Grid grid, IReadOnlyList<CaveHouseRoom1458> rooms, int platformStyle)
    {
        for (int index = 1; index < rooms.Count; index++)
        {
            CaveHouseRoom1458 room = rooms[index];
            CaveHouseRoom1458 above = rooms[index - 1];
            int leftDistance = above.X - room.X;
            int rightDistance = room.Right - above.Right;
            int direction = leftDistance > rightDistance ? -1 : 1;
            int startX = direction < 0 ? room.Right - 1 : room.Left;
            int startY = room.Top + 1;
            for (int step = 0; step < room.Height - 2; step++)
            {
                int x = startX + direction * (step + 1);
                int y = startY + step;
                if (!grid.InWorld(x, y))
                    break;
                SetPlatform(ref grid.At(x, y), platformStyle, direction > 0 ? (byte)2 : (byte)3);
            }
        }
    }

    private static void PlaceDoors(Grid grid, IReadOnlyList<CaveHouseRoom1458> rooms, int doorStyle,
        IWorldGenerationVanillaRandom random)
    {
        foreach (CaveHouseRoom1458 room in rooms)
        {
            int top = room.Bottom - 4;
            if (IsClearRectangle(grid, room.Right, top, 4, 3))
                PlaceDoor(grid, room.Right - 1, top, doorStyle, random);
            if (IsClearRectangle(grid, room.Left - 4, top, 4, 3))
                PlaceDoor(grid, room.Left, top, doorStyle, random);
        }
    }

    private static void PlacePlatforms(Grid grid, IReadOnlyList<CaveHouseRoom1458> rooms, int platformStyle)
    {
        CaveHouseRoom1458 top = rooms[0];
        int topX = top.Left + Math.Max(2, (top.Width - 3) / 2);
        if (IsClearRectangle(grid, topX, top.Top - 5, 3, 5))
        {
            for (int x = topX; x < topX + 3; x++)
                SetPlatform(ref grid.At(x, top.Top), platformStyle, 0);
        }

        CaveHouseRoom1458 bottom = rooms[^1];
        int bottomX = bottom.Left + Math.Max(2, (bottom.Width - 3) / 2);
        if (IsClearRectangle(grid, bottomX, bottom.Bottom, 3, 5))
        {
            for (int x = bottomX; x < bottomX + 3; x++)
                SetPlatform(ref grid.At(x, bottom.Bottom - 1), platformStyle, 0);
        }
    }

    private static void PlaceSupportBeams(Grid grid, IReadOnlyList<CaveHouseRoom1458> rooms, ushort beamType)
    {
        int left = rooms.Min(static room => room.Left);
        int right = rooms.Max(static room => room.Right) - 1;
        int spacing = 6;
        while (spacing > 4 && (right - left) % spacing != 0)
            spacing--;

        for (int x = left; x <= right; x += spacing)
        for (int roomIndex = 0; roomIndex < rooms.Count; roomIndex++)
        {
            CaveHouseRoom1458 room = rooms[roomIndex];
            if (x < room.Left || x >= room.Right)
                continue;
            int startY = room.Bottom;
            int maximum = 50;
            for (int next = roomIndex + 1; next < rooms.Count; next++)
            {
                CaveHouseRoom1458 lower = rooms[next];
                if (x >= lower.Left && x < lower.Right)
                    maximum = Math.Min(maximum, lower.Top - startY);
            }
            if (maximum <= 0 || !TryFindSolidDown(grid, x, startY, maximum, out int endY))
                continue;
            bool objectInBeamPath = false;
            for (int y = startY; y < endY; y++)
            {
                WorldTile existing = grid.At(x, y);
                if (existing.IsActive && VanillaWorldFrameImportance326.IsFrameImportant(existing.Type))
                {
                    objectInBeamPath = true;
                    break;
                }
            }
            if (objectInBeamPath)
                continue;
            for (int y = startY; y < endY; y++)
                SetBlock(ref grid.At(x, y), beamType);
            if (grid.InWorld(x, endY))
                grid.At(x, endY).Shape = 0;
        }
    }

    private static bool TryPlaceChest(
        Workspace workspace,
        Grid grid,
        ChestPlacementState1458 state,
        IWorldGenerationVanillaRandom random,
        IReadOnlyList<CaveHouseRoom1458> rooms,
        in CaveHousePalette1458 palette,
        out int chestX,
        out int chestY)
    {
        foreach (CaveHouseRoom1458 room in rooms)
        {
            int floorY = room.Bottom - 1;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int x = random.Next(2, room.Width - 2) + room.X;
                if (TryPlaceChestAt(workspace, grid, state, random, x, floorY, in palette,
                        out chestX, out chestY))
                    return true;
            }
            for (int x = room.X + 2; x <= room.Right - 2; x++)
            {
                if (TryPlaceChestAt(workspace, grid, state, random, x, floorY, in palette,
                        out chestX, out chestY))
                    return true;
            }
        }
        chestX = -1;
        chestY = -1;
        return false;
    }

    private static bool TryPlaceChestAt(
        Workspace workspace,
        Grid grid,
        ChestPlacementState1458 state,
        IWorldGenerationVanillaRandom random,
        int left,
        int floorY,
        in CaveHousePalette1458 palette,
        out int chestX,
        out int chestY)
    {
        int top = floorY - 2;
        chestX = -1;
        chestY = -1;
        if (!grid.InWorld(left, top, 2, 3, 1) ||
            grid.At(left, top).IsActive || grid.At(left + 1, top).IsActive ||
            grid.At(left, top + 1).IsActive || grid.At(left + 1, top + 1).IsActive ||
            !grid.IsSolid(left, floorY) || !grid.IsSolid(left + 1, floorY))
        {
            return false;
        }

        WorldGenerationChestItem[] loot = ChestLoot1458.BuildBuried(
            random,
            state.Bootstrap ?? throw new InvalidOperationException("Cave-house placement requires Reset bootstrap state."),
            floorY,
            state.RockLayer,
            state.LavaLine,
            grid.Height);
        int frameX = palette.ChestStyle * 36;
        SetObject(ref grid.At(left, top), palette.ChestTileType, checked((short)frameX), 0);
        SetObject(ref grid.At(left + 1, top), palette.ChestTileType, checked((short)(frameX + 18)), 0);
        SetObject(ref grid.At(left, top + 1), palette.ChestTileType, checked((short)frameX), 18);
        SetObject(ref grid.At(left + 1, top + 1), palette.ChestTileType, checked((short)(frameX + 18)), 18);
        if (!workspace.TryAddChest(left, top, string.Empty, loot))
            return false;
        chestX = left;
        chestY = top;
        return true;
    }

    private static bool HasWireOrChestNearby(Grid grid, int originX, int originY, int radius)
    {
        for (int x = originX - radius; x <= originX + radius; x++)
        for (int y = originY - radius; y <= originY + radius; y++)
        {
            WorldTile tile = grid.At(x, y);
            if (tile.HasAnyWire || tile.IsActive && tile.Type is Chest or Containers2)
                return true;
        }
        return false;
    }

    private static double GetSolidPercentage(Grid grid, in CaveHouseRoom1458 room)
    {
        int solid = 0;
        for (int x = room.Left; x < room.Right; x++)
        for (int y = room.Top; y < room.Bottom; y++)
        {
            if (grid.IsSolid(x, y))
                solid++;
        }
        return solid / (double)(room.Width * room.Height);
    }

    private static bool TryFindSolidDown(Grid grid, int x, int y, int distance, out int resultY) =>
        TryFindSolidVertical(grid, x, y, 1, distance, out resultY);

    private static bool TryFindSolidVertical(Grid grid, int x, int y, int direction, int distance, out int resultY)
    {
        for (int step = 0; step <= distance; step++)
        {
            int candidate = y + direction * step;
            if (!grid.InWorld(x, candidate))
                break;
            if (!grid.IsSolid(x, candidate))
                continue;
            resultY = candidate;
            return true;
        }
        resultY = y;
        return false;
    }

    private static bool TryFindSolidHorizontal(Grid grid, int x, int y, int direction, int distance, out int resultX)
    {
        for (int step = 0; step <= distance; step++)
        {
            int candidate = x + direction * step;
            if (!grid.InWorld(candidate, y))
                break;
            if (!grid.IsSolid(candidate, y))
                continue;
            resultX = candidate;
            return true;
        }
        resultX = x;
        return false;
    }

    private static bool IsClearRectangle(Grid grid, int left, int top, int width, int height)
    {
        if (!grid.InWorld(left, top, width, height, 1))
            return false;
        for (int x = left; x < left + width; x++)
        for (int y = top; y < top + height; y++)
        {
            if (grid.IsSolid(x, y))
                return false;
        }
        return true;
    }

    private static void PlaceDoor(Grid grid, int x, int top, int style, IWorldGenerationVanillaRandom random)
    {
        int styleGroup = style / 36;
        int styleWithinGroup = style % 36;
        int frameX = 54 * styleGroup + random.Next(3) * 18;
        int frameY = 54 * styleWithinGroup;
        for (int dy = 0; dy < 3; dy++)
            SetObject(ref grid.At(x, top + dy), ClosedDoor, checked((short)frameX), checked((short)(frameY + dy * 18)));
    }

    private static void SetBlock(ref WorldTile tile, ushort type)
    {
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.Flags &= ~(WorldTileFlags.Inactive | WorldTileFlags.Actuator);
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    private static void ClearTile(ref WorldTile tile)
    {
        tile.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive | WorldTileFlags.Actuator);
        tile.Type = 0;
        tile.FrameX = 0;
        tile.FrameY = 0;
        tile.Shape = 0;
    }

    private static void SetPlatform(ref WorldTile tile, int style, byte shape)
    {
        SetObject(ref tile, Platform, 0, checked((short)(style * 18)));
        tile.Shape = shape;
    }

    private static void SetObject(ref WorldTile tile, ushort type, short frameX, short frameY)
    {
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.Flags &= ~(WorldTileFlags.Inactive | WorldTileFlags.Actuator);
        tile.FrameX = frameX;
        tile.FrameY = frameY;
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    private static CaveHousePalette1458 ResolvePalette(CaveHouseType1458 type) => type switch
    {
        CaveHouseType1458.Wood => new(30, 27, 124, 0, 0, Chest, 1),
        CaveHouseType1458.Ice => new(321, 149, 574, 19, 30, Chest, 11),
        CaveHouseType1458.Desert => new(396, 187, 577, 42, 43, Containers2, 10),
        CaveHouseType1458.Jungle => new(158, 42, 575, 2, 2, Chest, 8),
        CaveHouseType1458.Mushroom => new(190, 74, 578, 18, 6, Chest, 32),
        CaveHouseType1458.Granite => new(369, 181, 576, 28, 34, Chest, 50),
        CaveHouseType1458.Marble => new(357, 179, 561, 29, 35, Chest, 51),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private readonly record struct CaveHousePalette1458(
        ushort TileType,
        ushort WallType,
        ushort BeamType,
        int PlatformStyle,
        int DoorStyle,
        ushort ChestTileType,
        int ChestStyle);

    private sealed class Grid(WorldTileStore store)
    {
        public int Width => store.Dimensions.WidthTiles;
        public int Height => store.Dimensions.HeightTiles;
        public ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
        public bool InWorld(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
        public bool InWorld(int x, int y, int width, int height, int padding) =>
            x >= padding && y >= padding && x + width <= Width - padding && y + height <= Height - padding;
        public bool IsSolid(int x, int y)
        {
            WorldTile tile = At(x, y);
            return tile.IsActive && !tile.IsActuated &&
                   VanillaTileCollisionCatalog.IsSolid(tile.TileType) &&
                   !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
        }
    }
}
