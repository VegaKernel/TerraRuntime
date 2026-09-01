namespace TerraRuntime.World;

public enum WorldFilePreservedSectionNormalizationResult : byte
{
    Normalized = 0,
    UnsupportedVersion = 1,
    TileEntityEncodeFailed = 2,
    PressurePlateEncodeFailed = 3,
    BestiaryEncodeFailed = 4,
    CreativePowersEncodeFailed = 5
}

public readonly record struct WorldFilePreservedSectionNormalizationDiagnostic(
    WorldFilePreservedSectionNormalizationResult Result,
    int StageResultCode = 0)
{
    public bool IsNormalized => Result == WorldFilePreservedSectionNormalizationResult.Normalized;
}

/// <summary>
/// Detached template for .wld sections that are not yet mutated by the authoritative runtime save slice.
/// Tiles and chests are intentionally excluded because they are rebuilt from live state. The complete header section is
/// preserved opaquely so save does not synthesize or discard vanilla fields that the runtime metadata model does not yet retain.
/// Decoded side-table sections can be normalized through their version-pinned semantic encoders before this template is used.
/// </summary>
public sealed class WorldFilePreservedSections
{
    private WorldFilePreservedSections(
        byte[] header,
        byte[] signs,
        byte[] npcs,
        byte[] tileEntities,
        byte[] pressurePlates,
        byte[] townRooms,
        byte[] bestiary,
        byte[] creativePowers)
    {
        Header = header;
        Signs = signs;
        Npcs = npcs;
        TileEntities = tileEntities;
        PressurePlates = pressurePlates;
        TownRooms = townRooms;
        Bestiary = bestiary;
        CreativePowers = creativePowers;
    }

    public ReadOnlyMemory<byte> Header { get; }
    public ReadOnlyMemory<byte> Signs { get; }
    public ReadOnlyMemory<byte> Npcs { get; }
    public ReadOnlyMemory<byte> TileEntities { get; }
    public ReadOnlyMemory<byte> PressurePlates { get; }
    public ReadOnlyMemory<byte> TownRooms { get; }
    public ReadOnlyMemory<byte> Bestiary { get; }
    public ReadOnlyMemory<byte> CreativePowers { get; }

    public long TotalBytes =>
        (long)Header.Length +
        Signs.Length +
        Npcs.Length +
        TileEntities.Length +
        PressurePlates.Length +
        TownRooms.Length +
        Bestiary.Length +
        CreativePowers.Length;

    /// <summary>
    /// Re-encodes validated static side-table state through the current semantic codecs while retaining opaque header,
    /// sign, NPC and town-room bytes. Production runtime stores mutate/re-encode the latter three at save time; the header
    /// remains byte-preserved apart from explicitly supported field patchers.
    /// </summary>
    public WorldFilePreservedSectionNormalizationDiagnostic TryNormalizeSemanticSections(
        WorldFileData world,
        out WorldFilePreservedSections? normalized)
    {
        ArgumentNullException.ThrowIfNull(world);
        normalized = null;

        if (world.Envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
        {
            return new WorldFilePreservedSectionNormalizationDiagnostic(
                WorldFilePreservedSectionNormalizationResult.UnsupportedVersion);
        }

        using var tileEntityStream = new MemoryStream();
        WorldFileTileEntityEncodeResult tileEntityResult = WorldFileTileEntityEncoder.TryEncode(
            world.TileEntities,
            world.Header.Dimensions,
            world.TileEntities.Length,
            tileEntityStream,
            out _);
        if (tileEntityResult != WorldFileTileEntityEncodeResult.Encoded)
        {
            return new WorldFilePreservedSectionNormalizationDiagnostic(
                WorldFilePreservedSectionNormalizationResult.TileEntityEncodeFailed,
                (int)tileEntityResult);
        }

        using var pressurePlateStream = new MemoryStream();
        WorldFilePressurePlateEncodeResult pressurePlateResult = WorldFilePressurePlateEncoder.TryEncode(
            world.PressurePlates,
            world.Header.Dimensions,
            pressurePlateStream,
            out _);
        if (pressurePlateResult != WorldFilePressurePlateEncodeResult.Encoded)
        {
            return new WorldFilePreservedSectionNormalizationDiagnostic(
                WorldFilePreservedSectionNormalizationResult.PressurePlateEncodeFailed,
                (int)pressurePlateResult);
        }

        var bestiaryLimits = new WorldFileBestiaryLimits(
            MaxKillEntries: world.Bestiary.Kills?.Length ?? 0,
            MaxSightEntries: world.Bestiary.Sightings?.Length ?? 0,
            MaxChatEntries: world.Bestiary.Chats?.Length ?? 0,
            MaxPersistentIdBytes: int.MaxValue,
            MaxTotalPersistentIdBytes: long.MaxValue);
        using var bestiaryStream = new MemoryStream();
        WorldFileBestiaryEncodeResult bestiaryResult = WorldFileBestiaryEncoder.TryEncode(
            world.Bestiary,
            bestiaryLimits,
            bestiaryStream,
            out _);
        if (bestiaryResult != WorldFileBestiaryEncodeResult.Encoded)
        {
            return new WorldFilePreservedSectionNormalizationDiagnostic(
                WorldFilePreservedSectionNormalizationResult.BestiaryEncodeFailed,
                (int)bestiaryResult);
        }

        using var creativePowersStream = new MemoryStream();
        WorldFileCreativePowersEncodeResult creativePowersResult = WorldFileCreativePowersEncoder.TryEncode(
            world.CreativePowers,
            creativePowersStream,
            out _);
        if (creativePowersResult != WorldFileCreativePowersEncodeResult.Encoded)
        {
            return new WorldFilePreservedSectionNormalizationDiagnostic(
                WorldFilePreservedSectionNormalizationResult.CreativePowersEncodeFailed,
                (int)creativePowersResult);
        }

        normalized = new WorldFilePreservedSections(
            Header.ToArray(),
            Signs.ToArray(),
            Npcs.ToArray(),
            tileEntityStream.ToArray(),
            pressurePlateStream.ToArray(),
            TownRooms.ToArray(),
            bestiaryStream.ToArray(),
            creativePowersStream.ToArray());
        return new WorldFilePreservedSectionNormalizationDiagnostic(
            WorldFilePreservedSectionNormalizationResult.Normalized);
    }

    public static bool TryCapture(
        ReadOnlySpan<byte> file,
        WorldFileEnvelope envelope,
        out WorldFilePreservedSections? sections)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        sections = null;

        if (!HasSupportedLayout(envelope))
            return false;

        IReadOnlyList<int> offsets = envelope.SectionOffsets;
        if (!TryCopySection(file, offsets, 0, out byte[] header) ||
            !TryCopySection(file, offsets, 3, out byte[] signs) ||
            !TryCopySection(file, offsets, 4, out byte[] npcs) ||
            !TryCopySection(file, offsets, 5, out byte[] tileEntities) ||
            !TryCopySection(file, offsets, 6, out byte[] pressurePlates) ||
            !TryCopySection(file, offsets, 7, out byte[] townRooms) ||
            !TryCopySection(file, offsets, 8, out byte[] bestiary) ||
            !TryCopySection(file, offsets, 9, out byte[] creativePowers))
        {
            return false;
        }

        sections = new WorldFilePreservedSections(
            header,
            signs,
            npcs,
            tileEntities,
            pressurePlates,
            townRooms,
            bestiary,
            creativePowers);
        return true;
    }

    /// <summary>
    /// Captures only the preserved sections from a seekable source stream. The original tile/chest payloads are
    /// skipped rather than materialized, so a cache-hit startup does not need a second full-world byte array merely
    /// to prepare later background saves. The caller-owned stream position is restored before this method returns.
    /// </summary>
    public static bool TryCapture(
        Stream file,
        WorldFileEnvelope envelope,
        out WorldFilePreservedSections? sections)
    {
        ArgumentNullException.ThrowIfNull(file);
        long fileLength;
        try
        {
            fileLength = file.Length;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            sections = null;
            return false;
        }

        return TryCapture(file, 0, fileLength, envelope, out sections);
    }

    /// <summary>
    /// Captures preserved sections from a complete .wld image embedded inside a larger seekable stream. Section
    /// offsets remain relative to the embedded world image, while <paramref name="worldOffset"/> locates that image
    /// in the containing stream. This is used by runtime caches so warm startup can prepare background persistence
    /// without reading the canonical source .wld or materializing its tile/chest payloads.
    /// </summary>
    public static bool TryCapture(
        Stream file,
        long worldOffset,
        long worldLength,
        WorldFileEnvelope envelope,
        out WorldFilePreservedSections? sections)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(envelope);
        sections = null;

        if (!file.CanRead || !file.CanSeek ||
            worldOffset < 0 || worldLength <= 0 ||
            !HasSupportedLayout(envelope))
        {
            return false;
        }

        long originalPosition;
        long streamLength;
        try
        {
            originalPosition = file.Position;
            streamLength = file.Length;
            if (worldOffset > streamLength || worldLength > streamLength - worldOffset)
                return false;
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            return false;
        }

        try
        {
            IReadOnlyList<int> offsets = envelope.SectionOffsets;
            if (!TryReadSection(file, worldOffset, worldLength, offsets, 0, out byte[] header) ||
                !TryReadSection(file, worldOffset, worldLength, offsets, 3, out byte[] signs) ||
                !TryReadSection(file, worldOffset, worldLength, offsets, 4, out byte[] npcs) ||
                !TryReadSection(file, worldOffset, worldLength, offsets, 5, out byte[] tileEntities) ||
                !TryReadSection(file, worldOffset, worldLength, offsets, 6, out byte[] pressurePlates) ||
                !TryReadSection(file, worldOffset, worldLength, offsets, 7, out byte[] townRooms) ||
                !TryReadSection(file, worldOffset, worldLength, offsets, 8, out byte[] bestiary) ||
                !TryReadSection(file, worldOffset, worldLength, offsets, 9, out byte[] creativePowers))
            {
                return false;
            }

            sections = new WorldFilePreservedSections(
                header,
                signs,
                npcs,
                tileEntities,
                pressurePlates,
                townRooms,
                bestiary,
                creativePowers);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ObjectDisposedException or ArgumentException or OverflowException)
        {
            sections = null;
            return false;
        }
        finally
        {
            try
            {
                file.Position = originalPosition;
            }
            catch (Exception exception) when (
                exception is IOException or NotSupportedException or ObjectDisposedException)
            {
            }
        }
    }

    private static bool HasSupportedLayout(WorldFileEnvelope envelope) =>
        envelope.FormatVersion == WorldFileFormatPolicy.CurrentVersion &&
        envelope.SectionOffsets.Count == VanillaWorldFormat326.SectionCount;

    private static bool TryCopySection(
        ReadOnlySpan<byte> file,
        IReadOnlyList<int> offsets,
        int sectionIndex,
        out byte[] section)
    {
        section = [];
        int start = offsets[sectionIndex];
        int end = offsets[sectionIndex + 1];
        if (start < 0 || end <= start || end > file.Length)
            return false;

        section = file.Slice(start, end - start).ToArray();
        return true;
    }

    private static bool TryReadSection(
        Stream file,
        long worldOffset,
        long worldLength,
        IReadOnlyList<int> offsets,
        int sectionIndex,
        out byte[] section)
    {
        section = [];
        int start = offsets[sectionIndex];
        int end = offsets[sectionIndex + 1];
        if (start < 0 || end <= start || end > worldLength)
            return false;

        section = new byte[end - start];
        file.Position = checked(worldOffset + start);
        file.ReadExactly(section);
        return true;
    }
}
