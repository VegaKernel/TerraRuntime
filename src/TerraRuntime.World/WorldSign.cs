namespace TerraRuntime.World;

public sealed record WorldSign(
    string Text,
    int X,
    int Y);

public enum WorldFileSignDecodeResult : byte
{
    Decoded = 0,
    UnsupportedVersion = 1,
    InvalidSectionBounds = 2,
    InvalidSignCount = 3,
    InvalidSignCoordinates = 4,
    TextBudgetExceeded = 5,
    Truncated = 6,
    InvalidStringLength = 7,
    InvalidUtf8 = 8,
    SectionLengthMismatch = 9
}
