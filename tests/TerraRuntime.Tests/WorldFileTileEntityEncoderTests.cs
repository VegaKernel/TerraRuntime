using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldFileTileEntityEncoderTests
{
    [Fact]
    public void Roundtrips_all_current_tile_entity_kinds_through_decoder()
    {
        WorldTileEntityItem itemA = Item(1, 0, 1);
        WorldTileEntityItem itemB = Item(2, 3, 4);
        var equipment = new WorldTileEntityItem?[9];
        equipment[0] = itemA;
        equipment[8] = itemB;
        var dyes = new WorldTileEntityItem?[9];
        dyes[1] = itemB;
        dyes[8] = itemA;
        var hatItems = new WorldTileEntityItem?[2];
        hatItems[0] = itemA;
        var hatDyes = new WorldTileEntityItem?[2];
        hatDyes[1] = itemB;

        WorldTileEntity[] source =
        [
            Entity(0, 1, 1, WorldTileEntityKind.TrainingDummy, new WorldTrainingDummyPayload(5)),
            Entity(1, 2, 1, WorldTileEntityKind.ItemFrame, new WorldItemTileEntityPayload(itemA)),
            Entity(2, 3, 1, WorldTileEntityKind.LogicSensor, new WorldLogicSensorPayload(4, true)),
            Entity(3, 4, 1, WorldTileEntityKind.DisplayDoll, new WorldDisplayDollPayload(7, equipment, dyes, itemA)),
            Entity(4, 5, 1, WorldTileEntityKind.WeaponsRack, new WorldItemTileEntityPayload(itemB)),
            Entity(5, 6, 1, WorldTileEntityKind.HatRack, new WorldHatRackPayload(hatItems, hatDyes)),
            Entity(6, 7, 1, WorldTileEntityKind.FoodPlatter, new WorldItemTileEntityPayload(itemA)),
            Entity(7, 8, 1, WorldTileEntityKind.TeleportationPylon, WorldEmptyTileEntityPayload.Instance),
            Entity(8, 9, 1, WorldTileEntityKind.DeadCellsDisplayJar, new WorldItemTileEntityPayload(itemB)),
            Entity(9, 10, 1, WorldTileEntityKind.KiteAnchor, new WorldLeashedAnchorPayload(1)),
            Entity(10, 11, 1, WorldTileEntityKind.CritterAnchor, new WorldLeashedAnchorPayload(2))
        ];
        var dimensions = new WorldDimensions(40, 30);

        using var stream = new MemoryStream();
        Assert.Equal(
            WorldFileTileEntityEncodeResult.Encoded,
            WorldFileTileEntityEncoder.TryEncode(source, dimensions, 64, stream, out long bytesWritten));
        Assert.Equal(stream.Length, bytesWritten);

        byte[] section = stream.ToArray();
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 1,
            favoriteFlags: 0,
            sectionOffsets: [0, 0, 0, 0, 0, 0, section.Length],
            frameImportanceCount: VanillaWorldFormat326.TileTypeCount,
            frameImportanceBits: new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);
        var header = Header(dimensions);

        Assert.Equal(
            WorldFileTileEntityDecodeResult.Decoded,
            WorldFileTileEntityDecoder.TryDecode(
                section,
                envelope,
                header,
                64,
                out WorldTileEntity[] decoded,
                out int consumed));

        Assert.Equal(section.Length, consumed);
        Assert.Equal(source.Length, decoded.Length);
        for (int i = 0; i < source.Length; i++)
        {
            Assert.Equal(source[i].PersistedId, decoded[i].PersistedId);
            Assert.Equal(source[i].X, decoded[i].X);
            Assert.Equal(source[i].Y, decoded[i].Y);
            Assert.Equal(source[i].Kind, decoded[i].Kind);
        }

        Assert.Equal(new WorldTrainingDummyPayload(5), decoded[0].Payload);
        Assert.Equal(new WorldItemTileEntityPayload(itemA), decoded[1].Payload);
        Assert.Equal(new WorldLogicSensorPayload(4, true), decoded[2].Payload);
        WorldDisplayDollPayload decodedDoll = Assert.IsType<WorldDisplayDollPayload>(decoded[3].Payload);
        Assert.Equal((byte)7, decodedDoll.Pose);
        Assert.Equal(equipment, decodedDoll.Equipment);
        Assert.Equal(dyes, decodedDoll.Dyes);
        Assert.Equal(itemA, decodedDoll.Misc);
        Assert.Equal(new WorldItemTileEntityPayload(itemB), decoded[4].Payload);
        WorldHatRackPayload decodedHat = Assert.IsType<WorldHatRackPayload>(decoded[5].Payload);
        Assert.Equal(hatItems, decodedHat.Items);
        Assert.Equal(hatDyes, decodedHat.Dyes);
        Assert.Equal(new WorldItemTileEntityPayload(itemA), decoded[6].Payload);
        Assert.IsType<WorldEmptyTileEntityPayload>(decoded[7].Payload);
        Assert.Equal(new WorldItemTileEntityPayload(itemB), decoded[8].Payload);
        Assert.Equal(new WorldLeashedAnchorPayload(1), decoded[9].Payload);
        Assert.Equal(new WorldLeashedAnchorPayload(2), decoded[10].Payload);
    }

    [Fact]
    public void Rejects_duplicate_identity_and_position_before_writing()
    {
        var dimensions = new WorldDimensions(40, 30);
        using var idStream = new MemoryStream();
        WorldTileEntity[] duplicateIds =
        [
            Entity(1, 1, 1, WorldTileEntityKind.TeleportationPylon, WorldEmptyTileEntityPayload.Instance),
            Entity(1, 2, 1, WorldTileEntityKind.TeleportationPylon, WorldEmptyTileEntityPayload.Instance)
        ];
        Assert.Equal(
            WorldFileTileEntityEncodeResult.DuplicatePersistedId,
            WorldFileTileEntityEncoder.TryEncode(duplicateIds, dimensions, 10, idStream, out long idBytes));
        Assert.Equal(0, idBytes);
        Assert.Equal(0, idStream.Length);

        using var positionStream = new MemoryStream();
        WorldTileEntity[] duplicatePositions =
        [
            Entity(1, 1, 1, WorldTileEntityKind.TeleportationPylon, WorldEmptyTileEntityPayload.Instance),
            Entity(2, 1, 1, WorldTileEntityKind.TeleportationPylon, WorldEmptyTileEntityPayload.Instance)
        ];
        Assert.Equal(
            WorldFileTileEntityEncodeResult.DuplicateCoordinates,
            WorldFileTileEntityEncoder.TryEncode(duplicatePositions, dimensions, 10, positionStream, out long positionBytes));
        Assert.Equal(0, positionBytes);
        Assert.Equal(0, positionStream.Length);
    }

    [Fact]
    public void Rejects_payload_shape_and_invalid_embedded_item_before_writing()
    {
        var dimensions = new WorldDimensions(40, 30);
        using var shapeStream = new MemoryStream();
        WorldTileEntity[] wrongPayload =
        [
            Entity(1, 1, 1, WorldTileEntityKind.ItemFrame, new WorldTrainingDummyPayload(0))
        ];
        Assert.Equal(
            WorldFileTileEntityEncodeResult.InvalidPayload,
            WorldFileTileEntityEncoder.TryEncode(wrongPayload, dimensions, 10, shapeStream, out long shapeBytes));
        Assert.Equal(0, shapeBytes);
        Assert.Equal(0, shapeStream.Length);

        using var itemStream = new MemoryStream();
        WorldTileEntity[] invalidItem =
        [
            Entity(
                1,
                1,
                1,
                WorldTileEntityKind.ItemFrame,
                new WorldItemTileEntityPayload(Item(short.MaxValue, 0, 1)))
        ];
        Assert.Equal(
            WorldFileTileEntityEncodeResult.InvalidItemType,
            WorldFileTileEntityEncoder.TryEncode(invalidItem, dimensions, 10, itemStream, out long itemBytes));
        Assert.Equal(0, itemBytes);
        Assert.Equal(0, itemStream.Length);
    }

    [Fact]
    public void Rejects_wrong_display_array_shape_before_writing()
    {
        var dimensions = new WorldDimensions(40, 30);
        WorldTileEntity[] source =
        [
            Entity(
                1,
                1,
                1,
                WorldTileEntityKind.DisplayDoll,
                new WorldDisplayDollPayload(0, new WorldTileEntityItem?[8], new WorldTileEntityItem?[9], null))
        ];
        using var stream = new MemoryStream();

        Assert.Equal(
            WorldFileTileEntityEncodeResult.InvalidPayload,
            WorldFileTileEntityEncoder.TryEncode(source, dimensions, 10, stream, out long bytesWritten));
        Assert.Equal(0, bytesWritten);
        Assert.Equal(0, stream.Length);
    }

    private static WorldTileEntity Entity(
        int id,
        short x,
        short y,
        WorldTileEntityKind kind,
        WorldTileEntityPayload payload) =>
        new(id, x, y, kind, payload);

    private static WorldTileEntityItem Item(short type, byte prefix, short stack) =>
        new(type, prefix, stack);

    private static WorldFileHeader Header(WorldDimensions dimensions) =>
        new(
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
}
