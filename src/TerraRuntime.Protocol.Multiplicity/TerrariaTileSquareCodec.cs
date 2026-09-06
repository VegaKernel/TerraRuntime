using System.Buffers.Binary;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Source-backed TerrariaServer 1.4.5.8 packet-20 encoder used for authoritative tile correction. Packet 20 writes
/// three fixed tile flag bytes followed by optional color/type/frame/wall/liquid fields for every cell in x/y order.
/// </summary>
public static class TerrariaTileSquareCodec
{
    public const byte DefaultCorrectionSize = 5;
    private const int MaximumEncodedBytesPerTile = 15;
    private const int FixedPayloadLength = 7;
    private const int Packet17WorldMargin = 3;

    public static bool TryEncodeCorrection(
        WorldTileStore tiles,
        int centerX,
        int centerY,
        out byte[] frame,
        byte size = DefaultCorrectionSize)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (size == 0 ||
            centerX < Packet17WorldMargin ||
            centerY < Packet17WorldMargin ||
            centerX >= tiles.Dimensions.WidthTiles - Packet17WorldMargin ||
            centerY >= tiles.Dimensions.HeightTiles - Packet17WorldMargin)
        {
            frame = [];
            return false;
        }

        int maxStartX = tiles.Dimensions.WidthTiles - Packet17WorldMargin - size;
        int maxStartY = tiles.Dimensions.HeightTiles - Packet17WorldMargin - size;
        if (maxStartX < Packet17WorldMargin || maxStartY < Packet17WorldMargin)
        {
            frame = [];
            return false;
        }

        int startX = Math.Clamp(centerX - size / 2, Packet17WorldMargin, maxStartX);
        int startY = Math.Clamp(centerY - size / 2, Packet17WorldMargin, maxStartY);
        return TryEncode(tiles, startX, startY, size, size, out frame);
    }

    public static bool TryEncode(
        WorldTileStore tiles,
        int startX,
        int startY,
        byte width,
        byte height,
        out byte[] frame) =>
        TryEncode(tiles, startX, startY, width, height, VanillaTileChangeType1458.None, out frame);

    public static bool TryEncode(
        WorldTileStore tiles,
        int startX,
        int startY,
        byte width,
        byte height,
        VanillaTileChangeType1458 changeType,
        out byte[] frame)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (width == 0 || height == 0 ||
            startX < 0 || startY < 0 ||
            startX + width > tiles.Dimensions.WidthTiles ||
            startY + height > tiles.Dimensions.HeightTiles)
        {
            frame = [];
            return false;
        }

        int maximumLength = checked(
            TerrariaFrameDecoderOptions.MinimumFrameLength +
            FixedPayloadLength +
            width * height * MaximumEncodedBytesPerTile);
        frame = new byte[maximumLength];
        int offset = TerrariaFrameDecoderOptions.MinimumFrameLength;
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(offset), checked((short)startX));
        offset += sizeof(short);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(offset), checked((short)startY));
        offset += sizeof(short);
        frame[offset++] = width;
        frame[offset++] = height;
        frame[offset++] = (byte)changeType;

        for (int x = startX; x < startX + width; x++)
        {
            for (int y = startY; y < startY + height; y++)
            {
                WorldTile tile = tiles.Get(x, y);
                if (!TryEncodeTile(in tile, frame, ref offset))
                {
                    frame = [];
                    return false;
                }
            }
        }

        Array.Resize(ref frame, offset);
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = (byte)TerrariaMessageId.TileSquare;
        return true;
    }

    private static bool TryEncodeTile(in WorldTile tile, byte[] destination, ref int offset)
    {
        if (!tile.HasOnlyKnownFlags || tile.Shape > 5 ||
            (tile.IsActive && tile.Type >= VanillaWorldFrameImportance326.Count) ||
            (tile.LiquidAmount != 0 && (byte)tile.LiquidKind > 3))
        {
            return false;
        }

        WorldTileFlags flags = tile.Flags;
        byte flags1 = 0;
        byte flags2 = 0;
        byte flags3 = 0;

        if (tile.IsActive) flags1 |= 1 << 0;
        if (tile.Wall != 0) flags1 |= 1 << 2;
        if (tile.LiquidAmount != 0) flags1 |= 1 << 3;
        if ((flags & WorldTileFlags.WireRed) != 0) flags1 |= 1 << 4;
        if (tile.Shape == 1) flags1 |= 1 << 5;
        if ((flags & WorldTileFlags.Actuator) != 0) flags1 |= 1 << 6;
        if ((flags & WorldTileFlags.Inactive) != 0) flags1 |= 1 << 7;

        if ((flags & WorldTileFlags.WireBlue) != 0) flags2 |= 1 << 0;
        if ((flags & WorldTileFlags.WireGreen) != 0) flags2 |= 1 << 1;
        if (tile.IsActive && tile.TileColor != 0) flags2 |= 1 << 2;
        if (tile.Wall != 0 && tile.WallColor != 0) flags2 |= 1 << 3;
        if (tile.Shape >= 2) flags2 |= checked((byte)((tile.Shape - 1) << 4));
        if ((flags & WorldTileFlags.WireYellow) != 0) flags2 |= 1 << 7;

        if ((flags & WorldTileFlags.FullbrightBlock) != 0) flags3 |= 1 << 0;
        if ((flags & WorldTileFlags.FullbrightWall) != 0) flags3 |= 1 << 1;
        if ((flags & WorldTileFlags.InvisibleBlock) != 0) flags3 |= 1 << 2;
        if ((flags & WorldTileFlags.InvisibleWall) != 0) flags3 |= 1 << 3;

        destination[offset++] = flags1;
        destination[offset++] = flags2;
        destination[offset++] = flags3;

        if ((flags2 & (1 << 2)) != 0)
            destination[offset++] = tile.TileColor;
        if ((flags2 & (1 << 3)) != 0)
            destination[offset++] = tile.WallColor;

        if (tile.IsActive)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(offset), tile.Type);
            offset += sizeof(ushort);
            if (VanillaWorldFrameImportance326.IsFrameImportant(tile.Type))
            {
                BinaryPrimitives.WriteInt16LittleEndian(destination.AsSpan(offset), tile.FrameX);
                offset += sizeof(short);
                BinaryPrimitives.WriteInt16LittleEndian(destination.AsSpan(offset), tile.FrameY);
                offset += sizeof(short);
            }
        }

        if (tile.Wall != 0)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination.AsSpan(offset), tile.Wall);
            offset += sizeof(ushort);
        }

        if (tile.LiquidAmount != 0)
        {
            destination[offset++] = tile.LiquidAmount;
            destination[offset++] = (byte)tile.LiquidKind;
        }

        return true;
    }
}
