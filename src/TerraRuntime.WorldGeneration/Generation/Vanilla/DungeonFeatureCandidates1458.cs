using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal readonly record struct DungeonDoorCandidate1458(DungeonPoint1458 Position, int Direction)
{
    public int WidthFluff { get; init; } = 10;
    public bool InAHallway { get; init; }
    public bool AlwaysClearArea { get; init; } = true;
}

/// <summary>Ordinary DungeonUtils discovery, retaining source order and duplicate candidates.</summary>
internal sealed class DungeonFeatureCandidates1458
{
    public List<DungeonDoorCandidate1458> Doors { get; } = [];
    public List<DungeonPlatformCandidate1458> Platforms { get; } = [];

    public static DungeonFeatureCandidates1458 Collect(WorldTileStore tiles, DungeonGraph1458 graph)
    {
        var result = new DungeonFeatureCandidates1458();
        foreach (var platform in graph.EntranceHallPlatforms)
            result.Platforms.Add(new(platform.Position) { InAHallway = platform.InAHallway, PotsChance = platform.PlacePotsChance });
        if (graph.EntrancePlatform is { } entrance) result.Platforms.Add(new(entrance));
        foreach (var platform in graph.EntranceBuildingPlatforms)
            result.Platforms.Add(new(platform.Position)
            {
                HeightFluff = platform.HeightFluff, ForcePlacement = platform.ForcePlacement,
                PotsChance = platform.PotsChance, BooksChance = platform.BooksChance,
                PotionsChance = platform.PotionsChance, NoWaterbolt = platform.NoWaterbolt
            });
        // Crawler visits all processed rooms before all processed halls. Ordinary style has empty
        // platform/door item arrays: GetPlatformStyle returns -1 WITHOUT consuming shared randomness.
        foreach (var room in graph.Components)
            if (room.Kind is DungeonComponentKind1458.StartingRoom or DungeonComponentKind1458.Room)
                result.AddRoom(tiles, room.InnerBounds ?? throw new InvalidOperationException("Missing dungeon room interior."));
        foreach (var hall in graph.Components)
        {
            if (hall.Kind != DungeonComponentKind1458.Hall) continue;
            var direction = hall.HallDirection ?? throw new InvalidOperationException("Missing dungeon hall direction.");
            result.AddHallEnd(tiles, hall.Start, direction.Y);
            result.AddHallEnd(tiles, hall.End, direction.Y);
            // AddExtraPlatformsIfNeeded is DualDungeon-only, not part of ordinary generation.
        }
        return result;
    }

    internal void AddRoom(WorldTileStore tiles, DungeonBounds1458 inner)
    {
        // These are the actual source InnerBounds values (UpdateBounds uses painted max-1),
        // not an inset of the shell or a converted Rectangle.Right/Bottom.
        if (!InWorld(tiles, new(inner.Left + (inner.Right - inner.Left) / 2,
                inner.Top + (inner.Bottom - inner.Top) / 2))) return;
        int left = Math.Max(5, inner.Left), right = Math.Min(tiles.Dimensions.WidthTiles - 5, inner.Right);
        int top = Math.Max(5, inner.Top), bottom = Math.Min(tiles.Dimensions.HeightTiles - 5, inner.Bottom);
        bool foundTop = false, foundBottom = false, foundLeft = false, foundRight = false;
        for (int x = left; x <= right && !(foundTop && foundBottom); x++)
        {
            if (!foundTop && !tiles.Get(x, top - 1).IsActive)
            { Platforms.Add(new(new(x, top - 1)) { HeightFluff = 3 }); foundTop = true; }
            if (!foundBottom && !tiles.Get(x, bottom + 1).IsActive)
            { Platforms.Add(new(new(x, bottom + 1)) { HeightFluff = 3 }); foundBottom = true; }
        }
        for (int y = top; y <= bottom && !(foundLeft && foundRight); y++)
        {
            if (!foundLeft && !tiles.Get(left - 1, y).IsActive)
            { Doors.Add(new(new(left - 1, y), -1) { WidthFluff = 3 }); foundLeft = true; }
            if (!foundRight && !tiles.Get(right + 1, y).IsActive)
            { Doors.Add(new(new(right + 1, y), 1) { WidthFluff = 3 }); foundRight = true; }
        }
    }

    internal void AddHallEnd(WorldTileStore tiles, DungeonPoint1458 point, double directionY)
    {
        if (!InWorld(tiles, point)) return;
        if (Math.Abs(directionY) <= .1) Doors.Add(new(point, 0) { InAHallway = true });
        else Platforms.Add(new(point) { InAHallway = true });
    }

    private static bool InWorld(WorldTileStore tiles, DungeonPoint1458 p) =>
        p.X >= 5 && p.Y >= 5 && p.X < tiles.Dimensions.WidthTiles - 5 && p.Y < tiles.Dimensions.HeightTiles - 5;
}
