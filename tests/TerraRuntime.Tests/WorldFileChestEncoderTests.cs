using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileChestEncoderTests
{
    [Fact]
    public void Roundtrips_canonical_chest_section_through_current_decoder()
    {
        var dimensions = new WorldDimensions(10, 10);
        WorldChest[] source =
        [
            new WorldChest(
                0,
                1,
                2,
                "alpha",
                [new WorldChestItem(5, 1, 3), default]),
            new WorldChest(
                1,
                4,
                5,
                "βeta",
                [new WorldChestItem(1, 1, 0)])
        ];

        using var stream = new MemoryStream();
        Assert.Equal(
            WorldFileChestEncodeResult.Encoded,
            WorldFileChestEncoder.TryEncode(source, dimensions, stream, out long bytesWritten));
        Assert.Equal(stream.Length, bytesWritten);

        byte[] section = stream.ToArray();
        var envelope = CreateEnvelope(section.Length);
        WorldFileHeader header = CreateHeader(dimensions);

        Assert.Equal(
            WorldFileChestDecodeResult.Decoded,
            WorldFileChestDecoder.TryDecode(
                section,
                envelope,
                header,
                maxItemsPerChest: byte.MaxValue + 1,
                maxTotalItems: 2 * (byte.MaxValue + 1L),
                out WorldChest[] decoded,
                out int consumed));

        Assert.Equal(section.Length, consumed);
        Assert.Equal(source.Length, decoded.Length);
        for (int index = 0; index < source.Length; index++)
        {
            Assert.Equal(source[index].SlotId, decoded[index].SlotId);
            Assert.Equal(source[index].X, decoded[index].X);
            Assert.Equal(source[index].Y, decoded[index].Y);
            Assert.Equal(source[index].Name, decoded[index].Name);
            Assert.Equal(source[index].Items, decoded[index].Items);
        }
    }

    [Fact]
    public void Runtime_mutation_snapshot_roundtrips_through_vanilla_chest_section()
    {
        var dimensions = new WorldDimensions(10, 10);
        var store = new RuntimeChestStore(
        [
            new WorldChest(
                0,
                1,
                2,
                "Base",
                [new WorldChestItem(1, 1, 0), default])
        ]);
        ConnectionHandle owner = Connection(1, 0, 1);
        Assert.True(store.TryOpen(owner, 1, 2, out _));

        var itemUpdate = new TerrariaChestItemState(0, 1, 7, 2, 1);
        Assert.True(store.TrySetItem(owner, in itemUpdate, out TerrariaChestItemState committed));
        Assert.Equal(itemUpdate, committed);

        var rename = new TerrariaActiveChestState(0, 1, 2, 4, "Loot");
        Assert.True(store.TryApplyActiveState(owner, in rename, out WorldChest? renamed, out bool closed));
        Assert.NotNull(renamed);
        Assert.False(closed);

        WorldChest[] snapshot = store.CaptureSnapshot();
        using var stream = new MemoryStream();
        Assert.Equal(
            WorldFileChestEncodeResult.Encoded,
            WorldFileChestEncoder.TryEncode(snapshot, dimensions, stream, out long bytesWritten));
        Assert.Equal(stream.Length, bytesWritten);

        byte[] section = stream.ToArray();
        Assert.Equal(
            WorldFileChestDecodeResult.Decoded,
            WorldFileChestDecoder.TryDecode(
                section,
                CreateEnvelope(section.Length),
                CreateHeader(dimensions),
                maxItemsPerChest: byte.MaxValue + 1,
                maxTotalItems: byte.MaxValue + 1L,
                out WorldChest[] decoded,
                out int consumed));

        Assert.Equal(section.Length, consumed);
        WorldChest persisted = Assert.Single(decoded);
        Assert.Equal((short)0, persisted.SlotId);
        Assert.Equal("Loot", persisted.Name);
        Assert.Equal(new WorldChestItem(1, 1, 0), persisted.Items[0]);
        Assert.Equal(new WorldChestItem(7, 1, 2), persisted.Items[1]);
    }

    [Fact]
    public void Rejects_sparse_slot_identity_before_writing()
    {
        var dimensions = new WorldDimensions(10, 10);
        WorldChest[] source =
        [
            new WorldChest(0, 1, 2, "first", []),
            new WorldChest(2, 4, 5, "hole", [])
        ];
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFileChestEncodeResult.NonCanonicalSlotOrder,
            WorldFileChestEncoder.TryEncode(source, dimensions, stream, out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Rejects_noncanonical_item_state_before_writing()
    {
        var dimensions = new WorldDimensions(10, 10);
        WorldChest[] source =
        [
            new WorldChest(
                0,
                1,
                2,
                "bad-stack",
                [new WorldChestItem(short.MaxValue + 1, 1, 0)])
        ];
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFileChestEncodeResult.InvalidItemState,
            WorldFileChestEncoder.TryEncode(source, dimensions, stream, out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, stream.Length);
    }

    private static WorldFileEnvelope CreateEnvelope(int chestSectionLength) =>
        new(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            sectionOffsets: [0, 0, 0, chestSectionLength],
            frameImportanceCount: VanillaWorldFormat326.TileTypeCount,
            frameImportanceBits: new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);

    private static WorldFileHeader CreateHeader(WorldDimensions dimensions) =>
        new(
            "test",
            "seed",
            1,
            Guid.Empty,
            1,
            0,
            160,
            0,
            160,
            dimensions);

    private static ConnectionHandle Connection(long connectionId, byte playerSlot, ulong generation) =>
        new(
            GameCommandSourceId.FromConnection(connectionId),
            new PlayerHandle(
                new PlayerSlotId(playerSlot),
                new PlayerSessionGeneration(generation)));
}
