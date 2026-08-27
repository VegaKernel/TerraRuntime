namespace TerraRuntime.World;

public sealed record WorldChest(
    short SlotId,
    int X,
    int Y,
    string Name,
    WorldChestItem[] Items);

public readonly record struct WorldChestItem(
    int Stack,
    int ItemType,
    byte Prefix)
{
    public bool IsEmpty => Stack <= 0;
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
    SectionLengthMismatch = 10
}
