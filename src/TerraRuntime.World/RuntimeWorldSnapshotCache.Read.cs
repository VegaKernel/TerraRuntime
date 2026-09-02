using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TerraRuntime.World;

public static partial class RuntimeWorldSnapshotCache
{
    public static RuntimeWorldSnapshotLoadDiagnostic TryLoad(
        string cachePath,
        RuntimeWorldSourceStamp sourceStamp,
        WorldFileLoadLimits limits,
        out WorldFileData? world) =>
        TryLoad(
            cachePath,
            sourceStamp,
            limits,
            RuntimeWorldCacheReadOptions.Default,
            expectedSourceFingerprint: null,
            out world);

    public static RuntimeWorldSnapshotLoadDiagnostic TryLoadValidatedSource(
        string cachePath,
        string worldPath,
        WorldFileLoadLimits limits,
        out WorldFileData? world)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        limits.Validate();
        world = null;

        if (!File.Exists(cachePath))
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.NotFound);

        if (!RuntimeWorldSourceFingerprint.TryCapture(worldPath, out RuntimeWorldSourceFingerprint fingerprint))
        {
            return new RuntimeWorldSnapshotLoadDiagnostic(
                RuntimeWorldSnapshotLoadResult.SourceFingerprintUnavailable);
        }

        return TryLoad(
            cachePath,
            new RuntimeWorldSourceStamp(fingerprint.Length, fingerprint.LastWriteTimeUtcTicks),
            limits,
            RuntimeWorldCacheReadOptions.Default,
            fingerprint,
            out world);
    }

    public static RuntimeWorldSnapshotLoadDiagnostic TryLoad(
        string cachePath,
        RuntimeWorldSourceStamp sourceStamp,
        WorldFileLoadLimits limits,
        RuntimeWorldCacheReadOptions readOptions,
        out WorldFileData? world) =>
        TryLoad(
            cachePath,
            sourceStamp,
            limits,
            readOptions,
            expectedSourceFingerprint: null,
            out world);

    private static RuntimeWorldSnapshotLoadDiagnostic TryLoad(
        string cachePath,
        RuntimeWorldSourceStamp sourceStamp,
        WorldFileLoadLimits limits,
        RuntimeWorldCacheReadOptions readOptions,
        RuntimeWorldSourceFingerprint? expectedSourceFingerprint,
        out WorldFileData? world)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        limits.Validate();
        readOptions.Validate();
        world = null;

        if (!NativeTileLayoutSupported)
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.TileLayoutMismatch);

        if (!File.Exists(cachePath))
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.NotFound);

        try
        {
            using SafeFileHandle handle = File.OpenHandle(
                cachePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileOptions.RandomAccess);

            RuntimeWorldSnapshotLoadDiagnostic headerDiagnostic = TryReadHeader(handle, out CacheHeader header);
            if (!headerDiagnostic.IsLoaded)
                return headerDiagnostic;

            if (sourceStamp.Length != header.SourceLength)
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.SourceLengthMismatch);

            if (expectedSourceFingerprint is RuntimeWorldSourceFingerprint fingerprint)
            {
                if (fingerprint.HashLow != header.SourceFingerprintLow ||
                    fingerprint.HashHigh != header.SourceFingerprintHigh)
                {
                    return new RuntimeWorldSnapshotLoadDiagnostic(
                        RuntimeWorldSnapshotLoadResult.SourceFingerprintMismatch);
                }
            }
            else if (sourceStamp.LastWriteTimeUtcTicks > header.SourceLastWriteTimeUtcTicks)
            {
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.SourceNewer);
            }

            if (header.TileCount > limits.MaxTileCount)
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.TileBudgetExceeded);

            WorldDimensions dimensions;
            try
            {
                dimensions = new WorldDimensions(header.Width, header.Height);
            }
            catch (ArgumentOutOfRangeException)
            {
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.DimensionsMismatch);
            }

            WorldTileStore tiles;
            try
            {
                tiles = WorldTileStore.CreateForSnapshotLoad(dimensions);
            }
            catch (ArgumentOutOfRangeException)
            {
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.TileStorageUnsupported);
            }

            byte[] shardTable = new byte[checked(header.ShardCount * ShardEntrySize)];
            if (!ReadExactlyAt(handle, shardTable, header.ShardTableOffset))
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Truncated);

            TileShardDescriptor[] shards = CreateShardLayout(header.TileCount);
            for (int shardIndex = 0; shardIndex < shards.Length; shardIndex++)
            {
                ReadOnlySpan<byte> entry = shardTable.AsSpan(shardIndex * ShardEntrySize, ShardEntrySize);
                TileShardDescriptor expected = shards[shardIndex];
                if (!TryDecodeShardEntry(entry, expected, out TileShardDescriptor decoded))
                {
                    return new RuntimeWorldSnapshotLoadDiagnostic(
                        RuntimeWorldSnapshotLoadResult.InvalidShardTable,
                        shardIndex);
                }

                shards[shardIndex] = decoded;
            }

            byte[] preparedPayload = new byte[header.PreparedPayloadLength];
            RuntimeWorldSnapshotLoadResult preparedReadResult = RuntimeWorldSnapshotLoadResult.Loaded;
            RuntimeWorldSnapshotLoadDiagnostic tileDiagnostic = default;
            RuntimeWorldSnapshotLoadResult liquidReadResult = RuntimeWorldSnapshotLoadResult.Loaded;
            WorldLiquidUpdateEntry[]? liquidActive = null;
            int[]? liquidBuffered = null;

            Parallel.Invoke(
                () => preparedReadResult = TryReadPreparedState(handle, header, preparedPayload),
                () => tileDiagnostic = TryReadTilesParallel(
                    handle,
                    header.TilePayloadOffset,
                    shards,
                    tiles.TileArray,
                    readOptions),
                () => liquidReadResult = TryReadLiquidState(
                    handle,
                    header,
                    out liquidActive,
                    out liquidBuffered));

            if (preparedReadResult != RuntimeWorldSnapshotLoadResult.Loaded)
                return new RuntimeWorldSnapshotLoadDiagnostic(preparedReadResult);

            if (!tileDiagnostic.IsLoaded)
                return tileDiagnostic;

            if (liquidReadResult != RuntimeWorldSnapshotLoadResult.Loaded ||
                liquidActive is null ||
                liquidBuffered is null)
            {
                return new RuntimeWorldSnapshotLoadDiagnostic(liquidReadResult);
            }

            if (!tiles.LiquidUpdates.TryRestoreSnapshot(liquidActive, liquidBuffered))
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidLiquidQueue);

            if (!RuntimeWorldPreparedStateCodec.TryDecode(preparedPayload, tiles, out world) || world is null)
            {
                world = null;
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidPreparedWorld);
            }

            if (world.Envelope.FormatVersion != header.WorldFormatVersion)
            {
                world = null;
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.WorldFormatMismatch);
            }

            if (world.Header.Dimensions.WidthTiles != header.Width ||
                world.Header.Dimensions.HeightTiles != header.Height)
            {
                world = null;
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.DimensionsMismatch);
            }

            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Loaded);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            world = null;
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.IoError);
        }
    }

}
