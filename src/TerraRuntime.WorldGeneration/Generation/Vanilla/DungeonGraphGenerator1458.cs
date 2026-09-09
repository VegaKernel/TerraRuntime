using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal enum DungeonEntranceKind1458 : byte
{
    Legacy,
    Dome,
    Tower,
}

internal readonly record struct DungeonSetupProfile1458(
    DungeonPalette1458 Palette,
    DungeonEntranceKind1458 EntranceKind,
    int EntranceRandomSeed)
{
    public bool PrecalculatesEntrance => EntranceKind is not DungeonEntranceKind1458.Legacy;
    public int RoughEntranceHeight => EntranceKind switch
    {
        DungeonEntranceKind1458.Dome => 55,
        DungeonEntranceKind1458.Tower => 120,
        _ => 40,
    };
}

/// <summary>
/// Named ordinary-world constants from TerrariaServer 1.4.5.8 <c>DungeonCrawler</c>,
/// <c>LegacyDungeonLayoutProvider</c>, <c>LegacyDungeonRoom</c>, and <c>LegacyDungeonHall</c>.
/// </summary>
internal static class DungeonGenerationCatalog1458
{
    public const int ShelfStyleMinimum = 9;
    public const int ShelfStyleMaximumExclusive = 13;
    public const int LanternStyleCount = 7;
    public const int DecorationStyleCount = 3;
    public const int LayoutStepDivisor = 60;
    public const int InitialRoomDelay = 5;
    public const int RoomChance = 3;
    public const int BranchChance = 2;
    public const int HallStrengthBase = 4;
    public const int HallStrengthVariation = 2;
    public const int HallStepBase = 35;
    public const int HallStepVariation = 45;
    public const int LargeHallChance = 5;
    public const double HallCrackedBrickChance = 0.166d;
    public const double HallInteriorToExteriorRatio = 0.5d;
    public const int RoomStrengthBase = 15;
    public const int RoomStrengthVariation = 15;
    public const int RoomStepBase = 10;
    public const int RoomStepVariation = 10;
    public const double RoomOuterRadiusRatio = 0.800000011920929d;
    public const double RoomInnerRadiusRatio = 0.5d;
    public const int RoomOuterPadding = 5;
    public const int WorldBorder = 50;
    public const int UnderworldClearance = 100;
    public const double PotentialBoundsMiddlePercent = 0.10000000149011612d;
    public const double PotentialBoundsEdgePercent = 0.05000000074505806d;
    public const int BeachDistance = 380;
    public const int EntranceSearchCountdown = 3_000;
    public const int EntranceSearchRadius = 100;
    // NPC.SetDefaults(37); generation passes NPC.NewNPC a bottom-center, .wld stores position.
    public const int OldManWidth = 18;
    public const int OldManHeight = 40;
}

internal readonly record struct DungeonDecorationProfile1458(
    int ShelfStyle0,
    int ShelfStyle1,
    int ShelfStyle2,
    int LanternStyle0,
    int LanternStyle1,
    int LanternStyle2,
    bool UseSkewedEntranceHalls)
{
    public int GetShelfStyle(int wallVariantIndex) => wallVariantIndex switch
    {
        1 => ShelfStyle1,
        2 => ShelfStyle2,
        _ => ShelfStyle0,
    };

    public int GetLanternStyle(int wallVariantIndex) => wallVariantIndex switch
    {
        1 => LanternStyle1,
        2 => LanternStyle2,
        _ => LanternStyle0,
    };

    public static DungeonDecorationProfile1458 Default { get; } =
        new(9, 10, 11, 0, 1, 2, false);
}

internal enum DungeonComponentKind1458 : byte
{
    StartingRoom,
    Room,
    Hall,
    EntranceHall,
    Entrance,
}

internal readonly record struct DungeonPoint1458(int X, int Y);

internal readonly record struct DungeonEntranceResult1458(
    DungeonComponent1458 Component, DungeonPoint1458 OldManSpawn, DungeonPoint1458? Platform)
{
    public IReadOnlyList<DungeonBuildingPlatform1458> BuildingPlatforms { get; init; } = [];
    public int? GenerationTopOverride { get; init; }
}

internal readonly record struct DungeonBounds1458(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left + 1;
    public int Height => Bottom - Top + 1;
    public static DungeonBounds1458 FromPoints(DungeonPoint1458 a, DungeonPoint1458 b, int padding) =>
        new(
            Math.Min(a.X, b.X) - padding,
            Math.Min(a.Y, b.Y) - padding,
            Math.Max(a.X, b.X) + padding,
            Math.Max(a.Y, b.Y) + padding);
}

internal readonly record struct DungeonComponent1458(
    DungeonComponentKind1458 Kind,
    DungeonPoint1458 Start,
    DungeonPoint1458 End,
    DungeonBounds1458 Bounds,
    int RandomSeed)
{
    public DungeonBounds1458? InnerBounds { get; init; }
    // Numeric DungeonData.dungeonBounds updates, distinct from the component's protected/painted footprint.
    // Null means this component did not update global sampling bounds (ordinary Tower, for example).
    public DungeonBounds1458? GenerationBounds { get; init; }
    public int? RoomStrength { get; init; }
    // LegacyHall retains an axis direction independently of its sloped/zigzag cursor displacement.
    // Candidate discovery uses StartDirection.Y/EndDirection.Y, not the end-to-end slope.
    public DungeonPoint1458? HallDirection { get; init; }
}

internal sealed class DungeonGraph1458
{
    public DungeonGraph1458(
        IReadOnlyList<DungeonComponent1458> components,
        DungeonPoint1458 anchor,
        ushort brickTileType,
        ushort wallType,
        DungeonDecorationProfile1458? decoration = null,
        DungeonPoint1458? entrancePlatform = null,
        IReadOnlyList<DungeonHallPlatform1458>? entranceHallPlatforms = null,
        IReadOnlyList<DungeonBuildingPlatform1458>? entranceBuildingPlatforms = null)
    {
        Components = components ?? throw new ArgumentNullException(nameof(components));
        Anchor = anchor;
        BrickTileType = brickTileType;
        WallType = wallType;
        Decoration = decoration ?? DungeonDecorationProfile1458.Default;
        EntrancePlatform = entrancePlatform;
        EntranceHallPlatforms = entranceHallPlatforms ?? [];
        EntranceBuildingPlatforms = entranceBuildingPlatforms ?? [];
    }

    public IReadOnlyList<DungeonComponent1458> Components { get; }
    public DungeonBounds1458? FeatureBounds { get; init; }
    public DungeonPoint1458 Anchor { get; }
    public ushort BrickTileType { get; }
    public ushort WallType { get; }
    public DungeonDecorationProfile1458 Decoration { get; }
    public DungeonPoint1458? EntrancePlatform { get; }
    public IReadOnlyList<DungeonHallPlatform1458> EntranceHallPlatforms { get; }
    public IReadOnlyList<DungeonBuildingPlatform1458> EntranceBuildingPlatforms { get; }
    public int RoomCount => Components.Count(static component =>
        component.Kind is DungeonComponentKind1458.StartingRoom or DungeonComponentKind1458.Room);
    public int HallCount => Components.Count(static component => component.Kind == DungeonComponentKind1458.Hall);
    public int HorizontalHallCount => Components.Count(static component =>
        component.Kind == DungeonComponentKind1458.Hall &&
        Math.Abs(component.End.X - component.Start.X) > Math.Abs(component.End.Y - component.Start.Y));
    public int VerticalHallCount => Components.Count(static component =>
        component.Kind == DungeonComponentKind1458.Hall &&
        Math.Abs(component.End.Y - component.Start.Y) >= Math.Abs(component.End.X - component.Start.X));
    public DungeonBounds1458 Bounds => new(
        Components.Min(static component => component.Bounds.Left),
        Components.Min(static component => component.Bounds.Top),
        Components.Max(static component => component.Bounds.Right),
        Components.Max(static component => component.Bounds.Bottom));
}

/// <summary>
/// Clean-room structural port of the ordinary 1.4.5.8 legacy dungeon graph. The caller-owned random stream selects
/// topology and component seeds; each renderer reconstructs the source component's isolated seeded stream.
/// </summary>
internal static class DungeonGraphGenerator1458
{
    public static DungeonGraph1458 Generate(
        Workspace workspace,
        IWorldGenerationVanillaRandom sharedRandom,
        double worldSurface,
        double rockLayer,
        int underworldTop,
        int dungeonLocation,
        int dungeonSide,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(sharedRandom);
        DungeonSetupProfile1458 setup = workspace.VanillaDungeonSetupProfile ??
            throw new InvalidOperationException("Dungeon generation requires the Dunes-owned dungeon setup profile.");

        DungeonDecorationProfile1458 decoration = CreateDecorationProfile(sharedRandom);
        bool useSkewedEntranceHalls = decoration.UseSkewedEntranceHalls;
        (int dungeonMinimumX, int dungeonMaximumX) = ResolveHorizontalBounds(workspace.WidthTiles, dungeonSide);
        int entranceX = Math.Clamp(dungeonLocation, dungeonMinimumX, dungeonMaximumX);
        int entranceSurface = Math.Clamp((int)worldSurface - 5, 20, workspace.HeightTiles - 200);
        if (setup.PrecalculatesEntrance)
        {
            if (TryResolvePrecalculatedEntrance(
                    workspace.TileStore,
                    sharedRandom,
                    dungeonLocation,
                    setup.RoughEntranceHeight,
                    cancellationToken,
                    out int candidateX,
                    out int candidateSurface))
            {
                entranceX = candidateX;
                entranceSurface = candidateSurface;
                dungeonLocation = candidateX + 25 - sharedRandom.Next(50);
            }
            else
            {
                // DungeonCrawler decrements its 3000 countdown before each candidate, then falls back to fresh
                // Legacy entrance settings when the 2999 source candidate evaluations fail.
                setup = new DungeonSetupProfile1458(setup.Palette, DungeonEntranceKind1458.Legacy, sharedRandom.Next());
                workspace.SetVanillaDungeonSetupProfile(setup);
            }
        }

        int startY = ResolveStartY(workspace.TileStore, dungeonLocation, worldSurface, rockLayer, sharedRandom);
        int entranceStrengthX = sharedRandom.Next(25, 30);
        int entranceStrengthY = sharedRandom.Next(20, 25);
        int entranceStrengthX2 = sharedRandom.Next(35, 50);
        int entranceStrengthY2 = sharedRandom.Next(10, 15);
        int baseSteps = workspace.WidthTiles / DungeonGenerationCatalog1458.LayoutStepDivisor;
        int steps = baseSteps + sharedRandom.Next(0, baseSteps / 3);

        var renderer = new Renderer(
            workspace.TileStore,
            setup.Palette.BrickTileType,
            setup.Palette.CrackedBrickTileType,
            setup.Palette.BrickWallType,
            worldSurface,
            rockLayer,
            underworldTop,
            dungeonMinimumX,
            dungeonMaximumX,
            entranceStrengthX,
            entranceStrengthY,
            entranceStrengthX2,
            entranceStrengthY2,
            cancellationToken);
        DungeonPoint1458 cursor = new(dungeonLocation, startY);
        DungeonPoint1458 lastHall = default;
        var components = GenerateLayout(renderer, sharedRandom, ref cursor, ref lastHall, steps,
            setup.PrecalculatesEntrance ? new DungeonPoint1458(entranceX, entranceSurface) : null, cancellationToken);
        DungeonPoint1458 entranceCursor = ResolveEntranceOrigin(components);
        DungeonPoint1458 entranceTarget = new(entranceX, entranceSurface);
        DungeonEntranceRoute1458? entranceRoute = setup.PrecalculatesEntrance
            ? new(new(entranceCursor.X, entranceCursor.Y), new(entranceTarget.X, entranceTarget.Y)) : null;
        var entranceHallPlatforms = new List<DungeonHallPlatform1458>();
        int generatingDungeonTopX = entranceCursor.X;
        int entranceRoomDelay = DungeonGenerationCatalog1458.InitialRoomDelay;
        bool reachedEntrance = false;
        for (int attempt = 0; attempt < 99 && !reachedEntrance; attempt++)
        {
            if (entranceRoomDelay > 0)
                entranceRoomDelay--;
            if (entranceRoomDelay == 0 &&
                sharedRandom.Next(5) == 0 &&
                entranceCursor.Y > worldSurface + 100d)
            {
                entranceRoomDelay = 10;
                DungeonPoint1458 saved = entranceCursor;
                (DungeonComponent1458 branchHall, entranceCursor, lastHall) = renderer.RenderHall(
                    entranceCursor, lastHall, sharedRandom.Next());
                components.Add(branchHall);
                components.Add(renderer.RenderRoom(entranceCursor, sharedRandom.Next(), startingRoom: false));
                entranceCursor = saved;
            }

            if (setup.PrecalculatesEntrance)
            {
                components.Add(renderer.RenderPrecalculatedEntranceSegment(
                    entranceRoute!.TakeNext(sharedRandom),
                    sharedRandom,
                    entranceHallPlatforms,
                    out entranceCursor));
                reachedEntrance = entranceRoute.Complete;
            }
            else
            {
                components.Add(renderer.RenderLegacyEntranceSegment(
                    entranceCursor,
                    generatingDungeonTopX,
                    sharedRandom.Next(),
                    useSkewedEntranceHalls,
                    sharedRandom,
                    out entranceCursor,
                    out reachedEntrance));
            }
        }
        // MakeDungeon_GetEntranceSettings allocates a seed before its pre-generated-settings overload
        // replaces that seed. The discarded draw still advances the shared stream before door framing.
        _ = sharedRandom.Next();
        DungeonEntranceResult1458 entrance = renderer.RenderEntrance(
            entranceCursor, setup.EntranceKind, setup.EntranceRandomSeed, sharedRandom, dungeonSide < 0);
        components.Add(entrance.Component);

        return new DungeonGraph1458(
            components,
            entrance.OldManSpawn,
            setup.Palette.BrickTileType,
            setup.Palette.BrickWallType,
            decoration,
            entrance.Platform,
            entranceHallPlatforms,
            entrance.BuildingPlatforms)
        {
            FeatureBounds = ResolveFeatureBounds(workspace.TileStore.Dimensions,
                new(dungeonLocation, startY), components, entrance.GenerationTopOverride)
        };
    }

    internal static DungeonBounds1458 ResolveFeatureBounds(WorldDimensions dimensions, DungeonPoint1458 initial,
        IReadOnlyList<DungeonComponent1458> components, int? entranceTopOverride)
    {
        int left = X(initial.X), right = X(initial.X + 1), top = Y(initial.Y), bottom = Y(initial.Y + 1);
        foreach (var component in components)
        {
            if (component.Kind == DungeonComponentKind1458.Entrance && entranceTopOverride is { } replacement) top = Y(replacement);
            if (component.GenerationBounds is not { } area) continue;
            left = Math.Min(left, X(area.Left)); right = Math.Max(right, X(area.Right));
            top = Math.Min(top, Y(area.Top)); bottom = Math.Max(bottom, Y(area.Bottom));
        }
        return new(left, top, right, bottom);
        int X(int value) => Math.Clamp(value, 10, dimensions.WidthTiles - 10);
        int Y(int value) => Math.Clamp(value, 10, dimensions.HeightTiles - 10);
    }

    internal static List<DungeonComponent1458> GenerateLayout(Renderer renderer, IWorldGenerationVanillaRandom random,
        ref DungeonPoint1458 cursor, ref DungeonPoint1458 lastHall, int steps, DungeonPoint1458? entrance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(steps);
        // LegacyDungeonLayoutProvider runs this AFTER the crawler's strength/step draws, before settings seeds.
        if (entrance is { } location) cursor = new(location.X - 10 + random.Next(20), location.Y + 30);
        var components = new List<DungeonComponent1458>(steps + 24);
        _ = random.Next(); // Initial hall-settings seed.
        _ = random.Next(); // Initial room-settings seed.
        components.Add(renderer.RenderRoom(cursor, random.Next(), startingRoom: true));
        int roomDelay = DungeonGenerationCatalog1458.InitialRoomDelay;
        for (int step = 0; step < steps; step++)
        {
            if ((step & 15) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (roomDelay > 0) roomDelay--;
            int roomRoll = random.Next(DungeonGenerationCatalog1458.RoomChance);
            if (roomDelay == 0 && roomRoll == 0)
            {
                roomDelay = DungeonGenerationCatalog1458.InitialRoomDelay;
                if (random.Next(DungeonGenerationCatalog1458.BranchChance) == 0)
                {
                    DungeonPoint1458 saved = cursor;
                    (DungeonComponent1458 hall, cursor, lastHall) = renderer.RenderHall(cursor, lastHall, random.Next());
                    components.Add(hall);
                    if (random.Next(DungeonGenerationCatalog1458.BranchChance) == 0)
                    {
                        (hall, cursor, lastHall) = renderer.RenderHall(cursor, lastHall, random.Next());
                        components.Add(hall);
                    }
                    components.Add(renderer.RenderRoom(cursor, random.Next(), startingRoom: false));
                    cursor = saved;
                }
                else components.Add(renderer.RenderRoom(cursor, random.Next(), startingRoom: false));
            }
            else
            {
                (DungeonComponent1458 hall, cursor, lastHall) = renderer.RenderHall(cursor, lastHall, random.Next());
                components.Add(hall);
            }
        }
        components.Add(renderer.RenderRoom(cursor, random.Next(), startingRoom: false));
        return components;
    }

    internal static DungeonPoint1458 ResolveEntranceOrigin(IReadOnlyList<DungeonComponent1458> components)
    {
        DungeonBounds1458? highest = null;
        foreach (var component in components)
        {
            if (component.Kind is not (DungeonComponentKind1458.StartingRoom or DungeonComponentKind1458.Room)) continue;
            var inner = component.InnerBounds ?? throw new InvalidOperationException("Dungeon room lacks its source inner bounds.");
            // DungeonCrawler retains the first room on a tie, not the highest exterior shell.
            if (highest is null || inner.Top < highest.Value.Top) highest = inner;
        }
        var bounds = highest ?? throw new InvalidOperationException("Dungeon layout has no generated room.");
        return new((bounds.Left + bounds.Right) / 2, bounds.Top);
    }

    private static DungeonDecorationProfile1458 CreateDecorationProfile(IWorldGenerationVanillaRandom random)
    {
        Span<int> shelfStyles = stackalloc int[DungeonGenerationCatalog1458.DecorationStyleCount];
        FillUniqueStyles(
            random,
            shelfStyles,
            DungeonGenerationCatalog1458.ShelfStyleMinimum,
            DungeonGenerationCatalog1458.ShelfStyleMaximumExclusive);

        Span<int> lanternStyles = stackalloc int[DungeonGenerationCatalog1458.DecorationStyleCount];
        FillUniqueStyles(random, lanternStyles, 0, DungeonGenerationCatalog1458.LanternStyleCount);

        return new DungeonDecorationProfile1458(
            shelfStyles[0],
            shelfStyles[1],
            shelfStyles[2],
            lanternStyles[0],
            lanternStyles[1],
            lanternStyles[2],
            random.Next(4) == 0);
    }

    private static void FillUniqueStyles(
        IWorldGenerationVanillaRandom random,
        Span<int> styles,
        int minimum,
        int maximum)
    {
        for (int index = 0; index < styles.Length; index++)
        {
            int style;
            do
            {
                style = random.Next(minimum, maximum);
            }
            while (styles[..index].Contains(style));
            styles[index] = style;
        }
    }

    internal static (int MinimumX, int MaximumX) ResolveHorizontalBounds(int worldWidth, int dungeonSide)
    {
        if (dungeonSide is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(dungeonSide), dungeonSide, "Dungeon side must be -1 or 1.");

        double middleHalf = DungeonGenerationCatalog1458.PotentialBoundsMiddlePercent / 2d;
        int edge = (int)(worldWidth * DungeonGenerationCatalog1458.PotentialBoundsEdgePercent);
        int minimum = dungeonSide < 0
            ? edge
            : (int)(worldWidth * (0.5d + middleHalf));
        int maximumExclusive = dungeonSide < 0
            ? (int)(worldWidth * (0.5d - middleHalf))
            : worldWidth - edge;
        return (minimum, maximumExclusive - 1);
    }

    internal static DungeonBounds1458 ResolvePotentialBounds(WorldDimensions dimensions, double worldSurface, int dungeonSide)
    {
        var (left, right) = ResolveHorizontalBounds(dimensions.WidthTiles, dungeonSide);
        int height = dimensions.HeightTiles;
        // CreatePotentialDungeonBounds performs division then multiplication before truncation.
        int top = (int)(height * ((worldSurface + 10d) / height));
        int bottom = (int)(height * ((height - 210d) / height));
        return new(Math.Clamp(left, 10, dimensions.WidthTiles - 10), Math.Clamp(top, 10, height - 10),
            Math.Clamp(right + 1, 10, dimensions.WidthTiles - 10), Math.Clamp(bottom, 10, height - 10));
    }

    internal static int ResolveStartY(
        WorldTileStore store,
        int x,
        double worldSurface,
        double rockLayer,
        IWorldGenerationVanillaRandom random)
    {
        int midpoint = (int)((worldSurface + rockLayer) / 2d);
        int y = midpoint + random.Next(-200, 200);
        int lowerLimit = midpoint + 200;
        bool solidAhead = false;
        for (int offset = 0; offset < 10; offset++)
            solidAhead |= IsSolid(store, x, y + offset);
        if (!solidAhead)
        {
            while (y < lowerLimit && !IsSolid(store, x, y + 10))
                y++;
        }
        else
        {
            int emptyDistance = 0;
            while (emptyDistance < 60 && IsSolid(store, x, y - emptyDistance))
                emptyDistance++;
            if (emptyDistance < 60)
                y += 60 - emptyDistance;
        }
        return y;
    }

    private static bool TryResolvePrecalculatedEntrance(
        WorldTileStore store,
        IWorldGenerationVanillaRandom random,
        int dungeonLocation,
        int roughEntranceHeight,
        CancellationToken cancellationToken,
        out int entranceX,
        out int entranceY)
    {
        for (int remaining = DungeonGenerationCatalog1458.EntranceSearchCountdown - 1; remaining > 0; remaining--)
        {
            if ((remaining & 127) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            int candidateX = dungeonLocation - DungeonGenerationCatalog1458.EntranceSearchRadius +
                random.Next(DungeonGenerationCatalog1458.EntranceSearchRadius * 2);
            if (candidateX <= DungeonGenerationCatalog1458.BeachDistance ||
                candidateX >= store.Dimensions.WidthTiles - DungeonGenerationCatalog1458.BeachDistance)
            {
                continue;
            }

            int candidateY = FindFirstOccupied(store, candidateX);
            if (!AreCloudTilesNearby(store, candidateX, candidateY, 15) &&
                !AreCloudTilesNearby(store, candidateX, Math.Max(50, candidateY - 50), 50) &&
                candidateY - 40 - roughEntranceHeight > 0)
            {
                entranceX = candidateX;
                entranceY = candidateY;
                return true;
            }
        }

        entranceX = 0;
        entranceY = 0;
        return false;
    }

    private static int FindFirstOccupied(WorldTileStore store, int x)
    {
        for (int y = 10; y < store.Dimensions.HeightTiles; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (tile.IsActive || tile.LiquidAmount > 0 || tile.Wall > 0)
                return y;
        }
        return store.Dimensions.HeightTiles - 1;
    }

    private static bool AreCloudTilesNearby(WorldTileStore store, int centerX, int centerY, int distance)
    {
        int left = Math.Max(0, centerX - distance);
        int right = Math.Min(store.Dimensions.WidthTiles - 1, centerX + distance);
        int top = Math.Max(0, centerY - distance);
        int bottom = Math.Min(store.Dimensions.HeightTiles - 1, centerY + distance);
        for (int x = left; x <= right; x++)
        {
            for (int y = top; y <= bottom; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.IsActive && tile.Type is 189 or 196 or 460 or 717 or 718 or 719)
                    return true;
            }
        }
        return false;
    }

    private static bool IsSolid(WorldTileStore store, int x, int y) =>
        (uint)x < (uint)store.Dimensions.WidthTiles &&
        (uint)y < (uint)store.Dimensions.HeightTiles &&
        HellFortGenerator1458.Solid(store.Get(x, y), noDoors: false);

    internal sealed class Renderer
    {
        private readonly WorldTileStore store;
        private readonly ushort brick;
        private readonly ushort crackedBrick;
        private readonly ushort wall;
        private readonly int minimumX;
        private readonly int maximumX;
        private readonly double worldSurface;
        private readonly double rockLayer;
        private readonly int underworldTop;
        private readonly int entranceStrengthX;
        private readonly int entranceStrengthY;
        private readonly int entranceStrengthX2;
        private readonly int entranceStrengthY2;
        private readonly CancellationToken cancellationToken;

        public Renderer(
            WorldTileStore store,
            ushort brick,
            ushort crackedBrick,
            ushort wall,
            double worldSurface,
            double rockLayer,
            int underworldTop,
            int minimumX,
            int maximumX,
            int entranceStrengthX,
            int entranceStrengthY,
            int entranceStrengthX2,
            int entranceStrengthY2,
            CancellationToken cancellationToken)
        {
            this.store = store;
            this.brick = brick;
            this.crackedBrick = crackedBrick;
            this.wall = wall;
            this.worldSurface = worldSurface;
            this.rockLayer = rockLayer;
            this.underworldTop = underworldTop;
            this.minimumX = minimumX;
            this.maximumX = maximumX;
            this.entranceStrengthX = entranceStrengthX;
            this.entranceStrengthY = entranceStrengthY;
            this.entranceStrengthX2 = entranceStrengthX2;
            this.entranceStrengthY2 = entranceStrengthY2;
            this.cancellationToken = cancellationToken;
        }

        public DungeonComponent1458 RenderRoom(DungeonPoint1458 origin, int seed, bool startingRoom) =>
            DungeonLegacyRoom1458.Generate(store, brick, wall, origin, seed, startingRoom, cancellationToken);

        public (DungeonComponent1458 Component, DungeonPoint1458 Cursor, DungeonPoint1458 Direction)
            RenderHall(DungeonPoint1458 origin, DungeonPoint1458 lastDirection, int seed) =>
            new DungeonLegacyHall1458(store, brick, crackedBrick, wall, rockLayer, underworldTop, cancellationToken)
                .Generate(origin, lastDirection, seed);

        public DungeonComponent1458 RenderLegacyEntranceSegment(
            DungeonPoint1458 start,
            int generatingDungeonTopX,
            int seed,
            bool useSkewedEntranceHalls,
            IWorldGenerationVanillaRandom sharedRandom,
            out DungeonPoint1458 end,
            out bool reachedSurface)
        {
            var result = new DungeonLegacyEntranceHall1458(store, brick, crackedBrick, wall, worldSurface,
                entranceStrengthX, entranceStrengthX2, entranceStrengthY2, sharedRandom, cancellationToken)
                .Generate(start, generatingDungeonTopX, seed, useSkewedEntranceHalls);
            end = result.Cursor;
            reachedSurface = result.ReachedSurface;
            return result.Component;
        }

        public DungeonComponent1458 RenderPrecalculatedEntranceSegment(
            DungeonEntranceSegment1458 segment,
            IWorldGenerationVanillaRandom sharedRandom,
            List<DungeonHallPlatform1458> platforms,
            out DungeonPoint1458 end)
        {
            var result = new DungeonLegacyEntranceHall1458(store, brick, crackedBrick, wall, worldSurface,
                entranceStrengthX, entranceStrengthX2, entranceStrengthY2, sharedRandom, cancellationToken)
                .GeneratePrecalculated(segment, platforms);
            end = result.Cursor;
            return result.Component;
        }

        public DungeonEntranceResult1458 RenderEntrance(
            DungeonPoint1458 anchor,
            DungeonEntranceKind1458 kind,
            int seed,
            IWorldGenerationVanillaRandom sharedRandom,
            bool leftDungeon)
        {
            if (kind == DungeonEntranceKind1458.Legacy)
                return new DungeonLegacyEntrance1458(store, brick, crackedBrick, wall, worldSurface,
                    entranceStrengthX, entranceStrengthY, entranceStrengthX2, entranceStrengthY2,
                    sharedRandom, cancellationToken).Generate(anchor, seed);

            var builder = new DungeonSurfaceBuildings1458(store, brick, crackedBrick, wall, worldSurface, sharedRandom, cancellationToken);
            var result = kind switch
            {
                DungeonEntranceKind1458.Dome => builder.GenerateDome(anchor, seed, leftDungeon),
                DungeonEntranceKind1458.Tower => builder.GenerateTower(anchor, seed, leftDungeon),
                _ => throw new InvalidOperationException("Unsupported dungeon entrance kind."),
            };
            return result.Entrance with { BuildingPlatforms = result.Platforms };
        }
    }

}
