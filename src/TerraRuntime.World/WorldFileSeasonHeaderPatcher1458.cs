namespace TerraRuntime.World;

public enum WorldFileSeasonHeaderPatchResult1458 : byte
{
    Patched,
    UnsupportedVersion,
    InvalidHeader
}

/// <summary>Changes only explicitly owned temporary-season booleans in a validated current world header.</summary>
public static class WorldFileSeasonHeaderPatcher1458
{
    private const int MaximumHeaderBytes = 16 * 1024 * 1024;
    private static readonly WorldFileRuntimeMetadataLimits Limits = new(
        16 * 1024, MaximumHeaderBytes, WorldAnglerQuestData1458.MaximumCompletedNames,
        WorldBannerData1458.MaximumEntries, 255, MaximumHeaderBytes);

    public static WorldFileSeasonHeaderPatchResult1458 TryPatch(ReadOnlySpan<byte> sourceHeader,
        WorldFileEnvelope envelope, WorldFileHeader header, out byte[] patchedHeader,
        bool? forceHalloweenForToday = null, bool? forceXMasForToday = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(header);
        patchedHeader = [];
        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFileSeasonHeaderPatchResult1458.UnsupportedVersion;
        if (sourceHeader.IsEmpty || sourceHeader.Length > MaximumHeaderBytes)
            return WorldFileSeasonHeaderPatchResult1458.InvalidHeader;
        // SaveWorldHeader 1.4.5.8 places these after variable-length angler, banner,
        // party and TreeTops fields. Reuse the complete parser's validated cursor;
        // neither a fixed offset nor a prepared-cache cursor is a save authority.
        var local = new WorldFileEnvelope(envelope.FormatVersion, envelope.Revision, envelope.FavoriteFlags,
            [0, sourceHeader.Length], 0, []);
        if (WorldFileRuntimeMetadataParser.TryParse(sourceHeader, local, header, Limits,
                out var metadata, out _) != WorldFileRuntimeMetadataParseResult.Parsed || metadata is null)
            return WorldFileSeasonHeaderPatchResult1458.InvalidHeader;
        patchedHeader = sourceHeader.ToArray();
        int offset = metadata.SeasonalFlagsOffset;
        if (forceHalloweenForToday is { } halloween) patchedHeader[offset] = halloween ? (byte)1 : (byte)0;
        if (forceXMasForToday is { } christmas) patchedHeader[offset + 1] = christmas ? (byte)1 : (byte)0;
        return WorldFileSeasonHeaderPatchResult1458.Patched;
    }
}
