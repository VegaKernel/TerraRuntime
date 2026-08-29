using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileTileChestPatchWriterTests
{
    [Fact]
    public void Writes_freshly_loadable_tiles_and_chests_while_preserving_later_sections()
    {
        var dimensions = new WorldDimensions(20, 20);
        byte[] source = BuildSourceWorld(dimensions, out WorldFileEnvelope sourceEnvelope);

        var liveTiles = new WorldTileStore(dimensions);
        var synchronizer = new WorldTileSaveShadowSynchronizer(liveTiles, dirtyBatchCapacity: 4);
        Assert.Equal(dimensions.SectionCount, synchronizer.CaptureBootstrap(dimensions.SectionCount));
        Assert.True(synchronizer.TryCaptureImage(out WorldTileSaveImage? tileImage));
        Assert.NotNull(tileImage);

        WorldChest[] chests =
        [
            new WorldChest(
                0,
                3,
                4,
                "patched",
                [new WorldChestItem(7, 1, 0)])
        ];

        using var destination = new MemoryStream();
        Assert.Equal(
            WorldFileTileChestPatchWriteResult.Written,
            WorldFileTileChestPatchWriter.TryWrite(
                source,
                tileImage!,
                chests,
                destination,
                out long bytesWritten));

        byte[] written = destination.ToArray();
        Assert.Equal(written.Length, bytesWritten);

        WorldFileCoreLoadDiagnostic coreDiagnostic = WorldFileCoreLoader.TryLoad(
            written,
            maxTileCount: 10_000,
            out WorldFileCore? core);
        Assert.Equal(WorldFileCoreLoadResult.Loaded, coreDiagnostic.Result);
        Assert.NotNull(core);
        Assert.Equal(dimensions.WidthTiles, core!.Header.Dimensions.WidthTiles);
        Assert.Equal(dimensions.HeightTiles, core.Header.Dimensions.HeightTiles);

        Assert.Equal(
            WorldFileEnvelopeParseResult.Parsed,
            WorldFileEnvelopeParser.TryParse(written, out WorldFileEnvelope? writtenEnvelope, out _));
        Assert.NotNull(writtenEnvelope);
        Assert.Equal(
            WorldFileHeaderParseResult.Parsed,
            WorldFileHeaderParser.TryParse(written, writtenEnvelope!, out WorldFileHeader? writtenHeader));
        Assert.NotNull(writtenHeader);

        Assert.Equal(
            WorldFileChestDecodeResult.Decoded,
            WorldFileChestDecoder.TryDecode(
                written,
                writtenEnvelope!,
                writtenHeader!,
                maxItemsPerChest: 256,
                maxTotalItems: 8_000,
                out WorldChest[] decodedChests,
                out _));
        WorldChest decoded = Assert.Single(decodedChests);
        Assert.Equal(0, decoded.SlotId);
        Assert.Equal(3, decoded.X);
        Assert.Equal(4, decoded.Y);
        Assert.Equal("patched", decoded.Name);
        WorldChestItem decodedItem = Assert.Single(decoded.Items);
        Assert.Equal(7, decodedItem.Stack);
        Assert.Equal(1, decodedItem.ItemType);
        Assert.Equal(0, decodedItem.Prefix);

        Assert.True(
            source.AsSpan(sourceEnvelope.SectionOffsets[3])
                .SequenceEqual(written.AsSpan(writtenEnvelope!.SectionOffsets[3])));

        int shift = writtenEnvelope.SectionOffsets[3] - sourceEnvelope.SectionOffsets[3];
        for (int section = 4; section < VanillaWorldFormat326.SectionCount; section++)
            Assert.Equal(sourceEnvelope.SectionOffsets[section] + shift, writtenEnvelope.SectionOffsets[section]);
    }

    [Fact]
    public void Rejects_nonempty_destination_before_emitting_any_bytes()
    {
        var dimensions = new WorldDimensions(20, 20);
        byte[] source = BuildSourceWorld(dimensions, out _);
        var liveTiles = new WorldTileStore(dimensions);
        var synchronizer = new WorldTileSaveShadowSynchronizer(liveTiles, dirtyBatchCapacity: 4);
        Assert.Equal(dimensions.SectionCount, synchronizer.CaptureBootstrap(dimensions.SectionCount));
        Assert.True(synchronizer.TryCaptureImage(out WorldTileSaveImage? tileImage));

        using var destination = new MemoryStream([0xAA]);
        destination.Position = 0;
        Assert.Equal(
            WorldFileTileChestPatchWriteResult.DestinationNotEmpty,
            WorldFileTileChestPatchWriter.TryWrite(
                source,
                tileImage!,
                [],
                destination,
                out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal([0xAA], destination.ToArray());
    }

    private static byte[] BuildSourceWorld(
        WorldDimensions dimensions,
        out WorldFileEnvelope envelope)
    {
        var header = new WorldFileHeader(
            "patch-source",
            "seed",
            1,
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            123,
            0,
            dimensions.WidthTiles * 16,
            0,
            dimensions.HeightTiles * 16,
            dimensions);

        using var headerStream = new MemoryStream();
        Assert.Equal(
            WorldFileHeaderPrefixEncodeResult.Encoded,
            WorldFileHeaderPrefixEncoder.TryEncode(header, headerStream, out _));
        byte[] headerBytes = headerStream.ToArray();

        int[] pointers = new int[VanillaWorldFormat326.SectionCount];
        pointers[0] = WorldFileEnvelopeEncoder.CurrentEncodedLength;
        pointers[1] = checked(pointers[0] + headerBytes.Length);
        for (int section = 2; section < pointers.Length; section++)
            pointers[section] = checked(pointers[section - 1] + 1);

        envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 7,
            favoriteFlags: 0,
            pointers,
            VanillaWorldFormat326.TileTypeCount,
            new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);

        byte[] source = new byte[checked(pointers[^1] + 1)];
        using var sourceStream = new MemoryStream(source, writable: true);
        Assert.Equal(
            WorldFileEnvelopeEncodeResult.Encoded,
            WorldFileEnvelopeEncoder.TryEncode(envelope, sourceStream, out _));
        sourceStream.Position = pointers[0];
        sourceStream.Write(headerBytes);

        for (int section = 1; section < pointers.Length; section++)
            source[pointers[section]] = checked((byte)(0x40 + section));

        return source;
    }
}
