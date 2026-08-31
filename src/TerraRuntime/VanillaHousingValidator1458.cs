using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime;

internal enum VanillaHousingValidationResult : byte
{
    Valid = 0,
    TooCloseToWorldEdge = 1,
    StartedInSolidTile = 2,
    RoomTooBig = 3,
    RoomTooSmall = 4,
    MissingOrUnsafeWall = 5,
    MissingFurniture = 6,
    StinkbugBlocked = 7,
    EvilRoom = 8,
    NoStandingSpace = 9,
    SpecialNpcConditionFailed = 10,
    RoomOccupied = 11
}

internal readonly record struct VanillaHousingPlacement(
    int HomeTileX,
    int HomeTileY,
    VanillaHousingValidationResult Result)
{
    public bool IsValid => Result == VanillaHousingValidationResult.Valid;
}

internal readonly record struct VanillaHousingOccupant(
    NpcTypeId Type,
    int HomeTileX,
    int HomeTileY);

/// <summary>
/// Source-shaped clean-room implementation of the TerrariaServer 1.4.5.8 MoveTownNPC room check. It imports the
/// pinned StartRoomCheck flood bounds, house-wall continuity, RoomNeeds sets, stinkbug gate, evil-room score and
/// ScoreRoom standing-spot selection and housing-category occupancy compatibility.
/// </summary>
internal sealed class VanillaHousingValidator1458
{
    private const int WorldEdgeMargin = 10;
    private const int MaximumRoomTiles = 750;
    private const int MinimumRoomTiles = 60;
    private const int MaximumRoomSize = 100;

    private static ReadOnlySpan<int> ChairTypes => [15, 79, 89, 102, 487, 497];
    private static ReadOnlySpan<int> TableTypes => [14, 18, 87, 88, 90, 101, 354, 355, 464, 469, 487, 699];
    private static ReadOnlySpan<int> TorchTypes =>
        [4, 33, 34, 35, 42, 49, 93, 95, 98, 100, 149, 173, 174, 270, 271, 316, 317, 318, 92, 372, 646, 405, 592, 572, 581, 660];
    private static ReadOnlySpan<int> DoorTypes => [10, 11, 19, 387, 386, 388, 389, 436, 435, 438, 427, 439, 437];
    private static ReadOnlySpan<int> IgnoredHouseScoreTypes => [4, 3, 73, 82, 83, 84, 386];

    private readonly WorldTileStore tiles;

    public VanillaHousingValidator1458(WorldTileStore tiles) =>
        this.tiles = tiles ?? throw new ArgumentNullException(nameof(tiles));

    internal static bool IsPotentialRoomAnchorType(int type) =>
        Contains(ChairTypes, type) ||
        Contains(TableTypes, type) ||
        Contains(TorchTypes, type) ||
        Contains(DoorTypes, type);

    private static bool Contains(ReadOnlySpan<int> values, int value)
    {
        foreach (int candidate in values)
        {
            if (candidate == value)
                return true;
        }
        return false;
    }

    public VanillaHousingPlacement Validate(
        int startX,
        int startY,
        NpcTypeId npcType,
        ReadOnlySpan<VanillaHousingOccupant> occupants = default)
    {
        if (!VanillaTownNpcFacts1458.TryGetHousingCategory(npcType, out int housingCategory))
            return new VanillaHousingPlacement(0, 0, VanillaHousingValidationResult.SpecialNpcConditionFailed);

        WorldDimensions dimensions = tiles.Dimensions;
        if (!IsInsideRoomCheckBounds(startX, startY, dimensions))
            return new VanillaHousingPlacement(0, 0, VanillaHousingValidationResult.TooCloseToWorldEdge);
        if (IsBlockingSolid(tiles.Get(startX, startY)))
            return new VanillaHousingPlacement(0, 0, VanillaHousingValidationResult.StartedInSolidTile);

        var visited = new HashSet<int>();
        var stack = new Stack<(int X, int Y)>();
        var roomTiles = new HashSet<int>();
        var presentTypes = new bool[VanillaTileIds.Count];
        stack.Push((startX, startY));

        int roomX1 = startX;
        int roomX2 = startX;
        int roomY1 = startY;
        int roomY2 = startY;
        bool roomHasStinkbug = false;
        bool roomHasEchoStinkbug = false;

        while (stack.Count != 0)
        {
            (int x, int y) = stack.Pop();
            if (!IsInsideRoomCheckBounds(x, y, dimensions))
                return new VanillaHousingPlacement(0, 0, VanillaHousingValidationResult.TooCloseToWorldEdge);
            if (Math.Abs(x - startX) >= MaximumRoomSize || Math.Abs(y - startY) >= MaximumRoomSize)
                return new VanillaHousingPlacement(0, 0, VanillaHousingValidationResult.RoomTooBig);

            int key = checked(x * dimensions.HeightTiles + y);
            if (!visited.Add(key))
                continue;
            if (visited.Count >= MaximumRoomTiles)
                return new VanillaHousingPlacement(0, 0, VanillaHousingValidationResult.RoomTooBig);

            roomTiles.Add(key);
            roomX1 = Math.Min(roomX1, x);
            roomX2 = Math.Max(roomX2, x);
            roomY1 = Math.Min(roomY1, y);
            roomY2 = Math.Max(roomY2, y);
            if (roomX2 - roomX1 >= MaximumRoomSize || roomY2 - roomY1 >= MaximumRoomSize)
                return new VanillaHousingPlacement(0, 0, VanillaHousingValidationResult.RoomTooBig);

            WorldTile tile = tiles.Get(x, y);
            if (IsNActive(in tile))
            {
                if (tile.Type < presentTypes.Length)
                    presentTypes[tile.Type] = true;
                roomHasStinkbug |= tile.Type == 630;
                roomHasEchoStinkbug |= tile.Type == 631;
                if (IsBlockingRoomBoundary(in tile))
                    continue;
            }

            if (!HasHorizontalHousingBoundary(x, y) || !HasVerticalHousingBoundary(x, y))
                return new VanillaHousingPlacement(0, 0, VanillaHousingValidationResult.MissingOrUnsafeWall);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    stack.Push((x + dx, y + dy));
                }
            }
        }

        if (roomTiles.Count < MinimumRoomTiles)
            return new VanillaHousingPlacement(0, 0, VanillaHousingValidationResult.RoomTooSmall);
        if (!ContainsAny(presentTypes, ChairTypes) ||
            !ContainsAny(presentTypes, TableTypes) ||
            !ContainsAny(presentTypes, TorchTypes) ||
            !ContainsAny(presentTypes, DoorTypes))
        {
            return new VanillaHousingPlacement(0, 0, VanillaHousingValidationResult.MissingFurniture);
        }

        if ((roomHasStinkbug || roomHasEchoStinkbug) && housingCategory != VanillaTownNpcFacts1458.PetHousingCategory)
            return new VanillaHousingPlacement(0, 0, VanillaHousingValidationResult.StinkbugBlocked);

        int sharedRoomX = -1;
        foreach (VanillaHousingOccupant occupant in occupants)
        {
            if (!IsRoomTile(roomTiles, dimensions, occupant.HomeTileX, occupant.HomeTileY) ||
                !IsRoomTile(roomTiles, dimensions, occupant.HomeTileX, occupant.HomeTileY - 1))
            {
                continue;
            }

            if (!VanillaTownNpcFacts1458.CanShareRoom(npcType, occupant.Type))
                return new VanillaHousingPlacement(0, 0, VanillaHousingValidationResult.RoomOccupied);

            sharedRoomX = occupant.HomeTileX;
        }

        int testedStartX = Math.Max(5, roomX1 - 46);
        int testedEndX = Math.Min(dimensions.WidthTiles - 6, roomX2 + 46);
        int testedStartY = Math.Max(5, roomY1 - 44);
        int testedEndY = Math.Min(dimensions.HeightTiles - 6, roomY2 + 44);
        if (!PassesSpecialNpcCondition(npcType, roomY2, testedStartX, testedEndX, testedStartY, testedEndY))
            return new VanillaHousingPlacement(0, 0, VanillaHousingValidationResult.SpecialNpcConditionFailed);

        int baseScore = CalculateBaseRoomScore(testedStartX + 1, testedEndX - 1, testedStartY + 2, testedEndY + 1);
        if (baseScore <= -250)
            return new VanillaHousingPlacement(0, 0, VanillaHousingValidationResult.EvilRoom);

        int bestScore = 0;
        int bestX = 0;
        int bestY = 0;
        bool hasStandingSpace = false;
        for (int x = roomX1 + 1; x < roomX2; x++)
        {
            for (int y = roomY1 + 2; y < roomY2 + 2; y++)
            {
                if (!IsValidStandingFloor(x, y, roomTiles, dimensions))
                    continue;

                int score = ScoreStandingSpot(x, y, baseScore, sharedRoomX);
                if (score > 0)
                    hasStandingSpace = true;
                if (score <= bestScore)
                    continue;
                if (!IsRoomTile(roomTiles, dimensions, x, y) ||
                    !IsRoomTile(roomTiles, dimensions, x, y - 1) ||
                    !IsRoomTile(roomTiles, dimensions, x, y - 2) ||
                    !IsRoomTile(roomTiles, dimensions, x, y - 3))
                {
                    continue;
                }

                bestScore = score;
                bestX = x;
                bestY = y;
            }
        }

        if (bestScore <= 0)
        {
            return new VanillaHousingPlacement(
                0,
                0,
                baseScore <= 0 ? VanillaHousingValidationResult.EvilRoom :
                hasStandingSpace ? VanillaHousingValidationResult.EvilRoom : VanillaHousingValidationResult.NoStandingSpace);
        }

        return new VanillaHousingPlacement(bestX, bestY, VanillaHousingValidationResult.Valid);
    }

    private bool PassesSpecialNpcCondition(
        NpcTypeId npcType,
        int roomY2,
        int startX,
        int endX,
        int startY,
        int endY)
    {
        _ = roomY2;
        _ = startX;
        _ = endX;
        _ = startY;
        _ = endY;

        // TerrariaServer has an additional Truffle-only mushroom-biome/unlock gate. The runtime does not yet own
        // NPC.unlockedTruffleSpawn/NoFunctionalSurface or the complete SceneMetrics mushroom threshold contract, so
        // Truffle room moves deliberately fail closed instead of approximating that irreversible assignment rule.
        return npcType != VanillaNpcIds.Truffle;
    }

    private int CalculateBaseRoomScore(int startX, int endX, int startY, int endY)
    {
        int corruption = 0;
        int crimson = 0;
        int hallow = 0;
        int sunflowers = 0;
        for (int x = startX; x <= endX; x++)
        {
            for (int y = startY; y <= endY; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!IsNActive(in tile))
                    continue;

                switch (tile.Type)
                {
                    case 23 or 24 or 25 or 32 or 112 or 163 or 400 or 398:
                        corruption++;
                        break;
                    case 199 or 203 or 200 or 401 or 399 or 234 or 352:
                        crimson++;
                        break;
                    case 109 or 110 or 113 or 117 or 116 or 164 or 403 or 402:
                        hallow++;
                        break;
                    case 27:
                        sunflowers++;
                        break;
                }
            }
        }

        int evilExcess = corruption + crimson - hallow - 5 * sunflowers;
        if (evilExcess < 50)
            evilExcess = 0;
        return 50 - evilExcess;
    }

    private int ScoreStandingSpot(int x, int y, int baseScore, int sharedRoomX)
    {
        int score = baseScore;
        int centerColumnObjects = 0;
        int nearbyChests = 0;
        for (int xx = x - 2; xx < x + 3; xx++)
        {
            for (int yy = y - 4; yy < y; yy++)
            {
                WorldTile tile = tiles.Get(xx, yy);
                if (!IsNActive(in tile) || IsIgnoredInHouseScore(tile.Type) ||
                    (tile.Type == 11 && !IsOpenDoorAnchorFrame(in tile)))
                {
                    continue;
                }

                if (xx == x)
                {
                    centerColumnObjects++;
                }
                else if (tile.Type is 21 or 467)
                {
                    nearbyChests++;
                }
                else if (tile.Type is 10 or 388 || IsOpenDoorAnchorFrame(in tile) || tile.Type == 389)
                {
                    score -= 20;
                }
                else if (!VanillaTileCollisionCatalog.IsSolid(tile.Type))
                {
                    score += 5;
                }
                else
                {
                    score -= 5;
                }
            }
        }

        if (sharedRoomX >= 0 && score >= 1 && Math.Abs(sharedRoomX - x) < 3)
            score = 1;
        if (score > 0 && nearbyChests > 0)
            score = Math.Max(1, score - 30 * nearbyChests);
        if (score > 0 && centerColumnObjects > 0)
            score = Math.Max(1, score - 15 * centerColumnObjects);
        return score;
    }

    private bool IsValidStandingFloor(int x, int y, HashSet<int> roomTiles, WorldDimensions dimensions)
    {
        if (x <= 0 || x >= dimensions.WidthTiles - 1 || y < 3 || y >= dimensions.HeightTiles)
            return false;
        WorldTile floor = tiles.Get(x, y);
        if (!IsNActive(in floor) || floor.Type == 379 || !VanillaTileCollisionCatalog.IsSolid(floor.Type))
            return false;
        if (HasSolidTile(x - 1, x + 1, y - 3, y - 1))
            return false;
        WorldTile left = tiles.Get(x - 1, y);
        WorldTile right = tiles.Get(x + 1, y);
        if (!IsNActive(in left) || !VanillaTileCollisionCatalog.IsSolid(left.Type) ||
            !IsNActive(in right) || !VanillaTileCollisionCatalog.IsSolid(right.Type))
        {
            return false;
        }
        return IsRoomTile(roomTiles, dimensions, x, y);
    }

    private bool HasSolidTile(int startX, int endX, int startY, int endY)
    {
        for (int x = startX; x <= endX; x++)
        {
            for (int y = startY; y <= endY; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (IsNActive(in tile) && VanillaTileCollisionCatalog.IsSolid(tile.Type))
                    return true;
            }
        }
        return false;
    }

    private bool HasHorizontalHousingBoundary(int x, int y)
    {
        for (int offset = -2; offset <= 2; offset++)
        {
            if (IsHousingBoundary(tiles.Get(x + offset, y)))
                return true;
        }
        return false;
    }

    private bool HasVerticalHousingBoundary(int x, int y)
    {
        for (int offset = -2; offset <= 2; offset++)
        {
            if (IsHousingBoundary(tiles.Get(x, y + offset)))
                return true;
        }
        return false;
    }

    private static bool IsHousingBoundary(in WorldTile tile)
    {
        if (VanillaWallDefinitionCatalog.TryGet(tile.WallType, out VanillaWallDefinition wall) && wall.IsHousingWall)
            return true;
        return IsNActive(in tile) &&
            (VanillaTileCollisionCatalog.IsSolid(tile.Type) || tile.Type is 11 or 389 or 386);
    }

    private static bool IsBlockingRoomBoundary(in WorldTile tile) =>
        IsBlockingSolid(in tile) ||
        (IsNActive(in tile) &&
         (tile.Type == 11 && tile.FrameX is 0 or 54 or 72 or 126 ||
          tile.Type == 389 ||
          tile.Type == 386 && ((tile.FrameX < 36 && tile.FrameY == 18) || (tile.FrameX >= 36 && tile.FrameY == 0))));

    private static bool IsBlockingSolid(in WorldTile tile) =>
        IsNActive(in tile) && VanillaTileCollisionCatalog.IsSolid(tile.Type);

    private static bool IsNActive(in WorldTile tile) => tile.IsActive && !tile.IsActuated;

    private static bool IsInsideRoomCheckBounds(int x, int y, WorldDimensions dimensions) =>
        x >= WorldEdgeMargin && y >= WorldEdgeMargin &&
        x < dimensions.WidthTiles - WorldEdgeMargin && y < dimensions.HeightTiles - WorldEdgeMargin;

    private static bool ContainsAny(bool[] presentTypes, ReadOnlySpan<int> requiredTypes)
    {
        foreach (int type in requiredTypes)
        {
            if ((uint)type < (uint)presentTypes.Length && presentTypes[type])
                return true;
        }
        return false;
    }

    private static bool IsIgnoredInHouseScore(int type) => type is 4 or 3 or 73 or 82 or 83 or 84 or 386;

    private static bool IsOpenDoorAnchorFrame(in WorldTile tile)
    {
        if (!IsNActive(in tile) || tile.Type != 11)
            return false;
        int frame = tile.FrameX % 72;
        return frame < 18 || frame >= 54;
    }

    private static bool IsRoomTile(HashSet<int> roomTiles, WorldDimensions dimensions, int x, int y)
    {
        if ((uint)x >= (uint)dimensions.WidthTiles || (uint)y >= (uint)dimensions.HeightTiles)
            return false;
        return roomTiles.Contains(checked(x * dimensions.HeightTiles + y));
    }
}
