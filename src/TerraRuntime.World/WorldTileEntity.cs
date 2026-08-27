namespace TerraRuntime.World;

public enum WorldTileEntityKind : byte
{
    TrainingDummy = 0,
    ItemFrame = 1,
    LogicSensor = 2,
    DisplayDoll = 3,
    WeaponsRack = 4,
    HatRack = 5,
    FoodPlatter = 6,
    TeleportationPylon = 7,
    DeadCellsDisplayJar = 8,
    KiteAnchor = 9,
    CritterAnchor = 10
}

public readonly record struct WorldTileEntityItem(short Type, byte Prefix, short Stack);

public abstract record WorldTileEntityPayload;

public sealed record WorldTrainingDummyPayload(short NpcIndex) : WorldTileEntityPayload;

public sealed record WorldItemTileEntityPayload(WorldTileEntityItem Item) : WorldTileEntityPayload;

public sealed record WorldLogicSensorPayload(byte LogicCheck, bool IsOn) : WorldTileEntityPayload;

public sealed record WorldDisplayDollPayload(
    byte Pose,
    WorldTileEntityItem?[] Equipment,
    WorldTileEntityItem?[] Dyes,
    WorldTileEntityItem? Misc) : WorldTileEntityPayload;

public sealed record WorldHatRackPayload(
    WorldTileEntityItem?[] Items,
    WorldTileEntityItem?[] Dyes) : WorldTileEntityPayload;

public sealed record WorldEmptyTileEntityPayload : WorldTileEntityPayload
{
    public static WorldEmptyTileEntityPayload Instance { get; } = new();
}

public sealed record WorldLeashedAnchorPayload(short ItemType) : WorldTileEntityPayload;

public sealed record WorldTileEntity(
    int PersistedId,
    short X,
    short Y,
    WorldTileEntityKind Kind,
    WorldTileEntityPayload Payload);

public enum WorldFileTileEntityDecodeResult : byte
{
    Decoded = 0,
    UnsupportedVersion = 1,
    InvalidSectionBounds = 2,
    Truncated = 3,
    InvalidEntityCount = 4,
    EntityBudgetExceeded = 5,
    UnknownEntityType = 6,
    InvalidPersistedId = 7,
    DuplicatePersistedId = 8,
    InvalidCoordinates = 9,
    InvalidPayloadFlags = 10,
    SectionLengthMismatch = 11
}
