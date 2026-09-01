using System.Text;

namespace TerraRuntime.Schematics;

internal static class SchematicValidator
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const SchematicTileFlags KnownTileFlags =
        SchematicTileFlags.Active |
        SchematicTileFlags.WireRed |
        SchematicTileFlags.WireBlue |
        SchematicTileFlags.WireGreen |
        SchematicTileFlags.WireYellow |
        SchematicTileFlags.Actuator |
        SchematicTileFlags.Inactive |
        SchematicTileFlags.InvisibleBlock |
        SchematicTileFlags.InvisibleWall |
        SchematicTileFlags.FullbrightBlock |
        SchematicTileFlags.FullbrightWall;

    public static void Validate(SchematicDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateHeader(document.ContentVersion, document.Width, document.Height, document.OriginX, document.OriginY);

        int expectedTileCount = checked(document.Width * document.Height);
        if (document.Tiles.Length != expectedTileCount)
            throw new SchematicFormatException($"Tile count {document.Tiles.Length} does not match dimensions {document.Width}x{document.Height}.");

        foreach (SchematicTile tile in document.Tiles)
        {
            if ((tile.Flags & ~KnownTileFlags) != 0)
                throw new SchematicFormatException($"Tile flags contain unsupported bits: 0x{(ushort)tile.Flags:X4}.");
            if (!Enum.IsDefined(tile.LiquidKind))
                throw new SchematicFormatException($"Unknown liquid kind {(byte)tile.LiquidKind}.");
        }

        ValidateCount(document.Chests.Length, SchematicLimits.MaxChests, "chests");
        foreach (SchematicChest chest in document.Chests)
        {
            ArgumentNullException.ThrowIfNull(chest);
            ValidateTilePosition(chest.X, chest.Y, document.Width, document.Height, "chest");
            ValidateString(chest.Name, SchematicLimits.MaxChestNameUtf8Bytes, allowNull: false, "chest name");
            ValidateItems(chest.Items, SchematicLimits.MaxChestItems, allowEmpty: true, "chest");
        }

        ValidateCount(document.Signs.Length, SchematicLimits.MaxSigns, "signs");
        foreach (SchematicSign sign in document.Signs)
        {
            ArgumentNullException.ThrowIfNull(sign);
            ValidateTilePosition(sign.X, sign.Y, document.Width, document.Height, "sign");
            ValidateString(sign.Text, SchematicLimits.MaxSignTextUtf8Bytes, allowNull: false, "sign text");
        }

        ValidateCount(document.TileEntities.Length, SchematicLimits.MaxTileEntities, "tile entities");
        foreach (SchematicTileEntity entity in document.TileEntities)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ValidateTilePosition(entity.X, entity.Y, document.Width, document.Height, "tile entity");
            switch (entity)
            {
                case SchematicTrainingDummyTileEntity _:
                case SchematicTeleportationPylonTileEntity _:
                    break;
                case SchematicItemFrameTileEntity itemFrame:
                    ValidateItem(itemFrame.Item, allowEmpty: true, "item frame");
                    break;
                case SchematicLogicSensorTileEntity _:
                    break;
                case SchematicDisplayDollTileEntity doll:
                    ValidateItems(doll.Items, SchematicLimits.MaxTileEntityItems, allowEmpty: true, "display doll items");
                    ValidateItems(doll.Dyes, SchematicLimits.MaxTileEntityItems, allowEmpty: true, "display doll dyes");
                    break;
                case SchematicWeaponsRackTileEntity rack:
                    ValidateItem(rack.Item, allowEmpty: true, "weapons rack");
                    break;
                case SchematicHatRackTileEntity hatRack:
                    ValidateItems(hatRack.Items, SchematicLimits.MaxTileEntityItems, allowEmpty: true, "hat rack items");
                    ValidateItems(hatRack.Dyes, SchematicLimits.MaxTileEntityItems, allowEmpty: true, "hat rack dyes");
                    break;
                case SchematicFoodPlatterTileEntity platter:
                    ValidateItem(platter.Item, allowEmpty: true, "food platter");
                    break;
                default:
                    throw new SchematicFormatException($"Unsupported tile entity model {entity.GetType().Name}.");
            }
        }

        ValidateCount(document.Npcs.Length, SchematicLimits.MaxNpcs, "NPCs");
        foreach (SchematicNpc npc in document.Npcs)
        {
            if (npc.NpcType <= 0)
                throw new SchematicFormatException("NPC type must be positive.");
            ValidatePixelPosition(npc.X, npc.Y, document.Width, document.Height, "NPC");
            if (npc.Direction is not (-1 or 1) || npc.SpriteDirection is not (-1 or 1))
                throw new SchematicFormatException("NPC direction and sprite direction must be -1 or 1.");
            ValidateString(npc.Name, SchematicLimits.MaxNpcNameUtf8Bytes, allowNull: true, "NPC name");
            if (!npc.Homeless)
                ValidateTilePosition(npc.HomeX, npc.HomeY, document.Width, document.Height, "NPC home");
            if (npc.LifeOverride.HasValue && npc.LifeOverride.Value <= 0)
                throw new SchematicFormatException("NPC life override must be positive when present.");
        }

        ValidateCount(document.WorldItems.Length, SchematicLimits.MaxWorldItems, "world items");
        foreach (SchematicWorldItem item in document.WorldItems)
        {
            ValidateItem(item.Item, allowEmpty: false, "world item");
            ValidatePixelPosition(item.X, item.Y, document.Width, document.Height, "world item");
        }

        ValidateCount(document.Markers.Length, SchematicLimits.MaxMarkers, "markers");
        foreach (SchematicMarker marker in document.Markers)
        {
            ValidateString(marker.Name, SchematicLimits.MaxMarkerNameUtf8Bytes, allowNull: false, "marker name");
            if (!Enum.IsDefined(marker.Kind))
                throw new SchematicFormatException($"Unknown marker kind {(byte)marker.Kind}.");
            ValidateTilePosition(marker.X, marker.Y, document.Width, document.Height, "marker");
            if (marker.Kind == SchematicMarkerKind.Point)
            {
                if (marker.Width != 0 || marker.Height != 0)
                    throw new SchematicFormatException("Point markers must have zero width and height.");
            }
            else
            {
                if (marker.Width <= 0 || marker.Height <= 0)
                    throw new SchematicFormatException("Region markers must have positive dimensions.");
                if ((long)marker.X + marker.Width > document.Width || (long)marker.Y + marker.Height > document.Height)
                    throw new SchematicFormatException("Region marker exceeds schematic bounds.");
            }
        }

        ValidateCount(document.Metadata.Length, SchematicLimits.MaxMetadataEntries, "metadata entries");
        foreach (SchematicMetadataEntry entry in document.Metadata)
        {
            ValidateString(entry.Key, SchematicLimits.MaxMetadataKeyUtf8Bytes, allowNull: false, "metadata key");
            ValidateString(entry.Value, SchematicLimits.MaxMetadataValueUtf8Bytes, allowNull: false, "metadata value");
        }
    }

    public static void ValidateHeader(int contentVersion, int width, int height, int originX, int originY)
    {
        if (contentVersion < 0)
            throw new SchematicFormatException("Content version cannot be negative.");
        if (width <= 0 || width > SchematicLimits.MaxWidthTiles)
            throw new SchematicFormatException($"Width must be between 1 and {SchematicLimits.MaxWidthTiles} tiles.");
        if (height <= 0 || height > SchematicLimits.MaxHeightTiles)
            throw new SchematicFormatException($"Height must be between 1 and {SchematicLimits.MaxHeightTiles} tiles.");

        long tileCount = checked((long)width * height);
        if (tileCount > SchematicLimits.MaxTileCount)
            throw new SchematicFormatException($"Tile count exceeds {SchematicLimits.MaxTileCount}.");
        if ((uint)originX >= (uint)width || (uint)originY >= (uint)height)
            throw new SchematicFormatException("Schematic origin must be inside the schematic bounds.");
    }

    private static void ValidateCount(int count, int max, string label)
    {
        if ((uint)count > (uint)max)
            throw new SchematicFormatException($"Too many {label}: {count}; maximum is {max}.");
    }

    private static void ValidateTilePosition(int x, int y, int width, int height, string label)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            throw new SchematicFormatException($"{label} position ({x}, {y}) is outside schematic bounds.");
    }

    private static void ValidatePixelPosition(float x, float y, int width, int height, string label)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || x < 0 || y < 0 || x >= width * 16f || y >= height * 16f)
            throw new SchematicFormatException($"{label} pixel position ({x}, {y}) is outside schematic bounds.");
    }

    private static void ValidateItems(SchematicItemStack[] items, int max, bool allowEmpty, string label)
    {
        ArgumentNullException.ThrowIfNull(items);
        ValidateCount(items.Length, max, label);
        foreach (SchematicItemStack item in items)
            ValidateItem(item, allowEmpty, label);
    }

    private static void ValidateItem(SchematicItemStack item, bool allowEmpty, string label)
    {
        if (item.Stack == 0)
        {
            if (!allowEmpty || item.ItemType != 0 || item.Prefix != 0)
                throw new SchematicFormatException($"Invalid empty item in {label}.");
            return;
        }

        if (item.Stack < 0 || item.ItemType <= 0)
            throw new SchematicFormatException($"Invalid item type/stack in {label}.");
    }

    private static void ValidateString(string? value, int maxUtf8Bytes, bool allowNull, string label)
    {
        if (value is null)
        {
            if (!allowNull)
                throw new SchematicFormatException($"{label} cannot be null.");
            return;
        }

        try
        {
            if (StrictUtf8.GetByteCount(value) > maxUtf8Bytes)
                throw new SchematicFormatException($"{label} exceeds {maxUtf8Bytes} UTF-8 bytes.");
        }
        catch (EncoderFallbackException exception)
        {
            throw new SchematicFormatException($"{label} contains invalid UTF-16 data.", exception);
        }
    }
}

public sealed class SchematicFormatException : FormatException
{
    public SchematicFormatException(string message) : base(message) { }
    public SchematicFormatException(string message, Exception innerException) : base(message, innerException) { }
}
