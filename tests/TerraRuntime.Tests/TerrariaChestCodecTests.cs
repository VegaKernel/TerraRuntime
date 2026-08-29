using System.Buffers;
using global::Multiplicity.Packets;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TerrariaChestCodecTests
{
    [Fact]
    public void Open_request_decodes_through_multiplicity()
    {
        TerrariaFrame frame = Frame(new ChestGetContents { TileX = 123, TileY = 456 });

        TerrariaChestDecodeResult result = TerrariaChestCodec.TryDecodeOpenRequest(
            in frame,
            out TerrariaChestOpenRequest request);

        Assert.Equal(TerrariaChestDecodeResult.Decoded, result);
        Assert.Equal((short)123, request.TileX);
        Assert.Equal((short)456, request.TileY);
    }

    [Fact]
    public void Chest_item_decodes_and_server_projection_round_trips()
    {
        TerrariaFrame frame = Frame(new ChestItem
        {
            ChestId = 7,
            ItemSlot = 3,
            Stack = 99,
            Prefix = 2,
            ItemNetId = 1
        });

        TerrariaChestDecodeResult result = TerrariaChestCodec.TryDecodeItem(
            in frame,
            out TerrariaChestItemState state);
        byte[] encoded = TerrariaChestCodec.EncodeChestItem(in state);
        ChestItem roundTrip = Assert.IsType<ChestItem>(Deserialize(encoded));

        Assert.Equal(TerrariaChestDecodeResult.Decoded, result);
        Assert.Equal((short)7, state.ChestId);
        Assert.Equal((byte)3, state.ItemSlot);
        Assert.Equal((short)99, state.Stack);
        Assert.Equal((byte)2, state.Prefix);
        Assert.Equal((short)1, state.ItemNetId);
        Assert.Equal(state.ChestId, roundTrip.ChestId);
        Assert.Equal(state.ItemSlot, roundTrip.ItemSlot);
        Assert.Equal(state.Stack, roundTrip.Stack);
        Assert.Equal(state.Prefix, roundTrip.Prefix);
        Assert.Equal(state.ItemNetId, roundTrip.ItemNetId);
    }

    [Fact]
    public void Active_chest_decodes_close_and_rename_shapes()
    {
        TerrariaFrame close = Frame(new ChestOpen
        {
            ChestId = -1,
            ChestX = 0,
            ChestY = 0,
            ChestName = string.Empty
        });
        TerrariaFrame rename = Frame(new ChestOpen
        {
            ChestId = 8,
            ChestX = 40,
            ChestY = 50,
            ChestName = "Loot"
        });

        Assert.Equal(
            TerrariaChestDecodeResult.Decoded,
            TerrariaChestCodec.TryDecodeActiveChest(in close, out TerrariaActiveChestState closeState));
        Assert.Equal((short)-1, closeState.ChestId);

        Assert.Equal(
            TerrariaChestDecodeResult.Decoded,
            TerrariaChestCodec.TryDecodeActiveChest(in rename, out TerrariaActiveChestState renameState));
        Assert.Equal((short)8, renameState.ChestId);
        Assert.Equal((short)40, renameState.ChestX);
        Assert.Equal((short)50, renameState.ChestY);
        Assert.Equal("Loot", renameState.ChestName);
        Assert.Equal((byte)4, renameState.NameLength);
    }

    [Fact]
    public void Chest_name_lookup_decodes_six_byte_request_without_response_name()
    {
        TerrariaFrame lookup = Frame(new ChestName
        {
            ChestId = -1,
            ChestX = 123,
            ChestY = 456,
            HasName = false
        });

        Assert.Equal(
            TerrariaChestDecodeResult.Decoded,
            TerrariaChestCodec.TryDecodeNameLookup(in lookup, out TerrariaChestNameLookupRequest request));
        Assert.Equal((short)-1, request.ChestId);
        Assert.Equal((short)123, request.ChestX);
        Assert.Equal((short)456, request.ChestY);

        TerrariaFrame response = Frame(new ChestName
        {
            ChestId = 7,
            ChestX = 123,
            ChestY = 456,
            HasName = true,
            Name = "Loot"
        });
        Assert.Equal(
            TerrariaChestDecodeResult.InvalidPayloadLength,
            TerrariaChestCodec.TryDecodeNameLookup(in response, out _));
    }

    [Fact]
    public void Server_chest_index_and_name_use_multiplicity_wire_types()
    {
        SyncPlayerChestIndex chestIndex = Assert.IsType<SyncPlayerChestIndex>(
            Deserialize(TerrariaChestCodec.EncodePlayerChestIndex(4, 12)));
        ChestName chestName = Assert.IsType<ChestName>(
            Deserialize(TerrariaChestCodec.EncodeChestName(12, 34, 56, "Treasure")));

        Assert.Equal((byte)4, chestIndex.Player);
        Assert.Equal((short)12, chestIndex.Chest);
        Assert.Equal((short)12, chestName.ChestId);
        Assert.Equal((short)34, chestName.ChestX);
        Assert.Equal((short)56, chestName.ChestY);
        Assert.True(chestName.HasName);
        Assert.Equal("Treasure", chestName.Name);
    }

    [Fact]
    public void Wrong_or_truncated_chest_packet_is_not_accepted()
    {
        TerrariaFrame wrong = RawFrame(TerrariaMessageId.PlayerMana, new byte[4]);
        TerrariaFrame truncated = RawFrame(TerrariaMessageId.RequestChestOpen, new byte[3]);

        Assert.Equal(
            TerrariaChestDecodeResult.WrongMessageId,
            TerrariaChestCodec.TryDecodeOpenRequest(in wrong, out _));
        Assert.Equal(
            TerrariaChestDecodeResult.InvalidPayloadLength,
            TerrariaChestCodec.TryDecodeOpenRequest(in truncated, out _));
    }

    private static TerrariaFrame Frame(TerrariaPacket packet)
    {
        using var stream = new MemoryStream();
        packet.ToStream(stream);
        byte[] bytes = stream.ToArray();
        var buffer = new ReadOnlySequence<byte>(bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        return frame;
    }

    private static TerrariaFrame RawFrame(TerrariaMessageId id, byte[] payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)id,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));

    private static TerrariaPacket Deserialize(byte[] frame)
    {
        Assert.True(TerrariaPacket.TryDeserializePayload(
            frame[2],
            frame.AsMemory(TerrariaPacket.PacketHeaderLength),
            out TerrariaPacket packet));
        return packet;
    }
}
