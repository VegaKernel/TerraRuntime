namespace TerraRuntime.World;

public sealed class WorldFileEnvelope
{
    internal WorldFileEnvelope(
        int formatVersion,
        uint revision,
        ulong favoriteFlags,
        int[] sectionOffsets,
        int frameImportanceCount,
        byte[] frameImportanceBits)
    {
        FormatVersion = formatVersion;
        Revision = revision;
        FavoriteFlags = favoriteFlags;
        SectionOffsets = sectionOffsets;
        FrameImportanceCount = frameImportanceCount;
        FrameImportanceBits = frameImportanceBits;
    }

    public int FormatVersion { get; }

    public uint Revision { get; }

    public ulong FavoriteFlags { get; }

    public IReadOnlyList<int> SectionOffsets { get; }

    public int FrameImportanceCount { get; }

    public ReadOnlyMemory<byte> FrameImportanceBits { get; }

    public WorldFormatCompatibility Compatibility => WorldFileFormatPolicy.Assess(FormatVersion);

    public bool IsFrameImportant(int tileType)
    {
        if ((uint)tileType >= (uint)FrameImportanceCount)
        {
            return false;
        }

        ReadOnlySpan<byte> bits = FrameImportanceBits.Span;
        return (bits[tileType >> 3] & (1 << (tileType & 7))) != 0;
    }
}
