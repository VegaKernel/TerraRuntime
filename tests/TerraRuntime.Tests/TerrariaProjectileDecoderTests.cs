using System.Buffers;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaProjectileDecoderTests
{
    [Fact]
    public void Update_round_trips_all_projectile_fields_through_Multiplicity()
    {
        var expected = new TerrariaProjectileUpdateState(
            Key: new TerrariaProjectileKeyState(7, 321, 1234),
            ProjectileType: 14,
            PositionX: 100.25f,
            PositionY: -20.5f,
            VelocityX: 3.5f,
            VelocityY: -4.25f,
            Ai0: 1.5f,
            Ai1: -2.5f,
            Ai2: 3.25f,
            BannerIdToRespondTo: 42,
            Damage: 71,
            KnockBack: 2.75f,
            OriginalDamage: 99);
        Assert.True(TerrariaProjectileEncoder.TryEncodeUpdate(in expected, out byte[] encoded));
        TerrariaFrame frame = ReadSingleFrame(encoded);

        TerrariaProjectileDecodeResult result = TerrariaProjectileDecoder.TryDecodeUpdate(in frame, out var actual);

        Assert.Equal(TerrariaProjectileDecodeResult.Decoded, result);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Update_round_trips_absent_optional_fields_as_vanilla_defaults()
    {
        var expected = new TerrariaProjectileUpdateState(
            Key: new TerrariaProjectileKeyState(1, 0, 1),
            ProjectileType: 1,
            PositionX: 10f,
            PositionY: 20f,
            VelocityX: 0f,
            VelocityY: 0f,
            Ai0: 0f,
            Ai1: 0f,
            Ai2: 0f,
            BannerIdToRespondTo: 0,
            Damage: 0,
            KnockBack: 0f,
            OriginalDamage: 0);
        Assert.True(TerrariaProjectileEncoder.TryEncodeUpdate(in expected, out byte[] encoded));
        TerrariaFrame frame = ReadSingleFrame(encoded);

        TerrariaProjectileDecodeResult result = TerrariaProjectileDecoder.TryDecodeUpdate(in frame, out var actual);

        Assert.Equal(TerrariaProjectileDecodeResult.Decoded, result);
        Assert.Equal(expected, actual);
        Assert.InRange((int)frame.Payload.Length,
            TerrariaProjectileDecoder.MinimumUpdatePayloadLength,
            TerrariaProjectileDecoder.MaximumUpdatePayloadLength);
    }

    [Fact]
    public void Destroy_round_trips_key_and_final_position()
    {
        var expected = new TerrariaProjectileDestroyState(
            new TerrariaProjectileKeyState(5, 1000, 16383),
            512.5f,
            -64.25f);
        Assert.True(TerrariaProjectileEncoder.TryEncodeDestroy(in expected, out byte[] encoded));
        TerrariaFrame frame = ReadSingleFrame(encoded);

        TerrariaProjectileDecodeResult result = TerrariaProjectileDecoder.TryDecodeDestroy(in frame, out var actual);

        Assert.Equal(TerrariaProjectileDecodeResult.Decoded, result);
        Assert.Equal(expected, actual);
        Assert.Equal(TerrariaProjectileDecoder.DestroyPayloadLength, frame.Payload.Length);
    }

    [Fact]
    public void Wrong_message_and_payload_lengths_are_rejected_before_view_materialization()
    {
        TerrariaFrame wrongUpdate = Frame((byte)TerrariaMessageId.ProjectileDestroy, new byte[23]);
        TerrariaFrame shortUpdate = Frame((byte)TerrariaMessageId.ProjectileNew, new byte[22]);
        TerrariaFrame wrongDestroy = Frame((byte)TerrariaMessageId.ProjectileNew, new byte[12]);
        TerrariaFrame longDestroy = Frame((byte)TerrariaMessageId.ProjectileDestroy, new byte[13]);

        Assert.Equal(TerrariaProjectileDecodeResult.WrongMessageId,
            TerrariaProjectileDecoder.TryDecodeUpdate(in wrongUpdate, out _));
        Assert.Equal(TerrariaProjectileDecodeResult.InvalidPayloadLength,
            TerrariaProjectileDecoder.TryDecodeUpdate(in shortUpdate, out _));
        Assert.Equal(TerrariaProjectileDecodeResult.WrongMessageId,
            TerrariaProjectileDecoder.TryDecodeDestroy(in wrongDestroy, out _));
        Assert.Equal(TerrariaProjectileDecodeResult.InvalidPayloadLength,
            TerrariaProjectileDecoder.TryDecodeDestroy(in longDestroy, out _));
    }

    [Fact]
    public void Structurally_parseable_but_illegal_key_is_reported_as_invalid_state()
    {
        TerrariaFrame update = Frame((byte)TerrariaMessageId.ProjectileNew, new byte[23]);
        TerrariaFrame destroy = Frame((byte)TerrariaMessageId.ProjectileDestroy, new byte[12]);

        Assert.Equal(TerrariaProjectileDecodeResult.InvalidState,
            TerrariaProjectileDecoder.TryDecodeUpdate(in update, out _));
        Assert.Equal(TerrariaProjectileDecodeResult.InvalidState,
            TerrariaProjectileDecoder.TryDecodeDestroy(in destroy, out _));
    }

    [Fact]
    public void Extra_bytes_not_described_by_projectile_flags_are_malformed()
    {
        TerrariaFrame frame = Frame((byte)TerrariaMessageId.ProjectileNew, new byte[24]);

        Assert.Equal(TerrariaProjectileDecodeResult.Malformed,
            TerrariaProjectileDecoder.TryDecodeUpdate(in frame, out _));
    }

    private static TerrariaFrame ReadSingleFrame(byte[] encoded)
    {
        var buffer = new ReadOnlySequence<byte>(encoded);
        TerrariaFrameReadResult result = TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame);
        Assert.Equal(TerrariaFrameReadResult.Frame, result);
        Assert.Equal(0, buffer.Length);
        return frame;
    }

    private static TerrariaFrame Frame(byte messageId, byte[] payload)
    {
        var sequence = new ReadOnlySequence<byte>(payload);
        return new TerrariaFrame(
            PacketLength: checked((ushort)(payload.Length + TerrariaFrameDecoderOptions.MinimumFrameLength)),
            MessageId: messageId,
            Packet: sequence,
            Payload: sequence);
    }
}
