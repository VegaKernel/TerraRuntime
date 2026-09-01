using System.Buffers.Binary;
using System.Text;

namespace TerraRuntime.Schematics;

public enum SchematicSectionKind : ushort
{
    Tiles = 1,
    Chests = 2,
    Signs = 3,
    TileEntities = 4,
    Npcs = 5,
    WorldItems = 6,
    Markers = 7,
    Metadata = 8
}

public static class SchematicBinary
{
    public const uint Magic = 0x43535254; // "TRSC" in little-endian bytes.
    public const ushort CurrentVersion = 1;
    public const int HeaderSize = 32;
    public const int DirectoryEntrySize = 24;

    private const ushort RequiredSectionFlag = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly SchematicSectionKind[] RequiredSections =
    [
        SchematicSectionKind.Tiles,
        SchematicSectionKind.Chests,
        SchematicSectionKind.Signs,
        SchematicSectionKind.TileEntities,
        SchematicSectionKind.Npcs,
        SchematicSectionKind.WorldItems,
        SchematicSectionKind.Markers,
        SchematicSectionKind.Metadata
    ];

    public static byte[] Serialize(SchematicDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();

        SectionData[] sections =
        [
            BuildSection(SchematicSectionKind.Tiles, writer => WriteTiles(writer, document.Tiles)),
            BuildSection(SchematicSectionKind.Chests, writer => WriteChests(writer, document.Chests)),
            BuildSection(SchematicSectionKind.Signs, writer => WriteSigns(writer, document.Signs)),
            BuildSection(SchematicSectionKind.TileEntities, writer => WriteTileEntities(writer, document.TileEntities)),
            BuildSection(SchematicSectionKind.Npcs, writer => WriteNpcs(writer, document.Npcs)),
            BuildSection(SchematicSectionKind.WorldItems, writer => WriteWorldItems(writer, document.WorldItems)),
            BuildSection(SchematicSectionKind.Markers, writer => WriteMarkers(writer, document.Markers)),
            BuildSection(SchematicSectionKind.Metadata, writer => WriteMetadata(writer, document.Metadata))
        ];

        int directorySize = checked(sections.Length * DirectoryEntrySize);
        long totalLength = HeaderSize + directorySize;
        foreach (SectionData section in sections)
        {
            if (section.Bytes.Length > SchematicLimits.MaxSectionBytes)
                throw new SchematicFormatException($"Section {section.Kind} exceeds the maximum encoded size.");
            totalLength = checked(totalLength + section.Bytes.Length);
        }

        if (totalLength > SchematicLimits.MaxFileBytes)
            throw new SchematicFormatException($"Encoded schematic exceeds {SchematicLimits.MaxFileBytes} bytes.");

        byte[] result = new byte[(int)totalLength];
        Span<byte> output = result;
        BinaryPrimitives.WriteUInt32LittleEndian(output, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(output[4..], CurrentVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(output[6..], HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(output[8..], document.ContentVersion);
        BinaryPrimitives.WriteInt32LittleEndian(output[12..], document.Width);
        BinaryPrimitives.WriteInt32LittleEndian(output[16..], document.Height);
        BinaryPrimitives.WriteInt32LittleEndian(output[20..], document.OriginX);
        BinaryPrimitives.WriteInt32LittleEndian(output[24..], document.OriginY);
        BinaryPrimitives.WriteUInt16LittleEndian(output[28..], checked((ushort)sections.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(output[30..], 0);

        int payloadOffset = checked(HeaderSize + directorySize);
        for (int index = 0; index < sections.Length; index++)
        {
            SectionData section = sections[index];
            Span<byte> entry = output.Slice(HeaderSize + (index * DirectoryEntrySize), DirectoryEntrySize);
            BinaryPrimitives.WriteUInt16LittleEndian(entry, (ushort)section.Kind);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], RequiredSectionFlag);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], checked((uint)payloadOffset));
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], checked((uint)section.Bytes.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], checked((uint)section.Bytes.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(entry[16..], Crc32.Compute(section.Bytes));
            BinaryPrimitives.WriteUInt32LittleEndian(entry[20..], 0);
            section.Bytes.AsSpan().CopyTo(output[payloadOffset..]);
            payloadOffset = checked(payloadOffset + section.Bytes.Length);
        }

        return result;
    }

    public static SchematicDocument Deserialize(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new SchematicFormatException("Schematic header is truncated.");
        if (data.Length > SchematicLimits.MaxFileBytes)
            throw new SchematicFormatException($"Schematic exceeds {SchematicLimits.MaxFileBytes} bytes.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
            throw new SchematicFormatException("Schematic magic is invalid.");

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]);
        if (version != CurrentVersion)
            throw new SchematicFormatException($"Unsupported schematic version {version}.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) != HeaderSize)
            throw new SchematicFormatException("Schematic header size is invalid for version 1.");

        int contentVersion = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        int width = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
        int originX = BinaryPrimitives.ReadInt32LittleEndian(data[20..]);
        int originY = BinaryPrimitives.ReadInt32LittleEndian(data[24..]);
        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]);
        if (BinaryPrimitives.ReadUInt16LittleEndian(data[30..]) != 0)
            throw new SchematicFormatException("Reserved header bits must be zero.");

        SchematicValidator.ValidateHeader(contentVersion, width, height, originX, originY);
        if (sectionCount == 0 || sectionCount > SchematicLimits.MaxSectionCount)
            throw new SchematicFormatException($"Invalid section count {sectionCount}.");

        int directoryEnd = checked(HeaderSize + (sectionCount * DirectoryEntrySize));
        if (directoryEnd > data.Length)
            throw new SchematicFormatException("Schematic section directory is truncated.");

        var entries = new List<SectionEntry>(sectionCount);
        var known = new Dictionary<SchematicSectionKind, SectionEntry>();
        for (int index = 0; index < sectionCount; index++)
        {
            ReadOnlySpan<byte> raw = data.Slice(HeaderSize + (index * DirectoryEntrySize), DirectoryEntrySize);
            ushort rawKind = BinaryPrimitives.ReadUInt16LittleEndian(raw);
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(raw[2..]);
            if ((flags & ~RequiredSectionFlag) != 0)
                throw new SchematicFormatException($"Section {rawKind} has unsupported flags 0x{flags:X4}.");

            uint rawOffset = BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]);
            uint rawStoredLength = BinaryPrimitives.ReadUInt32LittleEndian(raw[8..]);
            uint rawDecodedLength = BinaryPrimitives.ReadUInt32LittleEndian(raw[12..]);
            uint checksum = BinaryPrimitives.ReadUInt32LittleEndian(raw[16..]);
            if (BinaryPrimitives.ReadUInt32LittleEndian(raw[20..]) != 0)
                throw new SchematicFormatException($"Section {rawKind} reserved bits must be zero.");
            if (rawStoredLength > (uint)SchematicLimits.MaxSectionBytes || rawDecodedLength > (uint)SchematicLimits.MaxSectionBytes)
                throw new SchematicFormatException($"Section {rawKind} exceeds the supported size ceiling.");
            if (rawStoredLength != rawDecodedLength)
                throw new SchematicFormatException("Version 1 does not support compressed sections.");
            if (rawOffset < (uint)directoryEnd)
                throw new SchematicFormatException($"Section {rawKind} overlaps the header/directory.");

            long end = (long)rawOffset + rawStoredLength;
            if (end > data.Length)
                throw new SchematicFormatException($"Section {rawKind} exceeds the file bounds.");

            int offset = checked((int)rawOffset);
            int length = checked((int)rawStoredLength);
            if (Crc32.Compute(data.Slice(offset, length)) != checksum)
                throw new SchematicFormatException($"Section {rawKind} checksum mismatch.");

            var entry = new SectionEntry(offset, length);
            entries.Add(entry);
            if (Enum.IsDefined((SchematicSectionKind)rawKind))
            {
                SchematicSectionKind kind = (SchematicSectionKind)rawKind;
                if (!known.TryAdd(kind, entry))
                    throw new SchematicFormatException($"Duplicate section {kind}.");
            }
            else if ((flags & RequiredSectionFlag) != 0)
            {
                throw new SchematicFormatException($"Unknown required section {rawKind}.");
            }
        }

        entries.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
        for (int index = 1; index < entries.Count; index++)
        {
            SectionEntry previous = entries[index - 1];
            SectionEntry current = entries[index];
            if ((long)previous.Offset + previous.Length > current.Offset)
                throw new SchematicFormatException("Schematic sections overlap.");
        }

        foreach (SchematicSectionKind required in RequiredSections)
        {
            if (!known.ContainsKey(required))
                throw new SchematicFormatException($"Required section {required} is missing.");
        }

        SchematicTile[] tiles = ReadTiles(GetSection(data, known, SchematicSectionKind.Tiles), width, height);
        SchematicChest[] chests = ReadChests(GetSection(data, known, SchematicSectionKind.Chests));
        SchematicSign[] signs = ReadSigns(GetSection(data, known, SchematicSectionKind.Signs));
        SchematicTileEntity[] tileEntities = ReadTileEntities(GetSection(data, known, SchematicSectionKind.TileEntities));
        SchematicNpc[] npcs = ReadNpcs(GetSection(data, known, SchematicSectionKind.Npcs));
        SchematicWorldItem[] worldItems = ReadWorldItems(GetSection(data, known, SchematicSectionKind.WorldItems));
        SchematicMarker[] markers = ReadMarkers(GetSection(data, known, SchematicSectionKind.Markers));
        SchematicMetadataEntry[] metadata = ReadMetadata(GetSection(data, known, SchematicSectionKind.Metadata));

        var document = new SchematicDocument
        {
            ContentVersion = contentVersion,
            Width = width,
            Height = height,
            OriginX = originX,
            OriginY = originY,
            Tiles = tiles,
            Chests = chests,
            Signs = signs,
            TileEntities = tileEntities,
            Npcs = npcs,
            WorldItems = worldItems,
            Markers = markers,
            Metadata = metadata
        };
        document.Validate();
        return document;
    }

    public static void Write(Stream destination, SchematicDocument document)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("Destination stream is not writable.", nameof(destination));
        byte[] bytes = Serialize(document);
        destination.Write(bytes);
    }

    public static async ValueTask WriteAsync(Stream destination, SchematicDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("Destination stream is not writable.", nameof(destination));
        byte[] bytes = Serialize(document);
        await destination.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    public static SchematicDocument Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("Source stream is not readable.", nameof(source));
        return Deserialize(ReadBounded(source));
    }

    public static async ValueTask<SchematicDocument> ReadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("Source stream is not readable.", nameof(source));
        byte[] bytes = await ReadBoundedAsync(source, cancellationToken).ConfigureAwait(false);
        return Deserialize(bytes);
    }

    private static byte[] ReadBounded(Stream source)
    {
        if (source.CanSeek)
        {
            long remaining = source.Length - source.Position;
            if (remaining < 0 || remaining > SchematicLimits.MaxFileBytes)
                throw new SchematicFormatException("Schematic stream exceeds the file-size ceiling.");
        }

        using var buffer = new MemoryStream();
        byte[] chunk = new byte[64 * 1024];
        int total = 0;
        while (true)
        {
            int read = source.Read(chunk, 0, chunk.Length);
            if (read == 0)
                break;
            total = checked(total + read);
            if (total > SchematicLimits.MaxFileBytes)
                throw new SchematicFormatException("Schematic stream exceeds the file-size ceiling.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static async ValueTask<byte[]> ReadBoundedAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source.CanSeek)
        {
            long remaining = source.Length - source.Position;
            if (remaining < 0 || remaining > SchematicLimits.MaxFileBytes)
                throw new SchematicFormatException("Schematic stream exceeds the file-size ceiling.");
        }

        using var buffer = new MemoryStream();
        byte[] chunk = new byte[64 * 1024];
        int total = 0;
        while (true)
        {
            int read = await source.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            total = checked(total + read);
            if (total > SchematicLimits.MaxFileBytes)
                throw new SchematicFormatException("Schematic stream exceeds the file-size ceiling.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return buffer.ToArray();
    }

    private static ReadOnlySpan<byte> GetSection(
        ReadOnlySpan<byte> file,
        Dictionary<SchematicSectionKind, SectionEntry> entries,
        SchematicSectionKind kind)
    {
        SectionEntry entry = entries[kind];
        return file.Slice(entry.Offset, entry.Length);
    }

    private static SectionData BuildSection(SchematicSectionKind kind, Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true))
            write(writer);
        return new SectionData(kind, stream.ToArray());
    }

    private static void WriteTiles(BinaryWriter writer, SchematicTile[] tiles)
    {
        writer.Write(tiles.Length);
        foreach (SchematicTile tile in tiles)
        {
            writer.Write(tile.Type);
            writer.Write(tile.Wall);
            writer.Write(tile.FrameX);
            writer.Write(tile.FrameY);
            writer.Write((ushort)tile.Flags);
            writer.Write(tile.LiquidAmount);
            writer.Write(tile.TileColor);
            writer.Write(tile.WallColor);
            writer.Write(tile.Shape);
            writer.Write((byte)tile.LiquidKind);
            writer.Write((byte)0);
        }
    }

    private static SchematicTile[] ReadTiles(ReadOnlySpan<byte> bytes, int width, int height)
    {
        var reader = new SpanReader(bytes);
        int expected = checked(width * height);
        int count = reader.ReadCount(expected, "tiles");
        if (count != expected)
            throw new SchematicFormatException($"Tile section count {count} does not match expected {expected}.");

        var tiles = new SchematicTile[count];
        for (int index = 0; index < count; index++)
        {
            ushort type = reader.ReadUInt16();
            ushort wall = reader.ReadUInt16();
            short frameX = reader.ReadInt16();
            short frameY = reader.ReadInt16();
            SchematicTileFlags flags = (SchematicTileFlags)reader.ReadUInt16();
            byte liquidAmount = reader.ReadByte();
            byte tileColor = reader.ReadByte();
            byte wallColor = reader.ReadByte();
            byte shape = reader.ReadByte();
            SchematicLiquidKind liquidKind = (SchematicLiquidKind)reader.ReadByte();
            if (reader.ReadByte() != 0)
                throw new SchematicFormatException("Tile reserved byte must be zero.");
            tiles[index] = new SchematicTile(type, wall, frameX, frameY, flags, liquidAmount, tileColor, wallColor, shape, liquidKind);
        }
        reader.EnsureEnd("tiles");
        return tiles;
    }

    private static void WriteChests(BinaryWriter writer, SchematicChest[] chests)
    {
        writer.Write(chests.Length);
        foreach (SchematicChest chest in chests)
        {
            writer.Write(chest.X);
            writer.Write(chest.Y);
            WriteString(writer, chest.Name, SchematicLimits.MaxChestNameUtf8Bytes, allowNull: false);
            WriteItems(writer, chest.Items);
        }
    }

    private static SchematicChest[] ReadChests(ReadOnlySpan<byte> bytes)
    {
        var reader = new SpanReader(bytes);
        int count = reader.ReadCount(SchematicLimits.MaxChests, "chests");
        var result = new SchematicChest[count];
        for (int index = 0; index < count; index++)
        {
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            string name = reader.ReadString(SchematicLimits.MaxChestNameUtf8Bytes, allowNull: false)!;
            SchematicItemStack[] items = reader.ReadItems(SchematicLimits.MaxChestItems);
            result[index] = new SchematicChest(x, y, name, items);
        }
        reader.EnsureEnd("chests");
        return result;
    }

    private static void WriteSigns(BinaryWriter writer, SchematicSign[] signs)
    {
        writer.Write(signs.Length);
        foreach (SchematicSign sign in signs)
        {
            writer.Write(sign.X);
            writer.Write(sign.Y);
            WriteString(writer, sign.Text, SchematicLimits.MaxSignTextUtf8Bytes, allowNull: false);
        }
    }

    private static SchematicSign[] ReadSigns(ReadOnlySpan<byte> bytes)
    {
        var reader = new SpanReader(bytes);
        int count = reader.ReadCount(SchematicLimits.MaxSigns, "signs");
        var result = new SchematicSign[count];
        for (int index = 0; index < count; index++)
            result[index] = new SchematicSign(reader.ReadInt32(), reader.ReadInt32(), reader.ReadString(SchematicLimits.MaxSignTextUtf8Bytes, false)!);
        reader.EnsureEnd("signs");
        return result;
    }

    private static void WriteTileEntities(BinaryWriter writer, SchematicTileEntity[] entities)
    {
        writer.Write(entities.Length);
        foreach (SchematicTileEntity entity in entities)
        {
            writer.Write((ushort)entity.Kind);
            writer.Write(entity.X);
            writer.Write(entity.Y);
            switch (entity)
            {
                case SchematicTrainingDummyTileEntity _:
                case SchematicTeleportationPylonTileEntity _:
                    break;
                case SchematicItemFrameTileEntity itemFrame:
                    WriteItem(writer, itemFrame.Item);
                    break;
                case SchematicLogicSensorTileEntity sensor:
                    writer.Write(sensor.LogicCheck);
                    writer.Write(sensor.On ? (byte)1 : (byte)0);
                    break;
                case SchematicDisplayDollTileEntity doll:
                    WriteItems(writer, doll.Items);
                    WriteItems(writer, doll.Dyes);
                    break;
                case SchematicWeaponsRackTileEntity rack:
                    WriteItem(writer, rack.Item);
                    break;
                case SchematicHatRackTileEntity hatRack:
                    WriteItems(writer, hatRack.Items);
                    WriteItems(writer, hatRack.Dyes);
                    break;
                case SchematicFoodPlatterTileEntity platter:
                    WriteItem(writer, platter.Item);
                    break;
                default:
                    throw new SchematicFormatException($"Unsupported tile entity model {entity.GetType().Name}.");
            }
        }
    }

    private static SchematicTileEntity[] ReadTileEntities(ReadOnlySpan<byte> bytes)
    {
        var reader = new SpanReader(bytes);
        int count = reader.ReadCount(SchematicLimits.MaxTileEntities, "tile entities");
        var result = new SchematicTileEntity[count];
        for (int index = 0; index < count; index++)
        {
            SchematicTileEntityKind kind = (SchematicTileEntityKind)reader.ReadUInt16();
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            result[index] = kind switch
            {
                SchematicTileEntityKind.TrainingDummy => new SchematicTrainingDummyTileEntity(x, y),
                SchematicTileEntityKind.ItemFrame => new SchematicItemFrameTileEntity(x, y, reader.ReadItem()),
                SchematicTileEntityKind.LogicSensor => new SchematicLogicSensorTileEntity(x, y, reader.ReadByte(), reader.ReadBooleanByte()),
                SchematicTileEntityKind.DisplayDoll => new SchematicDisplayDollTileEntity(x, y, reader.ReadItems(SchematicLimits.MaxTileEntityItems), reader.ReadItems(SchematicLimits.MaxTileEntityItems)),
                SchematicTileEntityKind.WeaponsRack => new SchematicWeaponsRackTileEntity(x, y, reader.ReadItem()),
                SchematicTileEntityKind.HatRack => new SchematicHatRackTileEntity(x, y, reader.ReadItems(SchematicLimits.MaxTileEntityItems), reader.ReadItems(SchematicLimits.MaxTileEntityItems)),
                SchematicTileEntityKind.FoodPlatter => new SchematicFoodPlatterTileEntity(x, y, reader.ReadItem()),
                SchematicTileEntityKind.TeleportationPylon => new SchematicTeleportationPylonTileEntity(x, y),
                _ => throw new SchematicFormatException($"Unknown tile entity kind {(ushort)kind}.")
            };
        }
        reader.EnsureEnd("tile entities");
        return result;
    }

    private static void WriteNpcs(BinaryWriter writer, SchematicNpc[] npcs)
    {
        writer.Write(npcs.Length);
        foreach (SchematicNpc npc in npcs)
        {
            writer.Write(npc.NpcType);
            writer.Write(npc.X);
            writer.Write(npc.Y);
            writer.Write(unchecked((byte)npc.Direction));
            writer.Write(unchecked((byte)npc.SpriteDirection));
            byte flags = 0;
            if (npc.Name is not null) flags |= 1;
            if (!npc.Homeless) flags |= 2;
            if (npc.LifeOverride.HasValue) flags |= 4;
            writer.Write(flags);
            writer.Write((byte)0);
            if (npc.Name is not null)
                WriteString(writer, npc.Name, SchematicLimits.MaxNpcNameUtf8Bytes, allowNull: false);
            if (!npc.Homeless)
            {
                writer.Write(npc.HomeX);
                writer.Write(npc.HomeY);
            }
            if (npc.LifeOverride.HasValue)
                writer.Write(npc.LifeOverride.Value);
        }
    }

    private static SchematicNpc[] ReadNpcs(ReadOnlySpan<byte> bytes)
    {
        var reader = new SpanReader(bytes);
        int count = reader.ReadCount(SchematicLimits.MaxNpcs, "NPCs");
        var result = new SchematicNpc[count];
        for (int index = 0; index < count; index++)
        {
            int npcType = reader.ReadInt32();
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            sbyte direction = unchecked((sbyte)reader.ReadByte());
            sbyte spriteDirection = unchecked((sbyte)reader.ReadByte());
            byte flags = reader.ReadByte();
            if ((flags & ~0x07) != 0 || reader.ReadByte() != 0)
                throw new SchematicFormatException("NPC flags/reserved data is invalid.");
            string? name = (flags & 1) != 0 ? reader.ReadString(SchematicLimits.MaxNpcNameUtf8Bytes, false) : null;
            bool homeless = (flags & 2) == 0;
            int homeX = 0;
            int homeY = 0;
            if (!homeless)
            {
                homeX = reader.ReadInt32();
                homeY = reader.ReadInt32();
            }
            int? lifeOverride = (flags & 4) != 0 ? reader.ReadInt32() : null;
            result[index] = new SchematicNpc(npcType, x, y, direction, spriteDirection, name, homeless, homeX, homeY, lifeOverride);
        }
        reader.EnsureEnd("NPCs");
        return result;
    }

    private static void WriteWorldItems(BinaryWriter writer, SchematicWorldItem[] items)
    {
        writer.Write(items.Length);
        foreach (SchematicWorldItem item in items)
        {
            WriteItem(writer, item.Item);
            writer.Write(item.X);
            writer.Write(item.Y);
        }
    }

    private static SchematicWorldItem[] ReadWorldItems(ReadOnlySpan<byte> bytes)
    {
        var reader = new SpanReader(bytes);
        int count = reader.ReadCount(SchematicLimits.MaxWorldItems, "world items");
        var result = new SchematicWorldItem[count];
        for (int index = 0; index < count; index++)
            result[index] = new SchematicWorldItem(reader.ReadItem(), reader.ReadSingle(), reader.ReadSingle());
        reader.EnsureEnd("world items");
        return result;
    }

    private static void WriteMarkers(BinaryWriter writer, SchematicMarker[] markers)
    {
        writer.Write(markers.Length);
        foreach (SchematicMarker marker in markers)
        {
            WriteString(writer, marker.Name, SchematicLimits.MaxMarkerNameUtf8Bytes, allowNull: false);
            writer.Write((byte)marker.Kind);
            writer.Write(marker.X);
            writer.Write(marker.Y);
            writer.Write(marker.Width);
            writer.Write(marker.Height);
        }
    }

    private static SchematicMarker[] ReadMarkers(ReadOnlySpan<byte> bytes)
    {
        var reader = new SpanReader(bytes);
        int count = reader.ReadCount(SchematicLimits.MaxMarkers, "markers");
        var result = new SchematicMarker[count];
        for (int index = 0; index < count; index++)
        {
            string name = reader.ReadString(SchematicLimits.MaxMarkerNameUtf8Bytes, false)!;
            SchematicMarkerKind kind = (SchematicMarkerKind)reader.ReadByte();
            result[index] = new SchematicMarker(name, kind, reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        }
        reader.EnsureEnd("markers");
        return result;
    }

    private static void WriteMetadata(BinaryWriter writer, SchematicMetadataEntry[] metadata)
    {
        writer.Write(metadata.Length);
        foreach (SchematicMetadataEntry entry in metadata)
        {
            WriteString(writer, entry.Key, SchematicLimits.MaxMetadataKeyUtf8Bytes, allowNull: false);
            WriteString(writer, entry.Value, SchematicLimits.MaxMetadataValueUtf8Bytes, allowNull: false);
        }
    }

    private static SchematicMetadataEntry[] ReadMetadata(ReadOnlySpan<byte> bytes)
    {
        var reader = new SpanReader(bytes);
        int count = reader.ReadCount(SchematicLimits.MaxMetadataEntries, "metadata entries");
        var result = new SchematicMetadataEntry[count];
        for (int index = 0; index < count; index++)
            result[index] = new SchematicMetadataEntry(reader.ReadString(SchematicLimits.MaxMetadataKeyUtf8Bytes, false)!, reader.ReadString(SchematicLimits.MaxMetadataValueUtf8Bytes, false)!);
        reader.EnsureEnd("metadata");
        return result;
    }

    private static void WriteItems(BinaryWriter writer, SchematicItemStack[] items)
    {
        writer.Write(items.Length);
        foreach (SchematicItemStack item in items)
            WriteItem(writer, item);
    }

    private static void WriteItem(BinaryWriter writer, SchematicItemStack item)
    {
        writer.Write(item.ItemType);
        writer.Write(item.Stack);
        writer.Write(item.Prefix);
    }

    private static void WriteString(BinaryWriter writer, string? value, int maxUtf8Bytes, bool allowNull)
    {
        if (value is null)
        {
            if (!allowNull)
                throw new SchematicFormatException("Required string is null.");
            writer.Write(-1);
            return;
        }

        byte[] bytes;
        try
        {
            bytes = StrictUtf8.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new SchematicFormatException("String contains invalid UTF-16 data.", exception);
        }
        if (bytes.Length > maxUtf8Bytes)
            throw new SchematicFormatException($"String exceeds {maxUtf8Bytes} UTF-8 bytes.");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private readonly record struct SectionData(SchematicSectionKind Kind, byte[] Bytes);
    private readonly record struct SectionEntry(int Offset, int Length);

    private ref struct SpanReader
    {
        private readonly ReadOnlySpan<byte> source;
        private int offset;

        public SpanReader(ReadOnlySpan<byte> source)
        {
            this.source = source;
            offset = 0;
        }

        public byte ReadByte()
        {
            EnsureAvailable(1);
            return source[offset++];
        }

        public bool ReadBooleanByte()
        {
            byte value = ReadByte();
            return value switch
            {
                0 => false,
                1 => true,
                _ => throw new SchematicFormatException($"Boolean byte has invalid value {value}.")
            };
        }

        public short ReadInt16()
        {
            EnsureAvailable(2);
            short value = BinaryPrimitives.ReadInt16LittleEndian(source[offset..]);
            offset += 2;
            return value;
        }

        public ushort ReadUInt16()
        {
            EnsureAvailable(2);
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(source[offset..]);
            offset += 2;
            return value;
        }

        public int ReadInt32()
        {
            EnsureAvailable(4);
            int value = BinaryPrimitives.ReadInt32LittleEndian(source[offset..]);
            offset += 4;
            return value;
        }

        public float ReadSingle() => BitConverter.Int32BitsToSingle(ReadInt32());

        public int ReadCount(int max, string label)
        {
            int count = ReadInt32();
            if ((uint)count > (uint)max)
                throw new SchematicFormatException($"Invalid {label} count {count}; maximum is {max}.");
            return count;
        }

        public string? ReadString(int maxUtf8Bytes, bool allowNull)
        {
            int length = ReadInt32();
            if (length == -1 && allowNull)
                return null;
            if (length < 0 || length > maxUtf8Bytes)
                throw new SchematicFormatException($"Invalid UTF-8 string length {length}.");
            EnsureAvailable(length);
            string value;
            try
            {
                value = StrictUtf8.GetString(source.Slice(offset, length));
            }
            catch (DecoderFallbackException exception)
            {
                throw new SchematicFormatException("String contains invalid UTF-8 data.", exception);
            }
            offset += length;
            return value;
        }

        public SchematicItemStack ReadItem() => new(ReadInt32(), ReadInt32(), ReadByte());

        public SchematicItemStack[] ReadItems(int max)
        {
            int count = ReadCount(max, "item slots");
            var items = new SchematicItemStack[count];
            for (int index = 0; index < count; index++)
                items[index] = ReadItem();
            return items;
        }

        public void EnsureEnd(string section)
        {
            if (offset != source.Length)
                throw new SchematicFormatException($"Section {section} contains {source.Length - offset} trailing bytes.");
        }

        private void EnsureAvailable(int count)
        {
            if (count < 0 || offset > source.Length - count)
                throw new SchematicFormatException("Schematic section is truncated.");
        }
    }

    private static class Crc32
    {
        private static readonly uint[] Table = CreateTable();

        public static uint Compute(ReadOnlySpan<byte> bytes)
        {
            uint crc = uint.MaxValue;
            foreach (byte value in bytes)
                crc = Table[(int)((crc ^ value) & 0xFF)] ^ (crc >> 8);
            return ~crc;
        }

        private static uint[] CreateTable()
        {
            var table = new uint[256];
            for (int index = 0; index < table.Length; index++)
            {
                uint value = (uint)index;
                for (int bit = 0; bit < 8; bit++)
                    value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
                table[index] = value;
            }
            return table;
        }
    }
}
