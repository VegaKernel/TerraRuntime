using System.IO;
using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

public enum TerrariaPlayerPvpBuffDecodeResult : byte
{
    Decoded = 0,
    WrongMessageId = 1,
    InvalidPayloadLength = 2,
    Malformed = 3,
    InvalidBuffType = 4,
    InvalidDuration = 5
}

/// <summary>
/// Source-pinned adapter for TerrariaServer 1.4.5.8 packet 55 (AddPlayerBuffPvP).
/// The wire payload is exactly [target player byte][buff ushort][duration int32].
/// </summary>
public static class TerrariaPlayerPvpBuffCodec1458
{
    public const int PayloadLength = sizeof(byte) + sizeof(ushort) + sizeof(int);

    public static TerrariaPlayerPvpBuffDecodeResult TryDecode(
        in TerrariaFrame frame,
        out byte targetPlayer,
        out BuffTypeId buffType,
        out int durationTicks)
    {
        targetPlayer = 0;
        buffType = default;
        durationTicks = 0;
        if (frame.MessageId != (byte)TerrariaMessageId.AddPlayerBuffPvp)
            return TerrariaPlayerPvpBuffDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaPlayerPvpBuffDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodePayload(frame.Payload.FirstSpan, out targetPlayer, out buffType, out durationTicks);

        Span<byte> scratch = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }
        return DecodePayload(scratch, out targetPlayer, out buffType, out durationTicks);
    }

    private static TerrariaPlayerPvpBuffDecodeResult DecodePayload(
        ReadOnlySpan<byte> payload,
        out byte targetPlayer,
        out BuffTypeId buffType,
        out int durationTicks)
    {
        targetPlayer = 0;
        buffType = default;
        durationTicks = 0;
        try
        {
            PlayerAddBuffView view = PlayerAddBuffView.FromPayload(payload);
            targetPlayer = view.PlayerId;
            if (!VanillaBuffIds.TryCreate(view.Buff, out BuffTypeId decoded) || decoded == VanillaBuffIds.None)
                return TerrariaPlayerPvpBuffDecodeResult.InvalidBuffType;
            if (view.Time <= 0)
                return TerrariaPlayerPvpBuffDecodeResult.InvalidDuration;

            buffType = decoded;
            durationTicks = view.Time;
            return TerrariaPlayerPvpBuffDecodeResult.Decoded;
        }
        catch (InvalidDataException)
        {
            return TerrariaPlayerPvpBuffDecodeResult.Malformed;
        }
        catch (ArgumentException)
        {
            return TerrariaPlayerPvpBuffDecodeResult.Malformed;
        }
    }

    public static byte[] Encode(byte targetPlayer, BuffTypeId buffType, int durationTicks)
    {
        if (buffType == VanillaBuffIds.None || !VanillaBuffIds.TryCreate(buffType.Value, out _))
            throw new ArgumentOutOfRangeException(nameof(buffType));
        if (durationTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationTicks));

        return (new PlayerAddBuff
        {
            PlayerId = targetPlayer,
            Buff = checked((ushort)buffType.Value),
            Time = durationTicks
        }).ToArray();
    }
}
