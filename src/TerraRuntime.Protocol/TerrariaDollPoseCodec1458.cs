using System.Buffers;
using System.Buffers.Binary;

namespace TerraRuntime.Protocol;

public readonly record struct TerrariaDollPoseState1458(byte PlayerSlot, int EntityId, byte HeaderPose, byte Pose);

public enum TerrariaDollPoseDecodeResult1458 : byte { Decoded, NotApplicable, InvalidPayload }

/// <summary>The exact bounded case121 context2 subset; item/equipment contexts retain their existing routing.</summary>
public static class TerrariaDollPoseCodec1458
{
    public const byte MessageId = 121;
    public const byte PoseContext = 2;
    public const int PayloadLength = 8;
    public const ushort FrameLength = 11;

    public static TerrariaDollPoseDecodeResult1458 TryDecode(in TerrariaFrame frame, out TerrariaDollPoseState1458 state)
    {
        state = default;
        if (frame.MessageId != MessageId) return TerrariaDollPoseDecodeResult1458.NotApplicable;
        if (frame.Payload.Length < 7) return TerrariaDollPoseDecodeResult1458.InvalidPayload;
        Span<byte> header = stackalloc byte[7];
        frame.Payload.Slice(0, 7).CopyTo(header);
        if (header[6] != PoseContext) return TerrariaDollPoseDecodeResult1458.NotApplicable;
        if (frame.Payload.Length != PayloadLength) return TerrariaDollPoseDecodeResult1458.InvalidPayload;
        Span<byte> bytes = stackalloc byte[PayloadLength]; frame.Payload.CopyTo(bytes);
        state = new(bytes[0], BinaryPrimitives.ReadInt32LittleEndian(bytes[1..]), bytes[5], bytes[7]);
        return TerrariaDollPoseDecodeResult1458.Decoded;
    }
    public static byte[] Encode(in TerrariaDollPoseState1458 state)
    {
        var frame = new byte[FrameLength];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, FrameLength); frame[2] = MessageId;
        frame[3] = state.PlayerSlot; BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), state.EntityId);
        frame[8] = state.HeaderPose; frame[9] = PoseContext; frame[10] = state.Pose;
        return frame;
    }
}
