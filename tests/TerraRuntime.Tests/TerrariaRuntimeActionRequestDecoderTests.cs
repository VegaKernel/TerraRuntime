using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaRuntimeActionRequestDecoderTests
{
    [Fact]
    public void Packet_61_decodes_claimed_player_and_npc_type_through_typed_view()
    {
        byte[] payload = new byte[TerrariaRuntimeActionRequestDecoder.BossSummonPayloadLength];
        BinaryPrimitives.WriteInt16LittleEndian(payload, 27);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(2), -65);
        TerrariaFrame frame = CreateFrame(TerrariaMessageId.SpawnBoss, new ReadOnlySequence<byte>(payload));

        TerrariaRuntimeActionRequestDecodeResult result =
            TerrariaRuntimeActionRequestDecoder.TryDecodeBossSummon(in frame, out TerrariaBossSummonRequest request);

        Assert.Equal(TerrariaRuntimeActionRequestDecodeResult.Decoded, result);
        Assert.Equal((short)27, request.ClaimedPlayerId);
        Assert.Equal((short)-65, request.NpcType);
    }

    [Fact]
    public void Packet_61_decodes_segmented_payload_without_application_offsets()
    {
        byte[] firstBytes = [0xFE, 0xFF];
        byte[] secondBytes = [0x7B, 0x00];
        var first = new BufferSegment(firstBytes);
        BufferSegment last = first.Append(secondBytes);
        var payload = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        TerrariaFrame frame = CreateFrame(TerrariaMessageId.SpawnBoss, payload);

        TerrariaRuntimeActionRequestDecodeResult result =
            TerrariaRuntimeActionRequestDecoder.TryDecodeBossSummon(in frame, out TerrariaBossSummonRequest request);

        Assert.Equal(TerrariaRuntimeActionRequestDecodeResult.Decoded, result);
        Assert.Equal((short)-2, request.ClaimedPlayerId);
        Assert.Equal((short)123, request.NpcType);
    }

    [Fact]
    public void Packet_73_decodes_segmented_exact_subtype()
    {
        var first = new BufferSegment(ReadOnlyMemory<byte>.Empty);
        BufferSegment last = first.Append(new byte[] { 2 });
        var payload = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        TerrariaFrame frame = CreateFrame(TerrariaMessageId.TeleportRequest, payload);

        TerrariaRuntimeActionRequestDecodeResult result =
            TerrariaRuntimeActionRequestDecoder.TryDecodeTeleportRequest(in frame, out TerrariaTeleportRequest request);

        Assert.Equal(TerrariaRuntimeActionRequestDecodeResult.Decoded, result);
        Assert.Equal((byte)2, request.Subtype);
    }

    [Fact]
    public void Decoders_reject_wrong_message_and_non_exact_payload_lengths()
    {
        TerrariaFrame wrongBossId = CreateFrame(
            TerrariaMessageId.TeleportRequest,
            new ReadOnlySequence<byte>(new byte[TerrariaRuntimeActionRequestDecoder.BossSummonPayloadLength]));
        TerrariaFrame shortBoss = CreateFrame(
            TerrariaMessageId.SpawnBoss,
            new ReadOnlySequence<byte>(new byte[TerrariaRuntimeActionRequestDecoder.BossSummonPayloadLength - 1]));
        TerrariaFrame wrongTeleportId = CreateFrame(
            TerrariaMessageId.SpawnBoss,
            new ReadOnlySequence<byte>(new byte[TerrariaRuntimeActionRequestDecoder.TeleportRequestPayloadLength]));
        TerrariaFrame longTeleport = CreateFrame(
            TerrariaMessageId.TeleportRequest,
            new ReadOnlySequence<byte>(new byte[TerrariaRuntimeActionRequestDecoder.TeleportRequestPayloadLength + 1]));

        Assert.Equal(
            TerrariaRuntimeActionRequestDecodeResult.WrongMessageId,
            TerrariaRuntimeActionRequestDecoder.TryDecodeBossSummon(in wrongBossId, out _));
        Assert.Equal(
            TerrariaRuntimeActionRequestDecodeResult.InvalidPayloadLength,
            TerrariaRuntimeActionRequestDecoder.TryDecodeBossSummon(in shortBoss, out _));
        Assert.Equal(
            TerrariaRuntimeActionRequestDecodeResult.WrongMessageId,
            TerrariaRuntimeActionRequestDecoder.TryDecodeTeleportRequest(in wrongTeleportId, out _));
        Assert.Equal(
            TerrariaRuntimeActionRequestDecodeResult.InvalidPayloadLength,
            TerrariaRuntimeActionRequestDecoder.TryDecodeTeleportRequest(in longTeleport, out _));
    }

    private static TerrariaFrame CreateFrame(TerrariaMessageId messageId, ReadOnlySequence<byte> payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)messageId,
            default,
            payload);

    private sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        public BufferSegment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public BufferSegment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new BufferSegment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = next;
            return next;
        }
    }
}
