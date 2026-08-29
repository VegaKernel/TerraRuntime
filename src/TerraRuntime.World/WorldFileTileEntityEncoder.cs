using System.Buffers.Binary;

namespace TerraRuntime.World;

public enum WorldFileTileEntityEncodeResult : byte
{
    Encoded = 0,
    EntityBudgetExceeded = 1,
    UnknownEntityType = 2,
    InvalidPersistedId = 3,
    DuplicatePersistedId = 4,
    DuplicateCoordinates = 5,
    InvalidCoordinates = 6,
    InvalidPayload = 7,
    InvalidItemType = 8,
    DestinationNotWritable = 9,
    WriteFailed = 10
}

/// <summary>
/// Encodes the Terraria 1.4.5.8 tile-entity persistence section from canonical detached records.
/// Payload shape is validated against the entity kind before any bytes are written.
/// </summary>
public static class WorldFileTileEntityEncoder
{
    private const int CommonEntityBytes = sizeof(byte) + sizeof(int) + sizeof(short) + sizeof(short);
    private const int SerializedItemBytes = sizeof(short) + sizeof(byte) + sizeof(short);

    public static WorldFileTileEntityEncodeResult TryEncode(
        ReadOnlySpan<WorldTileEntity> source,
        WorldDimensions dimensions,
        int maxEntities,
        Stream destination,
        out long bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntities);
        ArgumentNullException.ThrowIfNull(destination);
        bytesWritten = 0;

        if (!destination.CanWrite)
            return WorldFileTileEntityEncodeResult.DestinationNotWritable;
        if (source.Length > maxEntities)
            return WorldFileTileEntityEncodeResult.EntityBudgetExceeded;

        WorldFileTileEntityEncodeResult validation = Validate(source, dimensions, out long encodedLength);
        if (validation != WorldFileTileEntityEncodeResult.Encoded)
            return validation;

        try
        {
            using var writer = new BinaryWriter(destination, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(source.Length);
            foreach (WorldTileEntity entity in source)
            {
                writer.Write((byte)entity.Kind);
                writer.Write(entity.PersistedId);
                writer.Write(entity.X);
                writer.Write(entity.Y);
                WritePayload(writer, entity.Kind, entity.Payload);
            }
            writer.Flush();
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            bytesWritten = 0;
            return WorldFileTileEntityEncodeResult.WriteFailed;
        }

        bytesWritten = encodedLength;
        return WorldFileTileEntityEncodeResult.Encoded;
    }

    private static WorldFileTileEntityEncodeResult Validate(
        ReadOnlySpan<WorldTileEntity> source,
        WorldDimensions dimensions,
        out long encodedLength)
    {
        encodedLength = sizeof(int);
        var persistedIds = new HashSet<int>();
        var positions = new HashSet<long>();

        foreach (WorldTileEntity entity in source)
        {
            if (entity is null)
                return WorldFileTileEntityEncodeResult.InvalidPayload;
            if ((byte)entity.Kind > (byte)WorldTileEntityKind.CritterAnchor)
                return WorldFileTileEntityEncodeResult.UnknownEntityType;
            if (entity.PersistedId < 0 || entity.PersistedId == int.MaxValue)
                return WorldFileTileEntityEncodeResult.InvalidPersistedId;
            if (!persistedIds.Add(entity.PersistedId))
                return WorldFileTileEntityEncodeResult.DuplicatePersistedId;
            if (entity.X < 0 || entity.Y < 0 ||
                entity.X >= dimensions.WidthTiles || entity.Y >= dimensions.HeightTiles)
            {
                return WorldFileTileEntityEncodeResult.InvalidCoordinates;
            }

            long positionKey = ((long)(uint)(ushort)entity.X << 32) | (ushort)entity.Y;
            if (!positions.Add(positionKey))
                return WorldFileTileEntityEncodeResult.DuplicateCoordinates;

            WorldFileTileEntityEncodeResult payloadResult = ValidatePayload(
                entity.Kind,
                entity.Payload,
                out int payloadBytes);
            if (payloadResult != WorldFileTileEntityEncodeResult.Encoded)
                return payloadResult;

            encodedLength = checked(encodedLength + CommonEntityBytes + payloadBytes);
        }

        return WorldFileTileEntityEncodeResult.Encoded;
    }

    private static WorldFileTileEntityEncodeResult ValidatePayload(
        WorldTileEntityKind kind,
        WorldTileEntityPayload payload,
        out int payloadBytes)
    {
        payloadBytes = 0;
        if (payload is null)
            return WorldFileTileEntityEncodeResult.InvalidPayload;

        switch (kind)
        {
            case WorldTileEntityKind.TrainingDummy:
                if (payload is not WorldTrainingDummyPayload)
                    return WorldFileTileEntityEncodeResult.InvalidPayload;
                payloadBytes = sizeof(short);
                break;

            case WorldTileEntityKind.ItemFrame:
            case WorldTileEntityKind.WeaponsRack:
            case WorldTileEntityKind.FoodPlatter:
            case WorldTileEntityKind.DeadCellsDisplayJar:
                if (payload is not WorldItemTileEntityPayload)
                    return WorldFileTileEntityEncodeResult.InvalidPayload;
                payloadBytes = SerializedItemBytes;
                break;

            case WorldTileEntityKind.LogicSensor:
                if (payload is not WorldLogicSensorPayload)
                    return WorldFileTileEntityEncodeResult.InvalidPayload;
                payloadBytes = sizeof(byte) * 2;
                break;

            case WorldTileEntityKind.DisplayDoll:
                if (payload is not WorldDisplayDollPayload doll ||
                    doll.Equipment is null || doll.Equipment.Length != 9 ||
                    doll.Dyes is null || doll.Dyes.Length != 9)
                {
                    return WorldFileTileEntityEncodeResult.InvalidPayload;
                }
                payloadBytes = checked(
                    4 + (CountItems(doll.Equipment) + CountItems(doll.Dyes) + (doll.Misc.HasValue ? 1 : 0)) * SerializedItemBytes);
                break;

            case WorldTileEntityKind.HatRack:
                if (payload is not WorldHatRackPayload hatRack ||
                    hatRack.Items is null || hatRack.Items.Length != 2 ||
                    hatRack.Dyes is null || hatRack.Dyes.Length != 2)
                {
                    return WorldFileTileEntityEncodeResult.InvalidPayload;
                }
                payloadBytes = checked(
                    1 + (CountItems(hatRack.Items) + CountItems(hatRack.Dyes)) * SerializedItemBytes);
                break;

            case WorldTileEntityKind.TeleportationPylon:
                if (payload is not WorldEmptyTileEntityPayload)
                    return WorldFileTileEntityEncodeResult.InvalidPayload;
                break;

            case WorldTileEntityKind.KiteAnchor:
            case WorldTileEntityKind.CritterAnchor:
                if (payload is not WorldLeashedAnchorPayload)
                    return WorldFileTileEntityEncodeResult.InvalidPayload;
                payloadBytes = sizeof(short);
                break;

            default:
                return WorldFileTileEntityEncodeResult.UnknownEntityType;
        }

        if (!WorldTileEntityItemValidator.HasValidItemTypes(payload))
            return WorldFileTileEntityEncodeResult.InvalidItemType;
        return WorldFileTileEntityEncodeResult.Encoded;
    }

    private static void WritePayload(
        BinaryWriter writer,
        WorldTileEntityKind kind,
        WorldTileEntityPayload payload)
    {
        switch (kind)
        {
            case WorldTileEntityKind.TrainingDummy:
                writer.Write(((WorldTrainingDummyPayload)payload).NpcIndex);
                return;

            case WorldTileEntityKind.ItemFrame:
            case WorldTileEntityKind.WeaponsRack:
            case WorldTileEntityKind.FoodPlatter:
            case WorldTileEntityKind.DeadCellsDisplayJar:
                WriteItem(writer, ((WorldItemTileEntityPayload)payload).Item);
                return;

            case WorldTileEntityKind.LogicSensor:
            {
                var sensor = (WorldLogicSensorPayload)payload;
                writer.Write(sensor.LogicCheck);
                writer.Write((byte)(sensor.IsOn ? 1 : 0));
                return;
            }

            case WorldTileEntityKind.DisplayDoll:
                WriteDisplayDoll(writer, (WorldDisplayDollPayload)payload);
                return;

            case WorldTileEntityKind.HatRack:
                WriteHatRack(writer, (WorldHatRackPayload)payload);
                return;

            case WorldTileEntityKind.TeleportationPylon:
                return;

            case WorldTileEntityKind.KiteAnchor:
            case WorldTileEntityKind.CritterAnchor:
                writer.Write(((WorldLeashedAnchorPayload)payload).ItemType);
                return;

            default:
                throw new InvalidOperationException("Validated tile entity kind became unsupported during encoding.");
        }
    }

    private static void WriteDisplayDoll(BinaryWriter writer, WorldDisplayDollPayload doll)
    {
        int equipmentMask = BuildMask(doll.Equipment);
        int dyeMask = BuildMask(doll.Dyes);
        byte extraMask = 0;
        if (doll.Misc.HasValue)
            extraMask |= 0x01;
        if ((equipmentMask & 0x100) != 0)
            extraMask |= 0x02;
        if ((dyeMask & 0x100) != 0)
            extraMask |= 0x04;

        writer.Write((byte)equipmentMask);
        writer.Write((byte)dyeMask);
        writer.Write(doll.Pose);
        writer.Write(extraMask);
        WritePresentItems(writer, doll.Equipment);
        WritePresentItems(writer, doll.Dyes);
        if (doll.Misc is { } misc)
            WriteItem(writer, misc);
    }

    private static void WriteHatRack(BinaryWriter writer, WorldHatRackPayload hatRack)
    {
        int itemMask = BuildMask(hatRack.Items);
        int dyeMask = BuildMask(hatRack.Dyes);
        byte mask = (byte)(itemMask | (dyeMask << 2));
        writer.Write(mask);
        WritePresentItems(writer, hatRack.Items);
        WritePresentItems(writer, hatRack.Dyes);
    }

    private static int CountItems(WorldTileEntityItem?[] items)
    {
        int count = 0;
        foreach (WorldTileEntityItem? item in items)
        {
            if (item.HasValue)
                count++;
        }
        return count;
    }

    private static int BuildMask(WorldTileEntityItem?[] items)
    {
        int mask = 0;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].HasValue)
                mask |= 1 << i;
        }
        return mask;
    }

    private static void WritePresentItems(BinaryWriter writer, WorldTileEntityItem?[] items)
    {
        foreach (WorldTileEntityItem? item in items)
        {
            if (item is { } value)
                WriteItem(writer, value);
        }
    }

    private static void WriteItem(BinaryWriter writer, in WorldTileEntityItem item)
    {
        writer.Write(item.Type);
        writer.Write(item.Prefix);
        writer.Write(item.Stack);
    }
}
