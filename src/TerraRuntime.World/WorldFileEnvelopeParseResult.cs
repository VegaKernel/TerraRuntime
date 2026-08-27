namespace TerraRuntime.World;

public enum WorldFileEnvelopeParseResult : byte
{
    Parsed = 0,
    Truncated = 1,
    InvalidVersion = 2,
    BadMagic = 3,
    NotWorldFile = 4,
    InvalidSectionCount = 5,
    SectionPointerOutOfRange = 6,
    NonMonotonicSectionPointers = 7,
    FrameImportanceTooLarge = 8,
    FirstSectionOverlapsEnvelope = 9
}
