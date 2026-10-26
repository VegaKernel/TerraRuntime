using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

public sealed class TerrariaFurnitureSquareState1458
{
    private readonly byte[] payload;
    internal TerrariaFurnitureSquareState1458(short x, short y, byte width, byte height, ushort tileType, byte[] payload)
    { TileX = x; TileY = y; Width = width; Height = height; TileType = tileType; this.payload = payload; }
    public short TileX { get; }
    public short TileY { get; }
    public byte Width { get; }
    public byte Height { get; }
    public ushort TileType { get; }
    public bool MatchesEncoded(ReadOnlySpan<byte> frame) => frame.Length >= 3 && frame[2] == 20 &&
        payload.AsSpan().SequenceEqual(frame[3..]);
    public bool HasSameWire(TerrariaFurnitureSquareState1458 other) => payload.AsSpan().SequenceEqual(other.payload);
}

public enum TerrariaFurnitureSquareDecodeResult1458 : byte { NotApplicable, Decoded, Malformed }

public static partial class TerrariaTileSquareCodec
{
    // A modern object's complete CLIENT footprint, bounded to twelve cells.
    // This is immutable evidence, never a replacement tile-table input.
    public static TerrariaFurnitureSquareDecodeResult1458 TryDecodeFurnitureFrame(in TerrariaFrame frame,
        out TerrariaFurnitureSquareState1458? state)
    {
        state = null;
        if (frame.MessageId != (byte)TerrariaMessageId.TileSquare) return TerrariaFurnitureSquareDecodeResult1458.NotApplicable;
        var reader = new SequenceReader<byte>(frame.Payload);
        if (!reader.TryReadLittleEndian(out short x) || !reader.TryReadLittleEndian(out short y) ||
            !reader.TryRead(out byte width) || !reader.TryRead(out byte height)) return TerrariaFurnitureSquareDecodeResult1458.Malformed;
        if ((width, height) is not ((2, 3) or (3, 3) or (3, 4))) return TerrariaFurnitureSquareDecodeResult1458.NotApplicable;
        if (x < 0 || y < 0 || frame.Payload.Length > FixedPayloadLength + width * height * MaximumEncodedBytesPerTile ||
            !reader.TryRead(out byte change) || change != 0) return TerrariaFurnitureSquareDecodeResult1458.Malformed;
        ushort common = 0;
        for (int cell = 0; cell < width * height; cell++)
        {
            if (!reader.TryRead(out byte first) || !reader.TryRead(out byte second) || !reader.TryRead(out byte third) ||
                (first & 11) != 1 || (third & 240) != 0 || ((second >> 4) & 7) > 4 ||
                (second & 8) != 0 && (first & 4) == 0) return TerrariaFurnitureSquareDecodeResult1458.Malformed;
            if ((second & 4) != 0 && (!reader.TryRead(out byte colour) || colour > 31)) return TerrariaFurnitureSquareDecodeResult1458.Malformed;
            if ((second & 8) != 0 && (!reader.TryRead(out byte wallColour) || wallColour > 31)) return TerrariaFurnitureSquareDecodeResult1458.Malformed;
            if (!reader.TryReadLittleEndian(out short type) || type is not (470 or 471 or 475)) return TerrariaFurnitureSquareDecodeResult1458.NotApplicable;
            if (common != 0 && common != type || !reader.TryReadLittleEndian(out short _) || !reader.TryReadLittleEndian(out short _))
                return TerrariaFurnitureSquareDecodeResult1458.Malformed;
            common = (ushort)type;
            if ((first & 4) != 0 && !reader.TryReadLittleEndian(out short _)) return TerrariaFurnitureSquareDecodeResult1458.Malformed;
        }
        if (reader.Remaining != 0 || common == 470 && (width != 2 || height != 3) ||
            common == 471 && (width != 3 || height != 3) || common == 475 && (width != 3 || height != 4))
            return TerrariaFurnitureSquareDecodeResult1458.Malformed;
        state = new(x, y, width, height, common, frame.Payload.ToArray());
        return TerrariaFurnitureSquareDecodeResult1458.Decoded;
    }

    public static bool TryEncodeFurnitureFrame(ReadOnlySpan<WorldTile> cells, int x, int y,
        byte width, byte height, out byte[] frame) => TryEncodeFurnitureFrameCore(cells, x, y, width, height, true, out frame);
    public static bool TryEncodeFurnitureServerFrame(ReadOnlySpan<WorldTile> cells, int x, int y,
        byte width, byte height, out byte[] frame) => TryEncodeFurnitureFrameCore(cells, x, y, width, height, false, out frame);
    private static bool TryEncodeFurnitureFrameCore(ReadOnlySpan<WorldTile> cells, int x, int y,
        byte width, byte height, bool clientRole, out byte[] frame)
    {
        frame = [];
        if ((uint)x > short.MaxValue || (uint)y > short.MaxValue ||
            (width, height) is not ((2, 3) or (3, 3) or (3, 4)) || cells.Length != width * height) return false;
        var bytes = new byte[3 + FixedPayloadLength + cells.Length * MaximumEncodedBytesPerTile];
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(3), (short)x);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(5), (short)y);
        bytes[7] = width; bytes[8] = height;
        int offset = 10;
        foreach (var cell in cells)
        {
            var tile = cell;
            if (!tile.IsActive || tile.Type is not (470 or 471 or 475)) return false;
            if (clientRole) tile.LiquidAmount = 0; // CLIENT omits liquid; SERVER relay includes its retained liquid.
            if (!TryEncodeTile(in tile, bytes, ref offset)) return false;
        }
        Array.Resize(ref bytes, offset); BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)offset));
        bytes[2] = (byte)TerrariaMessageId.TileSquare; frame = bytes; return true;
    }
}
