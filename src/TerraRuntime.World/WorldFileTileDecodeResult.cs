namespace TerraRuntime.World;

public enum WorldFileTileDecodeResult : byte
{
    Decoded = 0,
    UnsupportedVersion = 1,
    InvalidSectionBounds = 2,
    DimensionMismatch = 3,
    Truncated = 4,
    InvalidTileType = 5,
    InvalidRunLength = 6,
    SectionLengthMismatch = 7
}
