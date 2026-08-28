using System.Buffers.Binary;

namespace TerraRuntime.World;

/// <summary>
/// Decodes the Terraria 1.4.5.8 tile-entity persistence section into data-only records.
/// Gameplay TileEntity instances are intentionally not created while parsing an untrusted .wld file.
/// </summary>
public static class WorldFileTileEntityDecoder
{
    public static WorldFileTileEntityDecodeResult TryDecode(
        ReadOnlySpan<byte> file,
        WorldFileEnvelope envelope,
        WorldFileHeader header,
        int maxEntities,
        out WorldTileEntity[] entities,
        out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntities);

        entities = [];
        bytesConsumed = 0;

        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
            return WorldFileTileEntityDecodeResult.UnsupportedVersion;
        if (envelope.SectionOffsets.Count < 7)
            return WorldFileTileEntityDecodeResult.InvalidSectionBounds;

        int sectionStart = envelope.SectionOffsets[5];
        int sectionEnd = envelope.SectionOffsets[6];
        if (sectionStart < 0 || sectionEnd <= sectionStart || sectionEnd > file.Length)
            return WorldFileTileEntityDecodeResult.InvalidSectionBounds;

        var reader = new TileEntityReader(file.Slice(sectionStart, sectionEnd - sectionStart));
        if (!reader.TryReadInt32(out int entityCount))
            return WorldFileTileEntityDecodeResult.Truncated;
        if (entityCount < 0)
            return WorldFileTileEntityDecodeResult.InvalidEntityCount;
        if (entityCount > maxEntities)
            return WorldFileTileEntityDecodeResult.EntityBudgetExceeded;

        var loaded = new List<WorldTileEntity>(entityCount);
        var byPosition = new Dictionary<long, int>(entityCount);
        var activeIds = new HashSet<int>();

        for (int i = 0; i < entityCount; i++)
        {
            if (!reader.TryReadByte(out byte typeValue) ||
                !reader.TryReadInt32(out int persistedId) ||
                !reader.TryReadInt16(out short x) ||
                !reader.TryReadInt16(out short y))
            {
                bytesConsumed = reader.Offset;
                return WorldFileTileEntityDecodeResult.Truncated;
            }

            if (typeValue > (byte)WorldTileEntityKind.CritterAnchor)
            {
                bytesConsumed = reader.Offset;
                return WorldFileTileEntityDecodeResult.UnknownEntityType;
            }

            if (persistedId < 0 || persistedId == int.MaxValue)
            {
                bytesConsumed = reader.Offset;
                return WorldFileTileEntityDecodeResult.InvalidPersistedId;
            }

            if (x < 0 || y < 0 || x >= header.Dimensions.WidthTiles || y >= header.Dimensions.HeightTiles)
            {
                bytesConsumed = reader.Offset;
                return WorldFileTileEntityDecodeResult.InvalidCoordinates;
            }

            WorldFileTileEntityDecodeResult payloadResult = TryReadPayload(
                ref reader,
                (WorldTileEntityKind)typeValue,
                out WorldTileEntityPayload? payload);
            if (payloadResult != WorldFileTileEntityDecodeResult.Decoded || payload is null)
            {
                bytesConsumed = reader.Offset;
                return payloadResult;
            }

            if (!WorldTileEntityItemValidator.HasValidItemTypes(payload))
            {
                bytesConsumed = reader.Offset;
                return WorldFileTileEntityDecodeResult.InvalidItemType;
            }

            long positionKey = ((long)(uint)(ushort)x << 32) | (ushort)y;
            var entity = new WorldTileEntity(persistedId, x, y, (WorldTileEntityKind)typeValue, payload);

            if (byPosition.TryGetValue(positionKey, out int existingIndex))
            {
                WorldTileEntity previous = loaded[existingIndex];
                if (previous.PersistedId != persistedId && activeIds.Contains(persistedId))
                {
                    bytesConsumed = reader.Offset;
                    return WorldFileTileEntityDecodeResult.DuplicatePersistedId;
                }

                activeIds.Remove(previous.PersistedId);
                activeIds.Add(persistedId);
                loaded[existingIndex] = entity;
            }
            else
            {
                if (!activeIds.Add(persistedId))
                {
                    bytesConsumed = reader.Offset;
                    return WorldFileTileEntityDecodeResult.DuplicatePersistedId;
                }

                byPosition.Add(positionKey, loaded.Count);
                loaded.Add(entity);
            }
        }

        bytesConsumed = reader.Offset;
        if (reader.Remaining != 0)
            return WorldFileTileEntityDecodeResult.SectionLengthMismatch;

        entities = loaded.ToArray();
        return WorldFileTileEntityDecodeResult.Decoded;
    }

    private static WorldFileTileEntityDecodeResult TryReadPayload(
        ref TileEntityReader reader,
        WorldTileEntityKind kind,
        out WorldTileEntityPayload? payload)
    {
        payload = null;

        switch (kind)
        {
            case WorldTileEntityKind.TrainingDummy:
                if (!reader.TryReadInt16(out short npcIndex))
                    return WorldFileTileEntityDecodeResult.Truncated;
                payload = new WorldTrainingDummyPayload(npcIndex);
                return WorldFileTileEntityDecodeResult.Decoded;

            case WorldTileEntityKind.ItemFrame:
            case WorldTileEntityKind.WeaponsRack:
            case WorldTileEntityKind.FoodPlatter:
            case WorldTileEntityKind.DeadCellsDisplayJar:
                if (!reader.TryReadItem(out WorldTileEntityItem item))
                    return WorldFileTileEntityDecodeResult.Truncated;
                payload = new WorldItemTileEntityPayload(item);
                return WorldFileTileEntityDecodeResult.Decoded;

            case WorldTileEntityKind.LogicSensor:
                if (!reader.TryReadByte(out byte logicCheck) || !reader.TryReadByte(out byte onValue))
                    return WorldFileTileEntityDecodeResult.Truncated;
                payload = new WorldLogicSensorPayload(logicCheck, onValue != 0);
                return WorldFileTileEntityDecodeResult.Decoded;

            case WorldTileEntityKind.DisplayDoll:
                return TryReadDisplayDoll(ref reader, out payload);

            case WorldTileEntityKind.HatRack:
                return TryReadHatRack(ref reader, out payload);

            case WorldTileEntityKind.TeleportationPylon:
                payload = WorldEmptyTileEntityPayload.Instance;
                return WorldFileTileEntityDecodeResult.Decoded;

            case WorldTileEntityKind.KiteAnchor:
            case WorldTileEntityKind.CritterAnchor:
                if (!reader.TryReadInt16(out short itemType))
                    return WorldFileTileEntityDecodeResult.Truncated;
                payload = new WorldLeashedAnchorPayload(itemType);
                return WorldFileTileEntityDecodeResult.Decoded;

            default:
                return WorldFileTileEntityDecodeResult.UnknownEntityType;
        }
    }

    private static WorldFileTileEntityDecodeResult TryReadDisplayDoll(
        ref TileEntityReader reader,
        out WorldTileEntityPayload? payload)
    {
        payload = null;
        if (!reader.TryReadByte(out byte equipmentLow) ||
            !reader.TryReadByte(out byte dyesLow) ||
            !reader.TryReadByte(out byte pose) ||
            !reader.TryReadByte(out byte extraMask))
        {
            return WorldFileTileEntityDecodeResult.Truncated;
        }

        if ((extraMask & 0xF8) != 0)
            return WorldFileTileEntityDecodeResult.InvalidPayloadFlags;

        int equipmentMask = equipmentLow | (((extraMask & 0x02) != 0) ? 0x100 : 0);
        int dyeMask = dyesLow | (((extraMask & 0x04) != 0) ? 0x100 : 0);
        var equipment = new WorldTileEntityItem?[9];
        var dyes = new WorldTileEntityItem?[9];

        for (int i = 0; i < equipment.Length; i++)
        {
            if ((equipmentMask & (1 << i)) != 0)
            {
                if (!reader.TryReadItem(out WorldTileEntityItem item))
                    return WorldFileTileEntityDecodeResult.Truncated;
                equipment[i] = item;
            }
        }

        for (int i = 0; i < dyes.Length; i++)
        {
            if ((dyeMask & (1 << i)) != 0)
            {
                if (!reader.TryReadItem(out WorldTileEntityItem item))
                    return WorldFileTileEntityDecodeResult.Truncated;
                dyes[i] = item;
            }
        }

        WorldTileEntityItem? misc = null;
        if ((extraMask & 0x01) != 0)
        {
            if (!reader.TryReadItem(out WorldTileEntityItem item))
                return WorldFileTileEntityDecodeResult.Truncated;
            misc = item;
        }

        payload = new WorldDisplayDollPayload(pose, equipment, dyes, misc);
        return WorldFileTileEntityDecodeResult.Decoded;
    }

    private static WorldFileTileEntityDecodeResult TryReadHatRack(
        ref TileEntityReader reader,
        out WorldTileEntityPayload? payload)
    {
        payload = null;
        if (!reader.TryReadByte(out byte mask))
            return WorldFileTileEntityDecodeResult.Truncated;
        if ((mask & 0xF0) != 0)
            return WorldFileTileEntityDecodeResult.InvalidPayloadFlags;

        var items = new WorldTileEntityItem?[2];
        var dyes = new WorldTileEntityItem?[2];
        for (int i = 0; i < items.Length; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                if (!reader.TryReadItem(out WorldTileEntityItem item))
                    return WorldFileTileEntityDecodeResult.Truncated;
                items[i] = item;
            }
        }

        for (int i = 0; i < dyes.Length; i++)
        {
            if ((mask & (1 << (i + 2))) != 0)
            {
                if (!reader.TryReadItem(out WorldTileEntityItem item))
                    return WorldFileTileEntityDecodeResult.Truncated;
                dyes[i] = item;
            }
        }

        payload = new WorldHatRackPayload(items, dyes);
        return WorldFileTileEntityDecodeResult.Decoded;
    }

    private ref struct TileEntityReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public TileEntityReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
        }

        public int Offset => _offset;
        public int Remaining => _data.Length - _offset;

        public bool TryReadByte(out byte value)
        {
            if (_offset >= _data.Length) { value = default; return false; }
            value = _data[_offset++];
            return true;
        }

        public bool TryReadInt16(out short value)
        {
            if (_data.Length - _offset < sizeof(short)) { value = default; return false; }
            value = BinaryPrimitives.ReadInt16LittleEndian(_data[_offset..]);
            _offset += sizeof(short);
            return true;
        }

        public bool TryReadInt32(out int value)
        {
            if (_data.Length - _offset < sizeof(int)) { value = default; return false; }
            value = BinaryPrimitives.ReadInt32LittleEndian(_data[_offset..]);
            _offset += sizeof(int);
            return true;
        }

        public bool TryReadItem(out WorldTileEntityItem item)
        {
            if (!TryReadInt16(out short type) || !TryReadByte(out byte prefix) || !TryReadInt16(out short stack))
            {
                item = default;
                return false;
            }

            item = new WorldTileEntityItem(type, prefix, stack);
            return true;
        }
    }
}
