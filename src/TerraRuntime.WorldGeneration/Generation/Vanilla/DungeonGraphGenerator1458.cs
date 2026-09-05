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
    int RandomSeed);

internal sealed class DungeonGraph1458
{
    public DungeonGraph1458(
        IReadOnlyList<DungeonComponent1458> components,
        DungeonPoint1458 anchor,
        ushort brickTileType,
        ushort wallType)
    {
        Components = components ?? throw new ArgumentNullException(nameof(components));
        Anchor = anchor;
        BrickTileType = brickTileType;
        WallType = wallType;
    }

    public IReadOnlyList<DungeonComponent1458> Components { get; }
    public DungeonPoint1458 Anchor { get; }
    public ushort BrickTileType { get; }
    public ushort WallType { get; }
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
    private static readonly DungeonPoint1458[] CardinalDirections =
    [
        new(-1, 0),
        new(1, 0),
        new(0, -1),
        new(0, 1),
    ];

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

        bool useSkewedEntranceHalls = ConsumeDecorationSetup(sharedRandom);
        (int dungeonMinimumX, int dungeonMaximumX) = ResolveHorizontalBounds(workspace.WidthTiles, dungeonSide);
        int entranceX = Math.Clamp(dungeonLocation, dungeonMinimumX, dungeonMaximumX);
        int entranceSurface = FindSurface(workspace.TileStore, entranceX, (int)worldSurface + 300);
        if (setup.PrecalculatesEntrance)
        {
            int candidateX = dungeonLocation - 100 + sharedRandom.Next(200);
            candidateX = Math.Clamp(candidateX, dungeonMinimumX, dungeonMaximumX);
            int candidateSurface = FindSurface(workspace.TileStore, candidateX, (int)worldSurface + 300);
            if (candidateSurface - 40 - setup.RoughEntranceHeight > 0)
            {
                entranceX = candidateX;
                entranceSurface = candidateSurface;
                dungeonLocation = Math.Clamp(
                    candidateX + 25 - sharedRandom.Next(50),
                    dungeonMinimumX,
                    dungeonMaximumX);
            }
        }

        int startY = ResolveStartY(workspace.TileStore, dungeonLocation, worldSurface, rockLayer, sharedRandom);
        _ = sharedRandom.Next(25, 30);
        _ = sharedRandom.Next(20, 25);
        _ = sharedRandom.Next(35, 50);
        _ = sharedRandom.Next(10, 15);
        int baseSteps = workspace.WidthTiles / DungeonGenerationCatalog1458.LayoutStepDivisor;
        int steps = baseSteps + sharedRandom.Next(0, baseSteps / 3);

        if (setup.PrecalculatesEntrance)
        {
            dungeonLocation = Math.Clamp(
                entranceX - 10 + sharedRandom.Next(20),
                dungeonMinimumX,
                dungeonMaximumX);
            startY = entranceSurface + 30;
        }

        var renderer = new Renderer(workspace.TileStore, setup.Palette.BrickTileType, setup.Palette.BrickWallType,
            worldSurface, underworldTop, dungeonMinimumX, dungeonMaximumX, cancellationToken);
        DungeonPoint1458 cursor = new(dungeonLocation, startY);
        DungeonPoint1458 lastHall = default;
        var components = new List<DungeonComponent1458>(steps + 24);

        _ = sharedRandom.Next(); // initial hall-settings seed, retained by the source layout provider
        _ = sharedRandom.Next(); // initial room-settings seed, retained by the source layout provider
        int startingRoomSeed = sharedRandom.Next();
        components.Add(renderer.RenderRoom(cursor, startingRoomSeed, startingRoom: true));

        int roomDelay = DungeonGenerationCatalog1458.InitialRoomDelay;
        for (int step = 0; step < steps; step++)
        {
            if ((step & 15) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (roomDelay > 0)
                roomDelay--;

            int roomRoll = sharedRandom.Next(DungeonGenerationCatalog1458.RoomChance);
            if (roomDelay == 0 && roomRoll == 0)
            {
                roomDelay = DungeonGenerationCatalog1458.InitialRoomDelay;
                if (sharedRandom.Next(DungeonGenerationCatalog1458.BranchChance) == 0)
                {
                    DungeonPoint1458 saved = cursor;
                    (DungeonComponent1458 hall, cursor, lastHall) = renderer.RenderHall(
                        cursor, lastHall, sharedRandom.Next());
                    components.Add(hall);
                    if (sharedRandom.Next(DungeonGenerationCatalog1458.BranchChance) == 0)
                    {
                        (hall, cursor, lastHall) = renderer.RenderHall(cursor, lastHall, sharedRandom.Next());
                        components.Add(hall);
                    }
                    components.Add(renderer.RenderRoom(cursor, sharedRandom.Next(), startingRoom: false));
                    cursor = saved;
                }
                else
                {
                    components.Add(renderer.RenderRoom(cursor, sharedRandom.Next(), startingRoom: false));
                }
            }
            else
            {
                (DungeonComponent1458 hall, cursor, lastHall) = renderer.RenderHall(
                    cursor, lastHall, sharedRandom.Next());
                components.Add(hall);
            }
        }

        components.Add(renderer.RenderRoom(cursor, sharedRandom.Next(), startingRoom: false));
        DungeonComponent1458 topRoom = components
            .Where(static component => component.Kind is DungeonComponentKind1458.StartingRoom or DungeonComponentKind1458.Room)
            .MinBy(static component => component.Bounds.Top);
        DungeonPoint1458 entranceCursor = new((topRoom.Bounds.Left + topRoom.Bounds.Right) / 2, topRoom.Bounds.Top + 4);
        DungeonPoint1458 entranceTarget = new(entranceX, Math.Max(10, entranceSurface - 2));
        int entranceRoomDelay = DungeonGenerationCatalog1458.InitialRoomDelay;
        int entranceComponentStart = components.Count;
        for (int attempt = 0; attempt < 100 && entranceCursor.Y > entranceTarget.Y; attempt++)
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
                int segmentSteps = sharedRandom.Next(10, 30);
                components.Add(renderer.RenderPrecalculatedEntranceSegment(
                    entranceCursor,
                    entranceTarget,
                    segmentSteps,
                    sharedRandom.Next(),
                    out entranceCursor));
            }
            else
            {
                components.Add(renderer.RenderLegacyEntranceSegment(
                    entranceCursor,
                    entranceTarget,
                    sharedRandom.Next(),
                    useSkewedEntranceHalls,
                    out entranceCursor));
            }
        }
        if (entranceCursor.Y > entranceTarget.Y || components.Count == entranceComponentStart)
        {
            components.Add(renderer.RenderPrecalculatedEntranceSegment(
                entranceCursor,
                entranceTarget,
                Math.Max(Math.Abs(entranceTarget.X - entranceCursor.X), Math.Abs(entranceTarget.Y - entranceCursor.Y)),
                setup.EntranceRandomSeed,
                out entranceCursor));
        }
        DungeonPoint1458 entranceEnd = entranceCursor;
        components.Add(renderer.RenderEntrance(entranceEnd, setup.EntranceKind, setup.EntranceRandomSeed));

        return new DungeonGraph1458(components, entranceEnd, setup.Palette.BrickTileType, setup.Palette.BrickWallType);
    }

    private static bool ConsumeDecorationSetup(IWorldGenerationVanillaRandom random)
    {
        ConsumeUniqueStyles(random, DungeonGenerationCatalog1458.ShelfStyleMinimum,
            DungeonGenerationCatalog1458.ShelfStyleMaximumExclusive);
        ConsumeUniqueStyles(random, 0, DungeonGenerationCatalog1458.LanternStyleCount);
        return random.Next(4) == 0;
    }

    private static void ConsumeUniqueStyles(IWorldGenerationVanillaRandom random, int minimum, int maximum)
    {
        Span<int> styles = stackalloc int[DungeonGenerationCatalog1458.DecorationStyleCount];
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

    private static int ResolveStartY(
        WorldTileStore store,
        int x,
        double worldSurface,
        double rockLayer,
        IWorldGenerationVanillaRandom random)
    {
        int midpoint = (int)((worldSurface + rockLayer) / 2d);
        int y = Math.Clamp(midpoint + random.Next(-200, 200), 70, store.Dimensions.HeightTiles - 200);
        int lowerLimit = Math.Min(store.Dimensions.HeightTiles - 100, midpoint + 200);
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

    private static int FindSurface(WorldTileStore store, int x, int maximum)
    {
        int height = store.Dimensions.HeightTiles;
        for (int y = 10; y < Math.Min(height, maximum); y++)
        {
            if (IsSolid(store, x, y))
                return y;
        }
        return Math.Clamp(maximum - 1, 20, height - 200);
    }

    private static bool IsSolid(WorldTileStore store, int x, int y) =>
        (uint)x < (uint)store.Dimensions.WidthTiles &&
        (uint)y < (uint)store.Dimensions.HeightTiles &&
        store.Get(x, y).IsActive;

    private sealed class Renderer
    {
        private readonly WorldTileStore store;
        private readonly ushort brick;
        private readonly ushort wall;
        private readonly int surfaceFloor;
        private readonly int lowerLimit;
        private readonly int minimumX;
        private readonly int maximumX;
        private readonly CancellationToken cancellationToken;

        public Renderer(
            WorldTileStore store,
            ushort brick,
            ushort wall,
            double worldSurface,
            int underworldTop,
            int minimumX,
            int maximumX,
            CancellationToken cancellationToken)
        {
            this.store = store;
            this.brick = brick;
            this.wall = wall;
            surfaceFloor = (int)worldSurface + 80;
            lowerLimit = underworldTop - DungeonGenerationCatalog1458.UnderworldClearance;
            this.minimumX = minimumX;
            this.maximumX = maximumX;
            this.cancellationToken = cancellationToken;
        }

        public DungeonComponent1458 RenderRoom(DungeonPoint1458 origin, int seed, bool startingRoom)
        {
            var random = new DungeonUnifiedRandom1458(seed);
            int strength = DungeonGenerationCatalog1458.RoomStrengthBase +
                random.Next(DungeonGenerationCatalog1458.RoomStrengthVariation);
            double velocityX = random.Next(-10, 11) * 0.1d;
            double velocityY = random.Next(-10, 11) * 0.1d;
            if (velocityX == 0d && velocityY == 0d)
            {
                if (random.Next(2) == 0)
                    velocityX = random.Next(2) == 0 ? -1d : 1d;
                else
                    velocityY = random.Next(2) == 0 ? -1d : 1d;
            }
            double x = origin.X;
            double y = origin.Y - strength / 2d;
            DungeonPoint1458 start = new((int)x, (int)y);
            int steps = DungeonGenerationCatalog1458.RoomStepBase +
                random.Next(DungeonGenerationCatalog1458.RoomStepVariation);
            int left = start.X;
            int top = start.Y;
            int right = start.X;
            int bottom = start.Y;
            for (int step = 0; step < steps; step++)
            {
                int outer = (int)(strength * DungeonGenerationCatalog1458.RoomOuterRadiusRatio) +
                    DungeonGenerationCatalog1458.RoomOuterPadding;
                int inner = (int)(strength * DungeonGenerationCatalog1458.RoomInnerRadiusRatio);
                PaintEllipse((int)x, (int)y, outer, outer, inner, inner);
                left = Math.Min(left, (int)x - outer);
                top = Math.Min(top, (int)y - outer);
                right = Math.Max(right, (int)x + outer);
                bottom = Math.Max(bottom, (int)y + outer);
                x = Math.Clamp(x + velocityX, minimumX, maximumX);
                y += velocityY;
                velocityX = Math.Clamp(velocityX + random.Next(-10, 11) * 0.05d, -1d, 1d);
                velocityY = Math.Clamp(velocityY + random.Next(-10, 11) * 0.05d, -1d, 1d);
            }
            DungeonPoint1458 end = new((int)x, (int)y);
            return new(
                startingRoom ? DungeonComponentKind1458.StartingRoom : DungeonComponentKind1458.Room,
                start,
                end,
                new(left, top, right, bottom),
                seed);
        }

        public (DungeonComponent1458 Component, DungeonPoint1458 Cursor, DungeonPoint1458 Direction)
            RenderHall(DungeonPoint1458 origin, DungeonPoint1458 lastDirection, int seed)
        {
            var random = new DungeonUnifiedRandom1458(seed);
            int strength = DungeonGenerationCatalog1458.HallStrengthBase +
                random.Next(DungeonGenerationCatalog1458.HallStrengthVariation);
            int steps = DungeonGenerationCatalog1458.HallStepBase +
                random.Next(DungeonGenerationCatalog1458.HallStepVariation);
            if (random.Next(DungeonGenerationCatalog1458.LargeHallChance) == 0)
            {
                strength *= 2;
                steps /= 2;
            }

            DungeonPoint1458 direction = ChooseDirection(origin, lastDirection, random);
            double slant = random.Next(-10, 11) * 0.015d;
            double velocityX = direction.X == 0 ? slant : direction.X;
            double velocityY = direction.Y == 0 ? slant : direction.Y;
            double x = origin.X;
            double y = origin.Y;
            for (int step = 0; step < steps; step++)
            {
                int outerX = strength + random.Next(2, 6);
                int outerY = strength + random.Next(2, 6);
                PaintEllipse((int)x, (int)y, outerX, outerY, strength, strength);
                x = Math.Clamp(x + velocityX, minimumX, maximumX);
                y += velocityY;
            }
            DungeonPoint1458 end = new((int)x, (int)y);
            DungeonBounds1458 bounds = DungeonBounds1458.FromPoints(origin, end, strength + 6);
            return (new(DungeonComponentKind1458.Hall, origin, end, bounds, seed), end, direction);
        }

        public DungeonComponent1458 RenderLegacyEntranceSegment(
            DungeonPoint1458 start,
            DungeonPoint1458 target,
            int seed,
            bool useSkewedEntranceHalls,
            out DungeonPoint1458 end)
        {
            var random = new DungeonUnifiedRandom1458(seed);
            int strength = random.Next(5, 9);
            int steps = random.Next(10, 30);
            int direction = start.X <= target.X ? 1 : -1;
            if (start.X > store.Dimensions.WidthTiles - 400)
                direction = -1;
            else if (start.X < 400)
                direction = 1;
            double velocityX = direction;
            if (random.Next(3) != 0)
                velocityX *= 1d + random.Next(0, 200) * 0.01d;
            else if (random.Next(3) == 0)
                velocityX *= random.Next(50, 76) * 0.01d;
            else if (random.Next(6) == 0)
                steps *= 2;
            if (!useSkewedEntranceHalls)
                velocityX = Math.Clamp(velocityX, -0.5d, 0.5d);
            double x = start.X;
            double y = start.Y;
            for (int step = 0; step <= steps; step++)
            {
                PaintEllipse((int)x, (int)y, strength + 4 + random.Next(6), strength + 4 + random.Next(6), strength, strength);
                x = Math.Clamp(x + velocityX, minimumX, maximumX);
                y -= 1d;
            }
            end = new((int)x, (int)y);
            return new(DungeonComponentKind1458.EntranceHall, start, end,
                DungeonBounds1458.FromPoints(start, end, strength + 10), seed);
        }

        public DungeonComponent1458 RenderPrecalculatedEntranceSegment(
            DungeonPoint1458 start,
            DungeonPoint1458 target,
            int requestedSteps,
            int seed,
            out DungeonPoint1458 end)
        {
            int dx = target.X - start.X;
            int dy = target.Y - start.Y;
            double distance = Math.Sqrt((double)dx * dx + (double)dy * dy);
            int steps = Math.Clamp(requestedSteps, 1, Math.Max(1, (int)Math.Ceiling(distance)));
            double velocityX = distance == 0d ? 0d : dx / distance;
            double velocityY = distance == 0d ? 0d : dy / distance;
            var random = new DungeonUnifiedRandom1458(seed);
            int strength = random.Next(5, 9);
            double x = start.X;
            double y = start.Y;
            for (int step = 0; step < steps; step++)
            {
                PaintEllipse((int)x, (int)y, strength + 5, strength + 5, strength, strength);
                x = Math.Clamp(x + velocityX, minimumX, maximumX);
                y += velocityY;
            }
            end = new((int)x, (int)y);
            return new(DungeonComponentKind1458.EntranceHall, start, end,
                DungeonBounds1458.FromPoints(start, end, strength + 6), seed);
        }

        public DungeonComponent1458 RenderEntrance(
            DungeonPoint1458 anchor,
            DungeonEntranceKind1458 kind,
            int seed)
        {
            (int halfWidth, int height) = kind switch
            {
                DungeonEntranceKind1458.Dome => (18, 30),
                DungeonEntranceKind1458.Tower => (13, 48),
                _ => (10, 24),
            };
            int top = Math.Max(5, anchor.Y - height);
            for (int x = anchor.X - halfWidth; x <= anchor.X + halfWidth; x++)
            {
                for (int y = top; y <= anchor.Y + 7; y++)
                {
                    bool shell = x <= anchor.X - halfWidth + 2 || x >= anchor.X + halfWidth - 2 || y <= top + 2;
                    WriteTile(x, y, shell);
                }
            }
            return new(DungeonComponentKind1458.Entrance, new(anchor.X, top), anchor,
                new(anchor.X - halfWidth, top, anchor.X + halfWidth, anchor.Y + 7), seed);
        }

        private DungeonPoint1458 ChooseDirection(
            DungeonPoint1458 origin,
            DungeonPoint1458 lastDirection,
            DungeonUnifiedRandom1458 random)
        {
            Span<DungeonPoint1458> candidates = stackalloc DungeonPoint1458[4];
            int count = 0;
            foreach (DungeonPoint1458 direction in CardinalDirections)
            {
                if (direction.X == -lastDirection.X && direction.Y == -lastDirection.Y)
                    continue;
                if (direction.X < 0 && origin.X < minimumX + 100 ||
                    direction.X > 0 && origin.X > maximumX - 100 ||
                    direction.Y < 0 && origin.Y < surfaceFloor ||
                    direction.Y > 0 && origin.Y > lowerLimit)
                    continue;
                candidates[count++] = direction;
            }
            if (count == 0)
                return new(lastDirection.X == 0 ? 1 : 0, lastDirection.Y == 0 ? 1 : 0);
            return candidates[random.Next(count)];
        }

        private void PaintEllipse(int centerX, int centerY, int outerX, int outerY, int innerX, int innerY)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int minX = Math.Max(minimumX, centerX - outerX);
            int maxX = Math.Min(maximumX, centerX + outerX);
            int minY = Math.Max(1, centerY - outerY);
            int maxY = Math.Min(store.Dimensions.HeightTiles - 2, centerY + outerY);
            for (int y = minY; y <= maxY; y++)
            {
                double outerDy = (y - centerY) / (double)outerY;
                double innerDy = (y - centerY) / (double)Math.Max(1, innerY);
                for (int x = minX; x <= maxX; x++)
                {
                    double outerDx = (x - centerX) / (double)outerX;
                    if (outerDx * outerDx + outerDy * outerDy > 1d)
                        continue;
                    double innerDx = (x - centerX) / (double)Math.Max(1, innerX);
                    bool shell = innerDx * innerDx + innerDy * innerDy > 1d;
                    WriteTile(x, y, shell);
                }
            }
        }

        private void WriteTile(int x, int y, bool active)
        {
            ref WorldTile tile = ref store.Tiles[store.GetUncheckedIndex(x, y)];
            tile.Wall = wall;
            tile.LiquidAmount = 0;
            tile.LiquidKind = WorldLiquidKind.Water;
            tile.FrameX = -1;
            tile.FrameY = -1;
            tile.Shape = 0;
            if (active)
            {
                tile.Type = brick;
                tile.Flags |= WorldTileFlags.Active;
            }
            else
            {
                tile.Type = 0;
                tile.Flags &= ~WorldTileFlags.Active;
            }
        }
    }

    /// <summary>
    /// Component-local Terraria 1.4.5.8 UnifiedRandom stream. It is deliberately not shared with the pass RNG: the
    /// pinned room, hall, and entrance implementations construct a new stream from every graph component seed.
    /// </summary>
    private sealed class DungeonUnifiedRandom1458
    {
        private readonly int[] seedArray = new int[56];
        private uint inext;

        public DungeonUnifiedRandom1458(int seed)
        {
            int subtraction = seed == int.MinValue ? int.MaxValue : Math.Abs(seed);
            int mj = 161803398 - subtraction;
            seedArray[55] = mj;
            int mk = 1;
            for (int index = 1; index < 55; index++)
            {
                int destination = 21 * index % 55;
                seedArray[destination] = mk;
                mk = mj - mk;
                if (mk < 0)
                    mk += int.MaxValue;
                mj = seedArray[destination];
            }
            for (int pass = 1; pass < 5; pass++)
            {
                for (int index = 1; index < 56; index++)
                {
                    seedArray[index] -= seedArray[1 + (index + 30) % 55];
                    if (seedArray[index] < 0)
                        seedArray[index] += int.MaxValue;
                }
            }
        }

        public int Next(int maximum) => (int)(Sample() * maximum);
        public int Next(int minimum, int maximum) => (int)(Sample() * (maximum - (long)minimum)) + minimum;
        private double Sample() => InternalSample() * 4.656612875245797E-10;
        private int InternalSample()
        {
            uint next = inext + 1;
            if (next > 55)
                next = 1;
            uint second = next + 21;
            if (second > 55)
                second -= 55;
            int value = seedArray[next] - seedArray[second];
            if (value == int.MaxValue)
                value--;
            value = seedArray[next] = value + ((value >> 31) & int.MaxValue);
            inext = next;
            return value;
        }
    }
}
