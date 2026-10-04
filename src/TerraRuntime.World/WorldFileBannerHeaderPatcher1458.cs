namespace TerraRuntime.World;

/// <summary>Replaces only the decoded BannerSystem byte range; all unowned header bytes remain exact.</summary>
public static class WorldFileBannerHeaderPatcher1458
{
    public static bool TryPatch(ReadOnlySpan<byte> source, WorldFileHeader header, WorldBannerData1458 banners, out byte[] patched)
    {
        ArgumentNullException.ThrowIfNull(header); ArgumentNullException.ThrowIfNull(banners); patched = [];
        if (!banners.IsValid || source.Length > 16 * 1024 * 1024) return false;
        var envelope = new WorldFileEnvelope(WorldFileFormatPolicy.CurrentVersion, 0, 0, [0, source.Length], 0, []);
        var limits = new WorldFileRuntimeMetadataLimits(16 * 1024, 4L * 1024 * 1024, 100000, WorldBannerData1458.MaximumEntries, 100000, 4 * 1024 * 1024);
        if (WorldFileRuntimeMetadataParser.TryParse(source, envelope, header, limits, out var metadata, out _) != WorldFileRuntimeMetadataParseResult.Parsed || metadata is null)
            return false;
        byte[] replacement = banners.Encode();
        int offset = metadata.BannerSectionOffset, length = metadata.BannerSectionLength;
        patched = new byte[checked(source.Length - length + replacement.Length)];
        source[..offset].CopyTo(patched); replacement.CopyTo(patched.AsSpan(offset));
        source[(offset + length)..].CopyTo(patched.AsSpan(offset + replacement.Length));
        return true;
    }
}
