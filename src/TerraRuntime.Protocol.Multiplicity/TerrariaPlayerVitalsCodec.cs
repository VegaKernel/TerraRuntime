using System.IO;
using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Multiplicity-backed adapters for Terraria player health (16) and mana (42).
/// TerraRuntime owns authoritative identity; Multiplicity owns the wire layout.
/// </summary>
public static class TerrariaPlayerVitalsCodec
{
    public const int PayloadLength = 5;

    public static TerrariaPlayerHealthDecodeResult TryDecodeHealth(
        in TerrariaFrame frame,
        out TerrariaPlayerHealthState health)
    {
        health = default;
        if (frame.MessageId != (byte)TerrariaMessageId.PlayerHp)
            return TerrariaPlayerHealthDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaPlayerHealthDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodeHealthPayload(frame.Payload.FirstSpan, out health);

        Span<byte> scratch = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }

        return DecodeHealthPayload(scratch, out health);
    }

    public static TerrariaPlayerManaDecodeResult TryDecodeMana(
        in TerrariaFrame frame,
        out TerrariaPlayerManaState mana)
    {
        mana = default;
        if (frame.MessageId != (byte)TerrariaMessageId.PlayerMana)
            return TerrariaPlayerManaDecodeResult.WrongMessageId;
        if (frame.Payload.Length != PayloadLength)
            return TerrariaPlayerManaDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodeManaPayload(frame.Payload.FirstSpan, out mana);

        Span<byte> scratch = stackalloc byte[PayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }

        return DecodeManaPayload(scratch, out mana);
    }

    public static byte[] EncodeHealth(in TerrariaPlayerHealthState health) =>
        MultiplicityPacketSerializer.Serialize(new PlayerHp
        {
            PlayerId = health.PlayerId,
            Hp = health.Life,
            MaxHp = health.MaxLife
        });

    public static byte[] EncodeMana(in TerrariaPlayerManaState mana) =>
        MultiplicityPacketSerializer.Serialize(new PlayerMana
        {
            PlayerId = mana.PlayerId,
            Mana = mana.Mana,
            MaxMana = mana.MaxMana
        });

    private static TerrariaPlayerHealthDecodeResult DecodeHealthPayload(
        ReadOnlySpan<byte> payload,
        out TerrariaPlayerHealthState health)
    {
        try
        {
            var view = PlayerHpView.FromPayload(payload);
            health = new TerrariaPlayerHealthState(view.PlayerId, view.Hp, view.MaxHp);
            return TerrariaPlayerHealthDecodeResult.Decoded;
        }
        catch (InvalidDataException)
        {
            health = default;
            return TerrariaPlayerHealthDecodeResult.Malformed;
        }
    }

    private static TerrariaPlayerManaDecodeResult DecodeManaPayload(
        ReadOnlySpan<byte> payload,
        out TerrariaPlayerManaState mana)
    {
        try
        {
            var view = PlayerManaView.FromPayload(payload);
            mana = new TerrariaPlayerManaState(view.PlayerId, view.Mana, view.MaxMana);
            return TerrariaPlayerManaDecodeResult.Decoded;
        }
        catch (InvalidDataException)
        {
            mana = default;
            return TerrariaPlayerManaDecodeResult.Malformed;
        }
    }
}
