using System.Buffers.Binary;

namespace TerraRuntime.World;

public enum WorldFileTileEncodeResult : byte
{
    Encoded = 0,
    InvalidFrameImportance = 1,
    InvalidTileType = 2,
    InvalidWallType = 3,
    InvalidTileState = 4,
    DestinationNotWritable = 5,
    WriteFailed = 6
}

/// <summary>
/// Encodes the Terraria 1.4.5.8 .wld tile section from normalized runtime tile storage.
/// The layout is the inverse of <see cref="WorldFileTileDecoder"/>: columns are emitted left-to-right,
/// each column top-to-bottom, and save compression never crosses a column boundary.
/// </summary>
public static class WorldFileTileEncoder
{
    private const WorldTileFlags KnownFlags =
        WorldTileFlags.Active |
        WorldTileFlags.WireRed |
        WorldTileFlags.WireBlue |
        WorldTileFlags.WireGreen |
        WorldTileFlags.WireYellow |
        WorldTileFlags.Actuator |
        WorldTileFlags.Inactive |
        WorldTileFlags.InvisibleBlock |
        WorldTileFlags.InvisibleWall |
        WorldTileFlags.FullbrightBlock |
        WorldTileFlags.FullbrightWall;

    public static WorldFileTileEncodeResult TryEncode(
        WorldTileStore source,
        int frameImportanceCount,
        ReadOnlySpan<byte> frameImportanceBits,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (!destination.CanWrite)
            return WorldFileTileEncodeResult.DestinationNotWritable;

        if (frameImportanceCount != VanillaWorldFormat326.TileTypeCount ||
            frameImportanceBits.Length < (VanillaWorldFormat326.TileTypeCount + 7) / 8)
        {
            return WorldFileTileEncodeResult.InvalidFrameImportance;
        }

        int height = source.Dimensions.HeightTiles;
        ReadOnlySpan<WorldTile> allTiles = source.Tiles;

        try
        {
            for (int x = 0; x < source.Dimensions.WidthTiles; x++)
            {
                ReadOnlySpan<WorldTile> column = allTiles.Slice(x * height, height);
                for (int y = 0; y < height; y++)
                {
                    WorldTile tile = column[y];
                    WorldFileTileEncodeResult validation = ValidateTile(in tile);
                    if (validation != WorldFileTileEncodeResult.Encoded)
                        return validation;

                    int repeat = 0;
                    if (VanillaWorldFormat326.AllowsSaveCompressionBatching(tile.Type))
                    {
                        int maximumRepeat = Math.Min(short.MaxValue, height - y - 1);
                        while (repeat < maximumRepeat && TilesEqual(in tile, in column[y + repeat + 1]))
                            repeat++;
                    }

                    Span<byte> encoded = stackalloc byte[18];
                    int encodedLength = EncodeTile(
                        in tile,
                        repeat,
                        frameImportanceCount,
                        frameImportanceBits,
                        encoded);
                    destination.Write(encoded[..encodedLength]);
                    bytesWritten += encodedLength;
                    y += repeat;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            // The destination is caller-owned. A failed stream write may have emitted a prefix, so callers must
            // discard/replace the destination instead of attempting to publish it as a complete tile section.
            bytesWritten = 0;
            return WorldFileTileEncodeResult.WriteFailed;
        }

        return WorldFileTileEncodeResult.Encoded;
    }

    private static WorldFileTileEncodeResult ValidateTile(in WorldTile tile)
    {
        if ((tile.Flags & ~KnownFlags) != 0 || tile.Shape > 7 || !Enum.IsDefined(tile.LiquidKind))
            return WorldFileTileEncodeResult.InvalidTileState;

        if (tile.IsActive && tile.Type >= VanillaWorldFormat326.TileTypeCount)
            return WorldFileTileEncodeResult.InvalidTileType;

        if (tile.Wall >= VanillaWorldFormat326.WallTypeCount)
            return WorldFileTileEncodeResult.InvalidWallType;

        return WorldFileTileEncodeResult.Encoded;
    }

    private static int EncodeTile(
        in WorldTile tile,
        int repeat,
        int frameImportanceCount,
        ReadOnlySpan<byte> frameImportanceBits,
        Span<byte> buffer)
    {
        int dataIndex = 4;
        byte header1 = 0;
        byte header2 = 0;
        byte header3 = 0;
        byte header4 = 0;

        if (tile.IsActive)
        {
            header1 |= 0x02;
            buffer[dataIndex++] = (byte)tile.Type;
            if (tile.Type > byte.MaxValue)
            {
                header1 |= 0x20;
                buffer[dataIndex++] = (byte)(tile.Type >> 8);
            }

            if (IsFrameImportant(tile.Type, frameImportanceCount, frameImportanceBits))
            {
                BinaryPrimitives.WriteInt16LittleEndian(buffer[dataIndex..], tile.FrameX);
                dataIndex += sizeof(short);
                short frameY = tile.Type == VanillaWorldFormat326.TimersTileType ? (short)0 : tile.FrameY;
                BinaryPrimitives.WriteInt16LittleEndian(buffer[dataIndex..], frameY);
                dataIndex += sizeof(short);
            }

            if (tile.TileColor is not 0 and not 31)
            {
                header3 |= 0x08;
                buffer[dataIndex++] = tile.TileColor;
            }
        }

        if (tile.Wall != 0)
        {
            header1 |= 0x04;
            buffer[dataIndex++] = (byte)tile.Wall;
            if (tile.WallColor is not 0 and not 31)
            {
                header3 |= 0x10;
                buffer[dataIndex++] = tile.WallColor;
            }
        }

        if (tile.LiquidAmount != 0)
        {
            switch (tile.LiquidKind)
            {
                case WorldLiquidKind.Water:
                    header1 |= 0x08;
                    break;
                case WorldLiquidKind.Lava:
                    header1 |= 0x10;
                    break;
                case WorldLiquidKind.Honey:
                    header1 |= 0x18;
                    break;
                case WorldLiquidKind.Shimmer:
                    header1 |= 0x08;
                    header3 |= 0x80;
                    break;
            }

            buffer[dataIndex++] = tile.LiquidAmount;
        }

        if ((tile.Flags & WorldTileFlags.WireRed) != 0)
            header2 |= 0x02;
        if ((tile.Flags & WorldTileFlags.WireBlue) != 0)
            header2 |= 0x04;
        if ((tile.Flags & WorldTileFlags.WireGreen) != 0)
            header2 |= 0x08;
        header2 |= (byte)((tile.Shape & 0x07) << 4);

        if ((tile.Flags & WorldTileFlags.Actuator) != 0)
            header3 |= 0x02;
        if ((tile.Flags & WorldTileFlags.Inactive) != 0)
            header3 |= 0x04;
        if ((tile.Flags & WorldTileFlags.WireYellow) != 0)
            header3 |= 0x20;

        if (tile.Wall > byte.MaxValue)
        {
            header3 |= 0x40;
            buffer[dataIndex++] = (byte)(tile.Wall >> 8);
        }

        if ((tile.Flags & WorldTileFlags.InvisibleBlock) != 0)
            header4 |= 0x02;
        if ((tile.Flags & WorldTileFlags.InvisibleWall) != 0)
            header4 |= 0x04;
        if ((tile.Flags & WorldTileFlags.FullbrightBlock) != 0 || tile.TileColor == 31)
            header4 |= 0x08;
        if ((tile.Flags & WorldTileFlags.FullbrightWall) != 0 || tile.WallColor == 31)
            header4 |= 0x10;

        if (repeat > 0)
        {
            if (repeat <= byte.MaxValue)
            {
                header1 |= 0x40;
                buffer[dataIndex++] = (byte)repeat;
            }
            else
            {
                header1 |= 0x80;
                BinaryPrimitives.WriteInt16LittleEndian(buffer[dataIndex..], (short)repeat);
                dataIndex += sizeof(short);
            }
        }

        int headerIndex = 3;
        if (header4 != 0)
        {
            header3 |= 0x01;
            buffer[headerIndex--] = header4;
        }
        if (header3 != 0)
        {
            header2 |= 0x01;
            buffer[headerIndex--] = header3;
        }
        if (header2 != 0)
        {
            header1 |= 0x01;
            buffer[headerIndex--] = header2;
        }
        buffer[headerIndex] = header1;

        int encodedLength = dataIndex - headerIndex;
        buffer.Slice(headerIndex, encodedLength).CopyTo(buffer);
        return encodedLength;
    }

    private static bool IsFrameImportant(
        int tileType,
        int frameImportanceCount,
        ReadOnlySpan<byte> frameImportanceBits) =>
        tileType >= 0 &&
        tileType < frameImportanceCount &&
        (frameImportanceBits[tileType >> 3] & (1 << (tileType & 7))) != 0;

    private static bool TilesEqual(in WorldTile left, in WorldTile right) =>
        left.Type == right.Type &&
        left.Wall == right.Wall &&
        left.FrameX == right.FrameX &&
        left.FrameY == right.FrameY &&
        left.Flags == right.Flags &&
        left.LiquidAmount == right.LiquidAmount &&
        left.TileColor == right.TileColor &&
        left.WallColor == right.WallColor &&
        left.Shape == right.Shape &&
        left.LiquidKind == right.LiquidKind;
}
