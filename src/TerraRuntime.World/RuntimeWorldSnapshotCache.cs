using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace TerraRuntime.World;

/// <summary>
/// Self-contained startup snapshot. The live .wld is used only as a freshness/recovery checkpoint;
/// a valid warm snapshot contains the canonical bytes needed for non-tile persistence plus normalized
/// sharded tile state for fast parallel reconstruction.
/// </summary>
public static class RuntimeWorldSnapshotCache
{
    private const int HeaderSize = 128;
    private const int HashSize = 32;
    private const int TileRecordSize = 16;
    private const int ShardEntrySize = 48;
    private const int IoBufferSize = 64 * 1024;
    private const int TargetShardBytes = 16 * 1024 * 1024;
    private const int TilesPerShard = TargetShardBytes / TileRecordSize;

    private static ReadOnlySpan<byte> Magic => "TRWCACHE"u8;

    public const int SchemaVersion = 3;

    public static string GetCachePath(string worldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        return Path.ChangeExtension(worldPath, ".runtime-world");
    }

    public static bool TryCaptureSourceStamp(string worldPath, out RuntimeWorldSourceStamp stamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        try
        {
            var info = new FileInfo(worldPath);
            info.Refresh();
            if (!info.Exists)
            {
                stamp = default;
                return false;
            }

            stamp = new RuntimeWorldSourceStamp(info.Length, info.LastWriteTimeUtc.Ticks);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            stamp = default;
            return false;
        }
    }

    public static RuntimeWorldSnapshotLoadDiagnostic TryLoad(
        string cachePath,
        RuntimeWorldSourceStamp sourceStamp,
        WorldFileLoadLimits limits,
        out WorldFileData? world) =>
        TryLoad(cachePath, sourceStamp, limits, RuntimeWorldCacheReadOptions.Default, out world);

    public static RuntimeWorldSnapshotLoadDiagnostic TryLoad(
        string cachePath,
        RuntimeWorldSourceStamp sourceStamp,
        WorldFileLoadLimits limits,
        RuntimeWorldCacheReadOptions readOptions,
        out WorldFileData? world)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        limits.Validate();
        readOptions.Validate();
        world = null;

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

            if (sourceStamp.LastWriteTimeUtcTicks > header.SourceLastWriteTimeUtcTicks)
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.SourceNewer);

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
                tiles = new WorldTileStore(dimensions);
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

            byte[] canonical = new byte[checked((int)header.CanonicalLength)];
            RuntimeWorldSnapshotLoadResult canonicalReadResult = RuntimeWorldSnapshotLoadResult.Loaded;
            RuntimeWorldSnapshotLoadDiagnostic tileDiagnostic = default;

            Parallel.Invoke(
                () => canonicalReadResult = TryReadCanonical(handle, header, canonical),
                () => tileDiagnostic = TryReadTilesParallel(
                    handle,
                    header.TilePayloadOffset,
                    shards,
                    tiles.TileArray,
                    readOptions));

            if (canonicalReadResult != RuntimeWorldSnapshotLoadResult.Loaded)
                return new RuntimeWorldSnapshotLoadDiagnostic(canonicalReadResult);

            if (!tileDiagnostic.IsLoaded)
                return tileDiagnostic;

            WorldFileEnvelopeParseResult envelopeResult = WorldFileEnvelopeParser.TryParse(
                canonical,
                out WorldFileEnvelope? envelope,
                out _);
            if (envelopeResult != WorldFileEnvelopeParseResult.Parsed || envelope is null)
            {
                return new RuntimeWorldSnapshotLoadDiagnostic(
                    RuntimeWorldSnapshotLoadResult.InvalidCanonicalEnvelope,
                    (int)envelopeResult);
            }

            WorldFileHeaderParseResult worldHeaderResult = WorldFileHeaderParser.TryParse(
                canonical,
                envelope,
                out WorldFileHeader? worldHeader);
            if (worldHeaderResult != WorldFileHeaderParseResult.Parsed || worldHeader is null)
            {
                return new RuntimeWorldSnapshotLoadDiagnostic(
                    RuntimeWorldSnapshotLoadResult.InvalidCanonicalHeader,
                    (int)worldHeaderResult);
            }

            if (envelope.FormatVersion != header.WorldFormatVersion)
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.WorldFormatMismatch);

            if (worldHeader.Dimensions.WidthTiles != header.Width ||
                worldHeader.Dimensions.HeightTiles != header.Height)
            {
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.DimensionsMismatch);
            }

            var core = new WorldFileCore(envelope, worldHeader, tiles);
            WorldFileLoadDiagnostic canonicalDiagnostic = WorldFileLoader.TryLoadPreparedCore(
                canonical,
                limits,
                core,
                out world);
            if (!canonicalDiagnostic.IsLoaded || world is null)
            {
                world = null;
                return new RuntimeWorldSnapshotLoadDiagnostic(
                    RuntimeWorldSnapshotLoadResult.InvalidCanonicalWorld,
                    ((int)canonicalDiagnostic.Stage << 16) | (canonicalDiagnostic.StageResultCode & 0xFFFF));
            }

            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Loaded);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            world = null;
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.IoError);
        }
    }

    public static RuntimeWorldSnapshotWriteDiagnostic TryWriteAtomic(
        string cachePath,
        ReadOnlySpan<byte> sourceWorld,
        RuntimeWorldSourceStamp sourceStamp,
        WorldFileData world)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        ArgumentNullException.ThrowIfNull(world);

        if (sourceStamp.Length != sourceWorld.Length || sourceWorld.Length == 0)
            return new RuntimeWorldSnapshotWriteDiagnostic(RuntimeWorldSnapshotWriteResult.InvalidWorld);

        long expectedTileCount = (long)world.Header.Dimensions.WidthTiles * world.Header.Dimensions.HeightTiles;
        if (expectedTileCount != world.Tiles.Count || expectedTileCount < 0)
            return new RuntimeWorldSnapshotWriteDiagnostic(RuntimeWorldSnapshotWriteResult.InvalidWorld);

        long tilePayloadLength;
        long tilePayloadOffset;
        long shardTableOffset;
        int shardCount;
        try
        {
            tilePayloadLength = checked(expectedTileCount * TileRecordSize);
            tilePayloadOffset = checked(HeaderSize + sourceWorld.Length);
            shardCount = GetShardCount(expectedTileCount);
            shardTableOffset = checked(tilePayloadOffset + tilePayloadLength);
            _ = checked(shardTableOffset + ((long)shardCount * ShardEntrySize));
        }
        catch (OverflowException)
        {
            return new RuntimeWorldSnapshotWriteDiagnostic(RuntimeWorldSnapshotWriteResult.InvalidWorld);
        }

        Span<byte> canonicalHash = stackalloc byte[HashSize];
        SHA256.HashData(sourceWorld, canonicalHash);

        Span<byte> header = stackalloc byte[HeaderSize];
        header.Clear();
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], SchemaVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], HeaderSize);
        BinaryPrimitives.WriteInt64LittleEndian(header[16..], sourceStamp.Length);
        BinaryPrimitives.WriteInt64LittleEndian(header[24..], sourceStamp.LastWriteTimeUtcTicks);
        BinaryPrimitives.WriteInt64LittleEndian(header[32..], sourceWorld.Length);
        canonicalHash.CopyTo(header[40..72]);
        BinaryPrimitives.WriteInt32LittleEndian(header[72..], world.Envelope.FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[76..], world.Header.Dimensions.WidthTiles);
        BinaryPrimitives.WriteInt32LittleEndian(header[80..], world.Header.Dimensions.HeightTiles);
        BinaryPrimitives.WriteInt32LittleEndian(header[84..], TileRecordSize);
        BinaryPrimitives.WriteInt64LittleEndian(header[88..], expectedTileCount);
        BinaryPrimitives.WriteInt64LittleEndian(header[96..], tilePayloadLength);
        BinaryPrimitives.WriteInt32LittleEndian(header[104..], shardCount);
        BinaryPrimitives.WriteInt32LittleEndian(header[108..], ShardEntrySize);
        BinaryPrimitives.WriteInt64LittleEndian(header[112..], tilePayloadOffset);
        BinaryPrimitives.WriteInt64LittleEndian(header[120..], shardTableOffset);

        TileShardDescriptor[] shards = CreateShardLayout(expectedTileCount);
        byte[] shardTable = new byte[checked(shardCount * ShardEntrySize)];
        string tempPath = cachePath + ".tmp";
        bool replaced = false;

        try
        {
            string? directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                IoBufferSize,
                FileOptions.SequentialScan))
            {
                stream.Write(header);
                stream.Write(sourceWorld);

                ReadOnlySpan<WorldTile> sourceTiles = world.Tiles.Tiles;
                for (int shardIndex = 0; shardIndex < shards.Length; shardIndex++)
                {
                    TileShardDescriptor shard = shards[shardIndex];
                    byte[] hash = WriteTileShard(
                        stream,
                        sourceTiles,
                        checked((int)shard.TileStart),
                        shard.TileCount);
                    shards[shardIndex] = shard with { Hash = hash };
                    EncodeShardEntry(
                        shardTable.AsSpan(shardIndex * ShardEntrySize, ShardEntrySize),
                        shards[shardIndex]);
                }

                stream.Write(shardTable);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, cachePath, overwrite: true);
            replaced = true;
            return new RuntimeWorldSnapshotWriteDiagnostic(RuntimeWorldSnapshotWriteResult.Written);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new RuntimeWorldSnapshotWriteDiagnostic(RuntimeWorldSnapshotWriteResult.IoError);
        }
        finally
        {
            if (!replaced)
                TryDelete(tempPath);
        }
    }

    public static RuntimeWorldCheckpointSaveDiagnostic TrySaveCanonicalCheckpointAtomic(
        string cachePath,
        string worldPath,
        WorldFileLoadLimits limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        limits.Validate();

        if (!File.Exists(cachePath))
            return new RuntimeWorldCheckpointSaveDiagnostic(RuntimeWorldCheckpointSaveResult.CacheNotFound);

        byte[] canonical;
        WorldFileData? world;
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
            {
                return new RuntimeWorldCheckpointSaveDiagnostic(
                    RuntimeWorldCheckpointSaveResult.InvalidCache,
                    (int)headerDiagnostic.Result);
            }

            canonical = new byte[checked((int)header.CanonicalLength)];
            RuntimeWorldSnapshotLoadResult canonicalResult = TryReadCanonical(handle, header, canonical);
            if (canonicalResult != RuntimeWorldSnapshotLoadResult.Loaded)
            {
                return new RuntimeWorldCheckpointSaveDiagnostic(
                    RuntimeWorldCheckpointSaveResult.InvalidCache,
                    (int)canonicalResult);
            }

            WorldFileLoadDiagnostic loadDiagnostic = WorldFileLoader.TryLoad(
                canonical,
                limits,
                out world);
            if (!loadDiagnostic.IsLoaded || world is null)
            {
                return new RuntimeWorldCheckpointSaveDiagnostic(
                    RuntimeWorldCheckpointSaveResult.InvalidCanonicalWorld,
                    ((int)loadDiagnostic.Stage << 16) | (loadDiagnostic.StageResultCode & 0xFFFF));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            return new RuntimeWorldCheckpointSaveDiagnostic(RuntimeWorldCheckpointSaveResult.IoError);
        }

        string tempPath = worldPath + ".tmp";
        bool replaced = false;
        try
        {
            string? directory = Path.GetDirectoryName(worldPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (var stream = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                IoBufferSize,
                FileOptions.SequentialScan))
            {
                stream.Write(canonical);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, worldPath, overwrite: true);
            replaced = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new RuntimeWorldCheckpointSaveDiagnostic(RuntimeWorldCheckpointSaveResult.IoError);
        }
        finally
        {
            if (!replaced)
                TryDelete(tempPath);
        }

        if (!TryCaptureSourceStamp(worldPath, out RuntimeWorldSourceStamp sourceStamp))
            return new RuntimeWorldCheckpointSaveDiagnostic(RuntimeWorldCheckpointSaveResult.SourceStatFailed);

        RuntimeWorldSnapshotWriteDiagnostic refresh = TryWriteAtomic(
            cachePath,
            canonical,
            sourceStamp,
            world);
        return refresh.IsWritten
            ? new RuntimeWorldCheckpointSaveDiagnostic(RuntimeWorldCheckpointSaveResult.Saved)
            : new RuntimeWorldCheckpointSaveDiagnostic(
                RuntimeWorldCheckpointSaveResult.SavedCacheRefreshFailed,
                (int)refresh.Result);
    }

    private static RuntimeWorldSnapshotLoadDiagnostic TryReadHeader(
        SafeFileHandle handle,
        out CacheHeader header)
    {
        header = default;
        Span<byte> bytes = stackalloc byte[HeaderSize];
        if (!ReadExactlyAt(handle, bytes, 0))
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Truncated);

        if (!bytes[..Magic.Length].SequenceEqual(Magic))
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidMagic);

        int schemaVersion = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        if (schemaVersion != SchemaVersion)
        {
            return new RuntimeWorldSnapshotLoadDiagnostic(
                RuntimeWorldSnapshotLoadResult.UnsupportedSchema,
                schemaVersion);
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]) != HeaderSize)
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidHeader);

        long sourceLength = BinaryPrimitives.ReadInt64LittleEndian(bytes[16..]);
        long sourceLastWriteTimeUtcTicks = BinaryPrimitives.ReadInt64LittleEndian(bytes[24..]);
        long canonicalLength = BinaryPrimitives.ReadInt64LittleEndian(bytes[32..]);
        byte[] canonicalHash = bytes[40..72].ToArray();
        int worldFormatVersion = BinaryPrimitives.ReadInt32LittleEndian(bytes[72..]);
        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes[76..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(bytes[80..]);
        int tileRecordSize = BinaryPrimitives.ReadInt32LittleEndian(bytes[84..]);
        long tileCount = BinaryPrimitives.ReadInt64LittleEndian(bytes[88..]);
        long tilePayloadLength = BinaryPrimitives.ReadInt64LittleEndian(bytes[96..]);
        int shardCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[104..]);
        int shardEntrySize = BinaryPrimitives.ReadInt32LittleEndian(bytes[108..]);
        long tilePayloadOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes[112..]);
        long shardTableOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes[120..]);

        if (sourceLength <= 0 ||
            sourceLength != canonicalLength ||
            canonicalLength > int.MaxValue ||
            sourceLastWriteTimeUtcTicks < 0 ||
            width <= 0 ||
            height <= 0 ||
            tileRecordSize != TileRecordSize ||
            tileCount <= 0 ||
            shardCount <= 0 ||
            shardEntrySize != ShardEntrySize)
        {
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidHeader);
        }

        long expectedTileCount;
        long expectedPayloadLength;
        long expectedTilePayloadOffset;
        long expectedShardTableOffset;
        long expectedFileLength;
        try
        {
            expectedTileCount = checked((long)width * height);
            expectedPayloadLength = checked(tileCount * TileRecordSize);
            expectedTilePayloadOffset = checked(HeaderSize + canonicalLength);
            expectedShardTableOffset = checked(expectedTilePayloadOffset + tilePayloadLength);
            expectedFileLength = checked(expectedShardTableOffset + ((long)shardCount * ShardEntrySize));
        }
        catch (OverflowException)
        {
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidHeader);
        }

        if (tileCount != expectedTileCount ||
            tilePayloadLength != expectedPayloadLength ||
            tilePayloadOffset != expectedTilePayloadOffset ||
            shardTableOffset != expectedShardTableOffset ||
            shardCount != GetShardCount(tileCount))
        {
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidHeader);
        }

        if (RandomAccess.GetLength(handle) != expectedFileLength)
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.PayloadLengthMismatch);

        header = new CacheHeader(
            sourceLength,
            sourceLastWriteTimeUtcTicks,
            canonicalLength,
            canonicalHash,
            worldFormatVersion,
            width,
            height,
            tileCount,
            tilePayloadLength,
            shardCount,
            tilePayloadOffset,
            shardTableOffset);
        return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Loaded);
    }

    private static RuntimeWorldSnapshotLoadResult TryReadCanonical(
        SafeFileHandle handle,
        CacheHeader header,
        byte[] destination)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(IoBufferSize);
        try
        {
            using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int copied = 0;
            long fileOffset = HeaderSize;
            while (copied < destination.Length)
            {
                int byteCount = Math.Min(destination.Length - copied, IoBufferSize);
                Span<byte> chunk = buffer.AsSpan(0, byteCount);
                if (!ReadExactlyAt(handle, chunk, fileOffset))
                    return RuntimeWorldSnapshotLoadResult.Truncated;

                chunk.CopyTo(destination.AsSpan(copied, byteCount));
                hasher.AppendData(chunk);
                copied += byteCount;
                fileOffset += byteCount;
            }

            byte[] hash = hasher.GetHashAndReset();
            return CryptographicOperations.FixedTimeEquals(hash, header.CanonicalHash)
                ? RuntimeWorldSnapshotLoadResult.Loaded
                : RuntimeWorldSnapshotLoadResult.CanonicalPayloadHashMismatch;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RuntimeWorldSnapshotLoadResult.IoError;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static RuntimeWorldSnapshotLoadDiagnostic TryReadTilesParallel(
        SafeFileHandle handle,
        long tilePayloadOffset,
        TileShardDescriptor[] shards,
        WorldTile[] destination,
        RuntimeWorldCacheReadOptions readOptions)
    {
        int failure = 0;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(readOptions.MaxParallelReads, shards.Length)
        };

        Parallel.For(
            0,
            shards.Length,
            parallelOptions,
            (shardIndex, state) =>
            {
                if (Volatile.Read(ref failure) != 0)
                {
                    state.Stop();
                    return;
                }

                RuntimeWorldSnapshotLoadResult result = TryReadTileShard(
                    handle,
                    tilePayloadOffset,
                    shards[shardIndex],
                    destination);
                if (result == RuntimeWorldSnapshotLoadResult.Loaded)
                    return;

                int encodedFailure = ((int)result << 16) | (shardIndex & 0xFFFF);
                Interlocked.CompareExchange(ref failure, encodedFailure, 0);
                state.Stop();
            });

        if (failure == 0)
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Loaded);

        return new RuntimeWorldSnapshotLoadDiagnostic(
            (RuntimeWorldSnapshotLoadResult)((failure >> 16) & 0xFF),
            failure & 0xFFFF);
    }

    private static RuntimeWorldSnapshotLoadResult TryReadTileShard(
        SafeFileHandle handle,
        long tilePayloadOffset,
        TileShardDescriptor shard,
        WorldTile[] destination)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(IoBufferSize);
        try
        {
            using IncrementalHash payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int remainingTiles = shard.TileCount;
            int tileIndex = checked((int)shard.TileStart);
            long fileOffset = checked(tilePayloadOffset + (shard.TileStart * TileRecordSize));

            while (remainingTiles > 0)
            {
                int recordCount = Math.Min(remainingTiles, IoBufferSize / TileRecordSize);
                int byteCount = recordCount * TileRecordSize;
                Span<byte> chunk = buffer.AsSpan(0, byteCount);
                if (!ReadExactlyAt(handle, chunk, fileOffset))
                    return RuntimeWorldSnapshotLoadResult.Truncated;

                payloadHasher.AppendData(chunk);
                for (int recordIndex = 0; recordIndex < recordCount; recordIndex++)
                {
                    ReadOnlySpan<byte> record = chunk.Slice(recordIndex * TileRecordSize, TileRecordSize);
                    if (!TryDecodeTile(record, out WorldTile tile))
                        return RuntimeWorldSnapshotLoadResult.InvalidTileData;

                    destination[tileIndex + recordIndex] = tile;
                }

                remainingTiles -= recordCount;
                tileIndex += recordCount;
                fileOffset += byteCount;
            }

            byte[] computedHash = payloadHasher.GetHashAndReset();
            return CryptographicOperations.FixedTimeEquals(computedHash, shard.Hash)
                ? RuntimeWorldSnapshotLoadResult.Loaded
                : RuntimeWorldSnapshotLoadResult.PayloadHashMismatch;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RuntimeWorldSnapshotLoadResult.IoError;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool ReadExactlyAt(SafeFileHandle handle, Span<byte> destination, long fileOffset)
    {
        int read = 0;
        while (read < destination.Length)
        {
            int current = RandomAccess.Read(handle, destination[read..], fileOffset + read);
            if (current == 0)
                return false;
            read += current;
        }

        return true;
    }

    private static TileShardDescriptor[] CreateShardLayout(long tileCount)
    {
        int shardCount = GetShardCount(tileCount);
        var shards = new TileShardDescriptor[shardCount];
        long tileStart = 0;

        for (int shardIndex = 0; shardIndex < shardCount; shardIndex++)
        {
            int shardTileCount = checked((int)Math.Min(TilesPerShard, tileCount - tileStart));
            shards[shardIndex] = new TileShardDescriptor(tileStart, shardTileCount, new byte[HashSize]);
            tileStart += shardTileCount;
        }

        return shards;
    }

    private static int GetShardCount(long tileCount)
    {
        if (tileCount <= 0)
            return 1;
        return checked((int)((tileCount + TilesPerShard - 1) / TilesPerShard));
    }

    private static void EncodeShardEntry(Span<byte> destination, TileShardDescriptor shard)
    {
        destination.Clear();
        BinaryPrimitives.WriteInt64LittleEndian(destination, shard.TileStart);
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], shard.TileCount);
        BinaryPrimitives.WriteInt32LittleEndian(destination[12..], 0);
        shard.Hash.CopyTo(destination[16..48]);
    }

    private static bool TryDecodeShardEntry(
        ReadOnlySpan<byte> source,
        TileShardDescriptor expected,
        out TileShardDescriptor shard)
    {
        long tileStart = BinaryPrimitives.ReadInt64LittleEndian(source);
        int tileCount = BinaryPrimitives.ReadInt32LittleEndian(source[8..]);
        int reserved = BinaryPrimitives.ReadInt32LittleEndian(source[12..]);
        if (tileStart != expected.TileStart || tileCount != expected.TileCount || reserved != 0)
        {
            shard = default;
            return false;
        }

        shard = expected with { Hash = source[16..48].ToArray() };
        return true;
    }

    private static byte[] WriteTileShard(
        Stream stream,
        ReadOnlySpan<WorldTile> source,
        int tileStart,
        int tileCount)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(IoBufferSize);
        try
        {
            using IncrementalHash payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int writtenTiles = 0;
            while (writtenTiles < tileCount)
            {
                int recordCount = Math.Min(tileCount - writtenTiles, IoBufferSize / TileRecordSize);
                int byteCount = recordCount * TileRecordSize;
                Span<byte> chunk = buffer.AsSpan(0, byteCount);
                for (int recordIndex = 0; recordIndex < recordCount; recordIndex++)
                {
                    Span<byte> record = chunk.Slice(recordIndex * TileRecordSize, TileRecordSize);
                    EncodeTile(record, source[tileStart + writtenTiles + recordIndex]);
                }

                payloadHasher.AppendData(chunk);
                stream.Write(chunk);
                writtenTiles += recordCount;
            }

            return payloadHasher.GetHashAndReset();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void EncodeTile(Span<byte> destination, in WorldTile tile)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, tile.Type);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], tile.Wall);
        BinaryPrimitives.WriteInt16LittleEndian(destination[4..], tile.FrameX);
        BinaryPrimitives.WriteInt16LittleEndian(destination[6..], tile.FrameY);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[8..], (ushort)tile.Flags);
        destination[10] = tile.LiquidAmount;
        destination[11] = tile.TileColor;
        destination[12] = tile.WallColor;
        destination[13] = tile.Shape;
        destination[14] = (byte)tile.LiquidKind;
        destination[15] = 0;
    }

    private static bool TryDecodeTile(ReadOnlySpan<byte> source, out WorldTile tile)
    {
        WorldTileFlags flags = (WorldTileFlags)BinaryPrimitives.ReadUInt16LittleEndian(source[8..]);
        const WorldTileFlags knownFlags =
            WorldTileFlags.Active |
            WorldTileFlags.WireRed |
            WorldTileFlags.WireBlue |
            WorldTileFlags.WireGreen |
            WorldTileFlags.WireYellow |
            WorldTileFlags.Actuator |
            WorldTileFlags.Inactive |
            WorldTileFlags.InvisibleBlock |
            WorldTileFlags.InvisibleWall |
            WorldTileFlags.FullbrightBlock |
            WorldTileFlags.FullbrightWall;

        byte shape = source[13];
        byte liquidKind = source[14];
        if ((flags & ~knownFlags) != 0 || shape > 5 || liquidKind > (byte)WorldLiquidKind.Shimmer || source[15] != 0)
        {
            tile = default;
            return false;
        }

        tile = new WorldTile
        {
            Type = BinaryPrimitives.ReadUInt16LittleEndian(source),
            Wall = BinaryPrimitives.ReadUInt16LittleEndian(source[2..]),
            FrameX = BinaryPrimitives.ReadInt16LittleEndian(source[4..]),
            FrameY = BinaryPrimitives.ReadInt16LittleEndian(source[6..]),
            Flags = flags,
            LiquidAmount = source[10],
            TileColor = source[11],
            WallColor = source[12],
            Shape = shape,
            LiquidKind = (WorldLiquidKind)liquidKind
        };
        return true;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct CacheHeader(
        long SourceLength,
        long SourceLastWriteTimeUtcTicks,
        long CanonicalLength,
        byte[] CanonicalHash,
        int WorldFormatVersion,
        int Width,
        int Height,
        long TileCount,
        long TilePayloadLength,
        int ShardCount,
        long TilePayloadOffset,
        long ShardTableOffset);

    private readonly record struct TileShardDescriptor(long TileStart, int TileCount, byte[] Hash);
}

public readonly record struct RuntimeWorldSourceStamp(long Length, long LastWriteTimeUtcTicks);

public enum RuntimeWorldSnapshotLoadResult : byte
{
    Loaded = 0,
    NotFound = 1,
    IoError = 2,
    InvalidMagic = 3,
    UnsupportedSchema = 4,
    InvalidHeader = 5,
    SourceLengthMismatch = 6,
    SourceNewer = 7,
    WorldFormatMismatch = 8,
    DimensionsMismatch = 9,
    TileLayoutMismatch = 10,
    TileCountMismatch = 11,
    TileBudgetExceeded = 12,
    TileStorageUnsupported = 13,
    PayloadLengthMismatch = 14,
    PayloadHashMismatch = 15,
    InvalidTileData = 16,
    Truncated = 17,
    InvalidCanonicalEnvelope = 18,
    InvalidCanonicalHeader = 19,
    InvalidCanonicalWorld = 20,
    InvalidShardTable = 21,
    CanonicalPayloadHashMismatch = 22
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
