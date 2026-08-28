using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.World;

public sealed record WorldChest(
    short SlotId,
    int X,
    int Y,
    string Name,
    WorldChestItem[] Items);

/// <summary>
/// Persistence/runtime projection of one chest item slot. Raw primitive fields mirror the .wld and packet
/// boundaries; gameplay consumers should cross item/prefix identity through the typed accessors.
/// </summary>
public readonly record struct WorldChestItem(
    int Stack,
    int ItemType,
    byte Prefix)
{
    public bool IsEmpty => Stack <= 0;

    public bool TryGetItemType(out ItemTypeId itemType) => VanillaItemIds.TryCreate(ItemType, out itemType);

    public PrefixId PrefixId => new(Prefix);

    public bool HasValidItemType => IsEmpty || TryGetItemType(out _);
}

public enum WorldFileChestDecodeResult : byte
{
    Decoded = 0,
    UnsupportedVersion = 1,
    InvalidSectionBounds = 2,
    InvalidChestCount = 3,
    InvalidChestCoordinates = 4,
    ItemBudgetExceeded = 5,
    Truncated = 6,
    InvalidStringLength = 7,
    StringTooLarge = 8,
    InvalidUtf8 = 9,
    SectionLengthMismatch = 10,
    InvalidItemType = 11
}
