using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Deterministic richer-dungeon replacement for the bounded optimized dungeon reservation. The base provider still
/// owns placement of the reservation and metadata anchor; this pass rebuilds only the already-reserved Blue Dungeon
/// footprint into a connected room/hall graph, then adds source-backed dungeon chest/key and dart-trap roles.
/// It is intentionally not seed-identical to Terraria worldgen.
/// </summary>
internal static class OptimizedDungeonV2
{
    private const ushort Air = 0;
    private const ushort BlueDungeonBrick = 41;
    private const ushort Spike = 48;
    private const ushort PressurePlate = 135;
    private const ushort DartTrap = 137;
    private const ushort Containers = 21;
    private const ushort BlueDungeonUnsafeWall = 7;

    // TerrariaServer 1.4.5.8 WorldGen.GetDungeonLootAndChestStyle / ItemID.
    private static readonly ItemTypeId MagicMissile = new(113);
    private static readonly ItemTypeId Muramasa = new(155);
    private static readonly ItemTypeId CobaltShield = new(156);
    private static readonly ItemTypeId AquaScepter = new(157);
    private static readonly ItemTypeId BlueMoon = new(163);
    private static readonly ItemTypeId Handgun = new(164);
    private static readonly ItemTypeId GoldenKey = new(327);
    private static readonly ItemTypeId Valor = new(3317);

    private static readonly ItemTypeId[] LockedChestMainLoot =
    [
        Muramasa,
        CobaltShield,
        AquaScepter,
        BlueMoon,
        MagicMissile,
        Valor,
        Handgun
    ];

    internal static OptimizedDungeonV2Report Apply(IWorldGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
            throw new InvalidOperationException("Optimized dungeon v2 requires semantic world metadata.");
        if (context.Workspace is not RuntimeWorldGenerationWorkspace runtimeWorkspace ||
            context.Workspace is not IWorldGenerationChestWorkspace chestWorkspace)
        {
            throw new InvalidOperationException(
                "Optimized dungeon v2 requires the runtime generation workspace with persistent chest support.");
        }
        if (!metadata.TryGetDungeon(out WorldGenerationPoint dungeonAnchor))
            throw new InvalidOperationException("Optimized dungeon v2 requires the dungeon metadata anchor.");

        DungeonBounds bounds = RecoverDungeonBounds(context.Workspace, dungeonAnchor);
        ValidateBounds(context.Workspace, bounds, dungeonAnchor);

        context.CancellationToken.ThrowIfCancellationRequested();
        ResetDungeonMass(context.Workspace, bounds);
        context.ReportProgress(0.12d, "Resetting reserved dungeon mass");

        List<DungeonRoom> mainRooms = BuildMainGraph(context, bounds, dungeonAnchor);
        List<DungeonRoom> branchRooms = BuildBranchGraph(context, bounds, mainRooms);
        OpenEntrance(context.Workspace, bounds, dungeonAnchor, mainRooms[0]);
        context.ReportProgress(0.46d, "Carving connected dungeon rooms and branches");

        int lockedChestTarget = Math.Clamp(mainRooms.Count - 1, 3, LockedChestMainLoot.Length);
        PlaceKeyCache(context.Workspace, chestWorkspace, mainRooms[0], lockedChestTarget);
        int lockedChests = PlaceLockedChests(
            context.Workspace,
            chestWorkspace,
            mainRooms,
            lockedChestTarget);
        context.ReportProgress(0.64d, "Placing locked dungeon loot and Golden Keys");

        int trapTarget = Math.Clamp(branchRooms.Count, 2, 10);
        int traps = PlaceDartTraps(context.Workspace, branchRooms, trapTarget);
        int spikeTarget = Math.Clamp(mainRooms.Count * 3, 12, 72);
        int spikes = PlaceSpikes(context.Workspace, mainRooms, spikeTarget);
        context.ReportProgress(0.82d, "Wiring dungeon traps and placing spikes");

        int connectedInterior = MeasureConnectedInterior(context.Workspace, bounds, mainRooms[0].Center);
        OptimizedDungeonV2Report report = ValidateGeneratedDungeon(
            runtimeWorkspace,
            bounds,
            dungeonAnchor,
            mainRooms.Count,
            branchRooms.Count,
            lockedChestTarget,
            lockedChests,
            trapTarget,
            traps,
            spikeTarget,
            spikes,
            connectedInterior);

        context.ReportProgress(
            1d,
            $"Built dungeon v2: rooms={report.MainRooms}+{report.BranchRooms}, locked={report.LockedChests}, " +
            $"traps={report.DartTraps}, spikes={report.SpikeTiles}, connected={report.ConnectedInteriorCells}");
        return report;
    }

    private static DungeonBounds RecoverDungeonBounds(
        IWorldGenerationWorkspace workspace,
        WorldGenerationPoint dungeonAnchor)
    {
        int scanLeft = Math.Max(1, dungeonAnchor.X - 96);
        int scanRight = Math.Min(workspace.WidthTiles - 2, dungeonAnchor.X + 96);
        int scanTop = Math.Max(1, dungeonAnchor.Y - 4);
        int minX = int.MaxValue;
        int minY = int.MaxValue;
        int maxX = int.MinValue;
        int maxY = int.MinValue;

        for (int y = scanTop; y < workspace.HeightTiles - 1; y++)
        {
            for (int x = scanLeft; x <= scanRight; x++)
            {
                if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                    tile.Wall != BlueDungeonUnsafeWall)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (minX == int.MaxValue)
            throw new InvalidOperationException("Optimized dungeon v2 could not recover the reserved dungeon footprint.");

        return new DungeonBounds(minX, minY, maxX, maxY);
    }

    private static void ValidateBounds(
        IWorldGenerationWorkspace workspace,
        DungeonBounds bounds,
        WorldGenerationPoint anchor)
    {
        if (bounds.Left < 1 || bounds.Top < 1 ||
            bounds.Right >= workspace.WidthTiles - 1 || bounds.Bottom >= workspace.HeightTiles - 1 ||
            bounds.Width < 19 || bounds.Height < 64 ||
            anchor.X < bounds.Left || anchor.X > bounds.Right || anchor.Y > bounds.Top + 2)
        {
            throw new InvalidOperationException(
                $"Optimized dungeon v2 recovered an invalid footprint {bounds.Width}x{bounds.Height} at " +
                $"({bounds.Left},{bounds.Top})..({bounds.Right},{bounds.Bottom}).");
        }
    }

    private static void ResetDungeonMass(IWorldGenerationWorkspace workspace, DungeonBounds bounds)
    {
        for (int y = bounds.Top; y <= bounds.Bottom; y++)
        {
            for (int x = bounds.Left; x <= bounds.Right; x++)
                SetSolid(workspace, x, y, BlueDungeonBrick, BlueDungeonUnsafeWall);
        }
    }

    private static List<DungeonRoom> BuildMainGraph(
        IWorldGenerationContext context,
        DungeonBounds bounds,
        WorldGenerationPoint dungeonAnchor)
    {
        int roomCount = Math.Clamp(bounds.Height / 30, 4, 18);
        int halfWidth = Math.Clamp(bounds.Width / 5, 5, 10);
        int halfHeight = Math.Clamp(bounds.Height / (roomCount * 5), 4, 7);
        int usableTop = bounds.Top + 12;
        int usableBottom = bounds.Bottom - 10;
        var rooms = new List<DungeonRoom>(roomCount);

        for (int i = 0; i < roomCount; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            double fraction = (i + 1d) / (roomCount + 1d);
            int centerY = usableTop + (int)Math.Round((usableBottom - usableTop) * fraction);
            int horizontalAmplitude = Math.Clamp(bounds.Width / 7, 2, 12);
            int direction = (i % 3) switch { 0 => -1, 1 => 1, _ => 0 };
            int jitter = NextRange(context.Random, -2, 3);
            int centerX = Math.Clamp(
                dungeonAnchor.X + direction * horizontalAmplitude + jitter,
                bounds.Left + halfWidth + 2,
                bounds.Right - halfWidth - 2);
            int localHalfWidth = Math.Clamp(halfWidth + NextRange(context.Random, -1, 2), 5, 11);
            int localHalfHeight = Math.Clamp(halfHeight + NextRange(context.Random, -1, 2), 4, 8);
            var room = new DungeonRoom(
                Math.Max(bounds.Left + 1, centerX - localHalfWidth),
                Math.Max(bounds.Top + 2, centerY - localHalfHeight),
                Math.Min(bounds.Right - 1, centerX + localHalfWidth),
                Math.Min(bounds.Bottom - 1, centerY + localHalfHeight));
            CarveRoom(context.Workspace, room);

            if (rooms.Count > 0)
                CarveDoglegCorridor(context.Workspace, rooms[^1].Center, room.Center, corridorRadius: 2);
            rooms.Add(room);
        }

        if (rooms.Count < 4)
            throw new InvalidOperationException("Optimized dungeon v2 produced too few main rooms.");
        return rooms;
    }

    private static List<DungeonRoom> BuildBranchGraph(
        IWorldGenerationContext context,
        DungeonBounds bounds,
        IReadOnlyList<DungeonRoom> mainRooms)
    {
        int target = Math.Clamp(mainRooms.Count / 2, 2, 8);
        var branches = new List<DungeonRoom>(target);
        int halfWidth = Math.Clamp(bounds.Width / 9, 3, 5);
        const int halfHeight = 3;

        for (int i = 0; i < target; i++)
        {
            DungeonRoom parent = mainRooms[Math.Min(mainRooms.Count - 1, 1 + i * Math.Max(1, (mainRooms.Count - 1) / target))];
            bool left = (i & 1) == 0;
            int centerX = left
                ? bounds.Left + halfWidth + 2
                : bounds.Right - halfWidth - 2;
            int verticalJitter = NextRange(context.Random, -4, 5);
            int centerY = Math.Clamp(
                parent.Center.Y + verticalJitter,
                bounds.Top + halfHeight + 3,
                bounds.Bottom - halfHeight - 3);
            var room = new DungeonRoom(
                centerX - halfWidth,
                centerY - halfHeight,
                centerX + halfWidth,
                centerY + halfHeight);
            CarveRoom(context.Workspace, room);
            CarveHorizontalCorridor(context.Workspace, parent.Center, room.Center, corridorRadius: 1);
            branches.Add(room);
        }

        if (branches.Count != target)
            throw new InvalidOperationException($"Optimized dungeon v2 produced {branches.Count}/{target} branch rooms.");
        return branches;
    }

    private static void OpenEntrance(
        IWorldGenerationWorkspace workspace,
        DungeonBounds bounds,
        WorldGenerationPoint dungeonAnchor,
        DungeonRoom firstRoom)
    {
        int centerX = Math.Clamp(dungeonAnchor.X, bounds.Left + 3, bounds.Right - 3);
        int targetY = Math.Max(bounds.Top + 4, firstRoom.Top + 2);
        for (int y = Math.Max(1, dungeonAnchor.Y); y <= targetY; y++)
        {
            for (int x = centerX - 2; x <= centerX + 2; x++)
                SetAir(workspace, x, y, y >= bounds.Top ? BlueDungeonUnsafeWall : (ushort)0);
        }
        CarveDoglegCorridor(workspace, new WorldGenerationPoint(centerX, targetY), firstRoom.Center, corridorRadius: 2);
    }

    private static void PlaceKeyCache(
        IWorldGenerationWorkspace workspace,
        IWorldGenerationChestWorkspace chests,
        DungeonRoom room,
        int lockedChestTarget)
    {
        int left = Math.Clamp(room.Center.X - 1, room.Left + 2, room.Right - 3);
        int top = room.Bottom - 2;
        WorldGenerationChestItem[] loot =
        [
            new WorldGenerationChestItem(lockedChestTarget, GoldenKey)
        ];
        if (!TryPlaceDungeonChest(workspace, chests, left, top, style: 0, "Dungeon Key Cache", loot))
            throw new InvalidOperationException("Optimized dungeon v2 could not place the entrance Golden Key cache.");
    }

    private static int PlaceLockedChests(
        IWorldGenerationWorkspace workspace,
        IWorldGenerationChestWorkspace chests,
        IReadOnlyList<DungeonRoom> mainRooms,
        int target)
    {
        int placed = 0;
        for (int i = 1; i < mainRooms.Count && placed < target; i++)
        {
            DungeonRoom room = mainRooms[i];
            int left = Math.Clamp(room.Center.X - 1, room.Left + 2, room.Right - 3);
            int top = room.Bottom - 2;
            ItemTypeId primary = LockedChestMainLoot[placed % LockedChestMainLoot.Length];
            WorldGenerationChestItem[] loot =
            [
                new WorldGenerationChestItem(1, primary)
            ];
            if (!TryPlaceDungeonChest(
                    workspace,
                    chests,
                    left,
                    top,
                    style: 2,
                    $"Locked Dungeon Cache {placed + 1}",
                    loot))
            {
                continue;
            }
            placed++;
        }

        if (placed != target)
            throw new InvalidOperationException($"Optimized dungeon v2 placed only {placed}/{target} locked dungeon chests.");
        return placed;
    }

    private static int PlaceDartTraps(
        IWorldGenerationWorkspace workspace,
        IReadOnlyList<DungeonRoom> branchRooms,
        int target)
    {
        int placed = 0;
        for (int i = 0; i < branchRooms.Count && placed < target; i++)
        {
            DungeonRoom room = branchRooms[i];
            bool trapOnLeft = (i & 1) == 0;
            int plateX = room.Center.X;
            int plateY = room.Bottom - 1;
            int trapX = trapOnLeft ? room.Left : room.Right;
            int trapY = Math.Clamp(plateY - 1, room.Top + 1, room.Bottom - 2);

            if (!IsAir(workspace, plateX, plateY))
                continue;
            if (!workspace.TryGetTile(trapX, trapY, out _))
                continue;

            // TerrariaServer 1.4.5.8 WorldGen.placeTrap(type 0): wall-backed pressure plate style 2,
            // dart trap tile 137, frameX +18 when the trap is on the left, and a red-wire path between them.
            SetObject(workspace, plateX, plateY, PressurePlate, BlueDungeonUnsafeWall, frameX: 36, frameY: 0);
            SetObject(
                workspace,
                trapX,
                trapY,
                DartTrap,
                BlueDungeonUnsafeWall,
                frameX: trapOnLeft ? (short)18 : (short)0,
                frameY: 0);
            WireManhattan(workspace, plateX, plateY, trapX, trapY);
            placed++;
        }

        if (placed != target)
            throw new InvalidOperationException($"Optimized dungeon v2 wired only {placed}/{target} dart traps.");
        return placed;
    }

    private static int PlaceSpikes(
        IWorldGenerationWorkspace workspace,
        IReadOnlyList<DungeonRoom> mainRooms,
        int target)
    {
        int placed = 0;
        for (int roomIndex = 0; roomIndex < mainRooms.Count && placed < target; roomIndex++)
        {
            DungeonRoom room = mainRooms[roomIndex];
            int y = room.Bottom - 1;
            for (int x = room.Left + 2; x <= room.Right - 2 && placed < target; x++)
            {
                if (Math.Abs(x - room.Center.X) <= 2)
                    continue;
                if (!IsAir(workspace, x, y))
                    continue;
                SetObject(workspace, x, y, Spike, BlueDungeonUnsafeWall, 0, 0);
                placed++;
            }
        }

        if (placed != target)
            throw new InvalidOperationException($"Optimized dungeon v2 placed only {placed}/{target} spike tiles.");
        return placed;
    }

    private static OptimizedDungeonV2Report ValidateGeneratedDungeon(
        RuntimeWorldGenerationWorkspace workspace,
        DungeonBounds bounds,
        WorldGenerationPoint dungeonAnchor,
        int mainRooms,
        int branchRooms,
        int lockedTarget,
        int lockedPlaced,
        int trapTarget,
        int trapsPlaced,
        int spikeTarget,
        int spikesPlaced,
        int connectedInterior)
    {
        if (mainRooms < 4 || branchRooms < 2 || lockedPlaced != lockedTarget || trapsPlaced != trapTarget || spikesPlaced != spikeTarget)
            throw new InvalidOperationException("Optimized dungeon v2 feature budgets were not satisfied.");
        if (connectedInterior < Math.Max(80, (mainRooms + branchRooms) * 24))
        {
            throw new InvalidOperationException(
                $"Optimized dungeon v2 connected interior is too small ({connectedInterior} cells).");
        }

        int lockedChestCount = 0;
        int keyCount = 0;
        int sourceLootRoles = 0;
        foreach (WorldChest chest in workspace.CaptureGeneratedChests())
        {
            if (chest.Name == "Dungeon Key Cache")
            {
                keyCount += chest.Items
                    .Where(static item => !item.IsEmpty && item.ItemType == 327)
                    .Sum(static item => item.Stack);
                continue;
            }
            if (!chest.Name.StartsWith("Locked Dungeon Cache ", StringComparison.Ordinal))
                continue;

            lockedChestCount++;
            if (chest.Items.Any(item =>
                    !item.IsEmpty && LockedChestMainLoot.Any(role => role.Value == item.ItemType)))
            {
                sourceLootRoles++;
            }

            if (!workspace.TryGetTile(chest.X, chest.Y, out WorldGenerationTile anchor) ||
                anchor.Type != Containers || anchor.FrameX != 72 || anchor.FrameY != 0)
            {
                throw new InvalidOperationException(
                    $"Optimized dungeon v2 chest at ({chest.X},{chest.Y}) is not a locked style-2 Gold Chest anchor.");
            }
            if (!bounds.Contains(chest.X, chest.Y))
                throw new InvalidOperationException("Optimized dungeon v2 emitted a dungeon chest outside its recovered reservation.");
        }

        if (lockedChestCount != lockedTarget || keyCount < lockedChestCount || sourceLootRoles != lockedChestCount)
        {
            throw new InvalidOperationException(
                $"Optimized dungeon v2 key/loot validation failed: locked={lockedChestCount}/{lockedTarget}, " +
                $"keys={keyCount}, source-loot={sourceLootRoles}.");
        }

        if (!HasOpenEntrance(workspace, bounds, dungeonAnchor))
            throw new InvalidOperationException("Optimized dungeon v2 validation found no readable dungeon entrance.");
        if (CountActiveType(workspace, bounds, PressurePlate) < trapTarget ||
            CountActiveType(workspace, bounds, DartTrap) < trapTarget ||
            CountActiveType(workspace, bounds, Spike) < spikeTarget)
        {
            throw new InvalidOperationException("Optimized dungeon v2 validation lost generated trap/spike content.");
        }

        return new OptimizedDungeonV2Report(
            mainRooms,
            branchRooms,
            lockedChestCount,
            keyCount,
            trapsPlaced,
            spikesPlaced,
            connectedInterior,
            bounds.Left,
            bounds.Top,
            bounds.Right,
            bounds.Bottom);
    }

    private static bool TryPlaceDungeonChest(
        IWorldGenerationWorkspace workspace,
        IWorldGenerationChestWorkspace chests,
        int left,
        int top,
        int style,
        string name,
        WorldGenerationChestItem[] loot)
    {
        if (left < 1 || top < 1 || left + 1 >= workspace.WidthTiles - 1 || top + 2 >= workspace.HeightTiles - 1)
            return false;
        for (int dx = 0; dx < 2; dx++)
        for (int dy = 0; dy < 2; dy++)
        {
            if (!IsAir(workspace, left + dx, top + dy))
                return false;
        }
        for (int dx = 0; dx < 2; dx++)
        {
            if (!workspace.TryGetTile(left + dx, top + 2, out WorldGenerationTile floor) ||
                (floor.Flags & WorldGenerationTileFlags.Active) == 0)
                return false;
        }

        WorldGenerationTile a = Read(workspace, left, top);
        WorldGenerationTile b = Read(workspace, left + 1, top);
        WorldGenerationTile c = Read(workspace, left, top + 1);
        WorldGenerationTile d = Read(workspace, left + 1, top + 1);
        int baseFrameX = checked(style * 36);
        SetObject(workspace, left, top, Containers, BlueDungeonUnsafeWall, checked((short)baseFrameX), 0);
        SetObject(workspace, left + 1, top, Containers, BlueDungeonUnsafeWall, checked((short)(baseFrameX + 18)), 0);
        SetObject(workspace, left, top + 1, Containers, BlueDungeonUnsafeWall, checked((short)baseFrameX), 18);
        SetObject(workspace, left + 1, top + 1, Containers, BlueDungeonUnsafeWall, checked((short)(baseFrameX + 18)), 18);
        if (chests.TryAddChest(left, top, name, loot))
            return true;

        Write(workspace, left, top, in a);
        Write(workspace, left + 1, top, in b);
        Write(workspace, left, top + 1, in c);
        Write(workspace, left + 1, top + 1, in d);
        return false;
    }

    private static void CarveRoom(IWorldGenerationWorkspace workspace, DungeonRoom room)
    {
        for (int y = room.Top + 1; y < room.Bottom; y++)
        for (int x = room.Left + 1; x < room.Right; x++)
            SetAir(workspace, x, y, BlueDungeonUnsafeWall);
    }

    private static void CarveDoglegCorridor(
        IWorldGenerationWorkspace workspace,
        WorldGenerationPoint from,
        WorldGenerationPoint to,
        int corridorRadius)
    {
        int bendY = from.Y + (to.Y - from.Y) / 2;
        CarveVertical(workspace, from.X, from.Y, bendY, corridorRadius);
        CarveHorizontal(workspace, from.X, to.X, bendY, corridorRadius);
        CarveVertical(workspace, to.X, bendY, to.Y, corridorRadius);
    }

    private static void CarveHorizontalCorridor(
        IWorldGenerationWorkspace workspace,
        WorldGenerationPoint from,
        WorldGenerationPoint to,
        int corridorRadius)
    {
        CarveHorizontal(workspace, from.X, to.X, from.Y, corridorRadius);
        CarveVertical(workspace, to.X, from.Y, to.Y, corridorRadius);
    }

    private static void CarveHorizontal(IWorldGenerationWorkspace workspace, int fromX, int toX, int y, int radius)
    {
        int left = Math.Min(fromX, toX);
        int right = Math.Max(fromX, toX);
        for (int x = left; x <= right; x++)
        for (int dy = -radius; dy <= radius; dy++)
            SetAir(workspace, x, y + dy, BlueDungeonUnsafeWall);
    }

    private static void CarveVertical(IWorldGenerationWorkspace workspace, int x, int fromY, int toY, int radius)
    {
        int top = Math.Min(fromY, toY);
        int bottom = Math.Max(fromY, toY);
        for (int y = top; y <= bottom; y++)
        for (int dx = -radius; dx <= radius; dx++)
            SetAir(workspace, x + dx, y, BlueDungeonUnsafeWall);
    }

    private static void WireManhattan(IWorldGenerationWorkspace workspace, int fromX, int fromY, int toX, int toY)
    {
        int x = fromX;
        int y = fromY;
        SetRedWire(workspace, x, y);
        while (x != toX || y != toY)
        {
            if (x < toX)
                x++;
            else if (x > toX)
                x--;
            SetRedWire(workspace, x, y);
            if (y < toY)
                y++;
            else if (y > toY)
                y--;
            SetRedWire(workspace, x, y);
        }
    }

    private static void SetRedWire(IWorldGenerationWorkspace workspace, int x, int y)
    {
        WorldGenerationTile tile = Read(workspace, x, y);
        Write(workspace, x, y, tile with { Flags = tile.Flags | WorldGenerationTileFlags.WireRed });
    }

    private static int MeasureConnectedInterior(
        IWorldGenerationWorkspace workspace,
        DungeonBounds bounds,
        WorldGenerationPoint start)
    {
        int width = bounds.Width;
        int height = bounds.Height;
        bool[] visited = new bool[checked(width * height)];
        var queue = new Queue<int>();
        int sx = start.X - bounds.Left;
        int sy = start.Y - bounds.Top;
        if ((uint)sx >= (uint)width || (uint)sy >= (uint)height || !IsDungeonInterior(workspace, start.X, start.Y))
            return 0;

        int startIndex = sy * width + sx;
        visited[startIndex] = true;
        queue.Enqueue(startIndex);
        int count = 0;
        while (queue.TryDequeue(out int node))
        {
            count++;
            int localX = node % width;
            int localY = node / width;
            Visit(localX - 1, localY);
            Visit(localX + 1, localY);
            Visit(localX, localY - 1);
            Visit(localX, localY + 1);

            void Visit(int nx, int ny)
            {
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    return;
                int index = ny * width + nx;
                if (visited[index])
                    return;
                int x = bounds.Left + nx;
                int y = bounds.Top + ny;
                if (!IsDungeonInterior(workspace, x, y))
                    return;
                visited[index] = true;
                queue.Enqueue(index);
            }
        }
        return count;
    }

    private static bool IsDungeonInterior(IWorldGenerationWorkspace workspace, int x, int y) =>
        workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
        tile.Wall == BlueDungeonUnsafeWall &&
        (tile.Flags & WorldGenerationTileFlags.Active) == 0;

    private static bool HasOpenEntrance(
        IWorldGenerationWorkspace workspace,
        DungeonBounds bounds,
        WorldGenerationPoint anchor)
    {
        int centerX = Math.Clamp(anchor.X, bounds.Left + 2, bounds.Right - 2);
        for (int y = Math.Max(1, anchor.Y); y <= Math.Min(bounds.Bottom, bounds.Top + 14); y++)
        {
            int open = 0;
            for (int x = centerX - 2; x <= centerX + 2; x++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) == 0 && tile.LiquidAmount == 0)
                    open++;
            }
            if (open < 3)
                return false;
        }
        return true;
    }

    private static int CountActiveType(IWorldGenerationWorkspace workspace, DungeonBounds bounds, ushort type)
    {
        int count = 0;
        for (int y = bounds.Top; y <= bounds.Bottom; y++)
        for (int x = bounds.Left; x <= bounds.Right; x++)
        {
            if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                (tile.Flags & WorldGenerationTileFlags.Active) != 0 && tile.Type == type)
                count++;
        }
        return count;
    }

    private static bool IsAir(IWorldGenerationWorkspace workspace, int x, int y) =>
        workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
        (tile.Flags & WorldGenerationTileFlags.Active) == 0 && tile.LiquidAmount == 0;

    private static WorldGenerationTile Read(IWorldGenerationWorkspace workspace, int x, int y)
    {
        if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile))
            throw new InvalidOperationException($"Optimized dungeon v2 could not read ({x},{y}).");
        return tile;
    }

    private static void SetSolid(IWorldGenerationWorkspace workspace, int x, int y, ushort type, ushort wall) =>
        Write(
            workspace,
            x,
            y,
            new WorldGenerationTile(
                type,
                wall,
                0,
                0,
                WorldGenerationTileFlags.Active,
                0,
                0,
                0,
                0,
                WorldGenerationLiquidKind.Water));

    private static void SetAir(IWorldGenerationWorkspace workspace, int x, int y, ushort wall) =>
        Write(
            workspace,
            x,
            y,
            new WorldGenerationTile(
                Air,
                wall,
                0,
                0,
                WorldGenerationTileFlags.None,
                0,
                0,
                0,
                0,
                WorldGenerationLiquidKind.Water));

    private static void SetObject(
        IWorldGenerationWorkspace workspace,
        int x,
        int y,
        ushort type,
        ushort wall,
        short frameX,
        short frameY) =>
        Write(
            workspace,
            x,
            y,
            new WorldGenerationTile(
                type,
                wall,
                frameX,
                frameY,
                WorldGenerationTileFlags.Active,
                0,
                0,
                0,
                0,
                WorldGenerationLiquidKind.Water));

    private static void Write(IWorldGenerationWorkspace workspace, int x, int y, in WorldGenerationTile tile)
    {
        if (!workspace.TrySetTile(x, y, in tile))
            throw new InvalidOperationException($"Optimized dungeon v2 could not write ({x},{y}).");
    }

    private static int NextRange(IWorldGenerationRandom random, int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            return minInclusive;
        return minInclusive + random.NextInt32(maxExclusive - minInclusive);
    }

    private readonly record struct DungeonBounds(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left + 1;
        public int Height => Bottom - Top + 1;
        public bool Contains(int x, int y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
    }

    private readonly record struct DungeonRoom(int Left, int Top, int Right, int Bottom)
    {
        public WorldGenerationPoint Center => new(Left + (Right - Left) / 2, Top + (Bottom - Top) / 2);
    }
}

internal readonly record struct OptimizedDungeonV2Report(
    int MainRooms,
    int BranchRooms,
    int LockedChests,
    int GoldenKeys,
    int DartTraps,
    int SpikeTiles,
    int ConnectedInteriorCells,
    int Left,
    int Top,
    int Right,
    int Bottom);