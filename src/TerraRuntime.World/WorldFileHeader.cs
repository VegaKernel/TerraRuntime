namespace TerraRuntime.World;

public sealed record WorldFileHeader(
    string Name,
    string SeedText,
    ulong WorldGeneratorVersion,
    Guid UniqueId,
    int WorldId,
    int LeftWorld,
    int RightWorld,
    int TopWorld,
    int BottomWorld,
    WorldDimensions Dimensions);

public enum WorldFileHeaderParseResult
{
    Parsed,
    InvalidEnvelope,
    UnsupportedVersion,
    InvalidSectionBounds,
    Truncated,
    InvalidStringLength,
    StringTooLarge,
    InvalidUtf8,
    InvalidWorldBounds,
    InvalidDimensions
}
