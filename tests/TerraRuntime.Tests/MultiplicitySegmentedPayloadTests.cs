using System.Buffers;
using global::Multiplicity.Packets;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class MultiplicitySegmentedPayloadTests
{
    [Fact]
    public void Client_chat_decodes_when_fragmentation_splits_variable_text_fields()
    {
        var packet = new LoadNetModule
        {
            LoadedModule = new NetTextModule
            {
                PayloadKind = NetTextModulePayloadKind.ClientChatMessage,
                CommandName = "Say",
                ChatMessage = "fragmented-chat-payload"
            }
        };
        TerrariaFrame frame = SegmentedFrame(packet, 3, 8);

        Assert.Equal(
            TerrariaClientChatDecodeResult.Decoded,
            TerrariaChatCodec.TryDecodeClientMessage(in frame, out TerrariaClientChatMessage message));
        Assert.Equal("Say", message.CommandName);
        Assert.Equal("fragmented-chat-payload", message.Text);
    }

    [Fact]
    public void Variable_length_chest_state_decodes_across_fragmented_name()
    {
        var packet = new ChestOpen
        {
            ChestId = 17,
            ChestX = -120,
            ChestY = 350,
            ChestName = "fragmented-treasure"
        };
        TerrariaFrame frame = SegmentedFrame(packet, 4, 11);

        Assert.Equal(
            TerrariaChestDecodeResult.Decoded,
            TerrariaChestCodec.TryDecodeActiveChest(in frame, out TerrariaActiveChestState state));
        Assert.Equal((short)17, state.ChestId);
        Assert.Equal((short)-120, state.ChestX);
        Assert.Equal((short)350, state.ChestY);
        Assert.Equal("fragmented-treasure", state.ChestName);
    }

    [Fact]
    public void Fixed_chest_payloads_also_decode_from_multiple_receive_segments()
    {
        TerrariaFrame open = SegmentedFrame(new ChestGetContents { TileX = 123, TileY = -456 }, 1, 3);
        TerrariaFrame item = SegmentedFrame(new ChestItem
        {
            ChestId = 4,
            ItemSlot = 8,
            Stack = 99,
            Prefix = 2,
            ItemNetId = 1
        }, 2, 5);
        TerrariaFrame lookup = SegmentedFrame(new ChestName
        {
            ChestId = -1,
            ChestX = 20,
            ChestY = 30,
            HasName = false
        }, 1, 4);

        Assert.Equal(TerrariaChestDecodeResult.Decoded, TerrariaChestCodec.TryDecodeOpenRequest(in open, out TerrariaChestOpenRequest openState));
        Assert.Equal(new TerrariaChestOpenRequest(123, -456), openState);
        Assert.Equal(TerrariaChestDecodeResult.Decoded, TerrariaChestCodec.TryDecodeItem(in item, out TerrariaChestItemState itemState));
        Assert.Equal((short)4, itemState.ChestId);
        Assert.Equal((byte)8, itemState.ItemSlot);
        Assert.Equal(TerrariaChestDecodeResult.Decoded, TerrariaChestCodec.TryDecodeNameLookup(in lookup, out TerrariaChestNameLookupRequest lookupState));
        Assert.Equal(new TerrariaChestNameLookupRequest(-1, 20, 30), lookupState);
    }

    private static TerrariaFrame SegmentedFrame(TerrariaPacket packet, int firstSplit, int secondSplit)
    {
        using var stream = new MemoryStream();
        packet.ToStream(stream);
        byte[] frameBytes = stream.ToArray();
        byte[] payload = frameBytes[TerrariaPacket.PacketHeaderLength..];
        Assert.InRange(firstSplit, 1, payload.Length - 2);
        Assert.InRange(secondSplit, firstSplit + 1, payload.Length - 1);

        var first = new Segment(payload.AsMemory(0, firstSplit));
        var second = first.Append(payload.AsMemory(firstSplit, secondSplit - firstSplit));
        Segment last = second.Append(payload.AsMemory(secondSplit));
        var segmentedPayload = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);

        return new TerrariaFrame(
            checked((ushort)frameBytes.Length),
            frameBytes[2],
            ReadOnlySequence<byte>.Empty,
            segmentedPayload);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var segment = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = segment;
            return segment;
        }
    }
}
