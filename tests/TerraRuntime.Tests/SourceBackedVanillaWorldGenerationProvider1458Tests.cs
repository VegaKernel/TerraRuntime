using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SourceBackedVanillaWorldGenerationProvider1458Tests
{
    [Theory]
    [InlineData(4200, 1200, true)]
    [InlineData(6400, 1800, true)]
    [InlineData(8400, 2400, true)]
    [InlineData(192, 128, false)]
    [InlineData(4200, 1800, false)]
    public void Terrain_source_port_is_limited_to_official_world_dimensions(int width, int height, bool expected)
    {
        Assert.Equal(expected, VanillaTerrainPass1458.IsCanonicalWorldSize(width, height));
    }

    [Fact]
    public void Source_backed_provider_inserts_reset_before_terrain_without_changing_generator_identity()
    {
        var provider = new SourceBackedVanillaWorldGenerationProvider1458();
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Plan",
            Seed: 1458,
            WidthTiles: 192,
            HeightTiles: 128);
        var builder = new CaptureBuilder();

        provider.BuildPlan(in request, builder);

        Assert.Equal(VanillaWorldGenerationProvider1458.GeneratorId, provider.Id);
        Assert.Equal(8, builder.Entries.Count);
        CaptureEntry reset = Assert.Single(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationProvider1458.ResetPassId);
        CaptureEntry terrain = Assert.Single(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationProvider1458.TerrainPassId);
        CaptureEntry metadata = Assert.Single(builder.Entries, static entry =>
            entry.Descriptor.Id == SourceBackedVanillaWorldGenerationProvider1458.MetadataPassId);
        Assert.IsType<VanillaWorldGenerationBootstrapPass1458>(reset.Pass);
        Assert.IsType<VanillaTerrainPass1458>(terrain.Pass);
        Assert.IsType<VanillaMetadataParityPass1458>(metadata.Pass);
        Assert.Contains(
            SourceBackedVanillaWorldGenerationProvider1458.ResetPassId,
            terrain.Descriptor.RequiredAfter.ToArray());
        Assert.Equal(WorldGenerationRngMode.VanillaSharedRng, reset.Descriptor.RngMode);
    }

    [Fact]
    public void Ordinary_small_world_reset_matches_pinned_rng_checkpoint_and_world_origins()
    {
        var sourceRandom = new VanillaUnifiedRandom1458(1458);
        var adapter = new VanillaRandomAdapter(sourceRandom);

        VanillaWorldGenerationBootstrapState1458 bootstrap =
            VanillaWorldGenerationBootstrapPass1458.Run(adapter, 4200, effectiveCrimson: false);

        Assert.Equal(-1, bootstrap.DungeonSide);
        Assert.Equal(3402, bootstrap.JungleOriginX);
        Assert.Equal(1531, bootstrap.SnowOriginLeft);
        Assert.Equal(1801, bootstrap.SnowOriginRight);
        Assert.Equal(322, bootstrap.LeftBeachEnd);
        Assert.Equal(3830, bootstrap.RightBeachStart);
        Assert.Equal(484, bootstrap.DungeonLocation);
        Assert.Equal(2049485220, bootstrap.WorldId);
        Assert.Equal(new[] { 7, 167, 9, 8 },
            new[] { bootstrap.CopperOre, bootstrap.IronOre, bootstrap.SilverOre, bootstrap.GoldOre });
        Assert.Equal(new[] { 1356, 4200, 4200 }, bootstrap.TreeX);
        Assert.Equal(new[] { 5, 3, 0, 0 }, bootstrap.TreeStyle);
        Assert.Equal(6, bootstrap.MoonType);
        Assert.False(bootstrap.EffectiveCrimson);

        // This is the first value Terrain must observe after the ordinary WorldGen.Reset bootstrap for seed 1458.
        Assert.Equal(289143048, sourceRandom.Next());
    }

    [Fact]
    public void Noncanonical_reset_is_a_true_noop_and_does_not_advance_vanilla_rng()
    {
        var state = new VanillaWorldGenerationParityState1458();
        var pass = new VanillaWorldGenerationBootstrapPass1458(state);
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Synthetic",
            Seed: 1,
            WidthTiles: 192,
            HeightTiles: 128);
        var sourceRandom = new VanillaUnifiedRandom1458(1);
        var context = new GenerationContext(
            request,
            new CountingWorkspace(192, 128),
            new VanillaRandomAdapter(sourceRandom));

        pass.Execute(context);

        Assert.Null(state.Bootstrap);
        Assert.Equal(534011718, sourceRandom.Next());
    }

    [Fact]
    public void Canonical_default_terrain_consumes_reset_state_and_publishes_layers()
    {
        bool fallbackExecuted = false;
        var fallback = new ActionPass(_ => fallbackExecuted = true);
        var state = new VanillaWorldGenerationParityState1458();
        var bootstrapPass = new VanillaWorldGenerationBootstrapPass1458(state);
        var terrainPass = new VanillaTerrainPass1458(fallback, state);
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Canonical",
            Seed: 1458,
            WidthTiles: 4200,
            HeightTiles: 1200)
        {
            SeedText = "1458"
        };
        var workspace = new CountingWorkspace(request.WidthTiles, request.HeightTiles);
        var context = new GenerationContext(
            request,
            workspace,
            new VanillaRandomAdapter(new VanillaUnifiedRandom1458(1458)));

        bootstrapPass.Execute(context);
        terrainPass.Execute(context);

        Assert.False(fallbackExecuted);
        Assert.NotNull(state.Bootstrap);
        Assert.Equal(322, state.Bootstrap!.LeftBeachEnd);
        Assert.Equal(3830, state.Bootstrap.RightBeachStart);
        Assert.True(workspace.TryGetLayers(out WorldGenerationLayers layers));
        Assert.True(layers.WorldSurface > 0d);
        Assert.True(layers.RockLayer > layers.WorldSurface);
        Assert.Equal(layers, state.TerrainLayers);
        Assert.True(workspace.SetCount >= (long)request.WidthTiles * request.HeightTiles);
    }

    [Fact]
    public void Noncanonical_world_keeps_existing_compatibility_terrain()
    {
        bool fallbackExecuted = false;
        var fallback = new ActionPass(_ => fallbackExecuted = true);
        var pass = new VanillaTerrainPass1458(fallback, new VanillaWorldGenerationParityState1458());
        var request = new WorldGenerationRequest(
            VanillaWorldGenerationProvider1458.GeneratorId,
            "Compatibility",
            Seed: 1,
            WidthTiles: 192,
            HeightTiles: 128);
        var workspace = new CountingWorkspace(192, 128);
        var context = new GenerationContext(request, workspace, new VanillaRandomAdapter(new VanillaUnifiedRandom1458(1)));

        pass.Execute(context);

        Assert.True(fallbackExecuted);
        Assert.Equal(0, workspace.SetCount);
    }

    private readonly record struct CaptureEntry(
        WorldGenerationPassDescriptor Descriptor,
        IWorldGenerationPass Pass);

    private sealed class CaptureBuilder : IWorldGenerationPlanBuilder
    {
        public List<CaptureEntry> Entries { get; } = [];

        public void Add(WorldGenerationPassDescriptor descriptor, IWorldGenerationPass pass) =>
            Entries.Add(new CaptureEntry(descriptor, pass));
    }

    private sealed class ActionPass : IWorldGenerationPass
    {
        private readonly Action<IWorldGenerationContext> action;

        public ActionPass(Action<IWorldGenerationContext> action) => this.action = action;

        public void Execute(IWorldGenerationContext context) => action(context);
    }

    private sealed class VanillaRandomAdapter : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random;

        public VanillaRandomAdapter(VanillaUnifiedRandom1458 random) => this.random = random;

        public int Next() => random.Next();
        public int Next(int maxValue) => random.Next(maxValue);
        public int Next(int minValue, int maxValue) => random.Next(minValue, maxValue);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }

    private sealed class GenerationContext : IWorldGenerationContext
    {
        public GenerationContext(
            WorldGenerationRequest request,
            CountingWorkspace workspace,
            IWorldGenerationVanillaRandom vanillaRandom)
        {
            Request = request;
            Workspace = workspace;
            Metadata = workspace;
            VanillaRandom = vanillaRandom;
        }

        public WorldGenerationRequest Request { get; }
        public IWorldGenerationWorkspace Workspace { get; }
        public IWorldGenerationMetadataWorkspace? Metadata { get; }
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom? VanillaRandom { get; }
        public CancellationToken CancellationToken => global::System.Threading.CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null) { }
    }

    private sealed class CountingWorkspace : IWorldGenerationWorkspace, IWorldGenerationMetadataWorkspace
    {
        private WorldGenerationLayers? layers;

        public CountingWorkspace(int width, int height)
        {
            WidthTiles = width;
            HeightTiles = height;
        }

        public int WidthTiles { get; }
        public int HeightTiles { get; }
        public long SetCount { get; private set; }

        public bool TryGetTile(int x, int y, out WorldGenerationTile tile)
        {
            if ((uint)x >= (uint)WidthTiles || (uint)y >= (uint)HeightTiles)
            {
                tile = default;
                return false;
            }

            tile = default;
            return true;
        }

        public bool TrySetTile(int x, int y, in WorldGenerationTile tile)
        {
            if ((uint)x >= (uint)WidthTiles || (uint)y >= (uint)HeightTiles)
                return false;

            SetCount++;
            return true;
        }

        public bool TryGetSpawn(out WorldGenerationPoint spawn)
        {
            spawn = default;
            return false;
        }

        public bool TrySetSpawn(int x, int y) => true;

        public bool TryGetDungeon(out WorldGenerationPoint dungeon)
        {
            dungeon = default;
            return false;
        }

        public bool TrySetDungeon(int x, int y) => true;

        public bool TryGetLayers(out WorldGenerationLayers value)
        {
            if (layers is not WorldGenerationLayers current)
            {
                value = default;
                return false;
            }

            value = current;
            return true;
        }

        public bool TrySetLayers(double worldSurface, double rockLayer)
        {
            if (!(worldSurface > 0d && rockLayer > worldSurface && rockLayer < HeightTiles))
                return false;

            layers = new WorldGenerationLayers(worldSurface, rockLayer);
            return true;
        }
    }
}
