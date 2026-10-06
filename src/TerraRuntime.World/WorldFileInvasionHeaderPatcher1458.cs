using System.Buffers.Binary;

namespace TerraRuntime.World;

/// <summary>Detached save values. Gameplay admission, arithmetic and event identity belong to the invasion owner.</summary>
public readonly record struct WorldInvasionSaveState1458(
    int Delay,
    int Size,
    sbyte Type,
    double X,
    int SizeStart,
    bool? LanternNightNextNight = null);

public enum WorldFileInvasionHeaderPatchResult1458 : byte
{
    Patched,
    UnsupportedVersion,
    InvalidState,
    InvalidHeader
}

/// <summary>Changes five invasion fields and an explicitly owned pending Lantern flag in a validated header.</summary>
public static class WorldFileInvasionHeaderPatcher1458
{
    private const int MaximumHeaderBytes = 16 * 1024 * 1024;
    private static readonly WorldFileRuntimeMetadataLimits Limits = new(
        16 * 1024, MaximumHeaderBytes, 255, WorldBannerData1458.MaximumEntries, 255, MaximumHeaderBytes);

    public static WorldFileInvasionHeaderPatchResult1458 TryPatch(
        ReadOnlySpan<byte> sourceHeader,
        WorldFileEnvelope envelope,
        WorldFileHeader header,
        in WorldInvasionSaveState1458 state,
        out byte[] patchedHeader)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(header);
        patchedHeader = [];
        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFileInvasionHeaderPatchResult1458.UnsupportedVersion;
        if (!double.IsFinite(state.X))
            return WorldFileInvasionHeaderPatchResult1458.InvalidState;
        if (sourceHeader.IsEmpty || sourceHeader.Length > MaximumHeaderBytes)
            return WorldFileInvasionHeaderPatchResult1458.InvalidHeader;

        // Section-local bounds let the existing complete parser verify identity, variable strings/counts and all guards.
        var localEnvelope = new WorldFileEnvelope(envelope.FormatVersion, envelope.Revision, envelope.FavoriteFlags,
            [0, sourceHeader.Length], 0, []);
        if (WorldFileRuntimeMetadataParser.TryParse(sourceHeader, localEnvelope, header, Limits,
                out var metadata, out _) != WorldFileRuntimeMetadataParseResult.Parsed || metadata is null)
            return WorldFileInvasionHeaderPatchResult1458.InvalidHeader;

        patchedHeader = sourceHeader.ToArray();
        int offset = metadata.InvasionFieldsOffset;
        BinaryPrimitives.WriteInt32LittleEndian(patchedHeader.AsSpan(offset, sizeof(int)), state.Delay);
        BinaryPrimitives.WriteInt32LittleEndian(patchedHeader.AsSpan(offset + sizeof(int), sizeof(int)), state.Size);
        BinaryPrimitives.WriteInt32LittleEndian(patchedHeader.AsSpan(offset + sizeof(int) * 2, sizeof(int)), state.Type);
        BinaryPrimitives.WriteInt64LittleEndian(patchedHeader.AsSpan(offset + sizeof(int) * 3, sizeof(long)),
            BitConverter.DoubleToInt64Bits(state.X));
        BinaryPrimitives.WriteInt32LittleEndian(patchedHeader.AsSpan(metadata.InvasionSizeStartOffset, sizeof(int)), state.SizeStart);
        if (state.LanternNightNextNight is { } nextNight)
            patchedHeader[metadata.LanternNightNextNightOffset] = nextNight ? (byte)1 : (byte)0;
        return WorldFileInvasionHeaderPatchResult1458.Patched;
    }
}
