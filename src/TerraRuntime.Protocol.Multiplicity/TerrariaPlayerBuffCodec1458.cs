using System.IO;
using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public enum TerrariaPlayerBuffDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    Malformed = 3,
    InvalidBuffType = 4
}

/// <summary>
/// Source-pinned adapter for TerrariaServer 1.4.5.8 packet 50 (PlayerBuff).
/// The wire payload is [player byte][buff ushort ...][zero ushort terminator]. Player.maxBuffs is 44.
/// Packet 50 deliberately carries no buff durations.
/// </summary>
public static class TerrariaPlayerBuffCodec1458
{
    public const int MaximumBuffs = 44;
    public const int MinimumPayloadLength = 3;
    public const int MaximumPayloadLength = 1 + ((MaximumBuffs + 1) * sizeof(ushort));

    public static TerrariaPlayerBuffDecodeResult TryDecode(
        in TerrariaFrame frame,
        out byte claimedPlayer,
        out BuffTypeId[] buffTypes)
    {
        claimedPlayer = 0;
        buffTypes = [];
        if (frame.MessageId != (byte)TerrariaMessageId.PlayerBuffs)
            return TerrariaPlayerBuffDecodeResult.WrongMessageId;
        if (frame.Payload.Length < MinimumPayloadLength ||
            frame.Payload.Length > MaximumPayloadLength ||
            (frame.Payload.Length & 1) == 0)
        {
            return TerrariaPlayerBuffDecodeResult.InvalidPayloadLength;
        }

        if (frame.Payload.IsSingleSegment)
            return DecodePayload(frame.Payload.FirstSpan, out claimedPlayer, out buffTypes);

        int length = checked((int)frame.Payload.Length);
        Span<byte> scratch = stackalloc byte[MaximumPayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }
        return DecodePayload(scratch[..length], out claimedPlayer, out buffTypes);
    }

    private static TerrariaPlayerBuffDecodeResult DecodePayload(
        ReadOnlySpan<byte> payload,
        out byte claimedPlayer,
        out BuffTypeId[] buffTypes)
    {
        claimedPlayer = 0;
        buffTypes = [];
        try
        {
            PlayerBuffView view = PlayerBuffView.FromPayload(payload);
            claimedPlayer = view.PlayerId;
            ReadOnlySpan<ushort> wireBuffs = view.BuffTypesMemory.Span;
            if (wireBuffs.Length is < 1 or > MaximumBuffs + 1 || wireBuffs[^1] != 0)
                return TerrariaPlayerBuffDecodeResult.Malformed;

            var decoded = new BuffTypeId[wireBuffs.Length - 1];
            for (int i = 0; i < decoded.Length; i++)
            {
                ushort raw = wireBuffs[i];
                if (raw == 0)
                    return TerrariaPlayerBuffDecodeResult.Malformed;
                if (!VanillaBuffIds.TryCreate(raw, out BuffTypeId type) || type == VanillaBuffIds.None)
                    return TerrariaPlayerBuffDecodeResult.InvalidBuffType;
                decoded[i] = type;
            }

            buffTypes = decoded;
            return TerrariaPlayerBuffDecodeResult.Decoded;
        }
        catch (InvalidDataException)
        {
            return TerrariaPlayerBuffDecodeResult.Malformed;
        }
        catch (ArgumentException)
        {
            return TerrariaPlayerBuffDecodeResult.Malformed;
        }
    }

    public static byte[] Encode(byte player, ReadOnlySpan<BuffTypeId> buffTypes)
    {
        if (buffTypes.Length > MaximumBuffs)
            throw new ArgumentOutOfRangeException(nameof(buffTypes));

        var wire = new ushort[buffTypes.Length + 1];
        for (int i = 0; i < buffTypes.Length; i++)
        {
            BuffTypeId type = buffTypes[i];
            if (type == VanillaBuffIds.None || !VanillaBuffIds.TryCreate(type.Value, out _))
                throw new ArgumentOutOfRangeException(nameof(buffTypes));
            wire[i] = checked((ushort)type.Value);
        }
        // Multiplicity exposes the packet-50 ushort sequence verbatim. TerrariaServer 1.4.5.8 writes an explicit
        // zero terminator, so the adapter includes that terminator in the sequence rather than inventing another codec.
        wire[^1] = 0;
        return (new PlayerBuff { PlayerId = player, BuffTypes = wire }).ToArray();
    }
}
