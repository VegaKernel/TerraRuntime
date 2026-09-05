using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>
/// Source-shaped ordinary-dungeon decoration stage pinned to TerrariaServer 1.4.5.8 DungeonCrawler.MakeDungeon.
/// Layout owns rooms/halls/entrance; this stage owns the post-layout feature order and keeps consuming the same
/// shared UnifiedRandom stream. It deliberately does not reuse Optimized/DungeonV2 heuristics.
/// </summary>
internal static class DungeonFeaturePipeline1458
{
    private static readonly ushort DungeonSpike = Tile(VanillaTileIds.Spikes);
    private static readonly ushort ClosedDoor = Tile(VanillaTileIds.ClosedDoor);
    private static readonly ushort Platform = Tile(VanillaTileIds.Platforms);
    private static readonly ushort Containers = Tile(VanillaTileIds.Containers);
    private static readonly ushort Containers2 = Tile(VanillaTileIds.Containers2);
    private static readonly ushort Books = Tile(VanillaTileIds.Books);
    private static readonly ushort WaterCandle = Tile(VanillaTileIds.WaterCandle);
    private static readonly ushort PressurePlate = Tile(VanillaTileIds.PressurePlates);
    private static readonly ushort DartTrap = Tile(VanillaTileIds.Traps);
    private static readonly ushort Banner = Tile(VanillaTileIds.Banners);

    private static readonly int[] DungeonLootCycle =
    [
        VanillaItemIds.Muramasa.Value,
        VanillaItemIds.CobaltShield.Value,
        VanillaItemIds.AquaScepter.Value,
        VanillaItemIds.BlueMoon.Value,
        VanillaItemIds.MagicMissile.Value,
        VanillaItemIds.Valor.Value,
        VanillaItemIds.GoldenKey.Value,
        VanillaItemIds.Handgun.Value,
    ];

    internal readonly record struct Result(
        int Doors,
        int Platforms,
        int Spikes,
        int BiomeChests,
        int BasicChests,
        int Bookshelves,
        int Lights,
        int Traps,
        int Furniture,
        int Paintings,
        int Banners);

    public static Result Apply(
        Workspace workspace,
        DungeonGraph1458 graph,
        IWorldGenerationVanillaRandom random,
        double worldSurface,
        double rockLayer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(random);

        DungeonSetupProfile1458 setup = workspace.VanillaDungeonSetupProfile ??
            throw new InvalidOperationException("Dungeon feature generation requires the Dunes-owned dungeon setup profile.");
        VanillaWorldGenerationBootstrapState1458 bootstrap = workspace.VanillaBootstrapState ??
            throw new InvalidOperationException("Dungeon feature generation requires Reset bootstrap state.");

        var grid = new Grid(workspace.TileStore);
        DungeonBounds1458 bounds = ClampBounds(graph.Bounds, grid.Width, grid.Height, padding: 25);
        DungeonBounds1458[] rooms = graph.Components
            .Where(static component => component.Kind is DungeonComponentKind1458.StartingRoom or DungeonComponentKind1458.Room)
            .Select(static component => Inset(component.Bounds, 6))
            .Where(static room => room.Width >= 5 && room.Height >= 5)
            .ToArray();

        int[] wallVariants = ResolveWallVariants(setup.Palette.BrickWallType);

        // TerrariaServer 1.4.5.8 DungeonCrawler.MakeDungeon order:
        // CalculatePlatformsAndDoors -> spikes -> doors -> wall variants -> platforms -> biome chests ->
        // bookshelves -> basic chests -> lights -> traps -> furniture -> paintings -> banners.
        DoorPlatformCandidates candidates = CalculateDoorAndPlatformCandidates(grid, graph, rooms, setup.Palette.BrickWallType);
        int spikes = PlaceSpikes(
            grid,
            bounds,
            setup.Palette.BrickWallType,
            setup.Palette.CrackedBrickTileType,
            random,
            worldSurface,
            cancellationToken);
        int doors = PlaceDoors(grid, candidates.Doors, random, setup.Palette.Color, cancellationToken);
        ApplyWallVariants(grid, bounds, setup.Palette.BrickWallType, wallVariants, random, worldSurface, cancellationToken);
        int platforms = PlacePlatforms(grid, candidates.Platforms, setup.Palette.Color, cancellationToken);
        int biomeChests = PlaceBiomeChests(workspace, grid, graph, bounds, bootstrap.EffectiveCrimson, random, worldSurface, cancellationToken);
        int bookshelves = PlaceBookshelves(
            grid,
            bounds,
            wallVariants,
            graph.BrickTileType,
            setup.Palette.CrackedBrickTileType,
            graph.Decoration,
            random,
            worldSurface,
            rockLayer,
            cancellationToken);
        int basicChests = PlaceBasicChests(workspace, grid, rooms, random, worldSurface, cancellationToken);
        int lights = PlaceLights(
            grid,
            bounds,
            wallVariants,
            graph.BrickTileType,
            setup.Palette.CrackedBrickTileType,
            setup.Palette.Color,
            graph.Decoration,
            random,
            cancellationToken);
        int traps = PlaceTraps(grid, bounds, wallVariants, random, cancellationToken);
        int furniture = PlaceGroundFurniture(grid, rooms, wallVariants, random, cancellationToken);
        int paintings = PlacePaintings(grid, rooms, wallVariants, random, cancellationToken);
        int banners = PlaceBanners(grid, bounds, wallVariants, random, cancellationToken);

        return new Result(
            doors,
            platforms,
            spikes,
            biomeChests,
            basicChests,
            bookshelves,
            lights,
            traps,
            furniture,
            paintings,
            banners);
    }

    private static DoorPlatformCandidates CalculateDoorAndPlatformCandidates(
        Grid grid,
        DungeonGraph1458 graph,
        IReadOnlyList<DungeonBounds1458> rooms,
        ushort dungeonWall)
    {
        var doors = new List<DungeonPoint1458>();
        var platforms = new List<DungeonPoint1458>();
        var doorKeys = new HashSet<int>();
        var platformKeys = new HashSet<int>();

        foreach (DungeonBounds1458 room in rooms)
        {
            int left = Math.Clamp(room.Left, 3, grid.Width - 4);
            int right = Math.Clamp(room.Right, 3, grid.Width - 4);
            int top = Math.Clamp(room.Top, 3, grid.Height - 4);
            int bottom = Math.Clamp(room.Bottom, 3, grid.Height - 4);

            // Source CalculatePlatformsAndDoorsOnEdgesOfRoom records the first opening on every room edge.
            for (int x = left; x <= right; x++)
            {
                if (IsDungeonAir(grid, x, top - 1, dungeonWall))
                {
                    AddUnique(platforms, platformKeys, x, top - 1, grid.Width);
                    break;
                }
            }
            for (int x = left; x <= right; x++)
            {
                if (IsDungeonAir(grid, x, bottom + 1, dungeonWall))
                {
                    AddUnique(platforms, platformKeys, x, bottom + 1, grid.Width);
                    break;
                }
            }
            for (int y = top; y <= bottom; y++)
            {
                if (IsDungeonAir(grid, left - 1, y, dungeonWall))
                {
                    AddUnique(doors, doorKeys, left - 1, y, grid.Width);
                    break;
                }
            }
            for (int y = top; y <= bottom; y++)
            {
                if (IsDungeonAir(grid, right + 1, y, dungeonWall))
                {
                    AddUnique(doors, doorKeys, right + 1, y, grid.Width);
                    break;
                }
            }
        }

        foreach (DungeonComponent1458 hall in graph.Components.Where(static component => component.Kind == DungeonComponentKind1458.Hall))
        {
            int dx = hall.End.X - hall.Start.X;
            int dy = hall.End.Y - hall.Start.Y;
            if (Math.Abs(dy) > Math.Abs(dx))
            {
                AddUnique(platforms, platformKeys, hall.Start.X, hall.Start.Y, grid.Width);
                AddUnique(platforms, platformKeys, hall.End.X, hall.End.Y, grid.Width);
            }
            else
            {
                AddUnique(doors, doorKeys, hall.Start.X, hall.Start.Y, grid.Width);
                AddUnique(doors, doorKeys, hall.End.X, hall.End.Y, grid.Width);
            }
        }

        return new DoorPlatformCandidates(doors, platforms);
    }

    private static int PlaceDoors(
        Grid grid,
        IReadOnlyList<DungeonPoint1458> candidates,
        IWorldGenerationVanillaRandom random,
        int dungeonColor,
        CancellationToken cancellationToken)
    {
        int placed = 0;
        foreach (DungeonPoint1458 candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int centerY = Math.Clamp(candidate.Y, 3, grid.Height - 4);
            int x = Math.Clamp(candidate.X, 3, grid.Width - 4);
            if (!TryFindDoorCenter(grid, x, centerY, out int y))
                continue;

            // DungeonGlobalDoors uses style 13 two thirds of the time; the color-derived item style is one third.
            int style = 13;
            if (random.Next(3) == 0)
            {
                style = dungeonColor switch
                {
                    0 => 16, // item 1411, Blue Dungeon Door
                    1 => 17, // item 1412, Green Dungeon Door
                    _ => 18, // item 1413, Pink Dungeon Door
                };
            }

            if (!PlaceDoor(grid, x, y, style, random))
                continue;
            placed++;
        }
        return placed;
    }

    private static int PlacePlatforms(
        Grid grid,
        IReadOnlyList<DungeonPoint1458> candidates,
        int dungeonColor,
        CancellationToken cancellationToken)
    {
        int style = dungeonColor switch
        {
            0 => 6, // item 1384, Blue Dungeon Platform
            1 => 8, // item 1386, Green Dungeon Platform
            _ => 7, // item 1385, Pink Dungeon Platform
        };
        int placed = 0;
        foreach (DungeonPoint1458 candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = Math.Clamp(candidate.X, 4, grid.Width - 5);
            int y = Math.Clamp(candidate.Y, 4, grid.Height - 5);
            if (!TryFindDungeonAir(grid, x, y, 6, out int px, out int py))
                continue;

            int left = px;
            while (left > 3 && !grid.At(left - 1, py).IsActive && left > px - 6)
                left--;
            int right = px;
            while (right < grid.Width - 4 && !grid.At(right + 1, py).IsActive && right < px + 6)
                right++;
            if (right - left < 2)
                continue;

            for (int xx = left; xx <= right; xx++)
                SetObjectTile(ref grid.At(xx, py), Platform, 0, checked((short)(style * 18)));
            placed++;
        }
        return placed;
    }

    private static int PlaceSpikes(
        Grid grid,
        DungeonBounds1458 bounds,
        ushort baseWall,
        ushort crackedBrickType,
        IWorldGenerationVanillaRandom random,
        double worldSurface,
        CancellationToken cancellationToken)
    {
        double widthScale = grid.Width / 4200d;
        int targetPerOrientation = Math.Max(1, (int)(42d * widthScale));
        int minY = Math.Max(bounds.Top, (int)worldSurface + 25);
        int horizontal = PlaceSpikePass(
            grid,
            bounds,
            baseWall,
            crackedBrickType,
            random,
            minY,
            targetPerOrientation,
            scanVertically: true,
            cancellationToken);
        int vertical = PlaceSpikePass(
            grid,
            bounds,
            baseWall,
            crackedBrickType,
            random,
            minY,
            targetPerOrientation,
            scanVertically: false,
            cancellationToken);
        return horizontal + vertical;
    }

    private static int PlaceSpikePass(
        Grid grid,
        DungeonBounds1458 bounds,
        ushort baseWall,
        ushort crackedBrickType,
        IWorldGenerationVanillaRandom random,
        int minY,
        int target,
        bool scanVertically,
        CancellationToken cancellationToken)
    {
        int completed = 0;
        int failures = 0;
        while (completed < target)
        {
            if (((completed + failures) & 63) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (failures++ > 1000)
            {
                failures = 0;
                completed++;
                continue;
            }

            int x = random.Next(bounds.Left, Math.Max(bounds.Left + 1, bounds.Right));
            int y = random.Next(minY, Math.Max(minY + 1, bounds.Bottom));
            if (!grid.Contains(x, y) || grid.At(x, y).IsActive || grid.At(x, y).Wall != baseWall)
                continue;

            int scanDirection = random.Next(2) == 0 ? -1 : 1;
            if (scanVertically)
            {
                int supportY = y;
                while (supportY > bounds.Top + 3 && supportY < bounds.Bottom - 3 && !grid.At(x, supportY).IsActive)
                    supportY += scanDirection;
                if (!grid.Contains(x, supportY) || !CanSupportDungeonSpike(grid.At(x, supportY), crackedBrickType))
                    continue;
                int inwardY = supportY - scanDirection;
                if (!grid.Contains(x, inwardY) || grid.At(x, inwardY).IsActive)
                    continue;
                if (!CanSupportDungeonSpike(grid.At(x - 1, supportY), crackedBrickType) ||
                    !CanSupportDungeonSpike(grid.At(x + 1, supportY), crackedBrickType))
                {
                    continue;
                }

                int local = PlaceHorizontalSpikeRun(
                    grid,
                    x,
                    supportY,
                    -scanDirection,
                    crackedBrickType,
                    random);
                if (local == 0)
                    continue;
            }
            else
            {
                int supportX = x;
                while (supportX > bounds.Left + 3 && supportX < bounds.Right - 3 && !grid.At(supportX, y).IsActive)
                    supportX += scanDirection;
                if (!grid.Contains(supportX, y) || !CanSupportDungeonSpike(grid.At(supportX, y), crackedBrickType))
                    continue;
                int inwardX = supportX - scanDirection;
                if (!grid.Contains(inwardX, y) || grid.At(inwardX, y).IsActive)
                    continue;
                if (!CanSupportDungeonSpike(grid.At(supportX, y - 1), crackedBrickType) ||
                    !CanSupportDungeonSpike(grid.At(supportX, y + 1), crackedBrickType))
                {
                    continue;
                }

                int local = PlaceVerticalSpikeRun(
                    grid,
                    supportX,
                    y,
                    -scanDirection,
                    crackedBrickType,
                    random);
                if (local == 0)
                    continue;
            }

            failures = 0;
            completed++;
        }
        return completed;
    }

    private static int PlaceHorizontalSpikeRun(
        Grid grid,
        int centerX,
        int supportY,
        int inwardDirectionY,
        ushort crackedBrickType,
        IWorldGenerationVanillaRandom random)
    {
        int placed = 0;
        for (int sideIndex = 0; sideIndex < 2; sideIndex++)
        {
            int side = sideIndex == 0 ? -1 : 1;
            int length = random.Next(5, 13);
            int start = sideIndex == 0 ? 0 : 1;
            for (int step = start; step < length; step++)
            {
                int x = centerX + side * step;
                int inwardY = supportY + inwardDirectionY;
                if (!grid.Contains(x, supportY) || !grid.Contains(x, inwardY) ||
                    !CanSupportDungeonSpike(grid.At(x, supportY), crackedBrickType) || grid.At(x, inwardY).IsActive)
                {
                    break;
                }

                SetSpikeTile(ref grid.At(x, supportY));
                SetSpikeTile(ref grid.At(x, inwardY));
                int secondY = inwardY + inwardDirectionY;
                if (grid.Contains(x, secondY) && !grid.At(x, secondY).IsActive &&
                    !grid.At(Math.Max(0, x - 1), inwardY).IsActive &&
                    !grid.At(Math.Min(grid.Width - 1, x + 1), inwardY).IsActive)
                {
                    SetSpikeTile(ref grid.At(x, secondY));
                }
                placed++;
            }
        }
        return placed;
    }

    private static int PlaceVerticalSpikeRun(
        Grid grid,
        int supportX,
        int centerY,
        int inwardDirectionX,
        ushort crackedBrickType,
        IWorldGenerationVanillaRandom random)
    {
        int placed = 0;
        for (int sideIndex = 0; sideIndex < 2; sideIndex++)
        {
            int side = sideIndex == 0 ? -1 : 1;
            int length = random.Next(5, 13);
            int start = sideIndex == 0 ? 0 : 1;
            for (int step = start; step < length; step++)
            {
                int y = centerY + side * step;
                int inwardX = supportX + inwardDirectionX;
                if (!grid.Contains(supportX, y) || !grid.Contains(inwardX, y) ||
                    !CanSupportDungeonSpike(grid.At(supportX, y), crackedBrickType) || grid.At(inwardX, y).IsActive)
                {
                    break;
                }

                SetSpikeTile(ref grid.At(supportX, y));
                SetSpikeTile(ref grid.At(inwardX, y));
                int secondX = inwardX + inwardDirectionX;
                if (grid.Contains(secondX, y) && !grid.At(secondX, y).IsActive &&
                    !grid.At(inwardX, Math.Max(0, y - 1)).IsActive &&
                    !grid.At(inwardX, Math.Min(grid.Height - 1, y + 1)).IsActive)
                {
                    SetSpikeTile(ref grid.At(secondX, y));
                }
                placed++;
            }
        }
        return placed;
    }

    private static bool CanSupportDungeonSpike(WorldTile tile)
        => CanSupportDungeonSpike(tile, ushort.MaxValue);

    private static bool CanSupportDungeonSpike(WorldTile tile, ushort crackedBrickType)
    {
        if (!tile.IsActive || tile.Type == crackedBrickType || VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
            return false;
        return !VanillaProjectileTileCutFacts.IsCuttable(tile.TileType);
    }

    private static void SetSpikeTile(ref WorldTile tile)
    {
        tile.Type = DungeonSpike;
        tile.Flags |= WorldTileFlags.Active;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    private static void ApplyWallVariants(
        Grid grid,
        DungeonBounds1458 bounds,
        ushort baseWall,
        IReadOnlyList<int> wallVariants,
        IWorldGenerationVanillaRandom random,
        double worldSurface,
        CancellationToken cancellationToken)
    {
        // Source: five rounds, each of the three wall variants, random radius 40..239.
        for (int round = 0; round < 5; round++)
        {
            foreach (int variant in wallVariants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int radius = random.Next(40, 240);
                int centerX = random.Next(bounds.Left, bounds.Right + 1);
                int centerY = random.Next(bounds.Top, bounds.Bottom + 1);
                int effective = Math.Max(1, (int)(radius * 0.4f));
                int left = Math.Max(bounds.Left, centerX - effective);
                int right = Math.Min(bounds.Right, centerX + effective);
                int top = Math.Max(Math.Max(bounds.Top, (int)worldSurface + 1), centerY - effective);
                int bottom = Math.Min(bounds.Bottom, centerY + effective);
                int radiusSquared = effective * effective;
                for (int y = top; y <= bottom; y++)
                {
                    for (int x = left; x <= right; x++)
                    {
                        int dx = x - centerX;
                        int dy = y - centerY;
                        if (dx * dx + dy * dy >= radiusSquared)
                            continue;
                        ref WorldTile tile = ref grid.At(x, y);
                        if (IsDungeonWall(tile.Wall, baseWall, wallVariants))
                            tile.Wall = checked((ushort)variant);
                    }
                }
            }
        }
    }

    private static int PlaceBiomeChests(
        Workspace workspace,
        Grid grid,
        DungeonGraph1458 graph,
        DungeonBounds1458 bounds,
        bool crimson,
        IWorldGenerationVanillaRandom random,
        double worldSurface,
        CancellationToken cancellationToken)
    {
        // Ordinary 1.4.5.8 dungeon places five locked biome chests.
        (ushort Tile, int Style, int Item)[] definitions =
        [
            (Containers, 23, VanillaItemIds.PiranhaGun.Value),
            (Containers, crimson ? 25 : 24, crimson ? VanillaItemIds.VampireKnives.Value : VanillaItemIds.ScourgeOfTheCorruptor.Value),
            (Containers, 26, VanillaItemIds.RainbowGun.Value),
            (Containers, 27, VanillaItemIds.StaffOfTheFrostHydra.Value),
            (Containers2, 13, VanillaItemIds.StormTigerStaff.Value),
        ];

        int placed = 0;
        foreach ((ushort tileType, int style, int itemType) in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool success = false;
            DungeonBounds1458? entranceBounds = graph.Components
                .Where(static component => component.Kind == DungeonComponentKind1458.Entrance)
                .Select(static component => (DungeonBounds1458?)component.Bounds)
                .FirstOrDefault();
            int minY = Math.Clamp(Math.Max(bounds.Top, (int)worldSurface), 2, grid.Height - 4);
            int maxYExclusive = Math.Clamp(bounds.Bottom, minY + 1, grid.Height - 3);
            for (int attempt = 0; attempt < 1000 && !success; attempt++)
            {
                int x = random.Next(Math.Max(2, bounds.Left), Math.Max(Math.Max(2, bounds.Left) + 1, bounds.Right));
                int y = random.Next(minY, maxYExclusive);
                if (entranceBounds is { } entrance && Contains(entrance, x, y))
                    continue;
                WorldTile candidate = grid.At(x, y);
                if (candidate.IsActive || candidate.Wall == 0)
                    continue;
                success = TryPlaceDungeonChest(workspace, grid, x, y, tileType, style, itemType);
            }
            if (success)
                placed++;
        }
        return placed;
    }

    private static int PlaceBasicChests(
        Workspace workspace,
        Grid grid,
        IReadOnlyList<DungeonBounds1458> rooms,
        IWorldGenerationVanillaRandom random,
        double worldSurface,
        CancellationToken cancellationToken)
    {
        int placed = 0;
        int lootIndex = 0;
        foreach (DungeonBounds1458 room in rooms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool success = false;
            for (int attempt = 0; attempt < 1000 && !success; attempt++)
            {
                int x = random.Next(Math.Max(2, room.Left), Math.Max(Math.Max(2, room.Left) + 1, room.Right));
                int roomCenterY = room.Top + room.Height / 2;
                int y = random.Next(Math.Max(2, roomCenterY), Math.Max(Math.Max(2, roomCenterY) + 1, room.Bottom));
                int style;
                int item;
                if (y < worldSurface + 50d)
                {
                    style = 0;
                    item = VanillaItemIds.GoldenKey.Value;
                }
                else
                {
                    style = lootIndex == 6 ? 0 : 2;
                    item = DungeonLootCycle[lootIndex % DungeonLootCycle.Length];
                }
                success = TryPlaceDungeonChest(workspace, grid, x, y, Containers, style, item);
            }
            if (!success)
                continue;
            lootIndex = (lootIndex + 1) % DungeonLootCycle.Length;
            placed++;
        }
        return placed;
    }

    private static int PlaceBookshelves(
        Grid grid,
        DungeonBounds1458 bounds,
        IReadOnlyList<int> wallVariants,
        ushort brickType,
        ushort crackedBrickType,
        DungeonDecorationProfile1458 decoration,
        IWorldGenerationVanillaRandom random,
        double worldSurface,
        double rockLayer,
        CancellationToken cancellationToken)
    {
        // TerrariaServer 1.4.5.8 DungeonGlobalBookshelves creates short platform shelves, not bookcase objects.
        int target = Math.Max(1, grid.Width / 20);
        int placed = 0;
        int failures = 0;
        while (placed < target)
        {
            if (((placed + failures) & 127) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (failures++ > 1000)
            {
                failures = 0;
                placed++;
                continue;
            }

            int x = random.Next(bounds.Left, Math.Max(bounds.Left + 1, bounds.Right));
            int y = random.Next(bounds.Top, Math.Max(bounds.Top + 1, bounds.Bottom));
            if (!grid.Contains(x, y) || grid.At(x, y).IsActive || !IsAnyDungeonWall(grid.At(x, y).Wall, wallVariants))
                continue;

            int direction = random.Next(2) == 0 ? -1 : 1;
            int boundaryX = x;
            bool valid = true;
            while (grid.Contains(boundaryX, y) && !grid.At(boundaryX, y).IsActive)
            {
                boundaryX -= direction;
                if (boundaryX < bounds.Left || boundaryX > bounds.Right)
                {
                    valid = false;
                    break;
                }
                if (grid.At(boundaryX, y).IsActive && !IsDungeonStructuralTile(grid.At(boundaryX, y).Type, brickType, crackedBrickType))
                {
                    valid = false;
                    break;
                }
            }
            if (!valid || !grid.Contains(boundaryX, y - 1) || !grid.Contains(boundaryX, y + 1) ||
                !IsDungeonStructuralTile(grid.At(boundaryX, y).Type, brickType, crackedBrickType) ||
                !IsDungeonStructuralTile(grid.At(boundaryX, y - 1).Type, brickType, crackedBrickType) ||
                !IsDungeonStructuralTile(grid.At(boundaryX, y + 1).Type, brickType, crackedBrickType))
            {
                continue;
            }

            x = boundaryX + direction;
            if (!grid.Contains(x, y - 3) || grid.At(x, y).IsActive || grid.At(x, y - 1).IsActive ||
                grid.At(x, y - 2).IsActive || grid.At(x, y - 3).IsActive || HasPlatformNearby(grid, x, y, 3))
            {
                continue;
            }

            int corridorLength = 0;
            int scanX = x;
            while (scanX > bounds.Left && scanX < bounds.Right &&
                   !grid.At(scanX, y).IsActive && !grid.At(scanX, y - 1).IsActive && !grid.At(scanX, y + 1).IsActive)
            {
                corridorLength++;
                scanX += direction;
            }
            if (corridorLength <= 5)
                continue;

            bool placeBooks = random.Next(2) == 0;
            int runLength = random.Next(1, 4);
            int startX = x;
            int localPlaced = 0;
            for (int step = 0; step < runLength; step++)
            {
                int shelfX = x + step * direction;
                if (!grid.Contains(shelfX, y) || grid.At(shelfX, y).IsActive)
                    break;
                int variantIndex = ResolveWallVariantIndex(grid.At(shelfX, y).Wall, wallVariants);
                int shelfStyle = decoration.GetShelfStyle(variantIndex);
                SetObjectTile(ref grid.At(shelfX, y), Platform, 0, checked((short)(shelfStyle * 18)));
                localPlaced++;

                if (placeBooks && grid.Contains(shelfX, y - 1) && !grid.At(shelfX, y - 1).IsActive)
                {
                    short frameX = 0;
                    if (random.Next(50) == 0 && y > (worldSurface + rockLayer) / 2d)
                        frameX = 90; // Water Bolt book variant used by the source pass.
                    SetObjectTile(ref grid.At(shelfX, y - 1), Books, frameX, 0);
                }
            }
            if (localPlaced == 0)
                continue;

            failures = 0;
            placed++;
            if (!placeBooks && random.Next(2) == 0 && grid.Contains(startX, y - 1) && !grid.At(startX, y - 1).IsActive)
            {
                if (random.Next(4) == 0)
                {
                    SetObjectTile(ref grid.At(startX, y - 1), WaterCandle, 0, 0);
                }
                else
                {
                    short bottleFrame = random.Next(2) == 0 ? (short)18 : (short)36;
                    SetObjectTile(ref grid.At(startX, y - 1), Tile(VanillaTileIds.Bottles), bottleFrame, 0);
                }
            }
        }
        return placed;
    }

    private static int PlaceLights(
        Grid grid,
        DungeonBounds1458 bounds,
        IReadOnlyList<int> wallVariants,
        ushort brickType,
        ushort crackedBrickType,
        int dungeonColor,
        DungeonDecorationProfile1458 decoration,
        IWorldGenerationVanillaRandom random,
        CancellationToken cancellationToken)
    {
        int target = Math.Max(1, (int)(28d * grid.Width / 4200d));
        int placed = 0;
        int failures = 0;
        while (placed < target)
        {
            if (((placed + failures) & 63) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (failures++ > 1000)
            {
                failures = 0;
                placed++;
                continue;
            }

            int x = random.Next(bounds.Left, Math.Max(bounds.Left + 1, bounds.Right));
            int seedY = random.Next(bounds.Top, Math.Max(bounds.Top + 1, bounds.Bottom));
            if (!grid.Contains(x, seedY) || !IsAnyDungeonWall(grid.At(x, seedY).Wall, wallVariants))
                continue;

            int y = seedY;
            bool foundCeiling = false;
            while (y > bounds.Top + 1)
            {
                if (grid.At(x, y - 1).IsActive &&
                    IsDungeonStructuralTile(grid.At(x, y - 1).Type, brickType, crackedBrickType) &&
                    !grid.At(x, y).IsActive && IsAnyDungeonWall(grid.At(x, y).Wall, wallVariants))
                {
                    foundCeiling = true;
                    break;
                }
                y--;
            }
            if (!foundCeiling || HasDungeonLightNearby(grid, x, y, 15) || !HasLightClearance(grid, x, y))
                continue;

            bool placedLight = false;
            if (random.Next(7) == 0 && !HasSolidBelow(grid, x, y, 15))
            {
                int variantIndex = ResolveWallVariantIndex(grid.At(x, y).Wall, wallVariants);
                int style = variantIndex > 0 ? 53 : 27 + Math.Clamp(dungeonColor, 0, 2);
                placedLight = PlaceChandelier(grid, x, y, style);
            }

            if (!placedLight)
            {
                int variantIndex = ResolveWallVariantIndex(grid.At(x, y).Wall, wallVariants);
                int style = decoration.GetLanternStyle(variantIndex);
                if (variantIndex > 0 && random.Next(3) == 0)
                    style = 53;
                placedLight = PlaceOneByTwoTop(grid, x, y, Tile(VanillaTileIds.HangingLanterns), style);
            }

            if (!placedLight)
                continue;

            failures = 0;
            placed++;
            TryGenerateLightSwitch(grid, x, y, bounds, brickType, crackedBrickType, wallVariants, random);
        }
        return placed;
    }

    private static int PlaceTraps(
        Grid grid,
        DungeonBounds1458 bounds,
        IReadOnlyList<int> wallVariants,
        IWorldGenerationVanillaRandom random,
        CancellationToken cancellationToken)
    {
        int target = Math.Max(1, (int)(8.4d * grid.Width / 4200d));
        int placed = 0;
        for (int attempt = 0; attempt < target * 120 && placed < target; attempt++)
        {
            if ((attempt & 63) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(bounds.Left + 4, bounds.Right - 3);
            int y = random.Next(bounds.Top + 4, bounds.Bottom - 3);
            if (grid.At(x, y).IsActive || !IsAnyDungeonWall(grid.At(x, y).Wall, wallVariants))
                continue;
            int floor = FindFloor(grid, x, y, Math.Min(bounds.Bottom, y + 16));
            if (floor <= y + 1)
                continue;
            int plateY = floor - 1;
            int side = random.Next(2) == 0 ? -1 : 1;
            int trapX = x + side * random.Next(5, 13);
            if (!grid.Contains(trapX, plateY - 1) || grid.At(trapX, plateY - 1).IsActive)
                continue;
            SetObjectTile(ref grid.At(x, plateY), PressurePlate, 36, 0);
            SetObjectTile(ref grid.At(trapX, plateY - 1), DartTrap, side < 0 ? (short)0 : (short)18, 0);
            WireManhattan(grid, x, plateY, trapX, plateY - 1);
            placed++;
        }
        return placed;
    }

    private static int PlaceGroundFurniture(
        Grid grid,
        IReadOnlyList<DungeonBounds1458> rooms,
        IReadOnlyList<int> wallVariants,
        IWorldGenerationVanillaRandom random,
        CancellationToken cancellationToken)
    {
        int placed = 0;
        foreach (DungeonBounds1458 room in rooms)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (random.Next(2) != 0)
                continue;
            int x = room.Left + room.Width / 2;
            int floor = FindFloor(grid, x, room.Top + 2, room.Bottom);
            if (floor <= room.Top + 3 || !IsAnyDungeonWall(grid.At(x, floor - 1).Wall, wallVariants))
                continue;

            // Safe ordinary-dungeon furniture subset: a 3x2 table and paired 1x2 chairs.
            if (!CanPlaceRectangle(grid, x - 1, floor - 2, 3, 2, requireAir: true))
                continue;
            PlaceObjectRectangle(grid, x - 1, floor - 2, 3, 2, VanillaTileIds.Tables.Value, 0, 0);
            if (CanPlaceRectangle(grid, x - 3, floor - 2, 1, 2, requireAir: true))
                PlaceObjectRectangle(grid, x - 3, floor - 2, 1, 2, VanillaTileIds.Chairs.Value, 0, 0);
            if (CanPlaceRectangle(grid, x + 3, floor - 2, 1, 2, requireAir: true))
                PlaceObjectRectangle(grid, x + 3, floor - 2, 1, 2, VanillaTileIds.Chairs.Value, 0, 0);
            placed++;
        }
        return placed;
    }

    private static int PlacePaintings(
        Grid grid,
        IReadOnlyList<DungeonBounds1458> rooms,
        IReadOnlyList<int> wallVariants,
        IWorldGenerationVanillaRandom random,
        CancellationToken cancellationToken)
    {
        int target = Math.Max(1, (int)(100d * grid.Width / 4200d));
        int placed = 0;
        int attemptBudget = target * 3;
        for (int attempt = 0; attempt < attemptBudget && placed < target; attempt++)
        {
            if ((attempt & 63) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (rooms.Count == 0)
                break;

            DungeonBounds1458 room = rooms[random.Next(rooms.Count)];
            DungeonPainting1458 painting = PickDungeonPainting(random);
            int left = room.Left + Math.Max(1, (room.Width - painting.Width) / 2);
            int top = room.Top + Math.Max(1, (room.Height - painting.Height) / 2);
            left += random.Next(-2, 3);
            top += random.Next(-2, 3);
            if (!CanPlaceWallObject(grid, left, top, painting.Width, painting.Height, wallVariants))
                continue;

            int wrap = painting.TileType == Tile(VanillaTileIds.Painting3X3) ? 36 : 27;
            int frameXBase = painting.Style % wrap * painting.Width * 18;
            int frameYBase = painting.Style / wrap * painting.Height * 18;
            PlaceObjectRectangle(
                grid,
                left,
                top,
                painting.Width,
                painting.Height,
                painting.TileType,
                frameXBase,
                frameYBase);
            placed++;
        }
        return placed;
    }

    private static DungeonPainting1458 PickDungeonPainting(IWorldGenerationVanillaRandom random)
    {
        // DungeonGlobalPaintings.Paintings_RandomDungeonPainting, TerrariaServer 1.4.5.8.
        if (random.Next(3) <= 1)
        {
            int style = random.Next(7);
            if (style == 6)
                style = random.Next(7);
            style = style switch
            {
                0 => 12,
                1 => 13,
                2 => 14,
                3 => 15,
                4 => 18,
                5 => 19,
                _ => 23,
            };
            return new DungeonPainting1458(Tile(VanillaTileIds.Painting3X3), style, 3, 3);
        }

        int largeStyle = random.Next(17);
        largeStyle = largeStyle switch
        {
            14 => 15,
            15 => 16,
            16 => 30,
            _ => largeStyle,
        };
        return new DungeonPainting1458(Tile(VanillaTileIds.Painting6X4), largeStyle, 6, 4);
    }

    private static int PlaceBanners(
        Grid grid,
        DungeonBounds1458 bounds,
        IReadOnlyList<int> wallVariants,
        IWorldGenerationVanillaRandom random,
        CancellationToken cancellationToken)
    {
        int target = Math.Max(1, (int)(200d * grid.Width / 4200d));
        int placed = 0;
        for (int attempt = 0; attempt < target * 20 && placed < target; attempt++)
        {
            if ((attempt & 127) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            int x = random.Next(bounds.Left + 2, bounds.Right - 1);
            int y = random.Next(bounds.Top + 2, bounds.Bottom - 4);
            if (!IsAnyDungeonWall(grid.At(x, y).Wall, wallVariants) || grid.At(x, y).IsActive)
                continue;
            int ceiling = y;
            while (ceiling > bounds.Top + 1 && !grid.At(x, ceiling - 1).IsActive)
                ceiling--;
            if (!grid.At(x, ceiling - 1).IsActive || !CanPlaceRectangle(grid, x, ceiling, 1, 3, requireAir: true))
                continue;
            int variantIndex = ResolveWallVariantIndex(grid.At(x, ceiling).Wall, wallVariants);
            int style = 10 + variantIndex * 2 + random.Next(2);
            PlaceObjectRectangle(grid, x, ceiling, 1, 3, Banner, style * 18, 0);
            placed++;
        }
        return placed;
    }

    private static bool TryPlaceDungeonChest(
        Workspace workspace,
        Grid grid,
        int seedX,
        int seedY,
        ushort chestType,
        int style,
        int mainItemType)
    {
        int x = Math.Clamp(seedX, 2, grid.Width - 4);
        int startY = Math.Clamp(seedY, 2, grid.Height - 4);
        int floor = FindFloor(grid, x, startY, Math.Min(grid.Height - 2, startY + 40));
        if (floor >= grid.Height - 1)
            return false;
        int top = floor - 2;
        if (top < 1 || !CanPlaceRectangle(grid, x, top, 2, 2, requireAir: true))
            return false;
        if (!grid.At(x, floor).IsActive || !grid.At(x + 1, floor).IsActive)
            return false;

        WorldTile a = grid.At(x, top);
        WorldTile b = grid.At(x + 1, top);
        WorldTile c = grid.At(x, top + 1);
        WorldTile d = grid.At(x + 1, top + 1);
        int baseFrameX = style * 36;
        SetObjectTile(ref grid.At(x, top), chestType, checked((short)baseFrameX), 0);
        SetObjectTile(ref grid.At(x + 1, top), chestType, checked((short)(baseFrameX + 18)), 0);
        SetObjectTile(ref grid.At(x, top + 1), chestType, checked((short)baseFrameX), 18);
        SetObjectTile(ref grid.At(x + 1, top + 1), chestType, checked((short)(baseFrameX + 18)), 18);

        Span<WorldChestItem> items = stackalloc WorldChestItem[1];
        items[0] = new WorldChestItem(1, mainItemType, 0);
        if (workspace.TryAddGeneratedChest(x, top, string.Empty, items))
            return true;

        grid.At(x, top) = a;
        grid.At(x + 1, top) = b;
        grid.At(x, top + 1) = c;
        grid.At(x + 1, top + 1) = d;
        return false;
    }

    private static bool PlaceDoor(Grid grid, int x, int centerY, int style, IWorldGenerationVanillaRandom random)
    {
        int top = centerY - 1;
        int bottom = centerY + 1;
        if (top < 2 || bottom >= grid.Height - 2)
            return false;
        if (grid.At(x, top).IsActive || grid.At(x, centerY).IsActive || grid.At(x, bottom).IsActive)
            return false;
        if (!grid.At(x, top - 1).IsActive || !grid.At(x, bottom + 1).IsActive)
            return false;

        int styleGroup = style / 36;
        int styleWithinGroup = style % 36;
        int frameXBase = 54 * styleGroup;
        int frameYBase = 54 * styleWithinGroup;
        SetObjectTile(ref grid.At(x, top), ClosedDoor, checked((short)(frameXBase + random.Next(3) * 18)), checked((short)frameYBase));
        SetObjectTile(ref grid.At(x, centerY), ClosedDoor, checked((short)(frameXBase + random.Next(3) * 18)), checked((short)(frameYBase + 18)));
        SetObjectTile(ref grid.At(x, bottom), ClosedDoor, checked((short)(frameXBase + random.Next(3) * 18)), checked((short)(frameYBase + 36)));
        return true;
    }

    private static bool HasPlatformNearby(Grid grid, int x, int y, int radius)
    {
        int left = Math.Max(0, x - radius);
        int right = Math.Min(grid.Width - 1, x + radius);
        int top = Math.Max(0, y - radius);
        int bottom = Math.Min(grid.Height - 1, y + radius);
        for (int yy = top; yy <= bottom; yy++)
        for (int xx = left; xx <= right; xx++)
        {
            if (grid.At(xx, yy).IsActive && grid.At(xx, yy).Type == Platform)
                return true;
        }
        return false;
    }

    private static bool IsDungeonStructuralTile(ushort type, ushort brickType, ushort crackedBrickType) =>
        type == brickType || type == crackedBrickType;

    private static bool HasDungeonLightNearby(Grid grid, int x, int y, int radius)
    {
        int left = Math.Max(0, x - radius);
        int right = Math.Min(grid.Width - 1, x + radius);
        int top = Math.Max(0, y - radius);
        int bottom = Math.Min(grid.Height - 1, y + radius);
        ushort lantern = Tile(VanillaTileIds.HangingLanterns);
        ushort chandelier = Tile(VanillaTileIds.Chandeliers);
        for (int yy = top; yy <= bottom; yy++)
        for (int xx = left; xx <= right; xx++)
        {
            WorldTile tile = grid.At(xx, yy);
            if (tile.IsActive && (tile.Type == lantern || tile.Type == chandelier))
                return true;
        }
        return false;
    }

    private static bool HasLightClearance(Grid grid, int x, int y)
    {
        if (!grid.Contains(x - 1, y) || !grid.Contains(x + 1, y + 2))
            return false;
        return !grid.At(x - 1, y).IsActive &&
               !grid.At(x + 1, y).IsActive &&
               !grid.At(x - 1, y + 1).IsActive &&
               !grid.At(x + 1, y + 1).IsActive &&
               !grid.At(x, y + 2).IsActive;
    }

    private static bool HasSolidBelow(Grid grid, int x, int y, int distance)
    {
        int bottom = Math.Min(grid.Height - 1, y + distance);
        for (int yy = y; yy <= bottom; yy++)
        {
            if (grid.At(x, yy).IsActive)
                return true;
        }
        return false;
    }

    private static bool PlaceOneByTwoTop(Grid grid, int x, int y, ushort type, int style)
    {
        if (!grid.Contains(x, y - 1) || !grid.Contains(x, y + 1) ||
            !grid.At(x, y - 1).IsActive || grid.At(x, y).IsActive || grid.At(x, y + 1).IsActive)
        {
            return false;
        }
        short frameY = checked((short)(style * 36));
        SetObjectTile(ref grid.At(x, y), type, 0, frameY);
        SetObjectTile(ref grid.At(x, y + 1), type, 0, checked((short)(frameY + 18)));
        return true;
    }

    private static bool PlaceChandelier(Grid grid, int x, int y, int style)
    {
        if (!grid.Contains(x - 1, y) || !grid.Contains(x + 1, y + 2) ||
            !grid.At(x, y - 1).IsActive || !CanPlaceRectangle(grid, x - 1, y, 3, 3, requireAir: true))
        {
            return false;
        }

        int frameXBase = style / 36 * 108;
        int frameYBase = style * 54;
        if (frameXBase >= 108)
            frameYBase -= 54 * (frameXBase / 108) * 37;
        PlaceObjectRectangle(
            grid,
            x - 1,
            y,
            3,
            3,
            VanillaTileIds.Chandeliers.Value,
            frameXBase,
            frameYBase);
        return true;
    }

    private static void TryGenerateLightSwitch(
        Grid grid,
        int lightX,
        int lightY,
        DungeonBounds1458 bounds,
        ushort brickType,
        ushort crackedBrickType,
        IReadOnlyList<int> wallVariants,
        IWorldGenerationVanillaRandom random)
    {
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            int x = Math.Clamp(lightX + random.Next(-12, 13), bounds.Left + 1, bounds.Right - 1);
            int y = Math.Clamp(lightY + random.Next(3, 21), bounds.Top + 1, bounds.Bottom - 1);
            if (!grid.Contains(x, y + 1) || grid.At(x, y).IsActive || grid.At(x, y + 1).IsActive ||
                !IsAnyDungeonWall(grid.At(x, y).Wall, wallVariants))
            {
                continue;
            }

            bool sideSupport = IsDungeonStructuralTile(grid.At(x - 1, y).Type, brickType, crackedBrickType) ||
                               IsDungeonStructuralTile(grid.At(x + 1, y).Type, brickType, crackedBrickType);
            if (!sideSupport && !grid.At(x, y + 1).IsActive)
                continue;

            SetObjectTile(ref grid.At(x, y), Tile(VanillaTileIds.Switches), 0, 0);
            WireManhattan(grid, x, y, lightX, lightY);
            if (random.Next(3) > 0)
            {
                grid.At(lightX, lightY).FrameX = 18;
                if (grid.Contains(lightX, lightY + 1) && grid.At(lightX, lightY + 1).Type == Tile(VanillaTileIds.HangingLanterns))
                    grid.At(lightX, lightY + 1).FrameX = 18;
            }
            return;
        }
    }

    private static bool TryFindDoorCenter(Grid grid, int x, int y, out int centerY)
    {
        for (int offset = 0; offset <= 12; offset++)
        {
            int signCount = offset == 0 ? 1 : 2;
            for (int signIndex = 0; signIndex < signCount; signIndex++)
            {
                int sign = offset == 0 || signIndex == 1 ? 1 : -1;
                int candidate = y + offset * sign;
                if (candidate < 3 || candidate >= grid.Height - 3)
                    continue;
                if (!grid.At(x, candidate - 1).IsActive && !grid.At(x, candidate).IsActive && !grid.At(x, candidate + 1).IsActive &&
                    grid.At(x, candidate - 2).IsActive && grid.At(x, candidate + 2).IsActive)
                {
                    centerY = candidate;
                    return true;
                }
            }
        }
        centerY = 0;
        return false;
    }

    private static bool TryFindDungeonAir(Grid grid, int x, int y, int radius, out int foundX, out int foundY)
    {
        for (int r = 0; r <= radius; r++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    int px = x + dx;
                    int py = y + dy;
                    if (!grid.Contains(px, py) || grid.At(px, py).IsActive || grid.At(px, py).Wall == 0)
                        continue;
                    foundX = px;
                    foundY = py;
                    return true;
                }
            }
        }
        foundX = 0;
        foundY = 0;
        return false;
    }

    private static int FindFloor(Grid grid, int x, int startY, int maxY)
    {
        int bottom = Math.Min(grid.Height - 1, maxY);
        for (int y = Math.Max(1, startY); y <= bottom; y++)
        {
            if (grid.At(x, y).IsActive && grid.At(Math.Min(grid.Width - 1, x + 1), y).IsActive)
                return y;
        }
        return grid.Height;
    }

    private static bool CanPlaceWallObject(
        Grid grid,
        int left,
        int top,
        int width,
        int height,
        IReadOnlyList<int> wallVariants)
    {
        if (!CanPlaceRectangle(grid, left, top, width, height, requireAir: true))
            return false;
        for (int y = top; y < top + height; y++)
        for (int x = left; x < left + width; x++)
        {
            if (!IsAnyDungeonWall(grid.At(x, y).Wall, wallVariants))
                return false;
        }
        return true;
    }

    private static bool CanPlaceRectangle(Grid grid, int left, int top, int width, int height, bool requireAir)
    {
        if (left < 1 || top < 1 || left + width >= grid.Width - 1 || top + height >= grid.Height - 1)
            return false;
        for (int y = top; y < top + height; y++)
        for (int x = left; x < left + width; x++)
        {
            WorldTile tile = grid.At(x, y);
            if (requireAir && tile.IsActive)
                return false;
            if (tile.IsActive && VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
                return false;
        }
        return true;
    }

    private static void PlaceObjectRectangle(
        Grid grid,
        int left,
        int top,
        int width,
        int height,
        int type,
        int frameXBase,
        int frameYBase)
    {
        for (int dy = 0; dy < height; dy++)
        for (int dx = 0; dx < width; dx++)
            SetObjectTile(
                ref grid.At(left + dx, top + dy),
                checked((ushort)type),
                checked((short)(frameXBase + dx * 18)),
                checked((short)(frameYBase + dy * 18)));
    }

    private static void SetObjectTile(ref WorldTile tile, ushort type, short frameX, short frameY)
    {
        tile.Type = type;
        tile.Flags |= WorldTileFlags.Active;
        tile.FrameX = frameX;
        tile.FrameY = frameY;
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    private static void SetSolidTile(ref WorldTile tile, ushort type, ushort wall)
    {
        tile.Type = type;
        tile.Wall = wall;
        tile.Flags |= WorldTileFlags.Active;
        tile.FrameX = -1;
        tile.FrameY = -1;
        tile.Shape = 0;
        tile.LiquidAmount = 0;
        tile.LiquidKind = WorldLiquidKind.Water;
    }

    private static void WireManhattan(Grid grid, int x1, int y1, int x2, int y2)
    {
        int x = x1;
        int y = y1;
        while (x != x2)
        {
            grid.At(x, y).Flags |= WorldTileFlags.WireRed;
            x += Math.Sign(x2 - x);
        }
        while (y != y2)
        {
            grid.At(x, y).Flags |= WorldTileFlags.WireRed;
            y += Math.Sign(y2 - y);
        }
        grid.At(x2, y2).Flags |= WorldTileFlags.WireRed;
    }

    private static ushort Tile(TileTypeId type) => checked((ushort)type.Value);
    private static ushort Wall(WallTypeId type) => checked((ushort)type.Value);

    private static int[] ResolveWallVariants(ushort baseWall) => baseWall switch
    {
        var wall when wall == Wall(VanillaWallIds.BlueDungeonUnsafe) =>
            [Wall(VanillaWallIds.BlueDungeonUnsafe), Wall(VanillaWallIds.BlueDungeonSlabUnsafe), Wall(VanillaWallIds.BlueDungeonTileUnsafe)],
        var wall when wall == Wall(VanillaWallIds.GreenDungeonUnsafe) =>
            [Wall(VanillaWallIds.GreenDungeonUnsafe), Wall(VanillaWallIds.GreenDungeonSlabUnsafe), Wall(VanillaWallIds.GreenDungeonTileUnsafe)],
        var wall when wall == Wall(VanillaWallIds.PinkDungeonUnsafe) =>
            [Wall(VanillaWallIds.PinkDungeonUnsafe), Wall(VanillaWallIds.PinkDungeonSlabUnsafe), Wall(VanillaWallIds.PinkDungeonTileUnsafe)],
        _ => [baseWall, baseWall, baseWall],
    };

    private static int ResolveWallVariantIndex(ushort wall, IReadOnlyList<int> variants)
    {
        for (int index = 0; index < variants.Count; index++)
        {
            if (wall == variants[index])
                return index;
        }
        return 0;
    }

    private static bool Contains(DungeonBounds1458 bounds, int x, int y) =>
        x >= bounds.Left && x <= bounds.Right && y >= bounds.Top && y <= bounds.Bottom;

    private static bool IsDungeonAir(Grid grid, int x, int y, ushort baseWall) =>
        grid.Contains(x, y) && !grid.At(x, y).IsActive && grid.At(x, y).Wall == baseWall;

    private static bool IsDungeonWall(ushort wall, ushort baseWall, IReadOnlyList<int> variants) =>
        wall == baseWall || variants.Contains(wall);

    private static bool IsAnyDungeonWall(ushort wall, IReadOnlyList<int> variants) =>
        variants.Contains(wall);

    private static DungeonBounds1458 Inset(DungeonBounds1458 value, int amount) =>
        new(value.Left + amount, value.Top + amount, value.Right - amount, value.Bottom - amount);

    private static DungeonBounds1458 ClampBounds(DungeonBounds1458 value, int width, int height, int padding) =>
        new(
            Math.Clamp(value.Left - padding, 2, width - 3),
            Math.Clamp(value.Top - padding, 2, height - 3),
            Math.Clamp(value.Right + padding, 2, width - 3),
            Math.Clamp(value.Bottom + padding, 2, height - 3));

    private static void AddUnique(List<DungeonPoint1458> target, HashSet<int> keys, int x, int y, int width)
    {
        int key = checked(y * width + x);
        if (keys.Add(key))
            target.Add(new DungeonPoint1458(x, y));
    }

    private readonly record struct DungeonPainting1458(
        ushort TileType,
        int Style,
        int Width,
        int Height);

    private readonly record struct DoorPlatformCandidates(
        IReadOnlyList<DungeonPoint1458> Doors,
        IReadOnlyList<DungeonPoint1458> Platforms);

    private sealed class Grid(WorldTileStore store)
    {
        public int Width => store.Dimensions.WidthTiles;
        public int Height => store.Dimensions.HeightTiles;
        public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;
        public ref WorldTile At(int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
    }
}
