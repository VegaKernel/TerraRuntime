using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

public enum TerrariaToolTileSquareDecodeResult1458 : byte { NotApplicable, Decoded, Malformed }

// One owned cell, never an arbitrary client-controlled square or a mutation
// permission. The runtime must prove this complete wire image from a tool phase.
public readonly record struct TerrariaToolTileSquareState1458(short TileX, short TileY, WorldTile Tile);

public static partial class TerrariaTileSquareCodec
{
    public static TerrariaToolTileSquareDecodeResult1458 TryDecodeToolFrame(
        in TerrariaFrame frame, out TerrariaToolTileSquareState1458 state) =>
        TryDecodeToolFrame(in frame, true, out state);

    public static TerrariaToolTileSquareDecodeResult1458 TryDecodeToolFrame(
        in TerrariaFrame frame, bool clientRole, out TerrariaToolTileSquareState1458 state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.TileSquare)
            return TerrariaToolTileSquareDecodeResult1458.NotApplicable;
        var reader = new SequenceReader<byte>(frame.Payload);
        if (!reader.TryReadLittleEndian(out short x) || !reader.TryReadLittleEndian(out short y) ||
            !reader.TryRead(out byte width) || !reader.TryRead(out byte height))
            return TerrariaToolTileSquareDecodeResult1458.Malformed;
        if (width != 1 || height != 1)
            return TerrariaToolTileSquareDecodeResult1458.NotApplicable;
        if (frame.Payload.Length > FixedPayloadLength + MaximumEncodedBytesPerTile ||
            !reader.TryRead(out byte change) || change != 0 ||
            !reader.TryRead(out byte first) || !reader.TryRead(out byte second) || !reader.TryRead(out byte third) ||
            (first & 2) != 0 || (third & 240) != 0 || clientRole && (first & 8) != 0)
            return TerrariaToolTileSquareDecodeResult1458.Malformed;
        if ((first & 1) == 0) return TerrariaToolTileSquareDecodeResult1458.NotApplicable;
        byte slope = (byte)((second >> 4) & 7);
        bool half = (first & 32) != 0;
        if (x < 0 || y < 0 || slope > 4 ||
            (second & 8) != 0 && (first & 4) == 0)
            return TerrariaToolTileSquareDecodeResult1458.Malformed;

        var tile = new WorldTile { Flags = WorldTileFlags.Active };
        if (!tile.TrySetSourceShape(half, slope)) return TerrariaToolTileSquareDecodeResult1458.Malformed;
        if ((first & 16) != 0) tile.Flags |= WorldTileFlags.WireRed;
        if ((first & 64) != 0) tile.Flags |= WorldTileFlags.Actuator;
        if ((first & 128) != 0) tile.Flags |= WorldTileFlags.Inactive;
        if ((second & 1) != 0) tile.Flags |= WorldTileFlags.WireBlue;
        if ((second & 2) != 0) tile.Flags |= WorldTileFlags.WireGreen;
        if ((second & 128) != 0) tile.Flags |= WorldTileFlags.WireYellow;
        if ((third & 1) != 0) tile.Flags |= WorldTileFlags.FullbrightBlock;
        if ((third & 2) != 0) tile.Flags |= WorldTileFlags.FullbrightWall;
        if ((third & 4) != 0) tile.Flags |= WorldTileFlags.InvisibleBlock;
        if ((third & 8) != 0) tile.Flags |= WorldTileFlags.InvisibleWall;
        if ((second & 4) != 0)
        {
            if (!reader.TryRead(out byte colour) || colour > 31) return TerrariaToolTileSquareDecodeResult1458.Malformed;
            tile.TileColor = colour;
        }
        if ((second & 8) != 0)
        {
            if (!reader.TryRead(out byte colour) || colour > 31) return TerrariaToolTileSquareDecodeResult1458.Malformed;
            tile.WallColor = colour;
        }
        if (!reader.TryReadLittleEndian(out short type)) return TerrariaToolTileSquareDecodeResult1458.Malformed;
        if (type is not (137 or 424 or 442)) return TerrariaToolTileSquareDecodeResult1458.NotApplicable;
        tile.Type = (ushort)type;
        if (!reader.TryReadLittleEndian(out tile.FrameX) || !reader.TryReadLittleEndian(out tile.FrameY))
            return TerrariaToolTileSquareDecodeResult1458.Malformed;
        if ((first & 4) != 0)
        {
            if (!reader.TryReadLittleEndian(out short wall)) return TerrariaToolTileSquareDecodeResult1458.Malformed;
            tile.Wall = unchecked((ushort)wall);
        }
        if ((first & 8) != 0)
        {
            if (!reader.TryRead(out byte amount) || !reader.TryRead(out byte liquid) || liquid > 3)
                return TerrariaToolTileSquareDecodeResult1458.Malformed;
            tile.LiquidAmount = amount;
            tile.LiquidKind = (WorldLiquidKind)liquid;
        }
        if (reader.Remaining != 0) return TerrariaToolTileSquareDecodeResult1458.Malformed;
        state = new(x, y, tile);
        return TerrariaToolTileSquareDecodeResult1458.Decoded;
    }

    // CLIENT SendTileSquare omits liquid even when the owned cell contains it.
    public static bool TryEncodeToolFrame(in TerrariaToolTileSquareState1458 state, out byte[] frame) =>
        TryEncodeToolFrameCore(in state, true, out frame);

    public static bool TryEncodeToolServerFrame(in TerrariaToolTileSquareState1458 state, out byte[] frame) =>
        TryEncodeToolFrameCore(in state, false, out frame);

    public static bool HasSameToolWire(in TerrariaToolTileSquareState1458 left, in TerrariaToolTileSquareState1458 right) =>
        TryEncodeToolFrame(in left, out var a) && TryEncodeToolFrame(in right, out var b) && a.AsSpan().SequenceEqual(b);

    private static bool TryEncodeToolFrameCore(in TerrariaToolTileSquareState1458 state, bool clientRole, out byte[] frame)
    {
        frame = [];
        if (state.TileX < 0 || state.TileY < 0 || clientRole && (!state.Tile.IsActive || state.Tile.Type is not (137 or 424 or 442))) return false;
        var bytes = new byte[TerrariaFrameDecoderOptions.MinimumFrameLength + FixedPayloadLength + MaximumEncodedBytesPerTile];
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(3), state.TileX);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(5), state.TileY);
        bytes[7] = 1; bytes[8] = 1; // One-cell source SendTileSquare, changeType None.
        int offset = 10;
        var tile = state.Tile;
        if (clientRole) tile.LiquidAmount = 0;
        if (!TryEncodeTile(in tile, bytes, ref offset)) return false;
        Array.Resize(ref bytes, offset);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)offset));
        bytes[2] = (byte)TerrariaMessageId.TileSquare;
        frame = bytes;
        return true;
    }
}
