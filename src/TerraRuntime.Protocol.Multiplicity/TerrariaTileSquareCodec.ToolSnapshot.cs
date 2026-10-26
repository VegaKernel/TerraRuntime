using TerraRuntime.Contracts.Gameplay;
using System.Buffers.Binary;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

public static partial class TerrariaTileSquareCodec
{
    public static bool TryEncodeSingle(in WorldTile tile, int tileX, int tileY, out byte[] frame)
    {
        frame = [];
        if ((uint)tileX > short.MaxValue || (uint)tileY > short.MaxValue) return false;
        frame = new byte[TerrariaFrameDecoderOptions.MinimumFrameLength + FixedPayloadLength + MaximumEncodedBytesPerTile];
        int offset = TerrariaFrameDecoderOptions.MinimumFrameLength;
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(offset), (short)tileX); offset += sizeof(short);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(offset), (short)tileY); offset += sizeof(short);
        frame[offset++] = 1; frame[offset++] = 1; frame[offset++] = (byte)VanillaTileChangeType1458.None;
        if (!TryEncodeTile(in tile, frame, ref offset)) { frame = []; return false; }
        Array.Resize(ref frame, offset);
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)frame.Length));
        frame[2] = (byte)TerrariaMessageId.TileSquare;
        return true;
    }
}
