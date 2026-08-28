using System.Text;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public enum WorldSectionObjectMetadataEncodeResult : byte
{
    Encoded = 0,
    InvalidArea = 1,
    InvalidObjectState = 2,
    CountOverflow = 3
}

/// <summary>
/// Encodes the chest/sign/tile-entity tail appended by Terraria 1.4.5.8 CompressTileBlock_Inner.
/// Object discovery follows the same section-local anchor rules and y-then-x order as vanilla tile scanning.
/// </summary>
public static class WorldSectionObjectMetadataEncoder
{
    public static WorldSectionObjectMetadataEncodeResult TryEncode(
        WorldFileData world,
        int xStart,
        int yStart,
        int width,
        int height,
        out byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(world);
        payload = [];

        if (width < 1 || height < 1 || xStart < 0 || yStart < 0 ||
            xStart > world.Header.Dimensions.WidthTiles - width ||
            yStart > world.Header.Dimensions.HeightTiles - height)
        {
            return WorldSectionObjectMetadataEncodeResult.InvalidArea;
        }

        List<WorldChest> chests = CollectChests(world, xStart, yStart, width, height);
        List<WorldSign> signs = CollectSigns(world, xStart, yStart, width, height);
        List<WorldTileEntity> entities = CollectTileEntities(world, xStart, yStart, width, height);
        if (chests.Count > short.MaxValue || signs.Count > short.MaxValue || entities.Count > short.MaxValue)
            return WorldSectionObjectMetadataEncodeResult.CountOverflow;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, new UTF8Encoding(false), leaveOpen: true);

        writer.Write(checked((short)chests.Count));
        foreach (WorldChest chest in chests)
        {
            if (chest.SlotId < 0 || chest.SlotId >= VanillaWorldFormat326.MaximumChestSlots ||
                chest.X < short.MinValue || chest.X > short.MaxValue ||
                chest.Y < short.MinValue || chest.Y > short.MaxValue ||
                chest.Name is null)
            {
                return WorldSectionObjectMetadataEncodeResult.InvalidObjectState;
            }

            writer.Write(chest.SlotId);
            writer.Write(checked((short)chest.X));
            writer.Write(checked((short)chest.Y));
            writer.Write(chest.Name);
        }

        writer.Write(checked((short)signs.Count));
        foreach (WorldSign sign in signs)
        {
            if (sign.SlotId < 0 || sign.SlotId >= VanillaWorldFormat326.MaximumSignSlots ||
                sign.X < short.MinValue || sign.X > short.MaxValue ||
                sign.Y < short.MinValue || sign.Y > short.MaxValue ||
                sign.Text is null)
            {
                return WorldSectionObjectMetadataEncodeResult.InvalidObjectState;
            }

            writer.Write(sign.SlotId);
            writer.Write(checked((short)sign.X));
            writer.Write(checked((short)sign.Y));
            writer.Write(sign.Text);
        }

        writer.Write(checked((short)entities.Count));
        foreach (WorldTileEntity entity in entities)
        {
            if (entity.PersistedId is < 0 or > short.MaxValue)
                return WorldSectionObjectMetadataEncodeResult.InvalidObjectState;

            writer.Write((byte)entity.Kind);
            writer.Write(entity.PersistedId);
            writer.Write(entity.X);
            writer.Write(entity.Y);
            if (!TryWriteEntityPayload(writer, entity))
                return WorldSectionObjectMetadataEncodeResult.InvalidObjectState;
        }

        writer.Flush();
        payload = stream.ToArray();
        return WorldSectionObjectMetadataEncodeResult.Encoded;
    }

    private static List<WorldChest> CollectChests(
        WorldFileData world,
        int xStart,
        int yStart,
        int width,
        int height)
    {
        var result = new List<WorldChest>();
        foreach (WorldChest chest in world.Chests)
        {
            if (!InArea(chest.X, chest.Y, xStart, yStart, width, height))
                continue;

            WorldTile tile = world.Tiles.Get(chest.X, chest.Y);
            if (!tile.IsActive)
                continue;

            TileTypeId tileType = tile.TileType;
            bool basicChest =
                VanillaTileIds.IsChestAnchor(tileType) &&
                tileType != VanillaTileIds.Dressers &&
                tile.FrameX % 36 == 0 &&
                tile.FrameY % 36 == 0;
            bool dresser =
                tileType == VanillaTileIds.Dressers &&
                tile.FrameX % 54 == 0 &&
                tile.FrameY % 36 == 0;
            if (basicChest || dresser)
                result.Add(chest);
        }

        SortByVanillaScanOrder(result, static chest => chest.X, static chest => chest.Y);
        return result;
    }

    private static List<WorldSign> CollectSigns(
        WorldFileData world,
        int xStart,
        int yStart,
        int width,
        int height)
    {
        var result = new List<WorldSign>();
        foreach (WorldSign sign in world.Signs)
        {
            if (!InArea(sign.X, sign.Y, xStart, yStart, width, height))
                continue;

            WorldTile tile = world.Tiles.Get(sign.X, sign.Y);
            if (tile.IsActive &&
                VanillaTileIds.CarriesSignText(tile.TileType) &&
                tile.FrameX % 36 == 0 &&
                tile.FrameY % 36 == 0)
            {
                result.Add(sign);
            }
        }

        SortByVanillaScanOrder(result, static sign => sign.X, static sign => sign.Y);
        return result;
    }

    private static List<WorldTileEntity> CollectTileEntities(
        WorldFileData world,
        int xStart,
        int yStart,
        int width,
        int height)
    {
        var result = new List<WorldTileEntity>();
        foreach (WorldTileEntity entity in world.TileEntities)
        {
            if (!InArea(entity.X, entity.Y, xStart, yStart, width, height))
                continue;

            WorldTile tile = world.Tiles.Get(entity.X, entity.Y);
            if (tile.IsActive && MatchesVanillaTileEntityAnchor(entity.Kind, tile))
                result.Add(entity);
        }

        SortByVanillaScanOrder(result, static entity => entity.X, static entity => entity.Y);
        return result;
    }

    private static bool MatchesVanillaTileEntityAnchor(WorldTileEntityKind kind, in WorldTile tile)
    {
        TileTypeId tileType = tile.TileType;
        return kind switch
        {
            WorldTileEntityKind.TrainingDummy =>
                tileType == VanillaTileIds.TargetDummy && tile.FrameX % 36 == 0 && tile.FrameY == 0,
            WorldTileEntityKind.ItemFrame =>
                tileType == VanillaTileIds.ItemFrame && tile.FrameX % 36 == 0 && tile.FrameY == 0,
            WorldTileEntityKind.DeadCellsDisplayJar =>
                tileType == VanillaTileIds.DeadCellsDisplayJar && tile.FrameX % 18 == 0 && tile.FrameY == 0,
            WorldTileEntityKind.FoodPlatter =>
                tileType == VanillaTileIds.FoodPlatter && tile.FrameX % 18 == 0 && tile.FrameY == 0,
            WorldTileEntityKind.WeaponsRack =>
                tileType == VanillaTileIds.WeaponsRack2 && tile.FrameX % 54 == 0 && tile.FrameY == 0,
            WorldTileEntityKind.DisplayDoll =>
                tileType == VanillaTileIds.DisplayDoll && tile.FrameX % 36 == 0 && tile.FrameY == 0,
            WorldTileEntityKind.HatRack =>
                tileType == VanillaTileIds.HatRack && tile.FrameX % 54 == 0 && tile.FrameY == 0,
            WorldTileEntityKind.TeleportationPylon =>
                tileType == VanillaTileIds.TeleportationPylon && tile.FrameX % 54 == 0 && tile.FrameY % 72 == 0,
            _ => false
        };
    }

    private static bool TryWriteEntityPayload(BinaryWriter writer, WorldTileEntity entity)
    {
        switch (entity.Kind)
        {
            case WorldTileEntityKind.TrainingDummy when entity.Payload is WorldTrainingDummyPayload dummy:
                writer.Write(dummy.NpcIndex);
                return true;

            case WorldTileEntityKind.ItemFrame:
            case WorldTileEntityKind.WeaponsRack:
            case WorldTileEntityKind.FoodPlatter:
            case WorldTileEntityKind.DeadCellsDisplayJar:
                if (entity.Payload is not WorldItemTileEntityPayload itemPayload)
                    return false;
                WriteItem(writer, itemPayload.Item);
                return true;

            case WorldTileEntityKind.DisplayDoll when entity.Payload is WorldDisplayDollPayload doll:
                return TryWriteDisplayDoll(writer, doll);

            case WorldTileEntityKind.HatRack when entity.Payload is WorldHatRackPayload hatRack:
                return TryWriteHatRack(writer, hatRack);

            case WorldTileEntityKind.TeleportationPylon when entity.Payload is WorldEmptyTileEntityPayload:
                return true;

            default:
                return false;
        }
    }

    private static bool TryWriteDisplayDoll(BinaryWriter writer, WorldDisplayDollPayload payload)
    {
        if (payload.Equipment.Length != 9 || payload.Dyes.Length != 9)
            return false;

        byte equipmentLow = 0;
        byte dyesLow = 0;
        byte extra = 0;
        for (int i = 0; i < 8; i++)
        {
            if (payload.Equipment[i].HasValue) equipmentLow |= (byte)(1 << i);
            if (payload.Dyes[i].HasValue) dyesLow |= (byte)(1 << i);
        }
        if (payload.Misc.HasValue) extra |= 0x01;
        if (payload.Equipment[8].HasValue) extra |= 0x02;
        if (payload.Dyes[8].HasValue) extra |= 0x04;

        writer.Write(equipmentLow);
        writer.Write(dyesLow);
        writer.Write(payload.Pose);
        writer.Write(extra);
        foreach (WorldTileEntityItem? item in payload.Equipment)
            if (item.HasValue) WriteItem(writer, item.Value);
        foreach (WorldTileEntityItem? item in payload.Dyes)
            if (item.HasValue) WriteItem(writer, item.Value);
        if (payload.Misc.HasValue) WriteItem(writer, payload.Misc.Value);
        return true;
    }

    private static bool TryWriteHatRack(BinaryWriter writer, WorldHatRackPayload payload)
    {
        if (payload.Items.Length != 2 || payload.Dyes.Length != 2)
            return false;

        byte mask = 0;
        if (payload.Items[0].HasValue) mask |= 0x01;
        if (payload.Items[1].HasValue) mask |= 0x02;
        if (payload.Dyes[0].HasValue) mask |= 0x04;
        if (payload.Dyes[1].HasValue) mask |= 0x08;
        writer.Write(mask);
        foreach (WorldTileEntityItem? item in payload.Items)
            if (item.HasValue) WriteItem(writer, item.Value);
        foreach (WorldTileEntityItem? item in payload.Dyes)
            if (item.HasValue) WriteItem(writer, item.Value);
        return true;
    }

    private static void WriteItem(BinaryWriter writer, in WorldTileEntityItem item)
    {
        writer.Write(item.Type);
        writer.Write(item.Prefix);
        writer.Write(item.Stack);
    }

    private static bool InArea(int x, int y, int xStart, int yStart, int width, int height) =>
        x >= xStart && x < xStart + width && y >= yStart && y < yStart + height;

    private static void SortByVanillaScanOrder<T>(
        List<T> values,
        Func<T, int> getX,
        Func<T, int> getY) =>
        values.Sort((left, right) =>
        {
            int y = getY(left).CompareTo(getY(right));
            return y != 0 ? y : getX(left).CompareTo(getX(right));
        });
}
