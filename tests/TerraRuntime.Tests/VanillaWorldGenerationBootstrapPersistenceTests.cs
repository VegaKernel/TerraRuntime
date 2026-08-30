using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldGenerationBootstrapPersistenceTests
{
    [Fact]
    public void Metadata_pass_transfers_reset_state_into_finalized_runtime_snapshot()
    {
        var random = new VanillaRandomAdapter(new VanillaUnifiedRandom1458(1458));
        VanillaWorldGenerationBootstrapState1458 bootstrap =
            VanillaWorldGenerationBootstrapPass1458.Run(random, 4200, effectiveCrimson: false);
        var state = new VanillaWorldGenerationParityState1458
        {
            Bootstrap = bootstrap,
            TerrainLayers = new WorldGenerationLayers(300d, 500d)
        };
        var workspace = new RuntimeWorldGenerationWorkspace(4200, 1200);
        var fallback = new ActionPass(context =>
        {
            Assert.NotNull(context.Metadata);
            Assert.True(context.Metadata!.TrySetSpawn(2100, 280));
            Assert.True(context.Metadata.TrySetDungeon(500, 350));
            Assert.True(context.Metadata.TrySetLayers(310d, 510d));
        });
        var pass = new VanillaMetadataParityPass1458(fallback, state);
        var context = new GenerationContext(workspace);

        pass.Execute(context);
        RuntimeWorldGenerationFinalizationResult finalized = RuntimeWorldGenerationFinalizer.Finalize(workspace);

        Assert.True(finalized.Succeeded);
        Assert.Same(bootstrap, finalized.Metadata.VanillaBootstrapState);
        Assert.Equal(new WorldGenerationLayers(300d, 500d), finalized.Metadata.Layers);
    }

    [Fact]
    public void Source_backed_reset_state_roundtrips_through_fresh_wld_runtime_metadata()
    {
        var random = new VanillaRandomAdapter(new VanillaUnifiedRandom1458(1458));
        VanillaWorldGenerationBootstrapState1458 bootstrap =
            VanillaWorldGenerationBootstrapPass1458.Run(random, 4200, effectiveCrimson: false);
        WorldFileHeader header = VanillaFreshWorldHeader326.Create(
            "VanillaBootstrap",
            "1458",
            widthTiles: 4200,
            heightTiles: 1200,
            Guid.Parse("14580000-0000-4000-8000-000000000002"),
            worldId: 145800002);
        var generation = new RuntimeWorldGenerationMetadataSnapshot(
            new WorldGenerationPoint(2100, 280),
            new WorldGenerationPoint(500, 350),
            new WorldGenerationLayers(300d, 500d))
        {
            VanillaBootstrapState = bootstrap
        };
        var source = new WorldFileFreshRuntimeMetadata326(
            generation,
            GameMode: 0,
            Crimson: false,
            CreationTimeBinary: 123456789,
            LastPlayedBinary: 123456790);

        using var section = new MemoryStream();
        Assert.Equal(
            WorldFileHeaderPrefixEncodeResult.Encoded,
            WorldFileHeaderPrefixEncoder.TryEncode(header, section, out _));
        Assert.Equal(
            WorldFileFreshRuntimeMetadata326EncodeResult.Encoded,
            WorldFileFreshRuntimeMetadata326Encoder.TryEncode(header, in source, section, out _));

        byte[] file = section.ToArray();
        int[] pointers = new int[VanillaWorldFormat326.SectionCount];
        pointers[0] = 0;
        pointers[1] = file.Length;
        for (int i = 2; i < pointers.Length; i++)
            pointers[i] = file.Length + i - 1;
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            pointers,
            VanillaWorldFrameImportance326.Count,
            VanillaWorldFrameImportance326.CopyPackedBits());
        var limits = new WorldFileRuntimeMetadataLimits(
            MaxStringBytes: 4096,
            MaxTotalStringBytes: 16384,
            MaxAnglerNames: 0,
            MaxBannerEntries: 0,
            MaxPartyNpcEntries: 0,
            MaxManifestBytes: 16384);

        WorldFileRuntimeMetadataParseResult result = WorldFileRuntimeMetadataParser.TryParse(
            file,
            envelope,
            header,
            limits,
            out WorldFileRuntimeMetadata? metadata,
            out int bytesConsumed);

        Assert.Equal(WorldFileRuntimeMetadataParseResult.Parsed, result);
        Assert.NotNull(metadata);
        Assert.Equal(file.Length, bytesConsumed);
        Assert.Equal((byte)bootstrap.MoonType, metadata.MoonType);
        Assert.Equal(bootstrap.TreeX, metadata.TreeX);
        Assert.Equal(bootstrap.TreeStyle.Select(static value => checked((byte)value)).ToArray(), metadata.TreeStyles);
        Assert.Equal(bootstrap.CaveBackX, metadata.CaveBackX);
        Assert.Equal(bootstrap.CaveBackStyle.Select(static value => checked((byte)value)).ToArray(), metadata.CaveBackStyles);
        Assert.Equal((byte)bootstrap.IceBackStyle, metadata.IceBackStyle);
        Assert.Equal((byte)bootstrap.JungleBackStyle, metadata.JungleBackStyle);
        Assert.Equal((byte)bootstrap.HellBackStyle, metadata.HellBackStyle);
        Assert.Equal((double)bootstrap.SlimeRainTime, metadata.SlimeRainTime);
        Assert.Equal(
            new WorldOreTiers(
                checked((short)bootstrap.CopperOre),
                checked((short)bootstrap.IronOre),
                checked((short)bootstrap.SilverOre),
                checked((short)bootstrap.GoldOre),
                -1,
                -1,
                -1),
            metadata.OreTiers);
        Assert.Equal((byte)bootstrap.ForestBackgroundStyles[0], metadata.TreeBackground);
        Assert.Equal((byte)bootstrap.ForestBackgroundStyles[1], metadata.TreeBackground2);
        Assert.Equal((byte)bootstrap.ForestBackgroundStyles[2], metadata.TreeBackground3);
        Assert.Equal((byte)bootstrap.ForestBackgroundStyles[3], metadata.TreeBackground4);
        Assert.Equal((byte)bootstrap.CorruptBackground, metadata.CorruptionBackground);
        Assert.Equal((byte)bootstrap.JungleBackground, metadata.JungleBackground);
        Assert.Equal((byte)bootstrap.SnowBackground, metadata.SnowBackground);
        Assert.Equal((byte)bootstrap.HallowBackground, metadata.HallowBackground);
        Assert.Equal((byte)bootstrap.CrimsonBackground, metadata.CrimsonBackground);
        Assert.Equal((byte)bootstrap.DesertBackground, metadata.DesertBackground);
        Assert.Equal((byte)bootstrap.OceanBackground, metadata.OceanBackground);
        Assert.Equal((byte)bootstrap.MushroomBackground, metadata.MushroomBackground);
        Assert.Equal((byte)bootstrap.UnderworldBackground, metadata.UnderworldBackground);
        Assert.False(metadata.CloudBackgroundActive);
        Assert.Equal((byte)bootstrap.NumClouds, metadata.CloudCount);
        Assert.Equal(bootstrap.WindSpeedCurrent, metadata.WindSpeed);
    }

    private sealed class ActionPass : IWorldGenerationPass
    {
        private readonly Action<IWorldGenerationContext> action;

        public ActionPass(Action<IWorldGenerationContext> action) => this.action = action;

        public void Execute(IWorldGenerationContext context) => action(context);
    }

    private sealed class GenerationContext : IWorldGenerationContext
    {
        public GenerationContext(RuntimeWorldGenerationWorkspace workspace)
        {
            Workspace = workspace;
            Metadata = workspace;
            Request = new WorldGenerationRequest(
                VanillaWorldGenerationProvider1458.GeneratorId,
                "BootstrapPersistence",
                1458,
                workspace.WidthTiles,
                workspace.HeightTiles)
            {
                SeedText = "1458"
            };
        }

        public WorldGenerationRequest Request { get; }
        public IWorldGenerationWorkspace Workspace { get; }
        public IWorldGenerationMetadataWorkspace? Metadata { get; }
        public IWorldGenerationRandom Random => throw new NotSupportedException();
        public IWorldGenerationVanillaRandom? VanillaRandom => null;
        public global::System.Threading.CancellationToken CancellationToken =>
            global::System.Threading.CancellationToken.None;
        public void ReportProgress(double fraction, string? message = null) { }
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
}
