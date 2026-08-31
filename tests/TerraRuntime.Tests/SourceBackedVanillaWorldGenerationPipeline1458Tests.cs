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

    private static CaptureEntry Find(CaptureBuilder builder, string id) =>
        Assert.Single(builder.Entries, e => e.Descriptor.Id.Value == id);

    private readonly record struct CaptureEntry(WorldGenerationPassDescriptor Descriptor, IWorldGenerationPass Pass);

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CaptureEntry> Entries { get; } = [];
        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) => Entries.Add(new(descriptor, pass));
    }
}
