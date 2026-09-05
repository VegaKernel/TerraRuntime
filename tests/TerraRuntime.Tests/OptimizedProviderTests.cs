using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OptimizedWorldGenerationProviderTests
{
    [Fact]
    public void Optimized_generator_is_registered_and_builds_required_progression_geography()
    {
        BuiltInWorldGeneratorSource source = BuiltInWorldGeneratorSource.Instance;
        Assert.Contains(
            OptimizedProvider.GeneratorId,
            source.CaptureWorldGeneratorIds().Span.ToArray());
        Assert.True(
            source.TryResolveWorldGenerator(
                OptimizedProvider.GeneratorId,
                out IWorldGenerationProvider? provider));
        Assert.NotNull(provider);
        Assert.IsType<SurfaceDecorationProvider>(provider);

        var request = new WorldGenerationRequest(
            OptimizedProvider.GeneratorId,
            "Optimized",
            Seed: 0x5EEDC0DEUL,
            WidthTiles: 640,
            HeightTiles: 320);
        var pipeline = new RuntimeWorldCreationPipeline(source);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(
            result.Succeeded,
            $"{result.Status} gen={result.Generation.Status} fin={result.Finalization?.Status} validation={result.Finalization?.Validation} err={result.Generation.Execution?.Error}");
        Assert.NotNull(result.Candidate);
        Workspace world = result.Candidate!;
        ProgressionValidationReport progression = ProgressionWorldValidator.Validate(
            world,
            world,
            in request,
            TestContext.Current.CancellationToken);
        Assert.Equal(12, progression.ReachableTargetCount);
        Assert.True(progression.CopperTiles > 0);
        Assert.True(progression.IronTiles > 0);
        Assert.True(progression.SilverTiles > 0);
        Assert.True(progression.GoldTiles > 0);
        Assert.True(progression.HellstoneTiles > 0);
        Assert.True(progression.ObsidianTiles >= ProgressionContentProvider.ResolveObsidianTarget(request.WidthTiles));
        Assert.True(progression.EvilAnchorObjects >= ProgressionContentProvider.ResolveEvilAnchorTarget(request.WidthTiles));
        Assert.True(progression.LarvaObjects >= ProgressionContentProvider.ResolveLarvaTarget(request.WidthTiles));
        Assert.True(progression.DungeonInteriorCells >= 24);
        Assert.True(progression.HiveInteriorCells >= 18);
        Assert.True(progression.TempleInteriorCells >= 24);

        Assert.Equal(320, result.Metadata.Spawn.X);
        AssertSpawnHasGround(world, result.Metadata.Spawn);
        Assert.True(result.Metadata.Dungeon.X < request.WidthTiles / 3 || result.Metadata.Dungeon.X > request.WidthTiles * 2 / 3);
        Assert.True(result.Metadata.Layers.WorldSurface > 0d);
        Assert.True(result.Metadata.Layers.RockLayer > result.Metadata.Layers.WorldSurface);

        Assert.True(ContainsActiveTile(world, 41), "Dungeon brick must exist.");
        Assert.True(ContainsActiveTile(world, checked((ushort)VanillaTileIds.LihzahrdBrick.Value)), "Jungle Temple must exist.");
        Assert.True(ContainsActiveTile(world, checked((ushort)VanillaTileIds.Hive.Value)), "Hive must exist.");
        Assert.True(ContainsActiveTile(world, checked((ushort)VanillaTileIds.DemonAltar.Value)), "Evil altar must exist.");
        Assert.True(ContainsActiveTile(world, checked((ushort)VanillaTileIds.Hellforge.Value)), "Hellforge must exist.");
        Assert.True(ContainsActiveTile(world, 58), "Hellstone must exist.");
        Assert.True(ContainsActiveTile(world, checked((ushort)VanillaTileIds.Obsidian.Value)), "Obsidian progression material must exist.");
        Assert.True(ContainsActiveTile(world, checked((ushort)VanillaTileIds.ShadowOrbs.Value)), "Shadow Orb progression anchors must exist.");
        Assert.True(ContainsActiveTile(world, checked((ushort)VanillaTileIds.Larva.Value)), "Hive Larva progression anchor must exist.");
        Assert.True(ContainsLiquid(world, WorldGenerationLiquidKind.Water), "Water must exist.");
        Assert.True(ContainsLiquid(world, WorldGenerationLiquidKind.Lava), "Lava must exist.");
        Assert.True(ContainsLiquid(world, WorldGenerationLiquidKind.Honey), "Honey must exist.");
        Assert.True(ContainsLiquid(world, WorldGenerationLiquidKind.Shimmer), "Shimmer must exist.");

        int skyLimit = Math.Max(1, (int)result.Metadata.Layers.WorldSurface - 20);
        Assert.True(CountActiveTilesAbove(world, skyLimit) >= 90, "Floating-island terrain must exist above the normal surface.");
        Assert.True(ContainsWaterAbove(world, skyLimit), "The landmark layer must keep at least one explicit Floating Lake.");

        Assert.True(CountActiveTiles(world, 12) >= 32, "A 640x320 optimized world must contain at least eight complete Life Crystals.");
        Assert.True(world.GeneratedChestCount >= 12, "The optimized world must persist generic caches plus landmark caches.");
        Assert.True(ContainsInteriorWaterBelow(world, (int)result.Metadata.Layers.RockLayer), "Organic cavern generation must include inland underground water.");

        Assert.True(CountActiveTiles(world, 202) >= 30, "At least one Sunplate sky house must exist.");
        Assert.True(CountActiveTiles(world, 151) >= 120, "Optimized pyramids must contain a substantial solid sandstone-brick mass, not only an outline.");
        Assert.True(CountActiveTiles(world, 191) >= 40, "At least one Living Wood structure must exist.");
        Assert.True(CountActiveTiles(world, checked((ushort)VanillaTileIds.ObsidianBrick.Value)) >= 36, "At least one source-backed Obsidian Brick Underworld house must exist.");
        Assert.True(CountActiveTiles(world, checked((ushort)VanillaTileIds.HellstoneBrick.Value)) >= 36, "At least one source-backed Hellstone Brick Underworld house must exist.");
        Assert.True(CountWall(world, checked((ushort)VanillaWallIds.ObsidianBrickUnsafe.Value)) >= 90, "Obsidian Brick houses must retain the source-backed unsafe wall family.");
        Assert.True(CountWall(world, checked((ushort)VanillaWallIds.HellstoneBrickUnsafe.Value)) >= 90, "Hellstone Brick houses must retain the source-backed unsafe wall family.");
        Assert.True(CountObjectStyleAnchors(world, checked((ushort)VanillaTileIds.Tables.Value), width: 3, style: 13) >= 2, "Underworld houses must contain source-backed Hell tables.");
        Assert.True(CountObjectStyleAnchors(world, checked((ushort)VanillaTileIds.Bookcases.Value), width: 3, style: 4) >= 2, "Underworld houses must contain source-backed Hell bookcases.");
        Assert.True(CountActiveTiles(world, checked((ushort)VanillaTileIds.Granite.Value)) >= 35, "Granite micro-biome budget must exist.");
        Assert.True(CountActiveTiles(world, checked((ushort)VanillaTileIds.Marble.Value)) >= 35, "Marble micro-biome budget must exist.");
        Assert.True(CountWall(world, 62) >= 20, "Spider-grotto wall budget must exist.");
        Assert.True(CountActiveTiles(world, 5) >= 120, "Ordinary forest/jungle/snow tree trunks must decorate the optimized surface.");
        Assert.True(CountTreeFoliageAnchors(world) >= 10, "Optimized ordinary trees must publish foliage anchors instead of bare trunk tips.");
        Assert.True(CountShapedNaturalSurface(world, result.Metadata.Layers) >= 4, "Optimized surface finishing must create non-square natural transitions.");
        Assert.True(CountActiveTiles(world, 3) + CountActiveTiles(world, 61) >= 70, "Surface undergrowth must make optimized worlds visibly inhabited.");
        Assert.True(CountActiveTiles(world, 27) >= 8, "At least two complete sunflower patches must exist.");

        WorldChest[] generated = world.CaptureGeneratedChests();
        Assert.Contains(generated, static chest => chest.Items.Any(static item => !item.IsEmpty));
        Assert.Contains(generated, static chest => chest.Name.StartsWith("Sky Cache ", StringComparison.Ordinal));
        Assert.Contains(generated, static chest => chest.Name.StartsWith("Pyramid Cache ", StringComparison.Ordinal));
        Assert.Contains(generated, static chest => chest.Name.StartsWith("Living Tree Cache ", StringComparison.Ordinal));
        WorldChest[] underworldCaches = generated
            .Where(static chest => chest.Name.StartsWith("Underworld Cache ", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, underworldCaches.Length);
        foreach (WorldChest chest in underworldCaches)
        {
            Assert.True(world.TryGetTile(chest.X, chest.Y, out WorldGenerationTile anchor));
            Assert.Equal(VanillaTileIds.Containers.Value, anchor.Type);
            Assert.Equal((short)(4 * 36), anchor.FrameX);
            Assert.Equal((short)0, anchor.FrameY);
            Assert.Contains(chest.Items, static item => !item.IsEmpty && IsHellChestPrimary1458(item.ItemType));
        }
        WorldChest jungleProgression = Assert.Single(generated, static chest => chest.Name == "Jungle Progression Cache");
        Assert.Contains(jungleProgression.Items, static item => item.ItemType == VanillaItemIds.JungleSpores.Value && item.Stack >= 30);
        Assert.Contains(jungleProgression.Items, static item => item.ItemType == VanillaItemIds.Stinger.Value && item.Stack >= 20);
        Assert.Contains(jungleProgression.Items, static item => item.ItemType == VanillaItemIds.Vine.Value && item.Stack >= 6);
        Assert.True(HasEvilAnchorStyle(world, crimson: false), "Corruption optimized worlds must use source-backed Shadow Orb frames.");
    }

    [Fact]
    public void Optimized_generator_replays_deterministically_for_same_seed()
    {
        var request = new WorldGenerationRequest(
            OptimizedProvider.GeneratorId,
            "Optimized deterministic",
            Seed: 123456789UL,
            WidthTiles: 512,
            HeightTiles: 240);
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult first = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);
        RuntimeWorldCreationPipelineResult second = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded, first.Generation.Execution?.Error?.ToString());
        Assert.True(second.Succeeded, second.Generation.Execution?.Error?.ToString());
        Assert.Equal(first.Metadata, second.Metadata);
        Assert.NotNull(first.Candidate);
        Assert.NotNull(second.Candidate);
        Assert.Equal(first.Candidate!.GeneratedChestCount, second.Candidate!.GeneratedChestCount);

        for (int y = 0; y < request.HeightTiles; y += 7)
        {
            for (int x = 0; x < request.WidthTiles; x += 7)
            {
                Assert.True(first.Candidate.TryGetTile(x, y, out WorldGenerationTile a));
                Assert.True(second.Candidate.TryGetTile(x, y, out WorldGenerationTile b));
                Assert.Equal(a, b);
            }
        }

        AssertGeneratedChestsEqual(
            first.Candidate.CaptureGeneratedChests(),
            second.Candidate.CaptureGeneratedChests());
    }

    [Fact]
    public void Optimized_generator_builds_landmarks_for_crimson_worlds()
    {
        var request = new WorldGenerationRequest(
            OptimizedProvider.GeneratorId,
            "Optimized crimson",
            Seed: 0xC11A50UL,
            WidthTiles: 512,
            HeightTiles: 240)
        {
            Options = new WorldGenerationOptions(
                WorldGenerationGameMode.Classic,
                WorldGenerationEvil.Crimson)
        };
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Generation.Execution?.Error?.ToString());
        Assert.NotNull(result.Candidate);
        Assert.True(CountActiveTiles(result.Candidate!, 203) > 0, "Crimson optimized worlds must retain Crimstone.");
        Assert.True(HasEvilAnchorStyle(result.Candidate, crimson: true), "Crimson optimized worlds must use the +36 source-backed Crimson Heart frame style.");
        Assert.True(CountActiveTiles(result.Candidate, checked((ushort)VanillaTileIds.Larva.Value)) >= 9, "Crimson optimized worlds must retain a complete Hive Larva.");
        Assert.Contains(
            result.Candidate!.CaptureGeneratedChests(),
            static chest => chest.Name.StartsWith("Pyramid Cache ", StringComparison.Ordinal));
        Assert.Contains(
            result.Candidate.CaptureGeneratedChests(),
            static chest => chest.Name.StartsWith("Sky Cache ", StringComparison.Ordinal));
    }

    [Fact]
    public void Optimized_generator_creates_canonical_small_world_without_crashing()
    {
        var request = new WorldGenerationRequest(
            OptimizedProvider.GeneratorId,
            "Optimized canonical small",
            Seed: 0x0F7145EDUL,
            WidthTiles: 4200,
            HeightTiles: 1200);
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(
            result.Succeeded,
            $"{result.Status} gen={result.Generation.Status} fin={result.Finalization?.Status} validation={result.Finalization?.Validation} err={result.Generation.Execution?.Error}");
        Assert.NotNull(result.Candidate);
        Assert.Equal(4200, result.Candidate!.WidthTiles);
        Assert.Equal(1200, result.Candidate.HeightTiles);
        Assert.Equal(12, ProgressionWorldValidator.Validate(
            result.Candidate,
            result.Candidate,
            in request,
            TestContext.Current.CancellationToken).ReachableTargetCount);
        Assert.True(CountActiveTiles(result.Candidate, 5) >= 700, "Canonical Small optimized worlds must contain a substantial ordinary-tree population.");
        Assert.True(CountTreeFoliageAnchors(result.Candidate) >= 80, "Canonical Small optimized trees must include persistent foliage anchors.");
        Assert.True(CountShapedNaturalSurface(result.Candidate, result.Metadata.Layers) >= 20, "Canonical Small optimized terrain must retain visible shaped surface transitions.");
        Assert.True(CountActiveTiles(result.Candidate, checked((ushort)VanillaTileIds.ShadowOrbs.Value)) >= 24, "Canonical Small optimized worlds must retain at least six complete evil anchors.");
        Assert.True(CountActiveTiles(result.Candidate, checked((ushort)VanillaTileIds.Obsidian.Value)) >= ProgressionContentProvider.ResolveObsidianTarget(request.WidthTiles), "Canonical Small optimized worlds must retain the Obsidian progression budget.");
        Assert.True(CountActiveTiles(result.Candidate, checked((ushort)VanillaTileIds.Larva.Value)) >= 9, "Canonical Small optimized worlds must retain Hive Larva progression.");
    }

    private static void AssertSpawnHasGround(
        Workspace workspace,
        WorldGenerationPoint spawn)
    {
        Assert.True(workspace.TryGetTile(spawn.X, spawn.Y, out WorldGenerationTile spawnTile));
        Assert.Equal(WorldGenerationTileFlags.None, spawnTile.Flags & WorldGenerationTileFlags.Active);

        bool foundGround = false;
        for (int dy = 1; dy <= 3 && spawn.Y + dy < workspace.HeightTiles; dy++)
        {
            Assert.True(workspace.TryGetTile(spawn.X, spawn.Y + dy, out WorldGenerationTile tile));
            if ((tile.Flags & WorldGenerationTileFlags.Active) != 0)
            {
                foundGround = true;
                break;
            }
        }

        Assert.True(foundGround, "Spawn must have solid ground within three tiles below.");
    }

    private static bool ContainsActiveTile(Workspace workspace, ushort type) =>
        CountActiveTiles(workspace, type) > 0;

    private static int CountActiveTiles(Workspace workspace, ushort type)
    {
        int count = 0;
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            for (int x = 0; x < workspace.WidthTiles; x++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                    tile.Type == type)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool HasEvilAnchorStyle(Workspace workspace, bool crimson)
    {
        short expected = crimson ? (short)36 : (short)0;
        for (int y = 0; y < workspace.HeightTiles - 1; y++)
        for (int x = 0; x < workspace.WidthTiles - 1; x++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) ||
                (tile.Flags & WorldGenerationTileFlags.Active) == 0 ||
                tile.Type != VanillaTileIds.ShadowOrbs.Value || tile.FrameX != expected || tile.FrameY != 0)
            {
                continue;
            }
            return true;
        }
        return false;
    }

    private static int CountTreeFoliageAnchors(Workspace workspace)
    {
        int count = 0;
        for (int y = 0; y < workspace.HeightTiles; y++)
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                (tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                tile.Type == 5 && tile.FrameX >= 22 && tile.FrameY >= 198)
            {
                count++;
            }
        }
        return count;
    }

    private static int CountShapedNaturalSurface(Workspace workspace, WorldGenerationLayers layers)
    {
        int start = Math.Clamp((int)Math.Floor(layers.WorldSurface) - 60, 0, workspace.HeightTiles - 1);
        int end = Math.Clamp((int)Math.Ceiling(layers.WorldSurface) + 120, start, workspace.HeightTiles - 1);
        int count = 0;
        for (int y = start; y <= end; y++)
        for (int x = 1; x < workspace.WidthTiles - 1; x++)
        {
            if (!workspace.TryGetTile(x, y, out WorldGenerationTile tile) || tile.Shape == 0 ||
                (tile.Flags & WorldGenerationTileFlags.Active) == 0)
            {
                continue;
            }
            if (tile.Type is 0 or 2 or 53 or 59 or 60 or 147)
                count++;
        }
        return count;
    }

    private static int CountObjectStyleAnchors(Workspace workspace, ushort type, int width, int style)
    {
        short expectedFrameX = checked((short)(style * width * 18));
        int count = 0;
        for (int y = 0; y < workspace.HeightTiles; y++)
        for (int x = 0; x < workspace.WidthTiles; x++)
        {
            if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                (tile.Flags & WorldGenerationTileFlags.Active) != 0 &&
                tile.Type == type &&
                tile.FrameX == expectedFrameX &&
                tile.FrameY == 0)
            {
                count++;
            }
        }
        return count;
    }

    private static bool IsHellChestPrimary1458(int itemType) =>
        itemType == VanillaItemIds.DarkLance.Value ||
        itemType == VanillaItemIds.Sunfury.Value ||
        itemType == VanillaItemIds.FlowerOfFire.Value ||
        itemType == VanillaItemIds.Flamelash.Value ||
        itemType == VanillaItemIds.HellwingBow.Value;

    private static int CountWall(Workspace workspace, ushort wall)
    {
        int count = 0;
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            for (int x = 0; x < workspace.WidthTiles; x++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) && tile.Wall == wall)
                    count++;
            }
        }

        return count;
    }

    private static bool ContainsLiquid(
        Workspace workspace,
        WorldGenerationLiquidKind kind)
    {
        for (int y = 0; y < workspace.HeightTiles; y++)
        {
            for (int x = 0; x < workspace.WidthTiles; x++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    tile.LiquidAmount > 0 &&
                    tile.LiquidKind == kind)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsWaterAbove(
        Workspace workspace,
        int maxYExclusive)
    {
        for (int y = 1; y < Math.Min(maxYExclusive, workspace.HeightTiles); y++)
        {
            for (int x = 1; x < workspace.WidthTiles - 1; x++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    tile.LiquidAmount > 0 &&
                    tile.LiquidKind == WorldGenerationLiquidKind.Water)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsInteriorWaterBelow(
        Workspace workspace,
        int minY)
    {
        int margin = Math.Clamp(workspace.WidthTiles / 8, 50, 120);
        for (int y = Math.Clamp(minY, 1, workspace.HeightTiles - 2); y < workspace.HeightTiles * 4 / 5; y++)
        {
            for (int x = margin; x < workspace.WidthTiles - margin; x++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    tile.LiquidAmount > 0 &&
                    tile.LiquidKind == WorldGenerationLiquidKind.Water)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CountActiveTilesAbove(Workspace workspace, int yExclusive)
    {
        int count = 0;
        for (int y = 0; y < yExclusive; y++)
        {
            for (int x = 0; x < workspace.WidthTiles; x++)
            {
                if (workspace.TryGetTile(x, y, out WorldGenerationTile tile) &&
                    (tile.Flags & WorldGenerationTileFlags.Active) != 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void AssertGeneratedChestsEqual(WorldChest[] expected, WorldChest[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++)
        {
            WorldChest a = expected[index];
            WorldChest b = actual[index];
            Assert.Equal(a.SlotId, b.SlotId);
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y, b.Y);
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.Items.Length, b.Items.Length);
            for (int slot = 0; slot < a.Items.Length; slot++)
                Assert.Equal(a.Items[slot], b.Items[slot]);
        }
    }
}
