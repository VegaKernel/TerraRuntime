using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

/// <summary>
/// Runtime-owned deterministic Skyblock profile. The world starts as void, then receives spatially separated floating
/// biome islands, guaranteed progression-liquid reservoirs, a deliberately lowered dungeon island, persistent loot
/// chests and semantic spawn/layer metadata.
/// </summary>
public sealed class SkyblockWorldGenerationProvider : IWorldGenerationProvider
{
    public static readonly WorldGeneratorId GeneratorId = new("terraruntime:skyblock");

    private static readonly WorldGenerationPassId LayoutId = new("terraruntime:skyblock/layout");
    private static readonly WorldGenerationPassId IslandsId = new("terraruntime:skyblock/islands");
    private static readonly WorldGenerationPassId ResourcesId = new("terraruntime:skyblock/resources");
    private static readonly WorldGenerationPassId DungeonId = new("terraruntime:skyblock/dungeon");
    private static readonly WorldGenerationPassId ChestsId = new("terraruntime:skyblock/chests");
    private static readonly WorldGenerationPassId MetadataId = new("terraruntime:skyblock/metadata");

    public WorldGeneratorId Id => GeneratorId;

    public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        request.Validate();
        if (request.WidthTiles < 256 || request.HeightTiles < 160)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Skyblock generation requires at least a 256x160 candidate workspace.");
        }

        var state = new GenerationState();
        Add(builder, LayoutId, new LayoutPass(state));
        Add(builder, IslandsId, new IslandsPass(state), LayoutId);
        Add(builder, ResourcesId, new ResourcePass(state), IslandsId);
        Add(builder, DungeonId, new DungeonPass(state), ResourcesId);
        Add(builder, ChestsId, new ChestPass(state), DungeonId);
        Add(builder, MetadataId, new MetadataPass(state), ChestsId);
    }

    private static void Add(
        IWorldGenerationPlanBuilder builder,
        WorldGenerationPassId id,
        IWorldGenerationPass pass,
        params WorldGenerationPassId[] requiredAfter) =>
        builder.Add(
            new WorldGenerationPassDescriptor(
                id,
                WorldGenerationRngMode.IsolatedDeterministic,
                requiredAfter.Length == 0 ? null : requiredAfter),
            pass);

    private sealed class GenerationState
    {
        public List<IslandSpec> Islands { get; } = [];
        public IslandSpec SpawnIsland { get; set; }
        public IslandSpec AetherIsland { get; set; }
        public IslandSpec WaterIsland { get; set; }
        public IslandSpec LavaIsland { get; set; }
        public IslandSpec HoneyIsland { get; set; }
        public int DungeonCenterX { get; set; }
        public int DungeonSurfaceY { get; set; }
        public bool LayoutReady { get; set; }
    }

    private enum IslandKind : byte
    {
        Starter = 0,
        Forest = 1,
        Desert = 2,
        Snow = 3,
        Jungle = 4,
        Evil = 5,
        Cavern = 6,
        Aether = 7
    }

    private readonly record struct IslandSpec(
        int CenterX,
        int SurfaceY,
        int RadiusX,
        int Depth,
        bool HasChest,
        bool IsSpawn,
        IslandKind Kind);

    private sealed class LayoutPass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            state.Islands.Clear();
            state.LayoutReady = false;

            int width = context.Workspace.WidthTiles;
            int height = context.Workspace.HeightTiles;
            int spawnSurface = Math.Clamp((int)Math.Round(height * 0.28d), 28, height - 72);
            int spawnRadius = Math.Clamp(width / 100, 22, 38);
            int spawnDepth = Math.Clamp(height / 55, 10, 20);
            var spawn = new IslandSpec(
                CenterX: width / 2,
                SurfaceY: spawnSurface,
                RadiusX: spawnRadius,
                Depth: spawnDepth,
                HasChest: true,
                IsSpawn: true,
                Kind: IslandKind.Starter);
            state.SpawnIsland = spawn;
            state.Islands.Add(spawn);

            bool compactLayout = width < 512 || height < 220;
            int randomIslandCount = compactLayout ? 0 : Math.Clamp(width / 70, 12, 120);
            int regularMinY = Math.Clamp((int)Math.Round(height * 0.14d), 18, height - 90);
            int regularMaxY = Math.Clamp((int)Math.Round(height * 0.56d), regularMinY + 20, height - 64);
            int deepMinY = Math.Clamp((int)Math.Round(height * 0.66d), regularMaxY + 12, height - 48);
            int deepMaxY = Math.Clamp((int)Math.Round(height * 0.86d), deepMinY + 8, height - 24);
            int xMargin = Math.Clamp(width / 40, 28, 120);

            int dungeonMargin = Math.Clamp(width / 9, 64, width / 3);
            bool dungeonOnLeft = context.Random.NextInt32(2) == 0;
            state.DungeonCenterX = dungeonOnLeft
                ? dungeonMargin
                : width - dungeonMargin;
            state.DungeonSurfaceY = Math.Clamp((int)Math.Round(height * 0.72d), deepMinY, height - 40);
            var dungeonReserve = new IslandSpec(
                state.DungeonCenterX,
                state.DungeonSurfaceY,
                Math.Clamp(width / 80, 32, 54),
                Math.Clamp(height / 38, 18, 32),
                HasChest: false,
                IsSpawn: false,
                Kind: IslandKind.Cavern);

            int nearX = dungeonOnLeft ? width / 4 : width * 3 / 4;
            int farX = dungeonOnLeft ? width * 3 / 4 : width / 4;
            int resourceRadius = Math.Clamp(width / 120, 10, 18);
            int resourceDepth = Math.Clamp(height / 80, 7, 12);

            state.WaterIsland = AddReservedIsland(
                state, dungeonReserve,
                new IslandSpec(nearX, Math.Clamp((int)Math.Round(height * 0.20d), 24, height - 80), resourceRadius, resourceDepth, false, false, IslandKind.Snow),
                "Water");
            state.HoneyIsland = AddReservedIsland(
                state, dungeonReserve,
                new IslandSpec(nearX, Math.Clamp((int)Math.Round(height * 0.43d), 48, height - 64), resourceRadius, resourceDepth, false, false, IslandKind.Jungle),
                "Honey");
            state.AetherIsland = AddReservedIsland(
                state, dungeonReserve,
                new IslandSpec(farX, Math.Clamp((int)Math.Round(height * 0.42d), 44, height - 64), resourceRadius, resourceDepth, false, false, IslandKind.Aether),
                "Aether");
            state.LavaIsland = AddReservedIsland(
                state, dungeonReserve,
                new IslandSpec(width / 2, Math.Clamp((int)Math.Round(height * 0.84d), deepMinY + 8, height - 20), resourceRadius, resourceDepth, false, false, IslandKind.Cavern),
                "Lava");
            if (compactLayout)
            {
                int compactRadius = Math.Max(8, resourceRadius - 1);
                AddReservedIsland(
                    state, dungeonReserve,
                    new IslandSpec(farX, Math.Clamp((int)Math.Round(height * 0.18d), 22, height - 100), compactRadius, resourceDepth, true, false, IslandKind.Desert),
                    "compact Desert");
                AddReservedIsland(
                    state, dungeonReserve,
                    new IslandSpec(farX, Math.Clamp((int)Math.Round(height * 0.66d), 84, height - 42), compactRadius, resourceDepth, false, false, IslandKind.Evil),
                    "compact Evil");
            }

            int accepted = 0;
            int maxAttempts = randomIslandCount * 120;

            for (int attempt = 0; attempt < maxAttempts && accepted < randomIslandCount; attempt++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                bool deep = accepted > 0 && accepted % 6 == 5;
                IslandKind kind = deep ? IslandKind.Cavern : ChooseSurfaceKind(accepted);
                int radius = NextRange(context.Random, 10, Math.Clamp(width / 55, 16, 30) + 1);
                int depth = NextRange(context.Random, 7, Math.Clamp(height / 60, 10, 18) + 1);
                int x = NextRange(context.Random, xMargin, width - xMargin);
                int y = deep
                    ? NextRange(context.Random, deepMinY, deepMaxY + 1)
                    : NextRange(context.Random, regularMinY, regularMaxY + 1);

                var candidate = new IslandSpec(
                    CenterX: x,
                    SurfaceY: y,
                    RadiusX: radius,
                    Depth: depth,
                    HasChest: accepted % 4 == 1,
                    IsSpawn: false,
                    Kind: kind);

                if (OverlapsExisting(candidate, state.Islands) || Overlaps(candidate, dungeonReserve))
                    continue;

                state.Islands.Add(candidate);
                accepted++;
                if ((accepted & 7) == 0 || accepted == randomIslandCount)
                {
                    context.ReportProgress(
                        Math.Min(0.95d, accepted / (double)randomIslandCount),
                        "Planning biome and progression-resource islands");
                }
            }

            if (accepted < randomIslandCount)
            {
                throw new InvalidOperationException(
                    $"Skyblock layout placed only {accepted} of {randomIslandCount} requested random islands.");
            }
            state.LayoutReady = true;
            context.ReportProgress(1d, $"Planned {state.Islands.Count} floating biome and resource islands");
        }
    }

    private sealed class IslandsPass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            RequireLayout(state);
            int count = state.Islands.Count;
            for (int index = 0; index < count; index++)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                PlaceIsland(context.Workspace, state.Islands[index], context.Request.Options.Evil);
                context.ReportProgress(
                    (index + 1d) / count,
                    index == 0 ? "Building spawn island" : "Building biome and resource islands");
            }
        }
    }

    private sealed class ResourcePass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            RequireLayout(state);
            PlaceLiquidBasin(context.Workspace, state.AetherIsland, WorldGenerationLiquidKind.Shimmer, halfWidth: 4, depth: 3);
            PlaceLiquidBasin(context.Workspace, state.WaterIsland, WorldGenerationLiquidKind.Water, halfWidth: 4, depth: 3);
            PlaceLiquidBasin(context.Workspace, state.HoneyIsland, WorldGenerationLiquidKind.Honey, halfWidth: 3, depth: 2);
            PlaceLiquidBasin(context.Workspace, state.LavaIsland, WorldGenerationLiquidKind.Lava, halfWidth: 3, depth: 2);
            context.ReportProgress(1d, "Building guaranteed Water, Lava, Honey and Shimmer reservoirs");
        }
    }

    private sealed class DungeonPass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            RequireLayout(state);

            int radius = Math.Clamp(context.Workspace.WidthTiles / 80, 32, 54);
            int depth = Math.Clamp(context.Workspace.HeightTiles / 38, 18, 32);
            var dungeonIsland = new IslandSpec(
                state.DungeonCenterX,
                state.DungeonSurfaceY,
                radius,
                depth,
                HasChest: false,
                IsSpawn: false,
                Kind: IslandKind.Cavern);
            PlaceIsland(context.Workspace, dungeonIsland, context.Request.Options.Evil);

            int roomHalfWidth = Math.Min(14, radius - 5);
            int roomHeight = Math.Min(12, Math.Max(8, depth / 2));
            int left = state.DungeonCenterX - roomHalfWidth;
            int right = state.DungeonCenterX + roomHalfWidth;
            int top = state.DungeonSurfaceY - roomHeight;
            int bottom = state.DungeonSurfaceY - 1;
            ushort stone = checked((ushort)VanillaTileIds.Stone.Value);
            ushort dungeonWall = checked((ushort)VanillaWallIds.BlueDungeonUnsafe.Value);

            for (int x = left; x <= right; x++)
            {
                for (int y = top; y <= bottom; y++)
                {
                    bool solid = x == left || x == right || y == top;
                    SetTile(
                        context.Workspace,
                        x,
                        y,
                        type: solid ? stone : (ushort)0,
                        wall: dungeonWall,
                        flags: solid ? WorldGenerationTileFlags.Active : WorldGenerationTileFlags.None);
                }
            }

            context.ReportProgress(1d, "Building lowered dungeon island");
        }
    }

    private sealed class ChestPass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            RequireLayout(state);
            if (context.Workspace is not IWorldGenerationChestWorkspace chests)
            {
                throw new InvalidOperationException(
                    "Skyblock generation requires a workspace that supports persistent generated chests.");
            }

            int placed = 0;
            for (int index = 0; index < state.Islands.Count; index++)
            {
                IslandSpec island = state.Islands[index];
                if (!island.HasChest)
                    continue;

                int chestX = island.IsSpawn
                    ? island.CenterX + Math.Min(8, island.RadiusX - 4) - 1
                    : island.CenterX - 1;
                int chestY = island.SurfaceY - 2;
                PlaceChestTiles(context.Workspace, chestX, chestY);
                WorldGenerationChestItem[] loot = island.IsSpawn
                    ? BuildStarterLoot()
                    : BuildTreasureLoot(context.Random, placed);
                string name = island.IsSpawn ? "Skyblock Starter" : $"{island.Kind} Cache";
                if (!chests.TryAddChest(chestX, chestY, name, loot))
                {
                    throw new InvalidOperationException(
                        $"Skyblock generator could not register chest at ({chestX}, {chestY}).");
                }
                placed++;
            }

            int dungeonChestX = state.DungeonCenterX - 1;
            int dungeonChestY = state.DungeonSurfaceY - 2;
            PlaceChestTiles(context.Workspace, dungeonChestX, dungeonChestY);
            WorldGenerationChestItem[] dungeonLoot =
            [
                new(1, VanillaItemIds.SlimeStaff),
                new(99, VanillaItemIds.Gel),
                new(100, VanillaItemIds.DirtBlock)
            ];
            if (!chests.TryAddChest(dungeonChestX, dungeonChestY, "Dungeon Cache", dungeonLoot))
            {
                throw new InvalidOperationException(
                    $"Skyblock generator could not register dungeon chest at ({dungeonChestX}, {dungeonChestY}).");
            }

            placed++;
            context.ReportProgress(1d, $"Placed {placed} persistent loot chests");
        }
    }

    private sealed class MetadataPass(GenerationState state) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            RequireLayout(state);
            IWorldGenerationMetadataWorkspace metadata = context.Metadata ??
                throw new InvalidOperationException("Skyblock generation requires semantic world metadata.");

            int height = context.Workspace.HeightTiles;
            double worldSurface = Math.Clamp(Math.Round(height * 0.62d), 2d, height - 3d);
            double rockLayer = Math.Clamp(Math.Round(height * 0.80d), worldSurface + 1d, height - 2d);
            int spawnY = Math.Max(1, state.SpawnIsland.SurfaceY - 1);
            int dungeonY = Math.Max(1, state.DungeonSurfaceY - 1);

            if (!metadata.TrySetSpawn(state.SpawnIsland.CenterX, spawnY))
                throw new InvalidOperationException("Skyblock generator could not set spawn on the starter island.");
            if (!metadata.TrySetDungeon(state.DungeonCenterX, dungeonY))
                throw new InvalidOperationException("Skyblock generator could not set the lowered dungeon anchor.");
            if (!metadata.TrySetLayers(worldSurface, rockLayer))
                throw new InvalidOperationException("Skyblock generator could not set lowered underground/cavern layers.");

            context.ReportProgress(1d, "Finalizing Skyblock spawn, dungeon and vertical layers");
        }
    }

    private static IslandKind ChooseSurfaceKind(int ordinal) =>
        (ordinal % 5) switch
        {
            0 => IslandKind.Forest,
            1 => IslandKind.Desert,
            2 => IslandKind.Snow,
            3 => IslandKind.Jungle,
            _ => IslandKind.Evil
        };

    private static IslandSpec AddReservedIsland(
        GenerationState state,
        IslandSpec dungeonReserve,
        IslandSpec island,
        string role)
    {
        if (OverlapsExisting(island, state.Islands) || Overlaps(island, dungeonReserve))
        {
            throw new InvalidOperationException(
                $"Skyblock layout could not reserve the {role} progression island without an overlap.");
        }

        state.Islands.Add(island);
        return island;
    }

    private static bool OverlapsExisting(IslandSpec candidate, List<IslandSpec> existing)
    {
        foreach (IslandSpec other in existing)
        {
            if (Overlaps(candidate, other))
                return true;
        }

        return false;
    }

    private static bool Overlaps(IslandSpec candidate, IslandSpec other)
    {
        int horizontalClearance = candidate.RadiusX + other.RadiusX + 14;
        int verticalClearance = Math.Max(candidate.Depth, other.Depth) + 22;
        return Math.Abs(candidate.CenterX - other.CenterX) < horizontalClearance &&
               Math.Abs(candidate.SurfaceY - other.SurfaceY) < verticalClearance;
    }

    private static void PlaceIsland(
        IWorldGenerationWorkspace workspace,
        IslandSpec island,
        WorldGenerationEvil evil)
    {
        (ushort topType, ushort bodyType, int surfaceDepth) = ResolveIslandPalette(island.Kind, evil);
        int left = Math.Max(1, island.CenterX - island.RadiusX);
        int right = Math.Min(workspace.WidthTiles - 2, island.CenterX + island.RadiusX);

        for (int x = left; x <= right; x++)
        {
            double normalized = (x - island.CenterX) / (double)island.RadiusX;
            double arch = Math.Sqrt(Math.Max(0d, 1d - normalized * normalized));
            int columnDepth = Math.Max(2, (int)Math.Round(island.Depth * arch));
            int bottom = Math.Min(workspace.HeightTiles - 2, island.SurfaceY + columnDepth - 1);

            for (int y = island.SurfaceY; y <= bottom; y++)
            {
                ushort type = y < island.SurfaceY + surfaceDepth ? topType : bodyType;
                SetTile(workspace, x, y, type, wall: 0, WorldGenerationTileFlags.Active);
            }
        }
    }

    private static void PlaceLiquidBasin(
        IWorldGenerationWorkspace workspace,
        IslandSpec island,
        WorldGenerationLiquidKind kind,
        int halfWidth,
        int depth)
    {
        int basinHalfWidth = Math.Clamp(halfWidth, 2, Math.Max(2, island.RadiusX - 3));
        int basinDepth = Math.Clamp(depth, 1, Math.Max(1, island.Depth - 2));
        int left = island.CenterX - basinHalfWidth;
        int right = island.CenterX + basinHalfWidth;
        int top = island.SurfaceY;
        int bottom = top + basinDepth - 1;

        for (int x = left; x <= right; x++)
        {
            for (int y = top; y <= bottom; y++)
            {
                SetTile(
                    workspace,
                    x,
                    y,
                    type: 0,
                    wall: 0,
                    flags: WorldGenerationTileFlags.None,
                    liquidAmount: byte.MaxValue,
                    liquidKind: kind);
            }
        }
    }

    private static (ushort TopType, ushort BodyType, int SurfaceDepth) ResolveIslandPalette(
        IslandKind kind,
        WorldGenerationEvil evil)
    {
        ushort dirt = checked((ushort)VanillaTileIds.Dirt.Value);
        ushort stone = checked((ushort)VanillaTileIds.Stone.Value);
        return kind switch
        {
            IslandKind.Starter or IslandKind.Forest => (dirt, stone, 2),
            IslandKind.Desert => (
                checked((ushort)VanillaTileIds.Sand.Value),
                checked((ushort)VanillaTileIds.Sand.Value),
                1),
            IslandKind.Snow => (
                checked((ushort)VanillaTileIds.SnowBlock.Value),
                checked((ushort)VanillaTileIds.IceBlock.Value),
                2),
            IslandKind.Jungle => (
                checked((ushort)VanillaTileIds.JungleGrass.Value),
                checked((ushort)VanillaTileIds.Mud.Value),
                1),
            IslandKind.Evil when evil == WorldGenerationEvil.Crimson => (
                checked((ushort)VanillaTileIds.CrimsonGrass.Value),
                checked((ushort)VanillaTileIds.Crimstone.Value),
                1),
            IslandKind.Evil => (
                checked((ushort)VanillaTileIds.CorruptGrass.Value),
                checked((ushort)VanillaTileIds.Ebonstone.Value),
                1),
            IslandKind.Cavern or IslandKind.Aether => (stone, stone, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static void PlaceChestTiles(IWorldGenerationWorkspace workspace, int x, int y)
    {
        ushort container = checked((ushort)VanillaTileIds.Containers.Value);
        SetTile(workspace, x, y, container, wall: 0, WorldGenerationTileFlags.Active, frameX: 0, frameY: 0);
        SetTile(workspace, x + 1, y, container, wall: 0, WorldGenerationTileFlags.Active, frameX: 18, frameY: 0);
        SetTile(workspace, x, y + 1, container, wall: 0, WorldGenerationTileFlags.Active, frameX: 0, frameY: 18);
        SetTile(workspace, x + 1, y + 1, container, wall: 0, WorldGenerationTileFlags.Active, frameX: 18, frameY: 18);
    }

    private static WorldGenerationChestItem[] BuildStarterLoot() =>
    [
        new(1, VanillaItemIds.CopperPickaxe),
        new(100, VanillaItemIds.DirtBlock),
        new(50, VanillaItemIds.Gel)
    ];

    private static WorldGenerationChestItem[] BuildTreasureLoot(IWorldGenerationRandom random, int ordinal)
    {
        int dirt = 25 + random.NextInt32(101);
        int gel = 10 + random.NextInt32(51);
        if (ordinal > 0 && ordinal % 7 == 0)
        {
            return
            [
                new(1, VanillaItemIds.SlimeStaff),
                new(dirt, VanillaItemIds.DirtBlock),
                new(gel, VanillaItemIds.Gel)
            ];
        }

        return
        [
            new(dirt, VanillaItemIds.DirtBlock),
            new(gel, VanillaItemIds.Gel)
        ];
    }

    private static void SetTile(
        IWorldGenerationWorkspace workspace,
        int x,
        int y,
        ushort type,
        ushort wall,
        WorldGenerationTileFlags flags,
        short frameX = 0,
        short frameY = 0,
        byte liquidAmount = 0,
        WorldGenerationLiquidKind liquidKind = WorldGenerationLiquidKind.Water)
    {
        var tile = new WorldGenerationTile(
            Type: type,
            Wall: wall,
            FrameX: frameX,
            FrameY: frameY,
            Flags: flags,
            LiquidAmount: liquidAmount,
            TileColor: 0,
            WallColor: 0,
            Shape: 0,
            LiquidKind: liquidKind);
        if (!workspace.TrySetTile(x, y, in tile))
        {
            throw new InvalidOperationException(
                $"Skyblock generator could not write tile ({x}, {y}) inside a {workspace.WidthTiles}x{workspace.HeightTiles} workspace.");
        }
    }

    private static int NextRange(IWorldGenerationRandom random, int minimum, int exclusiveMaximum)
    {
        if (exclusiveMaximum <= minimum)
            return minimum;
        return minimum + random.NextInt32(exclusiveMaximum - minimum);
    }

    private static void RequireLayout(GenerationState state)
    {
        if (!state.LayoutReady || state.Islands.Count == 0)
            throw new InvalidOperationException("Skyblock layout pass did not complete before a dependent pass executed.");
    }
}
