using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Source-pinned packet-28 codec for TerrariaServer 1.4.5.8 / protocol 326.
/// Payload: npc byte, generation byte, damage int16, knockback single, hitDirection+1 byte, crit byte.
/// </summary>
public static class TerrariaNpcDamageCodec
{
    public const int PayloadLength = 10;
    public const int VanillaNpcSlots = 200;

    public static TerrariaNpcDamageDecodeResult TryDecode(
        in TerrariaFrame frame,
        out TerrariaNpcDamageState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.NpcDamage)
            return TerrariaNpcDamageDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaNpcDamageDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
        {
            state = DecodePayload(frame.Payload.FirstSpan);
        }
        else
        {
            Span<byte> scratch = stackalloc byte[PayloadLength];
            int offset = 0;
            foreach (ReadOnlyMemory<byte> segment in frame.Payload)
            {
                segment.Span.CopyTo(scratch[offset..]);
                offset += segment.Length;
            }
            state = DecodePayload(scratch);
        }

        return state.IsStructurallyValid
            ? TerrariaNpcDamageDecodeResult.Decoded
            : TerrariaNpcDamageDecodeResult.InvalidState;
    }

    private static TerrariaNpcDamageState DecodePayload(ReadOnlySpan<byte> payload)
    {
        float knockBack = BitConverter.Int32BitsToSingle(
            BinaryPrimitives.ReadInt32LittleEndian(payload[4..8]));
        return new TerrariaNpcDamageState(
            payload[0],
            payload[1],
            BinaryPrimitives.ReadInt16LittleEndian(payload[2..4]),
            knockBack,
            payload[8],
            payload[9]);
    }

    public static TerrariaNpcDamageEncodeResult TryEncode(
        in TerrariaNpcDamageState state,
        out byte[] frame)
    {
        frame = [];
        if (!state.IsStructurallyValid ||
            state.NpcSlot >= VanillaNpcSlots ||
            state.Damage < 0)
        {
            return TerrariaNpcDamageEncodeResult.InvalidState;
        }

        Span<byte> payload = stackalloc byte[PayloadLength];
        payload[0] = state.NpcSlot;
        payload[1] = state.Generation;
        BinaryPrimitives.WriteInt16LittleEndian(payload[2..4], state.Damage);
        BinaryPrimitives.WriteInt32LittleEndian(
            payload[4..8],
            BitConverter.SingleToInt32Bits(state.KnockBack));
        payload[8] = state.HitDirectionWire;
        payload[9] = state.CriticalRaw;

        var writer = new ArrayBufferWriter<byte>(PayloadLength + TerrariaFrameDecoderOptions.MinimumFrameLength);
        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.NpcDamage,
            payload);
        if (result == TerrariaFrameWriteResult.FrameTooLarge)
            return TerrariaNpcDamageEncodeResult.FrameTooLarge;
        if (result != TerrariaFrameWriteResult.Written)
            return TerrariaNpcDamageEncodeResult.Failed;

        frame = writer.WrittenSpan.ToArray();
        return TerrariaNpcDamageEncodeResult.Encoded;
    }

    public static TerrariaNpcDamageEncodeResult TryEncodeAck(out byte[] frame)
    {
        frame = [];
        var writer = new ArrayBufferWriter<byte>(TerrariaFrameDecoderOptions.MinimumFrameLength);
        TerrariaFrameWriteResult result = TerrariaFrameEncoder.TryWrite(
            writer,
            (byte)TerrariaMessageId.NpcDamageAck,
            ReadOnlySpan<byte>.Empty);
        if (result == TerrariaFrameWriteResult.FrameTooLarge)
            return TerrariaNpcDamageEncodeResult.FrameTooLarge;
        if (result != TerrariaFrameWriteResult.Written)
            return TerrariaNpcDamageEncodeResult.Failed;
        frame = writer.WrittenSpan.ToArray();
        return TerrariaNpcDamageEncodeResult.Encoded;
    }
}
