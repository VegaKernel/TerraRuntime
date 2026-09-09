using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.HostContracts.WorldGeneration;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldGenerationFullIntegrationTests
{
    private static readonly WorldGeneratorId VanillaId = new("terraruntime:vanilla");

    [Theory]
    [InlineData(4200,1200)] [InlineData(6400,1800)] [InlineData(8400,2400)]
    public void Ordinary_crimson_runs_placement_and_downstream_passes_without_coordinate_fallback(int width, int height)
    {
        var request = new WorldGenerationRequest(VanillaId, "Crimson placement", 1458, width, height)
        {
            SeedText = "1458",
            Options = new(WorldGenerationGameMode.Classic, WorldGenerationEvil.Crimson)
        };
        var created = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance)
            .CreateCandidate(in request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(created.Succeeded, created.Finalization?.Validation?.Detail ?? created.Generation.Execution?.Error?.ToString());
        Assert.NotNull(created.Candidate);
        bool crimstone = false;
        int hearts = 0, caveWalls = 0, altars = 0;
        foreach (WorldTile tile in created.Candidate.TileStore.Tiles)
        {
            if (tile.IsActive && tile.Type == 203) crimstone = true;
            if (tile.Wall == 83) caveWalls++;
            if (tile.IsActive && tile.Type == 31 && tile.FrameX == 36 && tile.FrameY == 0) hearts++;
            if (tile.IsActive && tile.Type == 26 && tile.FrameX == 54 && tile.FrameY == 0) altars++;
        }
        Assert.True(crimstone); // Execution/validation acceptance, not geometric equality.
        Assert.True(hearts >= 5, "Source Crimson cave branches must leave real heart objects after finalization.");
        Assert.True(caveWalls > 0);
        Assert.True(altars > 0, "Crimson altar objects must survive the production pass and finalization order.");
    }

    [Theory]
    [InlineData(4200,1200)] [InlineData(6400,1800)] [InlineData(8400,2400)]
    public void Canonical_seed1458_world_survives_real_post_load_liquid_preparation(int width, int height)
    {
        var request = new WorldGenerationRequest(VanillaId, "Load acceptance", 1458, width, height) { SeedText = "1458" };
        var created = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance)
            .CreateCandidate(in request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(created.Succeeded, created.Finalization?.Validation?.Detail ?? created.Generation.Execution?.Error?.ToString());
        Assert.NotNull(created.Candidate);
        int dyePlants = 0;
        foreach (WorldTile tile in created.Candidate.TileStore.Tiles)
        {
            if (!tile.IsActive || tile.Type != 227) continue;
            dyePlants++;
            Assert.Equal(0, tile.FrameY);
            Assert.Equal(0, tile.FrameX % 34); // PlaceDye's independent source stride, not ordinary18.
        }
        Assert.True(dyePlants > 0);
        var header = VanillaFreshWorldHeader326.Create(request.WorldName, "1458", width, height, Guid.NewGuid(), 1458);
        var compose = WorldFileFreshComposer326.TryCompose(header, created.Metadata!, created.Candidate.TileStore,
            created.Candidate.CaptureGeneratedChests(), created.Candidate.CaptureGeneratedNpcs(),
            0, false, 0, 0, out byte[] file);
        Assert.True(compose.Succeeded, compose.ToString());
        var load = WorldFileLoader.TryLoad(file, ServerWorldLoadPolicy.CreateLimits(), out WorldFileData? world);
        Assert.True(load.IsLoaded, load.ToString());
        Assert.NotNull(world);
        var prepared = VanillaWorldLiquidLoadInitializer1458.TryPrepare(world);
        Assert.True(prepared.IsPrepared, prepared.ToString());
        Assert.True(world.Tiles.IsPostLoadLiquidPrepared);
    }

    [Theory]
    [InlineData(4200, 1200)]
    [InlineData(6400, 1800)]
    [InlineData(8400, 2400)]
    public void Canonical_world_retains_connected_rideable_rail_frames(int width, int height)
    {
        var request = new WorldGenerationRequest(VanillaId, "Rails", 42, width, height) { SeedText = "42" };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);
        var result = pipeline.CreateCandidate(in request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Finalization?.Validation?.Detail ?? result.Generation.Execution?.Error?.ToString());
        var tiles = result.Candidate!.TileStore;
        int count = 0;
        for (int y = 1; y < height - 1; y++)
        for (int x = 1; x < width - 1; x++)
        {
            WorldTile tile = tiles.Get(x, y);
            if (!tile.IsActive || tile.Type != 314) continue;
            count++;
            SourceBackedMicroBiomes1458Tests.AssertTrackConnections(tiles, x, y);
        }
        Assert.True(count > 0, "Micro Biomes must actually publish tracks, not merely reject all routes.");
    }

    [Theory]
    [InlineData(4200, 1200)]
    [InlineData(6400, 1800)]
    [InlineData(8400, 2400)]
    public void Canonical_underworld_retains_forts_connections_and_house_hellforges(int width, int height)
    {
        var request = new WorldGenerationRequest(VanillaId, "Hell settlement", 42, width, height) { SeedText = "42" };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);
        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(in request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Finalization?.Validation?.Detail ?? result.Generation.Execution?.Error?.ToString());
        WorldTileStore store = result.Candidate!.TileStore;
        int bricks = 0, walls = 0, doors = 0, platforms = 0, forges = 0, torches = 0, ashGrass = 0, ashTrees = 0, furniture = 0;
        int paintings = 0, banners = 0, chandeliers = 0, lanterns = 0;
        for (int y = height - 250; y < height; y++)
        for (int x = 5; x < width - 5; x++)
        {
            WorldTile tile = store.Get(x, y);
            if (tile.Wall is 13 or 14) walls++;
            if (!tile.IsActive) continue;
            if (tile.Type is 75 or 76) bricks++;
            if (tile.Type is 14 or 15 or 18 or 79 or 87 or 88 or 89 or 90 or 93 or 100 or 101 or 104 or 105) furniture++;
            if (tile.Type is 240 or 242 or 245 or 246) paintings++;
            if (tile.Type == 91 && tile.FrameY == 0 && tile.FrameX is >= 288 and <= 378) banners++;
            if (tile.Type == 34 && tile.FrameY == 1728 && tile.FrameX == 0) chandeliers++;
            if (tile.Type == 42 && tile.FrameY == 1152) lanterns++;
            if (tile.Type is 240 or 242 or 245 or 246 or 91 or 34 or 42)
                AssertHellDecorationFootprint(store, x, y, tile);
            if (tile.Type == 633) ashGrass++;
            if (tile.Type == 634 && tile.FrameX is 0 or 22 && tile.FrameY >= 198)
            {
                ashTrees++;
                Assert.True(UnderworldVegetation1458.IsEdgeForest(x, width));
            }
            if (tile.Type == 4 && tile.FrameY == 154)
            {
                torches++;
                Assert.Contains(tile.FrameX, new short[] { 0, 22, 44 });
                Assert.True(tile.Wall > 0);
            }
            if (tile.Type == 19 && tile.FrameY == 234) platforms++;
            if (tile.Type == 10 && tile.FrameY == 1026)
            {
                doors++;
                for (int dy = 0; dy < 3; dy++)
                {
                    WorldTile cell = store.Get(x, y + dy);
                    Assert.True(cell.IsActive); Assert.Equal(10, cell.Type); Assert.Equal(1026 + dy * 18, cell.FrameY);
                }
            }
            if (tile.Type != 77 || tile.FrameX != 0 || tile.FrameY != 0) continue;
            forges++;
            Assert.True(store.Get(x + 1, y + 1).Wall is 13 or 14, $"Hellforge outside a house at {x},{y}");
            for (int dx = 0; dx < 3; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                WorldTile cell = store.Get(x + dx, y + dy);
                Assert.True(cell.IsActive); Assert.Equal(77, cell.Type);
                Assert.Equal(dx * 18, cell.FrameX); Assert.Equal(dy * 18, cell.FrameY);
            }
        }
        Assert.True(bricks > 0 && walls > 0 && doors > 0 && platforms > 0,
            $"Incomplete Underworld settlement: bricks={bricks}, walls={walls}, doors={doors}, platforms={platforms}");
        Assert.InRange(forges, 1, width / 200);
        Assert.InRange(torches, 1, HellFortLighting1458.AttemptCount(width));
        Assert.True(ashGrass > 0 && ashTrees > 0, $"Missing edge forests: grass={ashGrass}, crowns={ashTrees}");
        Assert.True(furniture > 0, "Missing Underworld furniture");
        Assert.True(paintings > 0 && banners > 0 && chandeliers > 0 && lanterns > 0,
            $"Missing Underworld decorations: paintings={paintings}, banners={banners}, chandeliers={chandeliers}, lanterns={lanterns}");
        // Dungeon now also places real dressers. This assertion concerns HellFort-owned furniture.
        WorldChest[] dressers = result.Candidate.CaptureGeneratedChests()
            .Where(chest => store.Get(chest.X, chest.Y) is { Type: 88, Wall: 13 or 14 }).ToArray();
        Assert.NotEmpty(dressers);
        Assert.All(dressers, chest =>
        {
            Assert.True(GeneratedContainerFootprint.IsValid(store, chest.X, chest.Y));
            Assert.Equal(40, chest.Items.Length); Assert.All(chest.Items, item => Assert.True(item.IsEmpty));
            Assert.InRange(chest.Y, height - 250, height - 20);
        });
    }

    private static void AssertHellDecorationFootprint(WorldTileStore store, int x, int y, WorldTile tile)
    {
        (int width, int height) = tile.Type switch { 240 => (3, 3), 242 => (6, 4), 245 => (2, 3), 246 => (3, 2), 91 => (1, 3), 34 => (3, 3), _ => (1, 2) };
        int dx = tile.FrameX % (width * 18) / 18, dy = tile.FrameY % (height * 18) / 18;
        if (dx != 0 || dy != 0) return;
        for (int column = 0; column < width; column++)
        for (int row = 0; row < height; row++)
        {
            WorldTile cell = store.Get(x + column, y + row);
            Assert.True(cell.IsActive, $"Partial Hell decoration {tile.Type} at {x},{y}"); Assert.Equal(tile.Type, cell.Type);
            Assert.Equal(tile.FrameX + column * 18, cell.FrameX); Assert.Equal(tile.FrameY + row * 18, cell.FrameY);
            if (tile.Type is 240 or 242 or 245 or 246) Assert.NotEqual(0, cell.Wall);
        }
    }

    [Fact]
    public void Canonical_passes_preserve_registered_chest_anchors()
    {
        var request = new WorldGenerationRequest(VanillaId, "Chest anchors", 42, 4200, 1200) { SeedText = "42" };
        var workspace = new Workspace(request.WidthTiles, request.HeightTiles);
        WorldGenerationExecutionResult result = RuntimeWorldGenerationExecutor.Execute(
            new ChestCheckingProvider(), in request, workspace,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Error?.ToString());
    }

    private sealed class ChestCheckingProvider : IWorldGenerationProvider
    {
        public WorldGeneratorId Id => VanillaId;
        public void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder) =>
            new SourceBackedFinal1458().BuildPlan(in request, new ChestCheckingBuilder(builder));
    }

    private sealed class ChestCheckingBuilder(IWorldGenerationPlanBuilder inner) : IWorldGenerationPlanBuilder
    {
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            inner.Add(descriptor, new ChestCheckingPass(descriptor.Id, pass));
    }

    private sealed class ChestCheckingPass(WorldGenerationPassId id, IWorldGenerationPass inner) : IWorldGenerationPass
    {
        public void Execute(IWorldGenerationContext context)
        {
            inner.Execute(context);
            var workspace = Assert.IsType<Workspace>(context.Workspace);
            foreach (WorldChest chest in workspace.CaptureGeneratedChests())
            {
                ushort containerType = workspace.TileStore.Get(chest.X, chest.Y).Type;
                for (int dy = 0; dy < 2; dy++)
                for (int dx = 0; dx < (containerType == 88 ? 3 : 2); dx++)
                {
                    WorldTile tile = workspace.TileStore.Get(chest.X + dx, chest.Y + dy);
                    Assert.True(
                        tile.IsActive && containerType is 21 or 467 or 88 && tile.Type == containerType,
                        $"Pass {id} damaged chest ({chest.X},{chest.Y}) cell ({chest.X + dx},{chest.Y + dy}): " +
                        $"type={tile.Type}, active={tile.IsActive}.");
                }
            }
        }
    }


    [Fact]
    public void Built_in_source_resolves_vanilla_provider()
    {
        var source = BuiltInWorldGeneratorSource.Instance;
        IWorldGenerationProvider? resolved = GetVanillaProvider(source, VanillaId);
        Assert.NotNull(resolved);
        Assert.Equal(VanillaId, resolved.Id);
    }

    [Fact]
    public void Canonical_small_generation_produces_valid_metadata_and_terrain()
    {
        var request = new WorldGenerationRequest(VanillaId, "Canonical", 42, 4200, 1200)
        {
            SeedText = "42"
        };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Finalization?.Validation?.Detail ?? result.Generation.Execution?.Error?.ToString());
        Assert.NotNull(result.Candidate);
        Assert.NotNull(result.Generation.Execution);
        Assert.Equal(WorldGenerationExecutionStatus.Completed, result.Generation.Execution.Value.Status);
        Assert.True(result.Candidate!.TryGetSpawn(out WorldGenerationPoint spawn));
        Assert.InRange(spawn.X, 0, request.WidthTiles - 1);
        Assert.InRange(spawn.Y, 0, request.HeightTiles - 1);
        Assert.True(result.Candidate.TryGetLayers(out WorldGenerationLayers layers));
        Assert.True(layers.WorldSurface > 0d);
        Assert.True(layers.RockLayer > layers.WorldSurface);
        AssertSourceShapedTerrain(result.Candidate);
        AssertSourceFramedTrees(result.Candidate);
        int palmBases = 0;
        int palmCrowns = 0;
        foreach (WorldTile tile in result.Candidate.TileStore.Tiles)
        {
            if (!tile.IsActive || tile.Type != 323)
                continue;
            Assert.Contains(tile.FrameX, new short[] { 0, 22, 44, 66, 88, 110, 132 });
            palmBases += tile.FrameX == 66 ? 1 : 0;
            palmCrowns += tile.FrameX >= 88 ? 1 : 0;
        }
        Assert.True(palmBases > 0, "Canonical generation must include source-framed palm bases.");
        Assert.Equal(palmBases, palmCrowns);
        int cactusArms = 0;
        WorldTileStore vegetation = result.Candidate.TileStore;
        for (int x = 1; x < request.WidthTiles - 1; x++)
        for (int y = 1; y < (int)layers.WorldSurface; y++)
        {
            if (vegetation.Get(x, y) is not { IsActive: true, Type: 80 } || vegetation.Get(x, y + 1).IsActive)
                continue;
            if (vegetation.Get(x - 1, y) is { IsActive: true, Type: 80 } ||
                vegetation.Get(x + 1, y) is { IsActive: true, Type: 80 })
                cactusArms++;
        }
        Assert.True(cactusArms > 0, "Canonical cacti must include raised arms, not only vertical columns.");
        DungeonGraph1458 graph = Assert.IsType<DungeonGraph1458>(result.Candidate.VanillaDungeonGraph);
        Assert.InRange(graph.RoomCount, 3, 40);
        Assert.InRange(graph.HallCount, 45, 120);
        Assert.True(graph.HorizontalHallCount > 0);
        Assert.True(graph.VerticalHallCount > 0);
        Assert.True(graph.Bounds.Width >= 120, $"Dungeon graph width was only {graph.Bounds.Width} tiles.");
        Assert.True(graph.Bounds.Height >= 120, $"Dungeon graph height was only {graph.Bounds.Height} tiles.");
        Assert.Contains(graph.Components, static component => component.Kind == DungeonComponentKind1458.EntranceHall);
        Assert.Contains(graph.Components, static component => component.Kind == DungeonComponentKind1458.Entrance);
        WorldTownNpc[] startingNpcs = result.Candidate.CaptureGeneratedNpcs().TownNpcs;
        Assert.Contains(startingNpcs, static npc => npc.NetId == VanillaNpcIds.Guide.Value);
        WorldTownNpc oldMan = Assert.Single(startingNpcs, static npc => npc.NetId == VanillaNpcIds.OldMan.Value);
        Assert.False(oldMan.Homeless);
        Assert.Equal(graph.Anchor.X, oldMan.HomeTileX);
        Assert.Equal(graph.Anchor.Y, oldMan.HomeTileY);
        // Official NPC.NewNPC assigns Bottom; WorldFile persists position, not Bottom (NPC37: 18x40).
        Assert.Equal(graph.Anchor.X * 16f - 1f, oldMan.X);
        Assert.Equal(graph.Anchor.Y * 16f - 40f, oldMan.Y);
        VanillaCaveHouseCounts1458 caveHouses = Assert.IsType<VanillaCaveHouseCounts1458>(
            result.Candidate.VanillaCaveHouseCounts);
        Assert.InRange(caveHouses.Ordinary, 35, 40);
        Assert.Equal(2, caveHouses.AdditionalDesert);
        WorldChest[] generatedChests = result.Candidate.CaptureGeneratedChests();
        Assert.True(generatedChests.Length >= caveHouses.Total);
        Assert.Contains(generatedChests, chest => IsCaveHouseChest(result.Candidate, chest));
        AssertCanonicalContentCoverage(result.Candidate);
    }

    [Fact]
    public void Canonical_seed_1458_rejects_clouds_as_precalculated_dungeon_surface()
    {
        var request = new WorldGenerationRequest(VanillaId, "Dungeon cloud regression", 1458, 4200, 1200)
        {
            SeedText = "1458"
        };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Finalization?.Validation?.Detail ?? result.Generation.Execution?.Error?.ToString());
        Assert.NotNull(result.Candidate);
        Assert.True(result.Candidate!.TryGetLayers(out WorldGenerationLayers layers));
        DungeonGraph1458 graph = Assert.IsType<DungeonGraph1458>(result.Candidate.VanillaDungeonGraph);
        Assert.InRange(graph.Anchor.Y, (int)layers.WorldSurface - 160, (int)layers.WorldSurface + 20);
        WorldTownNpc oldMan = Assert.Single(
            result.Candidate.CaptureGeneratedNpcs().TownNpcs,
            static npc => npc.NetId == VanillaNpcIds.OldMan.Value);
        Assert.Equal(graph.Anchor.X, oldMan.HomeTileX);
        Assert.Equal(graph.Anchor.Y, oldMan.HomeTileY);
    }

    [Theory]
    [InlineData(14419291354518832569UL)]
    [InlineData(18104330376949184882UL)]
    [InlineData(9876543210123456789UL)]
    public void Canonical_small_regression_seeds_finalize_without_world_corruption(ulong seed)
    {
        var request = new WorldGenerationRequest(VanillaId, $"Regression-{seed}", seed, 4200, 1200)
        {
            SeedText = seed.ToString()
        };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(
            result.Succeeded,
            result.Finalization?.Validation?.Detail ?? result.Generation.Execution?.Error?.ToString());
    }

    [Theory]
    [InlineData(4200, 1200)]
    [InlineData(6400, 1800)]
    [InlineData(8400, 2400)]
    public void Canonical_dimensions_are_supported(int width, int height)
    {
        Assert.True(TerrainPass1458.IsCanonicalWorldSize(width, height));
    }

    [Theory]
    [InlineData(4199, 1200)]
    [InlineData(4200, 1199)]
    [InlineData(6401, 1800)]
    [InlineData(8400, 2401)]
    public void Noncanonical_dimensions_are_rejected_by_canonical_size_check(int width, int height)
    {
        Assert.False(TerrainPass1458.IsCanonicalWorldSize(width, height));
    }

    [Fact]
    public void Same_request_produces_deterministic_workspace_hash()
    {
        var request = new WorldGenerationRequest(VanillaId, "Deterministic", 123456789, 192, 128)
        {
            SeedText = "123456789"
        };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult first = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);
        RuntimeWorldCreationPipelineResult second = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded, first.Generation.Execution?.Error?.ToString());
        Assert.True(second.Succeeded, second.Generation.Execution?.Error?.ToString());
        Assert.NotNull(first.Candidate);
        Assert.NotNull(second.Candidate);
        Assert.Equal(HashWorkspace(first.Candidate!), HashWorkspace(second.Candidate!));
    }

    [Fact]
    public void Different_seed_changes_compatible_world_hash()
    {
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);
        var firstRequest = new WorldGenerationRequest(VanillaId, "SeedA", 1, 192, 128) { SeedText = "1" };
        var secondRequest = new WorldGenerationRequest(VanillaId, "SeedB", 2, 192, 128) { SeedText = "2" };

        RuntimeWorldCreationPipelineResult first = pipeline.CreateCandidate(
            in firstRequest,
            cancellationToken: TestContext.Current.CancellationToken);
        RuntimeWorldCreationPipelineResult second = pipeline.CreateCandidate(
            in secondRequest,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded, first.Generation.Execution?.Error?.ToString());
        Assert.True(second.Succeeded, second.Generation.Execution?.Error?.ToString());
        Assert.NotNull(first.Candidate);
        Assert.NotNull(second.Candidate);
        Assert.NotEqual(HashWorkspace(first.Candidate!), HashWorkspace(second.Candidate!));
    }

    [Fact]
    public void Compatible_world_sets_spawn_and_layers_inside_bounds()
    {
        var request = new WorldGenerationRequest(VanillaId, "Compat", 77, 320, 180) { SeedText = "77" };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Finalization?.Validation?.Detail ?? result.Generation.Execution?.Error?.ToString());
        Assert.NotNull(result.Candidate);
        Assert.True(result.Candidate!.TryGetSpawn(out WorldGenerationPoint spawn));
        Assert.InRange(spawn.X, 0, request.WidthTiles - 1);
        Assert.InRange(spawn.Y, 0, request.HeightTiles - 1);
        Assert.True(result.Candidate.TryGetLayers(out WorldGenerationLayers layers));
        Assert.InRange(layers.WorldSurface, 0d, request.HeightTiles - 1d);
        Assert.InRange(layers.RockLayer, layers.WorldSurface, request.HeightTiles - 1d);
    }

    [Fact]
    public void Persistence_pipeline_enforces_budget_and_atomicity()
    {
        var source = BuiltInWorldGeneratorSource.Instance;
        var huge = new WorldGenerationRequest(VanillaId, "Huge", 1, 8000, 5000)
        {
            SeedText = "1"
        };
        var persistence = new RuntimeWorldCreationPersistencePipeline(source, maxTileCount: 32_000_000);
        string hugePath = Path.Combine(Path.GetTempPath(), $"terraruntime-huge-{Guid.NewGuid():N}.wld");
        RuntimeWorldCreationPersistenceResult hugeResult = persistence.TryCreateAndPersist(
            huge,
            hugePath,
            Guid.NewGuid(),
            1,
            0,
            0,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(RuntimeWorldCreationPersistenceStatus.GenerationBudgetExceeded, hugeResult.Status);
        Assert.False(File.Exists(hugePath));

        var tiny = new WorldGenerationRequest(VanillaId, "Tiny", 1, 8, 8) { SeedText = "1" };
        string tinyPath = Path.Combine(Path.GetTempPath(), $"terraruntime-tiny-{Guid.NewGuid():N}.wld");
        RuntimeWorldCreationPersistenceResult tinyResult = persistence.TryCreateAndPersist(
            tiny,
            tinyPath,
            Guid.NewGuid(),
            1,
            0,
            0,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(
            tinyResult.Status is RuntimeWorldCreationPersistenceStatus.Persisted or
                RuntimeWorldCreationPersistenceStatus.GenerationFailed or
                RuntimeWorldCreationPersistenceStatus.FinalizationFailed,
            $"Unexpected status {tinyResult.Status}");

        if (File.Exists(tinyPath))
            File.Delete(tinyPath);
    }

    [Fact]
    public void Executor_honors_cancellation_during_canonical_generation()
    {
        var source = BuiltInWorldGeneratorSource.Instance;
        var request = new WorldGenerationRequest(VanillaId, "Cancelled", 1, 4200, 1200) { SeedText = "1" };
        var candidate = new Workspace(request.WidthTiles, request.HeightTiles);
        IWorldGenerationProvider? resolved = GetVanillaProvider(source, VanillaId);
        Assert.NotNull(resolved);
        var provider = Assert.IsType<SourceBackedFinal1458>(resolved);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        WorldGenerationExecutionResult execResult = RuntimeWorldGenerationExecutor.Execute(
            provider,
            in request,
            candidate,
            cancellationToken: cts.Token);
        Assert.Equal(WorldGenerationExecutionStatus.Cancelled, execResult.Status);
    }

    [Theory]
    [InlineData(192, 128)]
    [InlineData(640, 240)]
    public void Noncanonical_world_uses_compatible_fallback_and_remains_valid(int w, int h)
    {
        var source = BuiltInWorldGeneratorSource.Instance;
        var request = new WorldGenerationRequest(VanillaId, "Compat", 42, w, h) { SeedText = "42" };
        var pipeline = new RuntimeWorldCreationPipeline(source);
        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Succeeded, result.Finalization?.Validation?.Detail ?? result.Generation.Execution?.Error?.ToString());
        Assert.NotNull(result.Candidate);
        Assert.Equal(w, result.Candidate!.WidthTiles);
        Assert.Equal(h, result.Candidate.HeightTiles);
    }


    private static void AssertCanonicalContentCoverage(Workspace workspace)
    {
        long dungeonTiles = 0;
        long dungeonWalls = 0;
        long underworldAsh = 0;
        long underworldHellstone = 0;
        long underworldLavaUnits = 0;
        long jungleTiles = 0;
        long snowTiles = 0;
        long desertTiles = 0;
        long evilTiles = 0;

        int underworldTop = Math.Max(0, workspace.HeightTiles - 250);
        for (int y = 0; y < workspace.HeightTiles; y++)
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            if (tile.IsActive)
            {
                if (tile.Type is 41 or 43 or 44)
                    dungeonTiles++;
                if (tile.Type is 59 or 60)
                    jungleTiles++;
                if (tile.Type is 147 or 161)
                    snowTiles++;
                // Sand, hardened sand and sandstone ids are pinned by the 1.4.5.8 ordinary-world passes.
                if (tile.Type is 53 or 396 or 397)
                    desertTiles++;
                if (tile.Type is 23 or 25 or 112 or 199 or 203 or 234 or 400 or 401)
                    evilTiles++;

                if (y >= underworldTop)
                {
                    if (tile.Type == 57)
                        underworldAsh++;
                    else if (tile.Type == VanillaTileIds.Hellstone.Value)
                        underworldHellstone++;
                }
            }

            if (tile.Wall is 7 or 8 or 9 or 94 or 95 or 96 or 97 or 98 or 99)
                dungeonWalls++;
            if (y >= underworldTop && tile.LiquidAmount > 0 && tile.LiquidKind == WorldLiquidKind.Lava)
                underworldLavaUnits += tile.LiquidAmount;
        }

        Assert.True(dungeonTiles >= 20_000, $"Canonical dungeon contained only {dungeonTiles} dungeon-brick tiles.");
        Assert.True(dungeonWalls >= 20_000, $"Canonical dungeon contained only {dungeonWalls} unsafe dungeon-wall tiles.");
        Assert.True(underworldAsh >= 50_000, $"Canonical Underworld contained only {underworldAsh} ash tiles.");
        Assert.True(underworldHellstone >= 1_000, $"Canonical Underworld contained only {underworldHellstone} hellstone tiles.");
        Assert.True(underworldLavaUnits >= 100_000, $"Canonical Underworld contained only {underworldLavaUnits} lava units.");
        Assert.True(jungleTiles >= 50_000, $"Canonical jungle footprint was only {jungleTiles} tiles.");
        Assert.True(snowTiles >= 20_000, $"Canonical snow footprint was only {snowTiles} tiles.");
        Assert.True(desertTiles >= 20_000, $"Canonical desert footprint was only {desertTiles} tiles.");
        Assert.True(evilTiles >= 5_000, $"Canonical evil-biome footprint was only {evilTiles} tiles.");

        WorldChest[] chests = workspace.CaptureGeneratedChests();
        Assert.True(chests.Length >= 50, $"Canonical small world generated only {chests.Length} chests.");
        // Chest.CreateChest gives generated dressers empty storage, not exploration loot.
        Assert.DoesNotContain(chests.Where(chest => workspace.TileStore.Get(chest.X, chest.Y).Type != 88),
            static chest => chest.Items.All(static item => item.IsEmpty));
        Assert.All(chests.Where(chest => workspace.TileStore.Get(chest.X, chest.Y).Type == 88),
            chest => { Assert.Equal(40, chest.Items.Length); Assert.All(chest.Items, item => Assert.True(item.IsEmpty)); });
        Assert.All(chests, static chest =>
            Assert.All(chest.Items.Where(static item => !item.IsEmpty), static item =>
            {
                Assert.InRange(item.ItemType, 1, VanillaItemIds.Count - 1);
                Assert.True(item.Stack > 0);
            }));
        int nonEmptyStacks = chests.Sum(static chest => chest.Items.Count(static item => !item.IsEmpty));
        Assert.True(nonEmptyStacks >= 200, $"Canonical small world generated only {nonEmptyStacks} non-empty chest item stacks.");

        int[] requiredStyles = [0, 1, 2, 4, 10, 17];
        foreach (int style in requiredStyles)
        {
            Assert.Contains(chests, chest =>
            {
                WorldTile anchor = workspace.TileStore.Get(chest.X, chest.Y);
                return anchor.IsActive && anchor.Type == VanillaTileIds.Containers.Value && anchor.FrameX / 36 == style;
            });
        }

        var surfacePrimary = new HashSet<int> { 280, 281, 284, 285, 327, 953, 946, 3068, 3069, 3084, 4341, 6165 };
        var undergroundPrimary = new HashSet<int> { 49, 50, 53, 54, 5011, 975, 906, 997, 930 };
        var hellPrimary = new HashSet<int> { 274, 220, 112, 218, 3019 };
        var junglePrimary = new HashSet<int> { 211, 212, 213, 964, 2292, 3017 };
        var waterPrimary = new HashSet<int> { 863, 186, 4404, 277, 187 };

        foreach (WorldChest chest in chests)
        {
            WorldTile anchor = workspace.TileStore.Get(chest.X, chest.Y);
            if (!anchor.IsActive || anchor.Type != VanillaTileIds.Containers.Value)
                continue;

            int style = anchor.FrameX / 36;
            WorldChestItem primary = chest.Items.First(static item => !item.IsEmpty);
            switch (style)
            {
                case 0:
                    Assert.Contains(primary.ItemType, surfacePrimary);
                    break;
                case 1:
                    Assert.Contains(primary.ItemType, undergroundPrimary);
                    break;
                case 4:
                    Assert.Contains(primary.ItemType, hellPrimary);
                    break;
                case 10:
                    Assert.Contains(primary.ItemType, junglePrimary);
                    break;
                case 17:
                    Assert.Contains(primary.ItemType, waterPrimary);
                    break;
            }
        }
    }

    private static IWorldGenerationProvider? GetVanillaProvider(
        ITerraRuntimeWorldGeneratorSource source,
        WorldGeneratorId id)
    {
        Assert.True(source.TryResolveWorldGenerator(id, out IWorldGenerationProvider? provider));
        return provider;
    }

    private static ulong HashWorkspace(Workspace workspace)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;

        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            for (int x = 0; x < workspace.WidthTiles; x++)
            {
                Assert.True(workspace.TryGetTile(x, y, out WorldGenerationTile tile));
                Mix(tile.Type);
                Mix(tile.Wall);
                Mix(tile.FrameX);
                Mix(tile.FrameY);
                Mix((ushort)tile.Flags);
                Mix(tile.LiquidAmount);
                Mix(tile.TileColor);
                Mix(tile.WallColor);
                Mix(tile.Shape);
                Mix((byte)tile.LiquidKind);
            }
        }

        return hash;

        void Mix<T>(T value)
            where T : unmanaged
        {
            foreach (byte b in System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                         System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref value, 1)))
            {
                hash ^= b;
                hash *= prime;
            }
        }
    }

    private static bool IsCaveHouseChest(Workspace workspace, WorldChest chest)
    {
        WorldTile anchor = workspace.TileStore.Get(chest.X, chest.Y);
        int style = anchor.FrameX / 36;
        if (anchor.Type == 467)
            return style == 10;
        if (anchor.Type != 21)
            return false;
        return style is 8 or 11 or 32 or 50 or 51;
    }

    private static void AssertSourceFramedTrees(Workspace workspace)
    {
        var topFrames = new HashSet<(short X, short Y)>();
        var branchFrames = new HashSet<(short X, short Y)>();
        var rootFrames = new HashSet<(short X, short Y)>();
        for (int variant = 0; variant < 3; variant++)
        {
            Add(topFrames, TreeFrameCatalog1458.Top(leafy: true, variant));
            Add(topFrames, TreeFrameCatalog1458.Top(leafy: false, variant));
            Add(branchFrames, TreeFrameCatalog1458.LeftBranch(leafy: true, variant));
            Add(branchFrames, TreeFrameCatalog1458.LeftBranch(leafy: false, variant));
            Add(branchFrames, TreeFrameCatalog1458.RightBranch(leafy: true, variant));
            Add(branchFrames, TreeFrameCatalog1458.RightBranch(leafy: false, variant));
            Add(rootFrames, TreeFrameCatalog1458.LeftRoot(variant));
            Add(rootFrames, TreeFrameCatalog1458.RightRoot(variant));
        }

        int treeCells = 0;
        int tops = 0;
        int branches = 0;
        int roots = 0;
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                if (!tile.IsActive || tile.TileType != VanillaTileIds.Trees)
                    continue;

                treeCells++;
                var frame = (tile.FrameX, tile.FrameY);
                tops += topFrames.Contains(frame) ? 1 : 0;
                branches += branchFrames.Contains(frame) ? 1 : 0;
                roots += rootFrames.Contains(frame) ? 1 : 0;
            }

        Assert.True(treeCells > 0, "Canonical generation must contain ordinary tree cells.");
        Assert.True(tops > 0, "Canonical trees must contain source-framed crowns.");
        Assert.True(branches > 0, "Canonical trees must contain source-framed branches.");
        Assert.True(roots > 0, "Canonical trees must contain source-framed roots.");

        static void Add(HashSet<(short X, short Y)> target, TreeFrame1458 frame) =>
            target.Add((frame.X, frame.Y));
    }

    private static void AssertSourceShapedTerrain(Workspace workspace)
    {
        var counts = new int[6];
        for (int x = 20; x < workspace.WidthTiles - 20; x++)
            for (int y = 20; y < workspace.HeightTiles - 20; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                if (tile.IsActive && tile.Shape < counts.Length)
                    counts[tile.Shape]++;
            }

        Assert.True(counts[(byte)TileShape1458.HalfBrick] > 0, "Smooth World must produce half-bricks.");
        Assert.True(counts[(byte)TileShape1458.SlopeDownRight] > 0, "Smooth World must produce slope 1.");
        Assert.True(counts[(byte)TileShape1458.SlopeDownLeft] > 0, "Smooth World must produce slope 2.");
        Assert.True(counts[(byte)TileShape1458.SlopeUpRight] > 0, "Smooth World must produce slope 3.");
        Assert.True(counts[(byte)TileShape1458.SlopeUpLeft] > 0, "Smooth World must produce slope 4.");
    }
}
