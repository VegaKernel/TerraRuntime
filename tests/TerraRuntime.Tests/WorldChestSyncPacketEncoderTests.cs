using System.Buffers.Binary;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldChestSyncPacketEncoderTests
{
    [Fact]
    public void Encodes_size_then_every_slot_including_empty_slots()
    {
        var chest = new WorldChest(
            SlotId: 12,
            X: 400,
            Y: 300,
            Name: "loot",
            Items:
            [
                new WorldChestItem(Stack: 7, ItemType: 42, Prefix: 3),
                default
            ]);

        WorldChestSyncPacketEncodeResult result = WorldChestSyncPacketEncoder.TryEncode(chest, out ReadOnlyMemory<byte>[] frames);

        Assert.Equal(WorldChestSyncPacketEncodeResult.Encoded, result);
        Assert.Equal(3, frames.Length);

        ReadOnlySpan<byte> size = frames[0].Span;
        Assert.Equal(7, BinaryPrimitives.ReadUInt16LittleEndian(size));
        Assert.Equal(155, size[2]);
        Assert.Equal(12, BinaryPrimitives.ReadInt16LittleEndian(size[3..5]));
        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(size[5..7]));

        ReadOnlySpan<byte> item = frames[1].Span;
        Assert.Equal(11, BinaryPrimitives.ReadUInt16LittleEndian(item));
        Assert.Equal(32, item[2]);
        Assert.Equal(12, BinaryPrimitives.ReadInt16LittleEndian(item[3..5]));
        Assert.Equal(0, item[5]);
        Assert.Equal(7, BinaryPrimitives.ReadInt16LittleEndian(item[6..8]));
        Assert.Equal(3, item[8]);
        Assert.Equal(42, BinaryPrimitives.ReadInt16LittleEndian(item[9..11]));

        ReadOnlySpan<byte> empty = frames[2].Span;
        Assert.Equal(32, empty[2]);
        Assert.Equal(1, empty[5]);
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(empty[6..8]));
        Assert.Equal(0, empty[8]);
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(empty[9..11]));
    }

    [Fact]
    public void Rejects_chests_whose_slot_count_cannot_fit_packet_32_slot_index()
    {
        var chest = new WorldChest(
            SlotId: 0,
            X: 0,
            Y: 0,
            Name: string.Empty,
            Items: new WorldChestItem[byte.MaxValue + 1]);

        WorldChestSyncPacketEncodeResult result = WorldChestSyncPacketEncoder.TryEncode(chest, out ReadOnlyMemory<byte>[] frames);

        Assert.Equal(WorldChestSyncPacketEncodeResult.InvalidChest, result);
        Assert.Empty(frames);
    }
}
