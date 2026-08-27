namespace TerraRuntime.World;

public readonly record struct WorldPressurePlate(int X, int Y);

public enum WorldFilePressurePlateDecodeResult : byte
{
    Decoded = 0,
    UnsupportedVersion = 1,
    InvalidSectionBounds = 2,
    InvalidCount = 3,
    CountBudgetExceeded = 4,
    InvalidCoordinates = 5,
    DuplicateCoordinates = 6,
    Truncated = 7,
    SectionLengthMismatch = 8
}
