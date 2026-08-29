using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileEnvelopeEncoderTests
{
    [Fact]
    public void Roundtrips_current_envelope_through_parser()
    {
        int envelopeLength = WorldFileEnvelopeEncoder.CurrentEncodedLength;
        int[] pointers = CreatePointers(envelopeLength);
        byte[] importance = new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3];
        importance[1 >> 3] |= (byte)(1 << (1 & 7));
        importance[300 >> 3] |= (byte)(1 << (300 & 7));
        var source = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 42,
            favoriteFlags: 0x1234UL,
            sectionOffsets: pointers,
            frameImportanceCount: VanillaWorldFormat326.TileTypeCount,
            frameImportanceBits: importance);

        using var stream = new MemoryStream();
        Assert.Equal(
            WorldFileEnvelopeEncodeResult.Encoded,
            WorldFileEnvelopeEncoder.TryEncode(source, stream, out long bytesWritten));
        Assert.Equal(envelopeLength, bytesWritten);
        Assert.Equal(envelopeLength, stream.Length);

        stream.Write(new byte[pointers[^1] - envelopeLength]);
        byte[] file = stream.ToArray();
        Assert.Equal(
            WorldFileEnvelopeParseResult.Parsed,
            WorldFileEnvelopeParser.TryParse(file, out WorldFileEnvelope? decoded, out int parsedLength));

        Assert.Equal(envelopeLength, parsedLength);
        Assert.NotNull(decoded);
        Assert.Equal(source.FormatVersion, decoded!.FormatVersion);
        Assert.Equal(source.Revision, decoded.Revision);
        Assert.Equal(source.FavoriteFlags, decoded.FavoriteFlags);
        Assert.Equal(source.SectionOffsets, decoded.SectionOffsets);
        Assert.Equal(source.FrameImportanceCount, decoded.FrameImportanceCount);
        Assert.Equal(source.FrameImportanceBits.ToArray(), decoded.FrameImportanceBits.ToArray());
        Assert.True(decoded.IsFrameImportant(1));
        Assert.True(decoded.IsFrameImportant(300));
    }

    [Fact]
    public void Rejects_noncanonical_section_pointers_before_writing()
    {
        int envelopeLength = WorldFileEnvelopeEncoder.CurrentEncodedLength;
        byte[] importance = new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3];

        int[] wrongFirst = CreatePointers(envelopeLength);
        wrongFirst[0]++;
        using var firstStream = new MemoryStream();
        Assert.Equal(
            WorldFileEnvelopeEncodeResult.InvalidSectionPointers,
            WorldFileEnvelopeEncoder.TryEncode(
                Envelope(wrongFirst, importance),
                firstStream,
                out long firstBytes));
        Assert.Equal(0, firstBytes);
        Assert.Equal(0, firstStream.Length);

        int[] nonMonotonic = CreatePointers(envelopeLength);
        nonMonotonic[5] = nonMonotonic[4];
        using var monotonicStream = new MemoryStream();
        Assert.Equal(
            WorldFileEnvelopeEncodeResult.InvalidSectionPointers,
            WorldFileEnvelopeEncoder.TryEncode(
                Envelope(nonMonotonic, importance),
                monotonicStream,
                out long monotonicBytes));
        Assert.Equal(0, monotonicBytes);
        Assert.Equal(0, monotonicStream.Length);
    }

    [Fact]
    public void Rejects_unknown_version_and_shape_mismatches_before_writing()
    {
        int envelopeLength = WorldFileEnvelopeEncoder.CurrentEncodedLength;
        int[] pointers = CreatePointers(envelopeLength);
        byte[] importance = new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3];

        using var versionStream = new MemoryStream();
        var future = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion + 1,
            1,
            0,
            pointers,
            VanillaWorldFormat326.TileTypeCount,
            importance);
        Assert.Equal(
            WorldFileEnvelopeEncodeResult.UnsupportedVersion,
            WorldFileEnvelopeEncoder.TryEncode(future, versionStream, out long versionBytes));
        Assert.Equal(0, versionBytes);
        Assert.Equal(0, versionStream.Length);

        using var sectionStream = new MemoryStream();
        var wrongSections = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            1,
            0,
            pointers[..^1],
            VanillaWorldFormat326.TileTypeCount,
            importance);
        Assert.Equal(
            WorldFileEnvelopeEncodeResult.InvalidSectionCount,
            WorldFileEnvelopeEncoder.TryEncode(wrongSections, sectionStream, out long sectionBytes));
        Assert.Equal(0, sectionBytes);
        Assert.Equal(0, sectionStream.Length);

        using var importanceStream = new MemoryStream();
        var wrongImportance = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            1,
            0,
            pointers,
            VanillaWorldFormat326.TileTypeCount - 1,
            importance);
        Assert.Equal(
            WorldFileEnvelopeEncodeResult.InvalidFrameImportance,
            WorldFileEnvelopeEncoder.TryEncode(wrongImportance, importanceStream, out long importanceBytes));
        Assert.Equal(0, importanceBytes);
        Assert.Equal(0, importanceStream.Length);
    }

    private static WorldFileEnvelope Envelope(int[] pointers, byte[] importance) =>
        new(
            WorldFileFormatPolicy.CurrentVersion,
            1,
            0,
            pointers,
            VanillaWorldFormat326.TileTypeCount,
            importance);

    private static int[] CreatePointers(int first)
    {
        var pointers = new int[VanillaWorldFormat326.SectionCount];
        for (int i = 0; i < pointers.Length; i++)
            pointers[i] = first + i;
        return pointers;
    }
}
