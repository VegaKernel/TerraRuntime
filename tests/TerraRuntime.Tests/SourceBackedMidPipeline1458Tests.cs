using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedMidPipeline1458Tests
{
    [Fact]
    public void Canonical_ordinary_world_expands_source_order_from_jungle_through_slush()
    {
        var provider = new SourceBackedMidPipeline1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "StageTwo",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(Provider1458.GeneratorId, provider.Id);
        Assert.Equal(40, builder.Entries.Count);
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
            "terraria:1.4.5.8/MudCavesToGrass",
            "terraria:1.4.5.8/FullDesert",
            "terraria:1.4.5.8/MushroomPatches",
            "terraria:1.4.5.8/Marble",
            "terraria:1.4.5.8/Granite",
            "terraria:1.4.5.8/FloatingIslands",
            "terraria:1.4.5.8/DirtToMud",
            "terraria:1.4.5.8/Silt",
            "terraria:1.4.5.8/Shinies",
            "terraria:1.4.5.8/Webs",
            "terraria:1.4.5.8/Underworld",
            "terraria:1.4.5.8/Corruption",
            "terraria:1.4.5.8/Lakes",
            "terraria:1.4.5.8/Slush",
            "terraria:1.4.5.8/Biomes",
            "terraria:1.4.5.8/Caves",
            "terraria:1.4.5.8/Ores",
            "terraria:1.4.5.8/Dungeon",
            "terraria:1.4.5.8/SecretSeeds",
            "terraria:1.4.5.8/Metadata"
        ];

        Assert.Equal(expected, builder.Entries.Select(static e => e.Descriptor.Id.Value));

        string[] midPasses =
        [
            "terraria:1.4.5.8/MudCavesToGrass",
            "terraria:1.4.5.8/FullDesert",
            "terraria:1.4.5.8/MushroomPatches",
            "terraria:1.4.5.8/Marble",
            "terraria:1.4.5.8/Granite",
            "terraria:1.4.5.8/FloatingIslands",
            "terraria:1.4.5.8/DirtToMud",
            "terraria:1.4.5.8/Silt",
            "terraria:1.4.5.8/Shinies",
            "terraria:1.4.5.8/Webs",
            "terraria:1.4.5.8/Underworld",
            "terraria:1.4.5.8/Corruption",
            "terraria:1.4.5.8/Lakes",
            "terraria:1.4.5.8/Slush"
        ];

        foreach (string passId in midPasses)
            Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, Find(builder, passId).Descriptor.RngMode);

        CaptureEntry residualBiomes = Find(builder, "terraria:1.4.5.8/Biomes");
        Assert.Equal(WorldGenerationRngMode.IsolatedDeterministic, residualBiomes.Descriptor.RngMode);
        Assert.IsType<OceanResidualCompatibilityBiomesPass1458>(residualBiomes.Pass);
        Assert.Contains(SourceBackedMidPipeline1458.SlushId, residualBiomes.Descriptor.RequiredAfter.ToArray());

        CaptureEntry ores = Find(builder, "terraria:1.4.5.8/Ores");
        Assert.IsType<SourceBackedOreCompatibilityBarrier1458>(ores.Pass);
        Assert.Contains(new WorldGenerationPassId("terraria:1.4.5.8/Caves"), ores.Descriptor.RequiredAfter.ToArray());
    }

    [Fact]
    public void Noncanonical_world_keeps_existing_compatibility_plan_unchanged()
    {
        var provider = new SourceBackedMidPipeline1458();
        var request = new WorldGenerationRequest(
            Provider1458.GeneratorId,
            "Synthetic",
            Seed: 1458,
            WidthTiles: 192,
            HeightTiles: 128);
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(8, builder.Entries.Count);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedMidPipeline1458.FullDesertId);
        Assert.DoesNotContain(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedMidPipeline1458.ShiniesId);
    }

    private static CaptureEntry Find(CaptureBuilder builder, string id) =>
        Assert.Single(builder.Entries, entry => entry.Descriptor.Id.Value == id);

    private readonly record struct CaptureEntry(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CaptureEntry> Entries { get; } = [];

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            Entries.Add(new CaptureEntry(descriptor, pass));
    }
}
