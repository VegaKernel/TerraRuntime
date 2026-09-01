using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class OptimizedDungeonV2Tests
{
    private static readonly HashSet<int> SourceBackedDungeonLoot =
    [
        113,  // Magic Missile
        155,  // Muramasa
        156,  // Cobalt Shield
        157,  // Aqua Scepter
        163,  // Blue Moon
        164,  // Handgun
        3317  // Valor
    ];

    [Fact]
    public void Optimized_world_contains_locked_dungeon_chests_keys_and_wired_traps()
    {
        var request = new WorldGenerationRequest(
            OptimizedWorldGenerationProvider.GeneratorId,
            "Dungeon v2",
            Seed: 0xD06E0A2UL,
            WidthTiles: 640,
            HeightTiles: 320);
        var pipeline = new RuntimeWorldCreationPipeline(BuiltInWorldGeneratorSource.Instance);

        RuntimeWorldCreationPipelineResult result = pipeline.CreateCandidate(
            in request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(
            result.Succeeded,
            $"{result.Status} gen={result.Generation.Status} fin={result.Finalization?.Status} " +
            $"validation={result.Finalization?.Validation} err={result.Generation.Execution?.Error}");
        Assert.NotNull(result.Candidate);
        RuntimeWorldGenerationWorkspace world = result.Candidate!;
        WorldChest[] chests = world.CaptureGeneratedChests();

        WorldChest keyCache = Assert.Single(chests, static chest => chest.Name == "Dungeon Key Cache");
        int goldenKeys = keyCache.Items
            .Where(static item => !item.IsEmpty && item.ItemType == 327)
            .Sum(static item => item.Stack);

        WorldChest[] locked = chests
            .Where(static chest => chest.Name.StartsWith("Locked Dungeon Cache ", StringComparison.Ordinal))
            .OrderBy(static chest => chest.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.InRange(locked.Length, 3, 7);
        Assert.True(goldenKeys >= locked.Length, $"Dungeon has {goldenKeys} Golden Keys for {locked.Length} locked chests.");

        var distinctPrimaryLoot = new HashSet<int>();
        foreach (WorldChest chest in locked)
        {
            Assert.True(world.TryGetTile(chest.X, chest.Y, out WorldGenerationTile anchor));
            Assert.Equal((ushort)21, anchor.Type);
            Assert.Equal((short)72, anchor.FrameX);
            Assert.Equal((short)0, anchor.FrameY);
            Assert.Equal((ushort)7, anchor.Wall);

            int primary = Assert.Single(
                chest.Items.Where(static item => !item.IsEmpty)).ItemType;
            Assert.Contains(primary, SourceBackedDungeonLoot);
            distinctPrimaryLoot.Add(primary);
        }
        Assert.Equal(locked.Length, distinctPrimaryLoot.Count);

        int pressurePlates = 0;
        int wiredPressurePlates = 0;
        int dartTraps = 0;
        int wiredDartTraps = 0;
        int spikes = 0;
        for (int y = 0; y < world.HeightTiles; y++)
        for (int x = 0; x < world.WidthTiles; x++)
        {
            Assert.True(world.TryGetTile(x, y, out WorldGenerationTile tile));
            if ((tile.Flags & WorldGenerationTileFlags.Active) == 0)
                continue;

            switch (tile.Type)
            {
                case 48:
                    spikes++;
                    break;
                case 135:
                    pressurePlates++;
                    if ((tile.Flags & WorldGenerationTileFlags.WireRed) != 0)
                        wiredPressurePlates++;
                    Assert.Equal((short)36, tile.FrameX);
                    Assert.Equal((ushort)7, tile.Wall);
                    break;
                case 137:
                    dartTraps++;
                    if ((tile.Flags & WorldGenerationTileFlags.WireRed) != 0)
                        wiredDartTraps++;
                    Assert.Contains(tile.FrameX, [(short)0, (short)18]);
                    Assert.Equal((ushort)7, tile.Wall);
                    break;
            }
        }

        Assert.InRange(pressurePlates, 2, 10);
        Assert.Equal(pressurePlates, dartTraps);
        Assert.Equal(pressurePlates, wiredPressurePlates);
        Assert.Equal(dartTraps, wiredDartTraps);
        Assert.True(spikes >= 16, $"Dungeon v2 generated only {spikes} spike tiles.");
    }

    [Fact]
    public void Dungeon_v2_is_ordered_after_metadata_and_before_base_validation()
    {
        var request = new WorldGenerationRequest(
            OptimizedWorldGenerationProvider.GeneratorId,
            "Dungeon pass order",
            Seed: 17,
            WidthTiles: 640,
            HeightTiles: 320);
        var builder = new CaptureBuilder();
        new OptimizedSurfaceDecorationWorldGenerationProvider().BuildPlan(in request, builder);

        CapturedPass metadata = Assert.Single(builder.Entries, static x => x.Id.Value == "terraruntime:optimized/metadata");
        CapturedPass dungeon = Assert.Single(builder.Entries, static x => x.Id.Value == "terraruntime:optimized/dungeon-v2");
        CapturedPass validation = Assert.Single(builder.Entries, static x => x.Id.Value == "terraruntime:optimized/validation");

        Assert.Contains(metadata.Id, dungeon.RequiredAfter);
        Assert.Contains(dungeon.Id, validation.RequiredAfter);
    }

    private readonly record struct CapturedPass(
        WorldGenerationPassId Id,
        WorldGenerationPassId[] RequiredAfter);

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CapturedPass> Entries { get; } = [];

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass)
        {
            _ = pass;
            Entries.Add(new CapturedPass(descriptor.Id, descriptor.RequiredAfter.ToArray()));
        }
    }
}
