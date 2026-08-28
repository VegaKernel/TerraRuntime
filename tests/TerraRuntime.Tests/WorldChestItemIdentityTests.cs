using System.Buffers.Binary;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldChestItemIdentityTests
{
    private const int EnvelopeEnd = 167;
    private const int ChestStart = 184;

    [Fact]
    public void Chest_item_exposes_typed_item_and_prefix_identity()
    {
        var item = new WorldChestItem(Stack: 5, ItemType: 100, Prefix: 3);

        Assert.True(item.TryGetItemType(out ItemTypeId itemType));
        Assert.Equal(100, itemType.Value);
        Assert.Equal(3, item.PrefixId.Value);
        Assert.True(item.HasValidItemType);
        Assert.True(default(WorldChestItem).HasValidItemType);
        Assert.False(new WorldChestItem(Stack: 1, ItemType: 0, Prefix: 0).HasValidItemType);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(VanillaItemIds.Count)]
    public void Decoder_rejects_invalid_identity_for_nonempty_item(int itemType)
    {
        byte[] chestBytes = CreateChestBytes(writer =>
        {
            writer.Write((short)1);
            writer.Write(1);
            writer.Write(1);
            writer.Write("invalid-item");
            writer.Write(1);
            writer.Write((short)1);
            writer.Write(itemType);
            writer.Write((byte)0);
        });
        byte[] file = CreateCurrentFile(chestBytes);

        Assert.Equal(
            WorldFileChestDecodeResult.InvalidItemType,
            WorldFileChestDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                CreateHeader(),
                maxItemsPerChest: 4,
                maxTotalItems: 4,
                out WorldChest[] chests,
                out _));
        Assert.Empty(chests);
    }

    [Fact]
    public void Decoder_keeps_legacy_negative_stack_normalization_for_valid_item_identity()
    {
        byte[] chestBytes = CreateChestBytes(writer =>
        {
            writer.Write((short)1);
            writer.Write(1);
            writer.Write(1);
            writer.Write("legacy-stack");
            writer.Write(1);
            writer.Write((short)-7);
            writer.Write(250);
            writer.Write((byte)9);
        });
        byte[] file = CreateCurrentFile(chestBytes);

        Assert.Equal(
            WorldFileChestDecodeResult.Decoded,
            WorldFileChestDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                CreateHeader(),
                maxItemsPerChest: 4,
                maxTotalItems: 4,
                out WorldChest[] chests,
                out _));

        WorldChestItem item = Assert.Single(chests).Items[0];
        Assert.Equal(new WorldChestItem(1, 250, 9), item);
        Assert.True(item.TryGetItemType(out ItemTypeId itemType));
        Assert.Equal(250, itemType.Value);
    }

    private static WorldFileHeader CreateHeader()
    {
        var dimensions = new WorldDimensions(10, 10);
        return new WorldFileHeader("test", "seed", 1, Guid.Empty, 1, 0, 160, 0, 160, dimensions);
    }

    private static byte[] CreateChestBytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true))
        {
            write(writer);
            writer.Flush();
        }
        return stream.ToArray();
    }

    private static WorldFileEnvelope ParseEnvelope(byte[] file)
    {
        Assert.Equal(
            WorldFileEnvelopeParseResult.Parsed,
            WorldFileEnvelopeParser.TryParse(file, out WorldFileEnvelope? envelope, out int envelopeLength));
        Assert.Equal(EnvelopeEnd, envelopeLength);
        return Assert.IsType<WorldFileEnvelope>(envelope);
    }

    private static byte[] CreateCurrentFile(byte[] chestBytes)
    {
        int chestEnd = ChestStart + chestBytes.Length;
        int[] pointers =
        [
            EnvelopeEnd,
            180,
            ChestStart,
            chestEnd,
            chestEnd + 8,
            chestEnd + 16,
            chestEnd + 24,
            chestEnd + 32,
            chestEnd + 40,
            chestEnd + 48,
            chestEnd + 56
        ];
        var file = new byte[pointers[^1] + 1];

        int offset = 0;
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), WorldFileFormatPolicy.CurrentVersion);
        offset += sizeof(int);
        "relogic"u8.CopyTo(file.AsSpan(offset));
        offset += 7;
        file[offset++] = 2;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(offset), 1);
        offset += sizeof(uint);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(offset), 0);
        offset += sizeof(ulong);
        BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(offset), VanillaWorldFormat326.SectionCount);
        offset += sizeof(short);
        foreach (int pointer in pointers)
        {
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(offset), pointer);
            offset += sizeof(int);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(offset), VanillaWorldFormat326.TileTypeCount);
        offset += sizeof(ushort);
        offset += (VanillaWorldFormat326.TileTypeCount + 7) >> 3;
        Assert.Equal(EnvelopeEnd, offset);

        chestBytes.CopyTo(file, ChestStart);
        return file;
    }
}
