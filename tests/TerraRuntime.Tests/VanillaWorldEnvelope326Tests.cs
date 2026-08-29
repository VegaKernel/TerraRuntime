using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaWorldEnvelope326Tests
{
    [Fact]
    public void Fresh_envelope_owns_current_format_identity_and_frame_catalog()
    {
        int[] pointers = CreateCanonicalPointers();

        WorldFileEnvelope envelope = VanillaWorldEnvelope326.CreateFresh(pointers);

        Assert.Equal(WorldFileFormatPolicy.CurrentVersion, envelope.FormatVersion);
        Assert.Equal(VanillaWorldEnvelope326.FreshRevision, envelope.Revision);
        Assert.Equal(VanillaWorldEnvelope326.FreshFavoriteFlags, envelope.FavoriteFlags);
        Assert.Equal(VanillaWorldFrameImportance326.Count, envelope.FrameImportanceCount);
        Assert.True(VanillaWorldFrameImportance326.PackedBits.SequenceEqual(envelope.FrameImportanceBits.Span));
        Assert.Equal(pointers, envelope.SectionOffsets);
    }

    [Fact]
    public void Fresh_envelope_encodes_to_exact_canonical_length()
    {
        WorldFileEnvelope envelope = VanillaWorldEnvelope326.CreateFresh(CreateCanonicalPointers());
        using var stream = new MemoryStream();

        WorldFileEnvelopeEncodeResult result = WorldFileEnvelopeEncoder.TryEncode(
            envelope,
            stream,
            out long bytesWritten);

        Assert.Equal(WorldFileEnvelopeEncodeResult.Encoded, result);
        Assert.Equal(WorldFileEnvelopeEncoder.CurrentEncodedLength, bytesWritten);
        Assert.Equal(WorldFileEnvelopeEncoder.CurrentEncodedLength, stream.Length);
    }

    [Fact]
    public void Fresh_envelope_rejects_noncanonical_boundaries()
    {
        int[] pointers = CreateCanonicalPointers();
        pointers[0]++;

        Assert.Throws<ArgumentException>(() => VanillaWorldEnvelope326.CreateFresh(pointers));

        pointers = CreateCanonicalPointers();
        pointers[5] = pointers[4];
        Assert.Throws<ArgumentException>(() => VanillaWorldEnvelope326.CreateFresh(pointers));
    }

    private static int[] CreateCanonicalPointers()
    {
        int[] pointers = new int[VanillaWorldFormat326.SectionCount];
        pointers[0] = WorldFileEnvelopeEncoder.CurrentEncodedLength;
        for (int index = 1; index < pointers.Length; index++)
            pointers[index] = pointers[index - 1] + 1;
        return pointers;
    }
}
