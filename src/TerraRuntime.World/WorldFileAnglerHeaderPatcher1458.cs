using System.Text;

namespace TerraRuntime.World;

/// <summary>Replaces the validated variable-length Angler fields while preserving all following world-header bytes.</summary>
public static class WorldFileAnglerHeaderPatcher1458
{
    public static bool TryPatch(ReadOnlySpan<byte> source, WorldFileHeader header,
        WorldAnglerQuestData1458 angler, out byte[] patched)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(angler);
        patched = [];
        const int maximumHeaderBytes = 16 * 1024 * 1024;
        if (source.Length > maximumHeaderBytes) return false;
        var envelope = new WorldFileEnvelope(WorldFileFormatPolicy.CurrentVersion, 0, 0, [0, source.Length], 0, []);
        var limits = new WorldFileRuntimeMetadataLimits(16 * 1024, maximumHeaderBytes,
            WorldAnglerQuestData1458.MaximumCompletedNames, WorldBannerData1458.MaximumEntries, 100_000, maximumHeaderBytes);
        if (WorldFileRuntimeMetadataParser.TryParse(source, envelope, header, limits, out var metadata, out _) !=
            WorldFileRuntimeMetadataParseResult.Parsed || metadata is null)
            return false;

        // SaveWorldHeader 1.4.5.8: names count, names, savedAngler, anglerQuest.
        // savedAngler remains owned by the town-rescue progression owner.
        using var replacementStream = new MemoryStream();
        using (var writer = new BinaryWriter(replacementStream, new UTF8Encoding(false, true), leaveOpen: true))
        {
            writer.Write(angler.CompletedNames.Count);
            foreach (string name in angler.CompletedNames) writer.Write(name);
            writer.Write(metadata.SavedAngler);
            writer.Write(angler.Quest);
        }
        byte[] replacement = replacementStream.ToArray();
        int offset = metadata.AnglerSectionOffset;
        int length = metadata.AnglerSectionLength;
        if (offset < 0 || length < 9 || offset > source.Length - length ||
            source.Length - length > maximumHeaderBytes - replacement.Length)
            return false;
        patched = new byte[source.Length - length + replacement.Length];
        source[..offset].CopyTo(patched);
        replacement.CopyTo(patched.AsSpan(offset));
        source[(offset + length)..].CopyTo(patched.AsSpan(offset + replacement.Length));
        return true;
    }
}
