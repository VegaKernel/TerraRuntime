using System.Buffers.Binary;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileTownRoomDecoderTests
{
    private const int EnvelopeEnd = 167;
    private const int TownStart = 240;

    [Fact]
    public void Decodes_current_town_rooms()
    {
        byte[] section = CreateSection(writer =>
        {
            writer.Write(2);
            writer.Write(17);
            writer.Write(10);
            writer.Write(20);
            writer.Write(18);
            writer.Write(30);
            writer.Write(40);
        });
        byte[] file = CreateCurrentFile(section);

        Assert.Equal(
            WorldFileTownRoomDecodeResult.Decoded,
            WorldFileTownRoomDecoder.TryDecode(
                file,
                ParseEnvelope(file),
                CreateHeader(),
                maxRooms: 16,
                out WorldTownRoom[] rooms,
                out int consumed));
        Assert.Equal(section.Length, consumed);
        Assert.Equal([new WorldTownRoom(17, 10, 20), new WorldTownRoom(18, 30, 40)], rooms);
    }

    [Fact]
    public void Rejects_invalid_npc_type_and_room_budget()
    {
        byte[] invalidType = CreateSection(writer =>
        {
            writer.Write(1);
            writer.Write(VanillaWorldFormat326.NpcTypeCount);
            writer.Write(10);
            writer.Write(20);
        });
        byte[] invalidTypeFile = CreateCurrentFile(invalidType);
        Assert.Equal(
            WorldFileTownRoomDecodeResult.InvalidNpcType,
            WorldFileTownRoomDecoder.TryDecode(
                invalidTypeFile,
                ParseEnvelope(invalidTypeFile),
                CreateHeader(),
                maxRooms: 1,
                out _,
                out _));

        byte[] budget = CreateSection(writer => writer.Write(2));
        byte[] budgetFile = CreateCurrentFile(budget);
        Assert.Equal(
            WorldFileTownRoomDecodeResult.RoomBudgetExceeded,
            WorldFileTownRoomDecoder.TryDecode(
                budgetFile,
                ParseEnvelope(budgetFile),
                CreateHeader(),
                maxRooms: 1,
                out _,
                out _));
    }

    private static byte[] CreateSection(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            write(writer);
        return stream.ToArray();
    }

    private static WorldFileHeader CreateHeader()
    {
        var dimensions = new WorldDimensions(100, 100);
        return new WorldFileHeader("test", "seed", 1, Guid.Empty, 1, 0, 1600, 0, 1600, dimensions);
    }

    private static WorldFileEnvelope ParseEnvelope(byte[] file)
    {
        Assert.Equal(WorldFileEnvelopeParseResult.Parsed, WorldFileEnvelopeParser.TryParse(file, out WorldFileEnvelope? envelope, out _));
        return Assert.IsType<WorldFileEnvelope>(envelope);
    }

    private static byte[] CreateCurrentFile(byte[] townSection)
    {
        int townEnd = TownStart + townSection.Length;
        int[] pointers = [EnvelopeEnd, 180, 190, 200, 210, 220, 230, TownStart, townEnd, townEnd + 10, townEnd + 20];
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
        townSection.CopyTo(file, TownStart);
        return file;
    }
}
