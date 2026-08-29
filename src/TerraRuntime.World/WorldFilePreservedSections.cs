namespace TerraRuntime.World;

/// <summary>
/// Detached byte-for-byte template for .wld sections that are not yet mutated by the authoritative runtime save slice.
/// Tiles and chests are intentionally excluded because they are rebuilt from live state. The complete header section is
/// preserved opaquely so save does not synthesize or discard vanilla fields that the runtime metadata model does not yet retain.
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

    public static bool TryCapture(
        ReadOnlySpan<byte> file,
        WorldFileEnvelope envelope,
        out WorldFilePreservedSections? sections)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        sections = null;

        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion ||
            envelope.SectionOffsets.Count != VanillaWorldFormat326.SectionCount)
        {
            return false;
        }

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
}
