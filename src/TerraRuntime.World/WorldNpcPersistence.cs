namespace TerraRuntime.World;

public sealed record WorldNpcPersistence(
    int[] ShimmeredTownNpcIndices,
    WorldTownNpc[] TownNpcs,
    WorldPersistentNpc[] PersistentNpcs);

public sealed record WorldTownNpc(
    int NetId,
    string GivenName,
    float X,
    float Y,
    bool Homeless,
    int HomeTileX,
    int HomeTileY,
    int? TownNpcVariationIndex,
    bool HomelessDespawn);

public readonly record struct WorldPersistentNpc(
    int NetId,
    float X,
    float Y);

public readonly record struct WorldFileNpcDecodeOptions(
    int MaxShimmeredTownNpcIndices,
    int MaxShimmerIndexExclusive,
    int MaxTownNpcs,
    int MaxPersistentNpcs,
    int MaxNameBytesPerTownNpc,
    long MaxTotalNameBytes)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxShimmeredTownNpcIndices);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxShimmerIndexExclusive);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTownNpcs);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxPersistentNpcs);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxNameBytesPerTownNpc);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTotalNameBytes);
    }
}

public enum WorldFileNpcDecodeResult : byte
{
    Decoded = 0,
    UnsupportedVersion = 1,
    InvalidSectionBounds = 2,
    InvalidShimmerCount = 3,
    InvalidShimmerIndex = 4,
    TownNpcBudgetExceeded = 5,
    PersistentNpcBudgetExceeded = 6,
    NameBudgetExceeded = 7,
    Truncated = 8,
    InvalidStringLength = 9,
    InvalidUtf8 = 10,
    NonFinitePosition = 11,
    SectionLengthMismatch = 12
}
