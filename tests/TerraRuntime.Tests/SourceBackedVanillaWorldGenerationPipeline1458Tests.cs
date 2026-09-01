using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedVanillaWorldGenerationPipeline1458Tests
{
    [Fact]
    public void Canonical_ordinary_world_expands_the_real_early_pass_order_through_jungle()
    {
        var provider = new SourceBackedVanillaWorldGenerationPipeline1458();
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "StageOne",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(VanillaWorldGenerationProvider1458.GeneratorId, provider.Id);
        Assert.Equal(26, builder.Entries.Count);
        string[] expected =
        [
            "terraria:1.4.5.8/Reset",
            "terraria:1.4.5.8/Terrain",
            "terraria:1.4.5.8/TerrainLayers",
            "terraria:1.4.5.8/Dunes",
            "terraria:1.4.5.8/OceanSand",
            "terraria:1.4.5.8/SandPatches",
            "terraria:1.4.5.8/Tunnels",
            "terraria:1.4.5.8/MountCaves",
            "terraria:1.4.5.8/DirtWallBackgrounds",
            "terraria:1.4.5.8/RocksInDirt",
            "terraria:1.4.5.8/DirtInRocks",
            "terraria:1.4.5.8/Clay",
            "terraria:1.4.5.8/SmallHoles",
            "terraria:1.4.5.8/DirtLayerCaves",
            "terraria:1.4.5.8/RockLayerCaves",
            "terraria:1.4.5.8/SurfaceCaves",
            "terraria:1.4.5.8/WavyCaves",
            "terraria:1.4.5.8/GenerateIceBiome",
            "terraria:1.4.5.8/Grass",
            "terraria:1.4.5.8/Jungle",
            "terraria:1.4.5.8/Biomes",
            "terraria:1.4.5.8/Caves",
            "terraria:1.4.5.8/Ores",
            "terraria:1.4.5.8/Dungeon",
            "terraria:1.4.5.8/SecretSeeds",
            "terraria:1.4.5.8/Metadata"
        ];

        Assert.Equal(expected, builder.Entries.Select(static e => e.Descriptor.Id.Value));
        Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, "terraria:1.4.5.8/Dunes").Descriptor.RngMode);
        Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, "terraria:1.4.5.8/RocksInDirt").Descriptor.RngMode);
        Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, "terraria:1.4.5.8/DirtWallBackgrounds").Descriptor.RngMode);
        Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, "terraria:1.4.5.8/Clay").Descriptor.RngMode);
        Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, "terraria:1.4.5.8/Jungle").Descriptor.RngMode);
        Assert.Equal(WorldGenerationRngMode.IsolatedDeterministic, Find(builder, "terraria:1.4.5.8/Biomes").Descriptor.RngMode);
        Assert.Contains(
            new WorldGenerationPassId("terraria:1.4.5.8/Jungle"),
            Find(builder, "terraria:1.4.5.8/Biomes").Descriptor.RequiredAfter.ToArray());
    }

    [Fact]
    public void Noncanonical_world_keeps_the_existing_compatibility_plan_unchanged()
    {
        var provider = new SourceBackedVanillaWorldGenerationPipeline1458();
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Synthetic",
            Seed: 1458,
            WidthTiles: 192,
            HeightTiles: 128);
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(8, builder.Entries.Count);
        Assert.DoesNotContain(builder.Entries, static e => e.Descriptor.Id.Value == "terraria:1.4.5.8/Dunes");
        Assert.Contains(builder.Entries, static e => e.Descriptor.Id == SourceBackedVanillaWorldGenerationProvider1458.MetadataPassId);
    }

    [Fact]
    public void Pure_remix_stops_at_source_backed_dunes_and_keeps_the_later_compatibility_tail()
    {
        var provider = new SourceBackedVanillaWorldGenerationPipeline1458();
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Don't Dig Up",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "don't dig up"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(
            [
                "terraria:1.4.5.8/Reset",
                "terraria:1.4.5.8/Terrain",
                "terraria:1.4.5.8/TerrainLayers",
                "terraria:1.4.5.8/Dunes",
                "terraria:1.4.5.8/Biomes",
                "terraria:1.4.5.8/Caves",
                "terraria:1.4.5.8/Ores",
                "terraria:1.4.5.8/Dungeon",
                "terraria:1.4.5.8/SecretSeeds",
                "terraria:1.4.5.8/Metadata"
            ],
            builder.Entries.Select(static entry => entry.Descriptor.Id.Value));
        Assert.Contains(
            new WorldGenerationPassId("terraria:1.4.5.8/Dunes"),
            Find(builder, "terraria:1.4.5.8/Biomes").Descriptor.RequiredAfter.ToArray());
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id.Value == "terraria:1.4.5.8/OceanSand");
    }

    [Fact]
    public void Zenith_still_keeps_the_compatibility_plan_even_though_it_implies_remix()
    {
        var provider = new SourceBackedVanillaWorldGenerationPipeline1458();
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Zenith",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "get fixed boi"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(8, builder.Entries.Count);
        Assert.DoesNotContain(builder.Entries, static entry => entry.Descriptor.Id.Value == "terraria:1.4.5.8/Dunes");
    }

    [Theory]
    [InlineData(4200, 1, 2)]
    [InlineData(6400, 1, 3)]
    [InlineData(8400, 2, 4)]
    public void Dune_count_range_uses_the_pinned_world_width_scaling(int width, int minimum, int maximum)
    {
        Assert.Equal((minimum, maximum), VanillaEarlyWorldGenerationPass1458.GetDuneCountRange(width));
    }

    [Theory]
    [InlineData(false, false, 1, 1, 43)]
    [InlineData(true, false, 1, 0, 41)]
    [InlineData(true, true, 1, 2, 44)]
    public void Remix_dungeon_palette_overrides_the_roll_without_changing_rng_consumption(
        bool remix,
        bool crimson,
        int roll,
        int expectedColor,
        ushort expectedBrick)
    {
        var random = new RecordingRandom(roll);

        VanillaDungeonPalette1458 palette =
            VanillaEarlyWorldGenerationPass1458.SetupDungeonPalette(random, remix, crimson);

        Assert.Equal(1, random.CallCount);
        Assert.Equal(expectedColor, palette.Color);
        Assert.Equal(expectedBrick, palette.BrickTileType);
    }

    [Fact]
    public void Dungeon_setup_consumes_palette_both_entrance_rolls_and_component_seed_in_source_order()
    {
        var random = new RecordingRandom(2, 0, 0, 123456);

        VanillaDungeonSetupProfile1458 profile =
            VanillaEarlyWorldGenerationPass1458.SetupDungeonProfile(random, isRemix: false, crimson: false);

        Assert.Equal(4, random.CallCount);
        Assert.Equal((ushort)44, profile.Palette.BrickTileType);
        Assert.Equal(VanillaDungeonEntranceKind1458.Tower, profile.EntranceKind);
        Assert.Equal(123456, profile.EntranceRandomSeed);
    }

    [Fact]
    public void Remix_liquid_lines_consume_both_rolls_but_replace_lava_with_the_source_formula()
    {
        var random = new RecordingRandom(10, 20);

        (int waterLine, int lavaLine) = VanillaEarlyWorldGenerationPass1458.ResolveLiquidLines(
            random,
            worldSurface: 250d,
            rockLayer: 700d,
            currentRockLayer: 725.75d,
            height: 1200,
            isRemix: true);

        Assert.Equal(960, waterLine);
        Assert.Equal(345, lavaLine);
        Assert.Equal(2, random.CallCount);
    }

    [Fact]
    public void Pyramid_candidate_workspace_state_preserves_source_generation_order()
    {
        var workspace = new RuntimeWorldGenerationWorkspace(64, 64);
        workspace.ResetVanillaPyramidCandidates();
        workspace.AddVanillaPyramidCandidate(10, 20);
        workspace.AddVanillaPyramidCandidate(30, 40);

        VanillaPyramidCandidate1458[] candidates = workspace.CaptureVanillaPyramidCandidates();

        Assert.Equal(
            [new VanillaPyramidCandidate1458(10, 20), new VanillaPyramidCandidate1458(30, 40)],
            candidates);

        workspace.ResetVanillaPyramidCandidates();
        Assert.Empty(workspace.CaptureVanillaPyramidCandidates());
    }

    private static CaptureEntry Find(CaptureBuilder builder, string id) =>
        Assert.Single(builder.Entries, e => e.Descriptor.Id.Value == id);

    private readonly record struct CaptureEntry(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CaptureEntry> Entries { get; } = [];
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) => Entries.Add(new(descriptor, pass));
    }

    private sealed class RecordingRandom(params int[] values) : IWorldGenerationVanillaRandom, VanillaEarlyWorldGenerationPass1458.IRandom
    {
        private int index;
        public int CallCount => index;
        public int Next() => Take();
        public int Next(int maxValue) => Take();
        public int Next(int minValue, int maxValue) => Take();
        public double NextDouble() => throw new NotSupportedException();
        public void NextBytes(byte[] buffer) => throw new NotSupportedException();
        private int Take() => values[index++];
    }
}
