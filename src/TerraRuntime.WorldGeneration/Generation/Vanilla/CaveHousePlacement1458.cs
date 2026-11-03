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
/// Ordinary-seed TerrariaServer 1.4.5.8 CaveHouseBiome/HouseUtils/HouseBuilder placement on the retained candidate,
/// including source construction callbacks, decoration, aging, protected rooms and shared chest/tool state.
/// </summary>
internal static partial class CaveHousePlacement1458
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

        var grid = new Grid(workspace.TileStore, workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid : null);
        if (!grid.InWorld(originX, originY, 1, 1, 30) || HasWireOrChestNearby(grid, originX, originY, 25) ||
            !TryCreateRooms(grid, random, originX, originY, out CaveHouseRoom1458[] rooms))
        {
            return false;
        }

        CaveHouseType1458 type = ResolveHouseType(grid, rooms);
        if (!AreRoomsValid(workspace, grid, state, rooms, type))
            return false;

        result = PlaceRooms(workspace, state, random, rooms, type);
        return true;
    }

    /// <summary>HouseBuilder construction and Place after the caller has accepted its room geometry.</summary>
    internal static CaveHousePlacementResult1458 PlaceRooms(Workspace workspace, ChestPlacementState1458 state,
        IWorldGenerationVanillaRandom random, CaveHouseRoom1458[] rooms, CaveHouseType1458 type)
    {
        var grid = new Grid(workspace.TileStore, workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid : null);
        Array.Sort(rooms, static (left, right) => left.Top.CompareTo(right.Top));
        if (type is CaveHouseType1458.Wood or CaveHouseType1458.Ice or CaveHouseType1458.Desert or CaveHouseType1458.Jungle)
            random.NextDouble(); // Ordinary PotentiallyConvertToSeedHouse draws before testing its empty choices.
        CaveHousePalette1458 palette = ResolvePalette(type);
        var construction = new AgingRoom(workspace, random);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: workspace.HasVanillaGenerationSolidity);
        PlaceEmptyRooms(grid, construction, framing, rooms, in palette);
        foreach (CaveHouseRoom1458 room in rooms)
        {
            state.ProtectedHouseRooms.Add(room.Inflate(8)); // retained house geometry, never a protection query
            workspace.VanillaStructureMap.AddProtectedStructure(new(room.Left,room.Top,room.Width,room.Height),8);
        }
        PlaceStairs(grid, construction.Walls, framing, rooms, in palette);
        PlaceDoors(grid, construction, framing, rooms, palette.DoorStyle, random);
        PlacePlatforms(grid, framing, rooms, palette.PlatformStyle);
        PlaceSupportBeams(grid, framing, rooms, palette.BeamType);

        PlacePriorityTool(workspace, state, random, rooms, type);
        FillRooms(workspace, random, rooms, type);
        AgeRooms(workspace, random, type, rooms);
        BuriedChestResult1458? chest = PlaceRoomChest(workspace, state, random, rooms, in palette);
        PlaceBiomeTool(workspace, state, random, rooms, type);
        return new CaveHousePlacementResult1458(type, rooms, chest?.Left ?? -1, chest?.Top ?? -1,
            chest?.TileType ?? palette.ChestTileType, chest?.Style ?? palette.ChestStyle);
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
        Workspace workspace,
        Grid grid,
        ChestPlacementState1458 state,
        IReadOnlyList<CaveHouseRoom1458> rooms,
        CaveHouseType1458 type)
    {
        foreach (CaveHouseRoom1458 room in rooms)
        {
            if(!workspace.VanillaStructureMap.CanPlace(new(room.Left,room.Top,room.Width,room.Height),5,
                static tile=>Array.IndexOf(InvalidStructureTiles,tile)<0))return false;

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

    private static void PlaceEmptyRooms(Grid grid, AgingRoom construction, GenerationTileFraming1458 framing,
        IReadOnlyList<CaveHouseRoom1458> rooms,
        in CaveHousePalette1458 palette)
    {
        foreach (CaveHouseRoom1458 room in rooms)
        {
            for (int x = room.Left; x < room.Right; x++)
                for (int y = room.Top; y < room.Bottom; y++)
                {
                    SetBlock(ref grid.At(x, y), palette.TileType);
                    FrameStructure(framing, x, y);
                }
            for (int x = room.Left + 1; x < room.Right - 1; x++)
                for (int y = room.Top + 1; y < room.Bottom - 1; y++)
                {
                    construction.ClearTile(x, y);
                    construction.Walls.PlaceWall(x, y, palette.WallType);
                }
        }
    }

    private static void PlaceStairs(Grid grid, GenerationWallFraming1458 walls, GenerationTileFraming1458 framing,
        IReadOnlyList<CaveHouseRoom1458> rooms, in CaveHousePalette1458 palette)
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
                PlaceStructurePlatform(grid, framing, x, y, palette.PlatformStyle);
                // Fresh room interiors have no sloping veto above their new platforms.
                if (grid.At(x, y).Type == Platform)
                    grid.At(x, y).TrySetSourceShape(false, direction > 0 ? (byte)1 : (byte)2);
                FrameStructure(framing, x, y);
            }
            int capX = startX + (direction > 0 ? 1 : -4), capY = startY - 1;
            for (int x = capX; x < capX + 4; x++)
            {
                grid.At(x, capY) = default; // Actions.Clear, including the old wall and wiring.
                walls.PlaceWall(x, capY, palette.WallType);
                PlaceStructurePlatform(grid, framing, x, capY, palette.PlatformStyle);
                FrameStructure(framing, x, capY);
            }
        }
    }

    private static void PlaceDoors(Grid grid, AgingRoom construction, GenerationTileFraming1458 framing,
        IReadOnlyList<CaveHouseRoom1458> rooms, int doorStyle,
        IWorldGenerationVanillaRandom random)
    {
        var exits = new List<(int X, int Y)>();
        foreach (CaveHouseRoom1458 room in rooms)
        {
            foreach (bool left in new[] { false, true })
                for (int step = 0; step < room.Height - 5; step++)
                {
                    int y = room.Bottom - 4 - step;
                    if (!IsClearRectangle(grid, left ? room.Left - 4 : room.Right, y, 4, 3))
                        continue;
                    exits.Add((left ? room.Left : room.Right - 1, y));
                    break;
                }
        }
        foreach ((int x, int y) in exits)
        {
            for (int row = y; row < y + 3; row++)
                construction.ClearTile(x, row);
            PlaceDoor(grid, framing, x, y, doorStyle, random);
        }
    }

    private static void PlacePlatforms(Grid grid, GenerationTileFraming1458 framing,
        IReadOnlyList<CaveHouseRoom1458> rooms, int platformStyle)
    {
        CaveHouseRoom1458 top = rooms[0];
        CaveHouseRoom1458 bottom = rooms[^1];
        var exits = new List<(int X, int Y)>();
        foreach ((CaveHouseRoom1458 room, bool up) in new[] { (top, true), (bottom, false) })
            for (int step = 0; step < room.Width - 7; step++)
            {
                int x = room.Right - 5 - step, y = up ? room.Top : room.Bottom - 1;
                if (!IsClearRectangle(grid, x, up ? y - 5 : y, 3, 5))
                    continue;
                exits.Add((x, y));
                break;
            }
        foreach ((int left, int y) in exits)
            for (int x = left; x < left + 3; x++)
            {
                WorldTile previous = grid.At(x, y);
                grid.At(x, y) = new WorldTile { Type = previous.Type, Wall = previous.Wall }; // Tile.ClearMetadata.
                PlaceStructurePlatform(grid, framing, x, y, platformStyle);
                FrameStructure(framing, x, y);
            }
    }

    private static void PlaceSupportBeams(Grid grid, GenerationTileFraming1458 framing,
        IReadOnlyList<CaveHouseRoom1458> rooms, ushort beamType)
    {
        int left = rooms.Min(static room => room.Left);
        int right = rooms.Max(static room => room.Right) - 1;
        int spacing = 6;
        while (spacing > 4 && (right - left) % spacing != 0)
            spacing--;

        var beams = new List<(int X, int Top, int Bottom)>();
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
                if (maximum <= 0)
                    continue;
                // HouseBuilder.CreateSupportBeamList forces the gap to a lower room when it is
                // shorter than 50, even if there is no terrain. Only the unconstrained 50-cell
                // search needs a solid hit. The preceding platform check is on the top cell,
                // not a blanket ban on frame-important objects anywhere inside the beam path.
                int endY;
                if (maximum < 50)
                    endY = startY + maximum;
                else if (!TryFindSolidDown(grid, x, startY, maximum, out endY))
                    continue;
                beams.Add((x, startY, endY));
            }
        foreach ((int x, int top, int bottom) in beams)
        {
            if (bottom - top <= 1 || grid.At(x, top - 1).Type == Platform)
                continue;
            for (int y = top; y < bottom; y++)
            {
                SetBlock(ref grid.At(x, y), beamType);
                FrameStructure(framing, x, y);
            }
            grid.At(x, bottom).ClearSourceShape();
        }
    }

    internal static void PlaceSupportBeamsForTesting(
        Workspace workspace, IReadOnlyList<CaveHouseRoom1458> rooms, ushort beamType,
        IWorldGenerationVanillaRandom random) =>
        PlaceSupportBeams(new Grid(workspace.TileStore, workspace.HasVanillaGenerationSolidity ? workspace.IsVanillaGenerationSolid : null), new GenerationTileFraming1458(workspace.TileStore, random, workspace, useWorldGenerationSolidity: workspace.HasVanillaGenerationSolidity),
            rooms, beamType);

    private static bool HasWireOrChestNearby(Grid grid, int originX, int originY, int radius)
    {
        for (int x = originX - radius; x <= originX + radius; x++)
            for (int y = originY - radius; y <= originY + radius; y++)
            {
                WorldTile tile = grid.At(x, y);
                if ((tile.Flags & WorldTileFlags.WireRed) != 0 || tile.Type is Chest or Containers2)
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
        for (int step = 0; step < distance; step++)
        {
            int candidate = y + direction * step;
            if (!grid.InWorld(x, candidate))
                break;
            if (!grid.IsSearchSolid(x, candidate))
                continue;
            resultY = candidate;
            return true;
        }
        resultY = y;
        return false;
    }

    private static bool TryFindSolidHorizontal(Grid grid, int x, int y, int direction, int distance, out int resultX)
    {
        for (int step = 0; step < distance; step++)
        {
            int candidate = x + direction * step;
            if (!grid.InWorld(candidate, y))
                break;
            if (!grid.IsSearchSolid(candidate, y))
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
                if (grid.IsSearchSolid(x, y))
                    return false;
            }
        return true;
    }

    internal static bool PlaceHouseDoor(Workspace workspace,IWorldGenerationVanillaRandom random,int x,int y,int style)
    {
        var grid=new Grid(workspace.TileStore,workspace.HasVanillaGenerationSolidity?workspace.IsVanillaGenerationSolid:null);
        if(grid.At(x,y).IsActive)return false;
        var framing=new GenerationTileFraming1458(workspace.TileStore,random,workspace,workspace.HasVanillaGenerationSolidity);
        PlaceDoor(grid,framing,x,y,style,random);
        return grid.At(x,y).IsActive;
    }

    private static void PlaceDoor(Grid grid, GenerationTileFraming1458 framing,
        int x, int anchorY, int style, IWorldGenerationVanillaRandom random)
    {
        HellFortGenerator1458.ClearPlacementAnchor(ref grid.At(x, anchorY));
        int middle;
        if (!grid.At(x, anchorY - 1).IsActive && !grid.At(x, anchorY - 2).IsActive &&
            grid.At(x, anchorY - 3).IsActive && grid.IsSolidType(grid.At(x, anchorY - 3).TileType))
            middle = anchorY - 1;
        else
        {
            if (grid.At(x, anchorY + 1).IsActive || grid.At(x, anchorY + 2).IsActive ||
                !grid.At(x, anchorY + 3).IsActive || !grid.IsSolidType(grid.At(x, anchorY + 3).TileType))
                return;
            middle = anchorY + 1;
        }
        WorldTile above = grid.At(x, middle - 2), below = grid.At(x, middle + 2);
        if (above.IsActive && !above.IsActuated && grid.IsSolidType(above.TileType) &&
            grid.IsSolid(x, middle + 2) && below.SourceSlope == 0 && !below.IsHalfBrick)
        {
            int frameX = style / 36 * 54, frameY = style % 36 * 54;
            for (int dy = 0; dy < 3; dy++)
            {
                ref WorldTile tile = ref grid.At(x, middle - 1 + dy);
                tile.Type = ClosedDoor;
                tile.Flags |= WorldTileFlags.Active;
                tile.FrameX = (short)(frameX + random.Next(3) * 18);
                tile.FrameY = (short)(frameY + dy * 18);
            }
        }
        framing.SquareTileFrame(x, anchorY);
        if (grid.At(x, anchorY).IsActive)
            framing.SquareTileFrame(x, anchorY);
    }

    private static void PlaceStructurePlatform(Grid grid, GenerationTileFraming1458 framing, int x, int y, int style)
    {
        if (grid.At(x, y).IsActive)
            return;
        ref WorldTile tile = ref grid.At(x, y);
        HellFortGenerator1458.ClearPlacementAnchor(ref tile);
        tile.Flags &= ~WorldTileFlags.HalfBrickWithSlope;
        tile.Flags |= WorldTileFlags.Active;
        tile.Type = Platform;
        tile.FrameY = (short)(style * 18);
        framing.SquareTileFrame(x, y);
    }

    private static void FrameStructure(GenerationTileFraming1458 framing, int x, int y)
    {
        framing.TileFrame(x, y, resetFrame: true);
        framing.TileFrame(x + 1, y, resetFrame: true);
        framing.TileFrame(x - 1, y, resetFrame: true);
        framing.TileFrame(x, y + 1, resetFrame: true);
        framing.TileFrame(x, y - 1, resetFrame: true);
    }

    private static void SetBlock(ref WorldTile tile, ushort type)
    {
        // Actions.SetTileKeepWall clears everything except wiring/actuation,
        // then restores only the old wall identity before the separate framing
        // stage. Wall paint/coatings do not survive that source clear.
        tile.Type = type;
        tile.Flags = (tile.Flags & (WorldTileFlagMasks.Wires | WorldTileFlagMasks.Actuation)) | WorldTileFlags.Active;
        tile.FrameX = 0;
        tile.FrameY = 0;
        tile.ClearSourceShape();
        tile.TileColor = 0;
        tile.WallColor = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    private static void ClearTile(ref WorldTile tile)
    {
        // WorldUtils.ClearTile delegates to Tile.ClearTile: retain material,
        // frames, paint, liquid, wires and the actuator installation itself.
        tile.Flags &= ~(WorldTileFlags.Active | WorldTileFlags.Inactive);
        tile.ClearSourceShape();
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

    private sealed class Grid(WorldTileStore store, Func<TileTypeId, bool>? solidity = null)
    {
        public int Width => store.Dimensions.WidthTiles;
        public int Height => store.Dimensions.HeightTiles;
        public ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
        public bool InWorld(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
        public bool InWorld(int x, int y, int width, int height, int padding) =>
            x >= padding && y >= padding && x + width <= Width - padding && y + height <= Height - padding;
        public bool IsSolidType(TileTypeId type) => solidity?.Invoke(type) ?? VanillaTileCollisionCatalog.IsSolid(type);

        public bool IsSolid(int x, int y)
        {
            WorldTile tile = At(x, y);
            return tile.IsActive && !tile.IsActuated &&
                   (solidity?.Invoke(tile.TileType) ?? VanillaTileCollisionCatalog.IsSolid(tile.TileType)) &&
                   !VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
        }
        public bool IsSearchSolid(int x, int y) => InWorld(x, y, 1, 1, 10) && At(x, y).IsActive &&
            (solidity?.Invoke(At(x, y).TileType) ?? VanillaTileCollisionCatalog.IsSolid(At(x, y).TileType));
    }
}
