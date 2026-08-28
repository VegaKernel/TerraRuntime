using TerraRuntime.Contracts.Gameplay;

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

/// <summary>
/// Persistence representation of one item embedded in a vanilla tile entity. Raw primitive fields are
/// retained because they are the .wld ABI; gameplay consumers should cross them through the typed accessors.
/// </summary>
public readonly record struct WorldTileEntityItem(short Type, byte Prefix, short Stack)
{
    public bool TryGetItemType(out ItemTypeId itemType) => VanillaItemIds.TryCreate(Type, out itemType);

    public PrefixId PrefixId => new(Prefix);
}

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

public sealed record WorldLeashedAnchorPayload(short ItemType) : WorldTileEntityPayload
{
    public bool TryGetItemType(out ItemTypeId itemType) => VanillaItemIds.TryCreate(ItemType, out itemType);
}

public sealed record WorldTileEntity(
    int PersistedId,
    short X,
    short Y,
    WorldTileEntityKind Kind,
    WorldTileEntityPayload Payload);

/// <summary>
/// Version-pinned validation for ordinary serialized Item payloads embedded in tile entities. This deliberately
/// validates only content identity at this stage; stack/prefix semantics remain source-backed follow-up work.
/// Leashed-anchor payloads are excluded until their sentinel semantics are independently verified.
/// </summary>
public static class WorldTileEntityItemValidator
{
    public static bool HasValidItemTypes(WorldTileEntityPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return payload switch
        {
            WorldItemTileEntityPayload item => IsValid(item.Item),
            WorldDisplayDollPayload doll =>
                AllValid(doll.Equipment) &&
                AllValid(doll.Dyes) &&
                (!doll.Misc.HasValue || IsValid(doll.Misc.Value)),
            WorldHatRackPayload hatRack => AllValid(hatRack.Items) && AllValid(hatRack.Dyes),
            _ => true
        };
    }

    private static bool AllValid(WorldTileEntityItem?[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i].HasValue && !IsValid(items[i].Value))
                return false;
        }

        return true;
    }

    private static bool IsValid(in WorldTileEntityItem item) => item.TryGetItemType(out _);
}

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
    SectionLengthMismatch = 11,
    InvalidItemType = 12
}
