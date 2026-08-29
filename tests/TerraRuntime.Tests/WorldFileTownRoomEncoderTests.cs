using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileTownRoomEncoderTests
{
    [Fact]
    public void Roundtrips_town_rooms_through_current_decoder()
    {
        var dimensions = new WorldDimensions(40, 30);
        WorldTownRoom[] source =
        [
            new WorldTownRoom(17, 3, 4),
            new WorldTownRoom(18, 20, 21)
        ];

        using var stream = new MemoryStream();
        Assert.Equal(
            WorldFileTownRoomEncodeResult.Encoded,
            WorldFileTownRoomEncoder.TryEncode(
                source,
                dimensions,
                maxRooms: VanillaWorldFormat326.NpcTypeCount,
                stream,
                out long bytesWritten));
        Assert.Equal(stream.Length, bytesWritten);

        byte[] section = stream.ToArray();
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            sectionOffsets: [0, 0, 0, 0, 0, 0, 0, 0, section.Length],
            frameImportanceCount: VanillaWorldFormat326.TileTypeCount,
            frameImportanceBits: new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);
        var header = new WorldFileHeader(
            "test",
            "seed",
            1,
            Guid.Empty,
            1,
            0,
            dimensions.WidthTiles * 16,
            0,
            dimensions.HeightTiles * 16,
            dimensions);

        Assert.Equal(
            WorldFileTownRoomDecodeResult.Decoded,
            WorldFileTownRoomDecoder.TryDecode(
                section,
                envelope,
                header,
                VanillaWorldFormat326.NpcTypeCount,
                out WorldTownRoom[] decoded,
                out int consumed));

        Assert.Equal(section.Length, consumed);
        Assert.Equal(source, decoded);
    }

    [Fact]
    public void Rejects_duplicate_npc_type_before_writing()
    {
        var dimensions = new WorldDimensions(40, 30);
        WorldTownRoom[] source =
        [
            new WorldTownRoom(17, 3, 4),
            new WorldTownRoom(17, 5, 6)
        ];
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFileTownRoomEncodeResult.DuplicateNpcType,
            WorldFileTownRoomEncoder.TryEncode(source, dimensions, 10, stream, out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void Rejects_invalid_room_state_before_writing()
    {
        var dimensions = new WorldDimensions(40, 30);

        using var typeStream = new MemoryStream();
        WorldTownRoom[] invalidType = [new WorldTownRoom(VanillaWorldFormat326.NpcTypeCount, 1, 1)];
        Assert.Equal(
            WorldFileTownRoomEncodeResult.InvalidNpcType,
            WorldFileTownRoomEncoder.TryEncode(invalidType, dimensions, 10, typeStream, out long typeBytes));
        Assert.Equal(0, typeBytes);
        Assert.Equal(0, typeStream.Length);

        using var coordinateStream = new MemoryStream();
        WorldTownRoom[] invalidCoordinates = [new WorldTownRoom(17, dimensions.WidthTiles, 1)];
        Assert.Equal(
            WorldFileTownRoomEncodeResult.InvalidCoordinates,
            WorldFileTownRoomEncoder.TryEncode(invalidCoordinates, dimensions, 10, coordinateStream, out long coordinateBytes));
        Assert.Equal(0, coordinateBytes);
        Assert.Equal(0, coordinateStream.Length);

        using var budgetStream = new MemoryStream();
        WorldTownRoom[] twoRooms = [new WorldTownRoom(17, 1, 1), new WorldTownRoom(18, 2, 2)];
        Assert.Equal(
            WorldFileTownRoomEncodeResult.RoomBudgetExceeded,
            WorldFileTownRoomEncoder.TryEncode(twoRooms, dimensions, 1, budgetStream, out long budgetBytes));
        Assert.Equal(0, budgetBytes);
        Assert.Equal(0, budgetStream.Length);
    }
}
