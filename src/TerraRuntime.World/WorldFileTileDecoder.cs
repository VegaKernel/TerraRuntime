using System.Buffers.Binary;

namespace TerraRuntime.World;

/// <summary>
/// Decodes the Terraria 1.4.5.8 .wld tile section into runtime tile storage.
/// The byte layout and RLE rules are derived from WorldFile.LoadWorldTiles / SaveWorldTiles.
/// </summary>
public static class WorldFileTileDecoder
{
    public static WorldFileTileDecodeResult TryDecode(
        ReadOnlySpan<byte> file,
        WorldFileEnvelope envelope,
        WorldFileHeader header,
        WorldTileStore destination,
        out int bytesConsumed)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(destination);
        bytesConsumed = 0;

        if (envelope.FormatVersion != WorldFileFormatPolicy.CurrentVersion)
        {
            return WorldFileTileDecodeResult.UnsupportedVersion;
        }

        if (envelope.SectionOffsets.Count < 3)
        {
            return WorldFileTileDecodeResult.InvalidSectionBounds;
        }

        if (header.Dimensions.WidthTiles != destination.Dimensions.WidthTiles ||
            header.Dimensions.HeightTiles != destination.Dimensions.HeightTiles)
        {
            return WorldFileTileDecodeResult.DimensionMismatch;
        }

        int sectionStart = envelope.SectionOffsets[1];
        int sectionEnd = envelope.SectionOffsets[2];
        if (sectionStart < 0 || sectionEnd <= sectionStart || sectionEnd > file.Length)
        {
            return WorldFileTileDecodeResult.InvalidSectionBounds;
        }

        var reader = new TileReader(file.Slice(sectionStart, sectionEnd - sectionStart));
        int height = destination.Dimensions.HeightTiles;
        Span<WorldTile> allTiles = destination.Tiles;

        for (int x = 0; x < destination.Dimensions.WidthTiles; x++)
        {
            Span<WorldTile> column = allTiles.Slice(x * height, height);
            for (int y = 0; y < height; y++)
            {
                WorldFileTileDecodeResult result = TryReadTile(ref reader, envelope, out WorldTile tile, out int repeat);
                if (result != WorldFileTileDecodeResult.Decoded)
                {
                    bytesConsumed = reader.Offset;
                    return result;
                }

                if (repeat > height - y - 1)
                {
                    bytesConsumed = reader.Offset;
                    return WorldFileTileDecodeResult.InvalidRunLength;
                }

                column.Slice(y, repeat + 1).Fill(tile);
                y += repeat;
            }
        }

        bytesConsumed = reader.Offset;
        return reader.Remaining == 0
            ? WorldFileTileDecodeResult.Decoded
            : WorldFileTileDecodeResult.SectionLengthMismatch;
    }

    private static WorldFileTileDecodeResult TryReadTile(
        ref TileReader reader,
        WorldFileEnvelope envelope,
        out WorldTile tile,
        out int repeat)
    {
        tile = default;
        repeat = 0;

        if (!reader.TryReadByte(out byte header1))
        {
            return WorldFileTileDecodeResult.Truncated;
        }

        byte header2 = 0;
        byte header3 = 0;
        byte header4 = 0;
        if ((header1 & 0x01) != 0)
        {
            if (!reader.TryReadByte(out header2))
            {
                return WorldFileTileDecodeResult.Truncated;
            }

            if ((header2 & 0x01) != 0)
            {
                if (!reader.TryReadByte(out header3))
                {
                    return WorldFileTileDecodeResult.Truncated;
                }

                if ((header3 & 0x01) != 0 && !reader.TryReadByte(out header4))
                {
                    return WorldFileTileDecodeResult.Truncated;
                }
            }
        }

        if ((header1 & 0x02) != 0)
        {
            if (!reader.TryReadByte(out byte typeLow))
            {
                return WorldFileTileDecodeResult.Truncated;
            }

            int tileType = typeLow;
            if ((header1 & 0x20) != 0)
            {
                if (!reader.TryReadByte(out byte typeHigh))
                {
                    return WorldFileTileDecodeResult.Truncated;
                }

                tileType |= typeHigh << 8;
            }

            if ((uint)tileType >= VanillaWorldFormat326.TileTypeCount ||
                (uint)tileType >= (uint)envelope.FrameImportanceCount)
            {
                return WorldFileTileDecodeResult.InvalidTileType;
            }

            tile.Type = (ushort)tileType;
            tile.Flags |= WorldTileFlags.Active;

            if (envelope.IsFrameImportant(tileType))
            {
                if (!reader.TryReadInt16(out tile.FrameX) || !reader.TryReadInt16(out tile.FrameY))
                {
                    return WorldFileTileDecodeResult.Truncated;
                }

                if (tile.Type == VanillaWorldFormat326.TimersTileType)
                {
                    tile.FrameY = 0;
                }
            }
            else
            {
                tile.FrameX = -1;
                tile.FrameY = -1;
            }

            if ((header3 & 0x08) != 0)
            {
                if (!reader.TryReadByte(out tile.TileColor))
                {
                    return WorldFileTileDecodeResult.Truncated;
                }
            }
        }

        if ((header1 & 0x04) != 0)
        {
            if (!reader.TryReadByte(out byte wallLow))
            {
                return WorldFileTileDecodeResult.Truncated;
            }

            tile.Wall = wallLow;
            if ((header3 & 0x10) != 0 && !reader.TryReadByte(out tile.WallColor))
            {
                return WorldFileTileDecodeResult.Truncated;
            }
        }

        int liquidCode = (header1 & 0x18) >> 3;
        if (liquidCode != 0)
        {
            if (!reader.TryReadByte(out tile.LiquidAmount))
            {
                return WorldFileTileDecodeResult.Truncated;
            }

            tile.LiquidKind = (header3 & 0x80) != 0
                ? WorldLiquidKind.Shimmer
                : liquidCode switch
                {
                    2 => WorldLiquidKind.Lava,
                    3 => WorldLiquidKind.Honey,
                    _ => WorldLiquidKind.Water
                };
        }

        if ((header2 & 0x02) != 0)
        {
            tile.Flags |= WorldTileFlags.WireRed;
        }
        if ((header2 & 0x04) != 0)
        {
            tile.Flags |= WorldTileFlags.WireBlue;
        }
        if ((header2 & 0x08) != 0)
        {
            tile.Flags |= WorldTileFlags.WireGreen;
        }
        tile.Shape = (byte)((header2 & 0x70) >> 4);

        if ((header3 & 0x02) != 0)
        {
            tile.Flags |= WorldTileFlags.Actuator;
        }
        if ((header3 & 0x04) != 0)
        {
            tile.Flags |= WorldTileFlags.Inactive;
        }
        if ((header3 & 0x20) != 0)
        {
            tile.Flags |= WorldTileFlags.WireYellow;
        }
        if ((header3 & 0x40) != 0)
        {
            if (!reader.TryReadByte(out byte wallHigh))
            {
                return WorldFileTileDecodeResult.Truncated;
            }

            tile.Wall = (ushort)((wallHigh << 8) | tile.Wall);
            if (tile.Wall >= VanillaWorldFormat326.WallTypeCount)
            {
                tile.Wall = 0;
            }
        }
        else if (tile.Wall >= VanillaWorldFormat326.WallTypeCount)
        {
            tile.Wall = 0;
        }

        if ((header4 & 0x02) != 0)
        {
            tile.Flags |= WorldTileFlags.InvisibleBlock;
        }
        if ((header4 & 0x04) != 0)
        {
            tile.Flags |= WorldTileFlags.InvisibleWall;
        }
        if ((header4 & 0x08) != 0)
        {
            tile.Flags |= WorldTileFlags.FullbrightBlock;
        }
        if ((header4 & 0x10) != 0)
        {
            tile.Flags |= WorldTileFlags.FullbrightWall;
        }

        int runMode = (header1 & 0xC0) >> 6;
        if (runMode == 1)
        {
            if (!reader.TryReadByte(out byte shortRun))
            {
                return WorldFileTileDecodeResult.Truncated;
            }
            repeat = shortRun;
        }
        else if (runMode >= 2)
        {
            if (!reader.TryReadInt16(out short longRun))
            {
                return WorldFileTileDecodeResult.Truncated;
            }
            if (longRun < 0)
            {
                return WorldFileTileDecodeResult.InvalidRunLength;
            }
            repeat = longRun;
        }

        return WorldFileTileDecodeResult.Decoded;
    }

    private ref struct TileReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _offset;

        public TileReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _offset = 0;
        }

        public int Offset => _offset;
        public int Remaining => _data.Length - _offset;

        public bool TryReadByte(out byte value)
        {
            if (_offset >= _data.Length)
            {
                value = default;
                return false;
            }

            value = _data[_offset++];
            return true;
        }

        public bool TryReadInt16(out short value)
        {
            if (_data.Length - _offset < sizeof(short))
            {
                value = default;
                return false;
            }

            value = BinaryPrimitives.ReadInt16LittleEndian(_data[_offset..]);
            _offset += sizeof(short);
            return true;
        }
    }
}
