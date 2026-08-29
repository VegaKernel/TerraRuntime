namespace TerraRuntime.World;

/// <summary>
/// Source-backed constructor for a fresh Terraria 1.4.5.8 world-file envelope. Callers provide only section
/// boundaries; format identity, initial revision, favorite flags and frame-important bits remain runtime-owned.
/// </summary>
public static class VanillaWorldEnvelope326
{
    public const uint FreshRevision = 1;
    public const ulong FreshFavoriteFlags = 0;

    public static WorldFileEnvelope CreateFresh(ReadOnlySpan<int> sectionOffsets)
    {
        if (sectionOffsets.Length != VanillaWorldFormat326.SectionCount)
        {
            throw new ArgumentException(
                $"Fresh format-326 worlds require exactly {VanillaWorldFormat326.SectionCount} section pointers.",
                nameof(sectionOffsets));
        }

        if (sectionOffsets[0] != WorldFileEnvelopeEncoder.CurrentEncodedLength)
        {
            throw new ArgumentException(
                $"The first section pointer must equal the canonical envelope length {WorldFileEnvelopeEncoder.CurrentEncodedLength}.",
                nameof(sectionOffsets));
        }

        for (int index = 1; index < sectionOffsets.Length; index++)
        {
            if (sectionOffsets[index] <= sectionOffsets[index - 1])
            {
                throw new ArgumentException(
                    "Fresh world section pointers must be strictly increasing.",
                    nameof(sectionOffsets));
            }
        }

        return new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            FreshRevision,
            FreshFavoriteFlags,
            sectionOffsets.ToArray(),
            VanillaWorldFrameImportance326.Count,
            VanillaWorldFrameImportance326.CopyPackedBits());
    }
}
