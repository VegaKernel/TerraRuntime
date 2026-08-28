using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaJoinRequestDecoderTests
{
    [Fact]
    public void World_request_requires_empty_payload()
    {
        TerrariaFrame valid = Frame(TerrariaMessageId.RequestWorldData, []);
        TerrariaFrame invalid = Frame(TerrariaMessageId.RequestWorldData, [1]);

        Assert.Equal(TerrariaJoinDecodeResult.Decoded, TerrariaJoinRequestDecoder.TryDecodeWorldRequest(valid));
        Assert.Equal(TerrariaJoinDecodeResult.InvalidPayloadLength, TerrariaJoinRequestDecoder.TryDecodeWorldRequest(invalid));
    }

    [Fact]
    public void Decodes_protocol_326_section_bootstrap_request_through_multiplicity_view()
    {
        byte[] payload = new byte[TerrariaJoinRequestDecoder.SectionRequestPayloadLength];
        BinaryPrimitives.WriteInt32LittleEndian(payload, 1234);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), -1);
        payload[8] = 3;

        TerrariaJoinDecodeResult result = TerrariaJoinRequestDecoder.TryDecodeSectionRequest(
            Frame(TerrariaMessageId.SpawnTileData, payload),
            out TerrariaSectionBootstrapRequest request);

        Assert.Equal(TerrariaJoinDecodeResult.Decoded, result);
        Assert.Equal(1234, request.TileX);
        Assert.Equal(-1, request.TileY);
        Assert.Equal((byte)3, request.Team);
    }

    [Fact]
    public void Decodes_protocol_326_player_spawn_through_multiplicity_view_without_trusting_claimed_slot()
    {
        byte[] payload = CreateSpawnPayload(claimedPlayerId: 99);

        TerrariaJoinDecodeResult result = TerrariaJoinRequestDecoder.TryDecodePlayerSpawn(
            Frame(TerrariaMessageId.PlayerSpawn, payload),
            out TerrariaPlayerSpawnRequest request);

        Assert.Equal(TerrariaJoinDecodeResult.Decoded, result);
        Assert.Equal((byte)99, request.ClaimedPlayerId);
        Assert.Equal((short)100, request.SpawnX);
        Assert.Equal((short)200, request.SpawnY);
        Assert.Equal(300, request.RespawnTimer);
        Assert.Equal((short)4, request.DeathsPve);
        Assert.Equal((short)5, request.DeathsPvp);
        Assert.Equal((byte)2, request.Team);
        Assert.Equal((byte)1, request.SpawnContext);
    }

    [Fact]
    public void Decodes_segmented_join_payloads_without_reimplementing_wire_layout()
    {
        byte[] sectionPayload = new byte[TerrariaJoinRequestDecoder.SectionRequestPayloadLength];
        BinaryPrimitives.WriteInt32LittleEndian(sectionPayload, 321);
        BinaryPrimitives.WriteInt32LittleEndian(sectionPayload.AsSpan(4), 654);
        sectionPayload[8] = 4;

        TerrariaJoinDecodeResult sectionResult = TerrariaJoinRequestDecoder.TryDecodeSectionRequest(
            SegmentedFrame(TerrariaMessageId.SpawnTileData, sectionPayload, split: 4),
            out TerrariaSectionBootstrapRequest section);
        TerrariaJoinDecodeResult spawnResult = TerrariaJoinRequestDecoder.TryDecodePlayerSpawn(
            SegmentedFrame(TerrariaMessageId.PlayerSpawn, CreateSpawnPayload(claimedPlayerId: 7), split: 6),
            out TerrariaPlayerSpawnRequest spawn);

        Assert.Equal(TerrariaJoinDecodeResult.Decoded, sectionResult);
        Assert.Equal(321, section.TileX);
        Assert.Equal(654, section.TileY);
        Assert.Equal((byte)4, section.Team);
        Assert.Equal(TerrariaJoinDecodeResult.Decoded, spawnResult);
        Assert.Equal((byte)7, spawn.ClaimedPlayerId);
        Assert.Equal((short)100, spawn.SpawnX);
        Assert.Equal((short)200, spawn.SpawnY);
    }

    [Fact]
    public void Rejects_wrong_message_id_and_wrong_fixed_lengths()
    {
        TerrariaFrame wrongId = Frame(TerrariaMessageId.WorldData, new byte[9]);
        TerrariaFrame badSection = Frame(TerrariaMessageId.SpawnTileData, new byte[8]);
        TerrariaFrame badSpawn = Frame(TerrariaMessageId.PlayerSpawn, new byte[14]);

        Assert.Equal(
            TerrariaJoinDecodeResult.WrongMessageId,
            TerrariaJoinRequestDecoder.TryDecodeSectionRequest(wrongId, out _));
        Assert.Equal(
            TerrariaJoinDecodeResult.InvalidPayloadLength,
            TerrariaJoinRequestDecoder.TryDecodeSectionRequest(badSection, out _));
        Assert.Equal(
            TerrariaJoinDecodeResult.InvalidPayloadLength,
            TerrariaJoinRequestDecoder.TryDecodePlayerSpawn(badSpawn, out _));
    }

    private static byte[] CreateSpawnPayload(byte claimedPlayerId)
    {
        byte[] payload = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];
        payload[0] = claimedPlayerId;
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), 100);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), 200);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(5), 300);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(9), 4);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(11), 5);
        payload[13] = 2;
        payload[14] = 1;
        return payload;
    }

    private static TerrariaFrame Frame(TerrariaMessageId id, byte[] payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)id,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));

    private static TerrariaFrame SegmentedFrame(TerrariaMessageId id, byte[] payload, int split)
    {
        var first = new SequenceSegment(payload.AsMemory(0, split));
        SequenceSegment last = first.Append(payload.AsMemory(split));
        var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        return new TerrariaFrame(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)id,
            ReadOnlySequence<byte>.Empty,
            sequence);
    }

    private sealed class SequenceSegment : ReadOnlySequenceSegment<byte>
    {
        public SequenceSegment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public SequenceSegment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new SequenceSegment(memory)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = segment;
            return segment;
        }
    }
}
