namespace TerraRuntime.Schematics;

public static class SchematicLimits
{
    public const int MaxWidthTiles = 16_384;
    public const int MaxHeightTiles = 16_384;
    public const int MaxTileCount = 24_000_000;
    public const int MaxFileBytes = 512 * 1024 * 1024;
    public const int MaxSectionCount = 64;
    public const int MaxSectionBytes = MaxFileBytes;
    public const int MaxChests = 16_384;
    public const int MaxSigns = 16_384;
    public const int MaxTileEntities = 65_536;
    public const int MaxNpcs = 16_384;
    public const int MaxWorldItems = 65_536;
    public const int MaxMarkers = 16_384;
    public const int MaxMetadataEntries = 4_096;
    public const int MaxChestItems = 40;
    public const int MaxTileEntityItems = 32;
    public const int MaxChestNameUtf8Bytes = 256;
    public const int MaxSignTextUtf8Bytes = 8_192;
    public const int MaxNpcNameUtf8Bytes = 256;
    public const int MaxMarkerNameUtf8Bytes = 256;
    public const int MaxMetadataKeyUtf8Bytes = 256;
    public const int MaxMetadataValueUtf8Bytes = 8_192;
}

[Flags]
public enum SchematicTileFlags : ushort
{
    None = 0,
    Active = 1 << 0,
    WireRed = 1 << 1,
    WireBlue = 1 << 2,
    WireGreen = 1 << 3,
    WireYellow = 1 << 4,
    Actuator = 1 << 5,
    Inactive = 1 << 6,
    InvisibleBlock = 1 << 7,
    InvisibleWall = 1 << 8,
    FullbrightBlock = 1 << 9,
    FullbrightWall = 1 << 10
}

public enum SchematicLiquidKind : byte
{
    Water = 0,
    Lava = 1,
    Honey = 2,
    Shimmer = 3
}

public readonly record struct SchematicTile(
    ushort Type,
    ushort Wall,
    short FrameX,
    short FrameY,
    SchematicTileFlags Flags,
    byte LiquidAmount,
    byte TileColor,
    byte WallColor,
    byte Shape,
    SchematicLiquidKind LiquidKind);

public readonly record struct SchematicItemStack(int ItemType, int Stack, byte Prefix = 0)
{
    public bool IsEmpty => Stack == 0;
}

public sealed record SchematicChest(int X, int Y, string Name, SchematicItemStack[] Items);
public sealed record SchematicSign(int X, int Y, string Text);

public enum SchematicTileEntityKind : ushort
{
    TrainingDummy = 1,
    ItemFrame = 2,
    LogicSensor = 3,
    DisplayDoll = 4,
    WeaponsRack = 5,
    HatRack = 6,
    FoodPlatter = 7,
    TeleportationPylon = 8
}

public abstract record SchematicTileEntity(int X, int Y)
{
    public abstract SchematicTileEntityKind Kind { get; }
}

public sealed record SchematicTrainingDummyTileEntity(int X, int Y) : SchematicTileEntity(X, Y)
{
    public override SchematicTileEntityKind Kind => SchematicTileEntityKind.TrainingDummy;
}

public sealed record SchematicItemFrameTileEntity(int X, int Y, SchematicItemStack Item) : SchematicTileEntity(X, Y)
{
    public override SchematicTileEntityKind Kind => SchematicTileEntityKind.ItemFrame;
}

public sealed record SchematicLogicSensorTileEntity(int X, int Y, byte LogicCheck, bool On) : SchematicTileEntity(X, Y)
{
    public override SchematicTileEntityKind Kind => SchematicTileEntityKind.LogicSensor;
}

public sealed record SchematicDisplayDollTileEntity(
    int X,
    int Y,
    SchematicItemStack[] Items,
    SchematicItemStack[] Dyes) : SchematicTileEntity(X, Y)
{
    public override SchematicTileEntityKind Kind => SchematicTileEntityKind.DisplayDoll;
}

public sealed record SchematicWeaponsRackTileEntity(int X, int Y, SchematicItemStack Item) : SchematicTileEntity(X, Y)
{
    public override SchematicTileEntityKind Kind => SchematicTileEntityKind.WeaponsRack;
}

public sealed record SchematicHatRackTileEntity(
    int X,
    int Y,
    SchematicItemStack[] Items,
    SchematicItemStack[] Dyes) : SchematicTileEntity(X, Y)
{
    public override SchematicTileEntityKind Kind => SchematicTileEntityKind.HatRack;
}

public sealed record SchematicFoodPlatterTileEntity(int X, int Y, SchematicItemStack Item) : SchematicTileEntity(X, Y)
{
    public override SchematicTileEntityKind Kind => SchematicTileEntityKind.FoodPlatter;
}

public sealed record SchematicTeleportationPylonTileEntity(int X, int Y) : SchematicTileEntity(X, Y)
{
    public override SchematicTileEntityKind Kind => SchematicTileEntityKind.TeleportationPylon;
}

public readonly record struct SchematicNpc(
    int NpcType,
    float X,
    float Y,
    sbyte Direction = 1,
    sbyte SpriteDirection = 1,
    string? Name = null,
    bool Homeless = true,
    int HomeX = 0,
    int HomeY = 0,
    int? LifeOverride = null);

public readonly record struct SchematicWorldItem(SchematicItemStack Item, float X, float Y);

public enum SchematicMarkerKind : byte
{
    Point = 1,
    Region = 2
}

public readonly record struct SchematicMarker(
    string Name,
    SchematicMarkerKind Kind,
    int X,
    int Y,
    int Width = 0,
    int Height = 0);

public readonly record struct SchematicMetadataEntry(string Key, string Value);

public sealed class SchematicDocument
{
    public int ContentVersion { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public int OriginX { get; init; }
    public int OriginY { get; init; }
    public SchematicTile[] Tiles { get; init; } = [];
    public SchematicChest[] Chests { get; init; } = [];
    public SchematicSign[] Signs { get; init; } = [];
    public SchematicTileEntity[] TileEntities { get; init; } = [];
    public SchematicNpc[] Npcs { get; init; } = [];
    public SchematicWorldItem[] WorldItems { get; init; } = [];
    public SchematicMarker[] Markers { get; init; } = [];
    public SchematicMetadataEntry[] Metadata { get; init; } = [];

    public void Validate() => SchematicValidator.Validate(this);
}
