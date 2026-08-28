using System.Buffers.Binary;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileTileEntityDecoderTests
{
    private const int EnvelopeEnd = 167;
    private const int TileEntityStart = 230;

    [Fact]
    public void Decodes_current_tile_entity_types_and_payloads()
    {
        byte[] section = CreateSection(writer =>
        {
            writer.Write(6);

            WriteEntityHeader(writer, WorldTileEntityKind.TrainingDummy, 1, 1, 1);
            writer.Write((short)-1);

            WriteEntityHeader(writer, WorldTileEntityKind.ItemFrame, 2, 2, 2);
            WriteItem(writer, 100, 3, 4);

            WriteEntityHeader(writer, WorldTileEntityKind.LogicSensor, 3, 3, 3);
            writer.Write((byte)5);
            writer.Write(true);

            WriteEntityHeader(writer, WorldTileEntityKind.DisplayDoll, 4, 4, 4);
            writer.Write((byte)0x01);
            writer.Write((byte)0x02);
            writer.Write((byte)7);
            writer.Write((byte)0x07);
            WriteItem(writer, 101, 1, 2);
            WriteItem(writer, 102, 2, 3);
            WriteItem(writer, 103, 3, 4);
            WriteItem(writer, 104, 4, 5);
            WriteItem(writer, 105, 5, 6);

            WriteEntityHeader(writer, WorldTileEntityKind.HatRack, 5, 5, 5);
            writer.Write((byte)0x09);
            WriteItem(writer, 106, 6, 7);
            WriteItem(writer, 107, 7, 8);

            WriteEntityHeader(writer, WorldTileEntityKind.KiteAnchor, 6, 6, 6);
            writer.Write((short)108);
        });

        byte[] file = CreateCurrentFile(section);
        WorldFileEnvelope envelope = ParseEnvelope(file);
        WorldFileHeader header = CreateHeader();

        WorldFileTileEntityDecodeResult result = WorldFileTileEntityDecoder.TryDecode(
            file,
            envelope,
            header,
            maxEntities: 16,
            out WorldTileEntity[] entities,
            out int consumed);

        Assert.Equal(WorldFileTileEntityDecodeResult.Decoded, result);
        Assert.Equal(section.Length, consumed);
        Assert.Equal(6, entities.Length);

        Assert.Equal(new WorldTrainingDummyPayload(-1), entities[0].Payload);
        WorldTileEntityItem frameItem = Assert.IsType<WorldItemTileEntityPayload>(entities[1].Payload).Item;
        Assert.Equal(new WorldTileEntityItem(100, 3, 4), frameItem);
        Assert.True(frameItem.TryGetItemType(out ItemTypeId frameItemType));
        Assert.Equal(100, frameItemType.Value);
        Assert.Equal(3, frameItem.PrefixId.Value);
        Assert.Equal(new WorldLogicSensorPayload(5, true), entities[2].Payload);

        WorldDisplayDollPayload doll = Assert.IsType<WorldDisplayDollPayload>(entities[3].Payload);
        Assert.Equal((byte)7, doll.Pose);
        Assert.Equal(new WorldTileEntityItem(101, 1, 2), doll.Equipment[0]);
        Assert.Equal(new WorldTileEntityItem(102, 2, 3), doll.Equipment[8]);
        Assert.Equal(new WorldTileEntityItem(103, 3, 4), doll.Dyes[1]);
        Assert.Equal(new WorldTileEntityItem(104, 4, 5), doll.Dyes[8]);
        Assert.Equal(new WorldTileEntityItem(105, 5, 6), doll.Misc);

        WorldHatRackPayload hatRack = Assert.IsType<WorldHatRackPayload>(entities[4].Payload);
        Assert.Equal(new WorldTileEntityItem(106, 6, 7), hatRack.Items[0]);
        Assert.Equal(new WorldTileEntityItem(107, 7, 8), hatRack.Dyes[1]);
        WorldLeashedAnchorPayload anchor = Assert.IsType<WorldLeashedAnchorPayload>(entities[5].Payload);
        Assert.Equal(new WorldLeashedAnchorPayload(108), anchor);
        Assert.True(anchor.TryGetItemType(out ItemTypeId anchorItemType));
        Assert.Equal(108, anchorItemType.Value);
    }

    [Fact]
    public void Replaces_earlier_entity_at_the_same_position_like_vanilla_load()
    {
        byte[] section = CreateSection(writer =>
        {
            writer.Write(2);
            WriteEntityHeader(writer, WorldTileEntityKind.TeleportationPylon, 10, 8, 9);
            WriteEntityHeader(writer, WorldTileEntityKind.TrainingDummy, 11, 8, 9);
            writer.Write((short)42);
        });

        byte[] file = CreateCurrentFile(section);
        Assert.Equal(
            WorldFileTileEntityDecodeResult.Decoded,
            WorldFileTileEntityDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                CreateHeader(),
                maxEntities: 2,
                out WorldTileEntity[] entities,
                out _));

        WorldTileEntity entity = Assert.Single(entities);
        Assert.Equal(11, entity.PersistedId);
        Assert.Equal(WorldTileEntityKind.TrainingDummy, entity.Kind);
    }

    [Fact]
    public void Rejects_unknown_type_and_entity_budget_overflow()
    {
        byte[] unknown = CreateSection(writer =>
        {
            writer.Write(1);
            writer.Write((byte)11);
            writer.Write(1);
            writer.Write((short)1);
            writer.Write((short)1);
        });
        byte[] unknownFile = CreateCurrentFile(unknown);
        Assert.Equal(
            WorldFileTileEntityDecodeResult.UnknownEntityType,
            WorldFileTileEntityDecoder.TryDecode(
                unknownFile,
                ParseEnvelope(unknownFile),
                CreateHeader(),
                maxEntities: 1,
                out _,
                out _));

        byte[] budget = CreateSection(writer => writer.Write(2));
        byte[] budgetFile = CreateCurrentFile(budget);
        Assert.Equal(
            WorldFileTileEntityDecodeResult.EntityBudgetExceeded,
            WorldFileTileEntityDecoder.TryDecode(
                budgetFile,
                ParseEnvelope(budgetFile),
                CreateHeader(),
                maxEntities: 1,
                out _,
                out _));
    }

    [Fact]
    public void Rejects_tile_entity_item_type_outside_vanilla_1458_catalog()
    {
        byte[] section = CreateSection(writer =>
        {
            writer.Write(1);
            WriteEntityHeader(writer, WorldTileEntityKind.ItemFrame, 1, 2, 3);
            WriteItem(writer, checked((short)VanillaItemIds.Count), 0, 1);
        });

        byte[] file = CreateCurrentFile(section);
        Assert.Equal(
            WorldFileTileEntityDecodeResult.InvalidItemType,
            WorldFileTileEntityDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                CreateHeader(),
                maxEntities: 1,
                out _,
                out _));
    }

    private static byte[] CreateSection(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            write(writer);
        }
        return stream.ToArray();
    }

    private static void WriteEntityHeader(BinaryWriter writer, WorldTileEntityKind kind, int id, short x, short y)
    {
        writer.Write((byte)kind);
        writer.Write(id);
        writer.Write(x);
        writer.Write(y);
    }

    private static void WriteItem(BinaryWriter writer, short type, byte prefix, short stack)
    {
        writer.Write(type);
        writer.Write(prefix);
        writer.Write(stack);
    }

    private static WorldFileHeader CreateHeader()
    {
        var dimensions = new WorldDimensions(100, 100);
        return new WorldFileHeader("test", "seed", 1, Guid.Empty, 1, 0, 1600, 0, 1600, dimensions);
    }

    private static WorldFileEnvelope ParseEnvelope(byte[] file)
    {
        Assert.Equal(
            WorldFileEnvelopeParseResult.Parsed,
            WorldFileEnvelopeParser.TryParse(file, out WorldFileEnvelope? envelope, out int length));
        Assert.Equal(EnvelopeEnd, length);
        return Assert.IsType<WorldFileEnvelope>(envelope);
    }

    private static byte[] CreateCurrentFile(byte[] tileEntitySection)
    {
        int entityEnd = TileEntityStart + tileEntitySection.Length;
        int[] pointers =
        [
            EnvelopeEnd,
            180,
            190,
            200,
            210,
            TileEntityStart,
            entityEnd,
            entityEnd + 10,
            entityEnd + 20,
            entityEnd + 30,
            entityEnd + 40
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

        tileEntitySection.CopyTo(file, TileEntityStart);
        return file;
    }
}
