namespace TerraRuntime.World;

public readonly record struct RuntimeWorldSourceStamp(long Length, long LastWriteTimeUtcTicks);

public readonly record struct RuntimeWorldCacheReadOptions(int MaxParallelReads)
{
    public static RuntimeWorldCacheReadOptions Default =>
        new(Math.Clamp(Environment.ProcessorCount, 1, 4));

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxParallelReads, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxParallelReads, 32);
    }
}

public enum RuntimeWorldSnapshotLoadResult : byte
{
    Loaded = 0,
    NotFound = 1,
    IoError = 2,
    InvalidMagic = 3,
    InvalidHeader = 4,
    SourceLengthMismatch = 5,
    SourceNewer = 6,
    WorldFormatMismatch = 7,
    DimensionsMismatch = 8,
    TileLayoutMismatch = 9,
    TileBudgetExceeded = 10,
    TileStorageUnsupported = 11,
    PayloadLengthMismatch = 12,
    PayloadHashMismatch = 13,
    InvalidTileData = 14,
    Truncated = 15,
    InvalidCanonicalEnvelope = 16,
    InvalidCanonicalHeader = 17,
    InvalidCanonicalWorld = 18,
    InvalidShardTable = 19,
    CanonicalPayloadHashMismatch = 20,
    LiquidQueueHashMismatch = 21,
    InvalidLiquidQueue = 22,
    PreparedPayloadHashMismatch = 23,
    InvalidPreparedWorld = 24,
    SourceFingerprintUnavailable = 25,
    SourceFingerprintMismatch = 26,
    SchemaVersionMismatch = 27,
    LayoutVersionMismatch = 28
}

public readonly record struct RuntimeWorldSnapshotLoadDiagnostic(
    RuntimeWorldSnapshotLoadResult Result,
    int DetailCode = 0)
{
    public bool IsLoaded => Result == RuntimeWorldSnapshotLoadResult.Loaded;
}

public enum RuntimeWorldSnapshotWriteResult : byte
{
    Written = 0,
    InvalidWorld = 1,
    IoError = 2
}

public readonly record struct RuntimeWorldSnapshotWriteDiagnostic(RuntimeWorldSnapshotWriteResult Result)
{
    public bool IsWritten => Result == RuntimeWorldSnapshotWriteResult.Written;
}

public enum RuntimeWorldCheckpointSaveResult : byte
{
    Saved = 0,
    CacheNotFound = 1,
    InvalidCache = 2,
    InvalidCanonicalWorld = 3,
    IoError = 4,
    SourceStatFailed = 5,
    SavedCacheRefreshFailed = 6
}

public readonly record struct RuntimeWorldCheckpointSaveDiagnostic(
    RuntimeWorldCheckpointSaveResult Result,
    int DetailCode = 0)
{
    public bool IsSaved => Result is RuntimeWorldCheckpointSaveResult.Saved or RuntimeWorldCheckpointSaveResult.SavedCacheRefreshFailed;
}
