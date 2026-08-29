using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileFreshRuntimeMetadata326EncoderTests
{
    [Fact]
    public void Fresh_metadata_roundtrips_through_current_runtime_parser()
    {
        WorldFileHeader header = VanillaFreshWorldHeader326.Create(
            "Flat",
            "123",
            widthTiles: 64,
            heightTiles: 48,
            Guid.Parse("3f0bc1e7-48e5-4291-9437-d66ff5cb6411"),
            worldId: 99);
        var generation = new RuntimeWorldGenerationMetadataSnapshot(
            new WorldGenerationPoint(32, 15),
            new WorldGenerationPoint(8, 15),
            new WorldGenerationLayers(16d, 30d));
        var source = new WorldFileFreshRuntimeMetadata326(
            generation,
            GameMode: 0,
            Crimson: false,
            CreationTimeBinary: 123456789,
            LastPlayedBinary: 123456790);

        using var section = new MemoryStream();
        Assert.Equal(
            WorldFileHeaderPrefixEncodeResult.Encoded,
            WorldFileHeaderPrefixEncoder.TryEncode(header, section, out long prefixBytes));
        Assert.Equal(
            WorldFileFreshRuntimeMetadata326EncodeResult.Encoded,
            WorldFileFreshRuntimeMetadata326Encoder.TryEncode(header, in source, section, out long metadataBytes));
        Assert.Equal(section.Length, prefixBytes + metadataBytes);

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
            MaxAnglerNames: 64,
            MaxBannerEntries: 1024,
            MaxPartyNpcEntries: 64,
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
        Assert.Equal((short)32, metadata.SpawnX);
        Assert.Equal((short)15, metadata.SpawnY);
        Assert.Equal((short)8, metadata.DungeonX);
        Assert.Equal((short)15, metadata.DungeonY);
        Assert.Equal((short)16, metadata.WorldSurface);
        Assert.Equal((short)30, metadata.RockLayer);
        Assert.Equal(13500, metadata.Time);
        Assert.True(metadata.DayTime);
        Assert.False(metadata.HardMode);
        Assert.Equal(new WorldOreTiers(7, 6, 9, 8, -1, -1, -1), metadata.OreTiers);
        Assert.Equal(WorldFileFreshRuntimeMetadata326Encoder.InitialCultistDelay > 0, true);
        Assert.Empty(metadata.ExtraSpawnPoints);
    }

    [Fact]
    public void Fresh_metadata_rejects_semantic_anchors_outside_header_dimensions()
    {
        WorldFileHeader header = VanillaFreshWorldHeader326.Create(
            "Flat", "1", 32, 24, Guid.NewGuid(), 1);
        var source = new WorldFileFreshRuntimeMetadata326(
            new RuntimeWorldGenerationMetadataSnapshot(
                new WorldGenerationPoint(32, 8),
                new WorldGenerationPoint(1, 8),
                new WorldGenerationLayers(8d, 16d)),
            GameMode: 0,
            Crimson: false,
            CreationTimeBinary: 1,
            LastPlayedBinary: 1);
        using var output = new MemoryStream();

        Assert.Equal(
            WorldFileFreshRuntimeMetadata326EncodeResult.InvalidMetadata,
            WorldFileFreshRuntimeMetadata326Encoder.TryEncode(header, in source, output, out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, output.Length);
    }
}
