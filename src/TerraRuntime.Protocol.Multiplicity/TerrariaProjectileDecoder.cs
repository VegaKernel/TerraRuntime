using System.IO;
using global::Multiplicity.Packets.Models;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Decodes Terraria projectile lifecycle packets through Multiplicity's protocol-326 views. Packed key
/// layout and optional field flags stay inside the adapter; callers receive only protocol-neutral state.
/// </summary>
public static class TerrariaProjectileDecoder
{
    public const int MinimumUpdatePayloadLength = 23;
    public const int MaximumUpdatePayloadLength = 46;
    public const int DestroyPayloadLength = 12;

    public static TerrariaProjectileDecodeResult TryDecodeUpdate(
        in TerrariaFrame frame,
        out TerrariaProjectileUpdateState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.ProjectileNew)
            return TerrariaProjectileDecodeResult.WrongMessageId;

        long payloadLength = frame.Payload.Length;
        if (payloadLength is < MinimumUpdatePayloadLength or > MaximumUpdatePayloadLength)
            return TerrariaProjectileDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodeUpdatePayload(frame.Payload.FirstSpan, out state);

        int length = checked((int)payloadLength);
        Span<byte> scratch = stackalloc byte[MaximumUpdatePayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }

        return DecodeUpdatePayload(scratch[..length], out state);
    }

    public static TerrariaProjectileDecodeResult TryDecodeDestroy(
        in TerrariaFrame frame,
        out TerrariaProjectileDestroyState state)
    {
        state = default;
        if (frame.MessageId != (byte)TerrariaMessageId.ProjectileDestroy)
            return TerrariaProjectileDecodeResult.WrongMessageId;
        if (frame.Payload.Length != DestroyPayloadLength)
            return TerrariaProjectileDecodeResult.InvalidPayloadLength;

        if (frame.Payload.IsSingleSegment)
            return DecodeDestroyPayload(frame.Payload.FirstSpan, out state);

        Span<byte> scratch = stackalloc byte[DestroyPayloadLength];
        int offset = 0;
        foreach (ReadOnlyMemory<byte> segment in frame.Payload)
        {
            segment.Span.CopyTo(scratch[offset..]);
            offset += segment.Length;
        }

        return DecodeDestroyPayload(scratch, out state);
    }

    private static TerrariaProjectileDecodeResult DecodeUpdatePayload(
        ReadOnlySpan<byte> payload,
        out TerrariaProjectileUpdateState state)
    {
        try
        {
            var view = ProjectileNewView.FromPayload(payload);
            ProjectileKey key = view.Key;
            state = new TerrariaProjectileUpdateState(
                Key: new TerrariaProjectileKeyState(key.Spawner, key.Index, key.Generation),
                ProjectileType: view.Type,
                PositionX: view.PositionX,
                PositionY: view.PositionY,
                VelocityX: view.VelocityX,
                VelocityY: view.VelocityY,
                Ai0: view.AI0,
                Ai1: view.AI1,
                Ai2: view.AI2,
                BannerIdToRespondTo: view.BannerIdToRespondTo,
                Damage: view.Damage,
                KnockBack: view.KnockBack,
                OriginalDamage: view.OriginalDamage);
            return state.IsValid
                ? TerrariaProjectileDecodeResult.Decoded
                : TerrariaProjectileDecodeResult.InvalidState;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentOutOfRangeException)
        {
            state = default;
            return TerrariaProjectileDecodeResult.Malformed;
        }
    }

    private static TerrariaProjectileDecodeResult DecodeDestroyPayload(
        ReadOnlySpan<byte> payload,
        out TerrariaProjectileDestroyState state)
    {
        try
        {
            var view = ProjectileDestroyView.FromPayload(payload);
            ProjectileKey key = view.Key;
            state = new TerrariaProjectileDestroyState(
                Key: new TerrariaProjectileKeyState(key.Spawner, key.Index, key.Generation),
                PositionX: view.PositionX,
                PositionY: view.PositionY);
            return state.IsValid
                ? TerrariaProjectileDecodeResult.Decoded
                : TerrariaProjectileDecodeResult.InvalidState;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentOutOfRangeException)
        {
            state = default;
            return TerrariaProjectileDecodeResult.Malformed;
        }
    }
}
