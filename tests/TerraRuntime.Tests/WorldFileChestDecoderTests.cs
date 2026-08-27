using System.Buffers.Binary;
using System.Text;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileChestDecoderTests
{
    private const int EnvelopeEnd = 167;
    private const int ChestStart = 184;

    [Fact]
    public void Decodes_current_chests_and_keeps_first_duplicate_position()
    {
        byte[] chestBytes = CreateChestBytes(writer =>
        {
            writer.Write((short)2);

            writer.Write(1);
            writer.Write(2);
            writer.Write("first");
            writer.Write(2);
            writer.Write((short)5);
            writer.Write(100);
            writer.Write((byte)3);
            writer.Write((short)0);

            writer.Write(1);
            writer.Write(2);
            writer.Write("duplicate");
            writer.Write(1);
            writer.Write((short)-1);
            writer.Write(200);
            writer.Write((byte)4);
        });
        byte[] file = CreateCurrentFile(chestBytes);
        WorldFileEnvelope envelope = ParseEnvelope(file);
        WorldFileHeader header = CreateHeader();

        WorldFileChestDecodeResult result = WorldFileChestDecoder.TryDecode(
            file,
            envelope,
            header,
            maxItemsPerChest: 10,
            maxTotalItems: 20,
            out WorldChest[] chests,
            out int consumed);

        Assert.Equal(WorldFileChestDecodeResult.Decoded, result);
        Assert.Equal(chestBytes.Length, consumed);
        WorldChest chest = Assert.Single(chests);
        Assert.Equal(1, chest.X);
        Assert.Equal(2, chest.Y);
        Assert.Equal("first", chest.Name);
        Assert.Equal(2, chest.Items.Length);
        Assert.Equal(new WorldChestItem(5, 100, 3), chest.Items[0]);
        Assert.True(chest.Items[1].IsEmpty);
    }

    [Fact]
    public void Normalizes_legacy_negative_stack_like_vanilla_current_loader()
    {
        byte[] chestBytes = CreateChestBytes(writer =>
        {
            writer.Write((short)1);
            writer.Write(1);
            writer.Write(1);
            writer.Write("negative-stack");
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

        Assert.Equal(new WorldChestItem(1, 250, 9), Assert.Single(chests).Items[0]);
    }

    [Fact]
    public void Rejects_declared_item_count_before_large_item_array_allocation()
    {
        byte[] chestBytes = CreateChestBytes(writer =>
        {
            writer.Write((short)1);
            writer.Write(1);
            writer.Write(1);
            writer.Write("budget");
            writer.Write(1000);
        });
        byte[] file = CreateCurrentFile(chestBytes);

        Assert.Equal(
            WorldFileChestDecodeResult.ItemBudgetExceeded,
            WorldFileChestDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                CreateHeader(),
                maxItemsPerChest: 40,
                maxTotalItems: 100,
                out WorldChest[] chests,
                out _));
        Assert.Empty(chests);
    }

    [Fact]
    public void Requires_exact_end_of_chest_section()
    {
        byte[] chestBytes = CreateChestBytes(writer => writer.Write((short)0));
        Array.Resize(ref chestBytes, chestBytes.Length + 1);
        byte[] file = CreateCurrentFile(chestBytes);

        Assert.Equal(
            WorldFileChestDecodeResult.SectionLengthMismatch,
            WorldFileChestDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                CreateHeader(),
                maxItemsPerChest: 40,
                maxTotalItems: 100,
                out _,
                out int consumed));
        Assert.Equal(sizeof(short), consumed);
    }

    [Fact]
    public void Rejects_chest_whose_two_by_two_footprint_crosses_world_edge()
    {
        byte[] chestBytes = CreateChestBytes(writer =>
        {
            writer.Write((short)1);
            writer.Write(9);
            writer.Write(1);
        });
        byte[] file = CreateCurrentFile(chestBytes);

        Assert.Equal(
            WorldFileChestDecodeResult.InvalidChestCoordinates,
            WorldFileChestDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                CreateHeader(),
                maxItemsPerChest: 40,
                maxTotalItems: 100,
                out _,
                out _));
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
