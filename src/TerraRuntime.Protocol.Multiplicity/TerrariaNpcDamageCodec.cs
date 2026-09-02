using System.Buffers.Binary;
using global::Multiplicity.Packets;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Source-pinned packet-28 adapter for TerrariaServer 1.4.5.8 / protocol 326.
/// Multiplicity owns the slot/generation/damage/knockback/direction/crit wire layout.
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

        var packet = new NpcStrike
        {
            NpcSlot = state.NpcSlot,
            Generation = state.Generation,
            Damage = state.Damage,
            Knockback = state.KnockBack,
            Direction = state.HitDirectionWire,
            Crit = state.CriticalRaw
        };

        return packet.TrySerialize(out frame)
            ? TerrariaNpcDamageEncodeResult.Encoded
            : TerrariaNpcDamageEncodeResult.Failed;
    }

    public static TerrariaNpcDamageEncodeResult TryEncodeAck(out byte[] frame)
    {
        var packet = new DamageNPCAck();
        return packet.TrySerialize(out frame)
            ? TerrariaNpcDamageEncodeResult.Encoded
            : TerrariaNpcDamageEncodeResult.Failed;
    }
}
