using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TerraRuntime.World;

/// <summary>
/// Detailed diagnostic timings for one successful runtime-world snapshot load. Tile I/O/hash values are
/// aggregate worker time and may exceed wall time when multiple shards execute concurrently.
/// </summary>
public readonly record struct RuntimeWorldSnapshotLoadProfile(
    TimeSpan Total,
    TimeSpan Header,
    TimeSpan TileAllocation,
    TimeSpan ShardTable,
    TimeSpan ParallelWall,
    TimeSpan TileWall,
    TimeSpan TileIoAggregate,
    TimeSpan TileHashAggregate,
    TimeSpan PreparedIo,
    TimeSpan PreparedHash,
    TimeSpan LiquidIo,
    TimeSpan LiquidHash,
    TimeSpan LiquidDecode,
    TimeSpan LiquidRestore,
    TimeSpan PreparedDecode,
    int ShardCount,
    long TilePayloadBytes,
    int PreparedPayloadBytes,
    int LiquidPayloadBytes);

/// <summary>
/// Instrumented counterpart of <see cref="RuntimeWorldSnapshotCache"/> used by verification/benchmark tooling.
/// It intentionally does not participate in normal server startup so profiling cannot perturb the production path.
/// </summary>
public static class RuntimeWorldSnapshotProfiler
{
    private const int HeaderSize = 128;
    private const int TileRecordSize = 16;
    private const int ShardEntrySize = 24;
    private const int LiquidActiveEntrySize = 12;
    private const int LiquidBufferEntrySize = 4;
    private const int LiquidTrailerHeaderSize = 64;
    private const int PreparedTrailerHeaderSize = 32;
    private const int TargetShardBytes = 16 * 1024 * 1024;
    private const int TilesPerShard = TargetShardBytes / TileRecordSize;

    private static ReadOnlySpan<byte> Magic => "TRWCACHE"u8;

    private static ReadOnlySpan<byte> LiquidTrailerMagic => "LIQSTATE"u8;

    private static ReadOnlySpan<byte> PreparedTrailerMagic => "PREPARED"u8;

    public static RuntimeWorldSnapshotLoadDiagnostic TryLoad(
        string cachePath,
        RuntimeWorldSourceStamp sourceStamp,
        WorldFileLoadLimits limits,
        RuntimeWorldCacheReadOptions readOptions,
        out WorldFileData? world,
        out RuntimeWorldSnapshotLoadProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        limits.Validate();
        readOptions.Validate();
        world = null;
        profile = default;

        if (!File.Exists(cachePath))
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.NotFound);

        long totalStart = Stopwatch.GetTimestamp();
        long headerTicks = 0;
        long allocationTicks = 0;
        long shardTableTicks = 0;
        long parallelTicks = 0;
        long tileWallTicks = 0;
        long tileIoTicks = 0;
        long tileHashTicks = 0;
        long preparedIoTicks = 0;
        long preparedHashTicks = 0;
        long liquidIoTicks = 0;
        long liquidHashTicks = 0;
        long liquidDecodeTicks = 0;
        long liquidRestoreTicks = 0;
        long preparedDecodeTicks = 0;

        try
        {
            using SafeFileHandle handle = File.OpenHandle(
                cachePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileOptions.RandomAccess);

            long stageStart = Stopwatch.GetTimestamp();
            RuntimeWorldSnapshotLoadDiagnostic headerDiagnostic = TryReadHeader(handle, out ProfileHeader header);
            headerTicks = ElapsedTicks(stageStart);
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
            stageStart = Stopwatch.GetTimestamp();
            try
            {
                tiles = WorldTileStore.CreateForSnapshotLoad(dimensions);
            }
            catch (ArgumentOutOfRangeException)
            {
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.TileStorageUnsupported);
            }
            allocationTicks = ElapsedTicks(stageStart);

            stageStart = Stopwatch.GetTimestamp();
            byte[] shardTable = new byte[checked(header.ShardCount * ShardEntrySize)];
            if (!ReadExactlyAt(handle, shardTable, header.ShardTableOffset))
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Truncated);

            TileShardDescriptor[] shards = CreateShardLayout(header.TileCount);
            for (int shardIndex = 0; shardIndex < shards.Length; shardIndex++)
            {
                ReadOnlySpan<byte> entry = shardTable.AsSpan(shardIndex * ShardEntrySize, ShardEntrySize);
                TileShardDescriptor expected = shards[shardIndex];
                if (!TryDecodeShardEntry(entry, expected, out TileShardDescriptor decodedShard))
                {
                    return new RuntimeWorldSnapshotLoadDiagnostic(
                        RuntimeWorldSnapshotLoadResult.InvalidShardTable,
                        shardIndex);
                }

                shards[shardIndex] = decodedShard;
            }
            shardTableTicks = ElapsedTicks(stageStart);

            byte[] preparedPayload = new byte[header.PreparedPayloadLength];
            RuntimeWorldSnapshotLoadResult preparedReadResult = RuntimeWorldSnapshotLoadResult.Loaded;
            RuntimeWorldSnapshotLoadDiagnostic tileDiagnostic = default;
            RuntimeWorldSnapshotLoadResult liquidReadResult = RuntimeWorldSnapshotLoadResult.Loaded;
            WorldLiquidUpdateEntry[]? liquidActive = null;
            int[]? liquidBuffered = null;

            stageStart = Stopwatch.GetTimestamp();
            Parallel.Invoke(
                () => preparedReadResult = TryReadPreparedState(
                    handle,
                    header,
                    preparedPayload,
                    out preparedIoTicks,
                    out preparedHashTicks),
                () => tileDiagnostic = TryReadTilesParallel(
                    handle,
                    header.TilePayloadOffset,
                    shards,
                    tiles.TileArray,
                    readOptions,
                    out tileWallTicks,
                    out tileIoTicks,
                    out tileHashTicks),
                () => liquidReadResult = TryReadLiquidState(
                    handle,
                    header,
                    out liquidActive,
                    out liquidBuffered,
                    out liquidIoTicks,
                    out liquidHashTicks,
                    out liquidDecodeTicks));
            parallelTicks = ElapsedTicks(stageStart);

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

            stageStart = Stopwatch.GetTimestamp();
            bool restored = tiles.LiquidUpdates.TryRestoreSnapshot(liquidActive, liquidBuffered);
            liquidRestoreTicks = ElapsedTicks(stageStart);
            if (!restored)
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidLiquidQueue);

            stageStart = Stopwatch.GetTimestamp();
            bool decoded = RuntimeWorldPreparedStateCodec.TryDecode(preparedPayload, tiles, out world);
            preparedDecodeTicks = ElapsedTicks(stageStart);
            if (!decoded || world is null)
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

            profile = new RuntimeWorldSnapshotLoadProfile(
                TimeSpan.FromTicks(ElapsedTicks(totalStart)),
                TimeSpan.FromTicks(headerTicks),
                TimeSpan.FromTicks(allocationTicks),
                TimeSpan.FromTicks(shardTableTicks),
                TimeSpan.FromTicks(parallelTicks),
                TimeSpan.FromTicks(tileWallTicks),
                TimeSpan.FromTicks(tileIoTicks),
                TimeSpan.FromTicks(tileHashTicks),
                TimeSpan.FromTicks(preparedIoTicks),
                TimeSpan.FromTicks(preparedHashTicks),
                TimeSpan.FromTicks(liquidIoTicks),
                TimeSpan.FromTicks(liquidHashTicks),
                TimeSpan.FromTicks(liquidDecodeTicks),
                TimeSpan.FromTicks(liquidRestoreTicks),
                TimeSpan.FromTicks(preparedDecodeTicks),
                header.ShardCount,
                checked(header.TileCount * TileRecordSize),
                header.PreparedPayloadLength,
                checked((header.LiquidActiveCount * LiquidActiveEntrySize) +
                        (header.LiquidBufferCount * LiquidBufferEntrySize)));
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Loaded);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            world = null;
            profile = default;
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.IoError);
        }
    }

    private static RuntimeWorldSnapshotLoadDiagnostic TryReadHeader(
        SafeFileHandle handle,
        out ProfileHeader header)
    {
        header = default;
        Span<byte> bytes = stackalloc byte[HeaderSize];
        if (!ReadExactlyAt(handle, bytes, 0))
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Truncated);

        if (!bytes[..Magic.Length].SequenceEqual(Magic))
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidMagic);

        long sourceLength = BinaryPrimitives.ReadInt64LittleEndian(bytes[16..]);
        long sourceLastWriteTimeUtcTicks = BinaryPrimitives.ReadInt64LittleEndian(bytes[24..]);
        long canonicalLength = BinaryPrimitives.ReadInt64LittleEndian(bytes[32..]);
        int worldFormatVersion = BinaryPrimitives.ReadInt32LittleEndian(bytes[72..]);
        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes[76..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(bytes[80..]);
        long tileCount = BinaryPrimitives.ReadInt64LittleEndian(bytes[88..]);
        long tilePayloadLength = BinaryPrimitives.ReadInt64LittleEndian(bytes[96..]);
        int shardCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[104..]);
        int reserved = BinaryPrimitives.ReadInt32LittleEndian(bytes[108..]);
        long tilePayloadOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes[112..]);
        long shardTableOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes[120..]);

        if (BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]) != HeaderSize ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]) != TileRecordSize ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[84..]) != ShardEntrySize ||
            !bytes[48..72].SequenceEqual(stackalloc byte[24]) ||
            reserved != 0 ||
            sourceLength <= 0 ||
            sourceLength != canonicalLength ||
            canonicalLength > int.MaxValue ||
            sourceLastWriteTimeUtcTicks < 0 ||
            width <= 0 ||
            height <= 0 ||
            tileCount <= 0 ||
            shardCount <= 0)
        {
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidHeader);
        }

        long expectedTileCount;
        long expectedPayloadLength;
        long expectedTilePayloadOffset;
        long expectedShardTableOffset;
        long liquidHeaderOffset;
        try
        {
            expectedTileCount = checked((long)width * height);
            expectedPayloadLength = checked(tileCount * TileRecordSize);
            expectedTilePayloadOffset = checked(HeaderSize + canonicalLength);
            expectedShardTableOffset = checked(expectedTilePayloadOffset + tilePayloadLength);
            liquidHeaderOffset = checked(expectedShardTableOffset + ((long)shardCount * ShardEntrySize));
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

        Span<byte> liquidHeader = stackalloc byte[LiquidTrailerHeaderSize];
        if (!ReadExactlyAt(handle, liquidHeader, liquidHeaderOffset))
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Truncated);

        if (!liquidHeader[..LiquidTrailerMagic.Length].SequenceEqual(LiquidTrailerMagic))
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidLiquidQueue);

        int liquidActiveEntrySize = BinaryPrimitives.ReadInt32LittleEndian(liquidHeader[8..]);
        int liquidBufferEntrySize = BinaryPrimitives.ReadInt32LittleEndian(liquidHeader[12..]);
        int liquidActiveCount = BinaryPrimitives.ReadInt32LittleEndian(liquidHeader[16..]);
        int liquidBufferCount = BinaryPrimitives.ReadInt32LittleEndian(liquidHeader[20..]);
        long liquidPayloadLength = BinaryPrimitives.ReadInt64LittleEndian(liquidHeader[24..]);
        ulong liquidHash = BinaryPrimitives.ReadUInt64LittleEndian(liquidHeader[32..]);

        long expectedLiquidPayloadLength;
        long liquidPayloadOffset;
        long preparedHeaderOffset;
        try
        {
            expectedLiquidPayloadLength = checked(
                ((long)liquidActiveCount * LiquidActiveEntrySize) +
                ((long)liquidBufferCount * LiquidBufferEntrySize));
            liquidPayloadOffset = checked(liquidHeaderOffset + LiquidTrailerHeaderSize);
            preparedHeaderOffset = checked(liquidPayloadOffset + liquidPayloadLength);
        }
        catch (OverflowException)
        {
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidLiquidQueue);
        }

        if (liquidActiveEntrySize != LiquidActiveEntrySize ||
            liquidBufferEntrySize != LiquidBufferEntrySize ||
            liquidActiveCount < 0 ||
            liquidActiveCount > tileCount ||
            liquidBufferCount < 0 ||
            liquidBufferCount > tileCount ||
            liquidPayloadLength != expectedLiquidPayloadLength ||
            liquidPayloadLength > int.MaxValue ||
            !liquidHeader[40..64].SequenceEqual(stackalloc byte[24]))
        {
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidLiquidQueue);
        }

        Span<byte> preparedHeader = stackalloc byte[PreparedTrailerHeaderSize];
        if (!ReadExactlyAt(handle, preparedHeader, preparedHeaderOffset))
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Truncated);

        if (!preparedHeader[..PreparedTrailerMagic.Length].SequenceEqual(PreparedTrailerMagic))
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidPreparedWorld);

        long preparedPayloadLength = BinaryPrimitives.ReadInt64LittleEndian(preparedHeader[8..]);
        ulong preparedHash = BinaryPrimitives.ReadUInt64LittleEndian(preparedHeader[16..]);
        long preparedPayloadOffset;
        long expectedFileLength;
        try
        {
            preparedPayloadOffset = checked(preparedHeaderOffset + PreparedTrailerHeaderSize);
            expectedFileLength = checked(preparedPayloadOffset + preparedPayloadLength);
        }
        catch (OverflowException)
        {
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidPreparedWorld);
        }

        if (preparedPayloadLength <= 0 ||
            preparedPayloadLength > RuntimeWorldPreparedStateCodec.MaximumPayloadBytes ||
            preparedPayloadLength > int.MaxValue ||
            !preparedHeader[24..32].SequenceEqual(stackalloc byte[8]))
        {
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidPreparedWorld);
        }

        if (RandomAccess.GetLength(handle) != expectedFileLength)
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.PayloadLengthMismatch);

        header = new ProfileHeader(
            sourceLength,
            sourceLastWriteTimeUtcTicks,
            worldFormatVersion,
            width,
            height,
            tileCount,
            shardCount,
            tilePayloadOffset,
            shardTableOffset,
            liquidActiveCount,
            liquidBufferCount,
            liquidPayloadOffset,
            liquidHash,
            checked((int)preparedPayloadLength),
            preparedPayloadOffset,
            preparedHash);
        return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Loaded);
    }

    private static RuntimeWorldSnapshotLoadResult TryReadPreparedState(
        SafeFileHandle handle,
        ProfileHeader header,
        byte[] destination,
        out long ioTicks,
        out long hashTicks)
    {
        ioTicks = 0;
        hashTicks = 0;
        try
        {
            long start = Stopwatch.GetTimestamp();
            bool read = destination.Length == header.PreparedPayloadLength &&
                ReadExactlyAt(handle, destination, header.PreparedPayloadOffset);
            ioTicks = ElapsedTicks(start);
            if (!read)
                return RuntimeWorldSnapshotLoadResult.Truncated;

            start = Stopwatch.GetTimestamp();
            ulong hash = RuntimeWorldIntegrity.Hash64(destination);
            hashTicks = ElapsedTicks(start);
            return hash == header.PreparedHash
                ? RuntimeWorldSnapshotLoadResult.Loaded
                : RuntimeWorldSnapshotLoadResult.PreparedPayloadHashMismatch;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RuntimeWorldSnapshotLoadResult.IoError;
        }
    }

    private static RuntimeWorldSnapshotLoadResult TryReadLiquidState(
        SafeFileHandle handle,
        ProfileHeader header,
        out WorldLiquidUpdateEntry[]? active,
        out int[]? buffered,
        out long ioTicks,
        out long hashTicks,
        out long decodeTicks)
    {
        active = null;
        buffered = null;
        ioTicks = 0;
        hashTicks = 0;
        decodeTicks = 0;
        try
        {
            int payloadLength = checked(
                (header.LiquidActiveCount * LiquidActiveEntrySize) +
                (header.LiquidBufferCount * LiquidBufferEntrySize));
            byte[] payload = new byte[payloadLength];

            long start = Stopwatch.GetTimestamp();
            bool read = ReadExactlyAt(handle, payload, header.LiquidPayloadOffset);
            ioTicks = ElapsedTicks(start);
            if (!read)
                return RuntimeWorldSnapshotLoadResult.Truncated;

            start = Stopwatch.GetTimestamp();
            ulong hash = RuntimeWorldIntegrity.Hash64(payload);
            hashTicks = ElapsedTicks(start);
            if (hash != header.LiquidHash)
                return RuntimeWorldSnapshotLoadResult.LiquidQueueHashMismatch;

            start = Stopwatch.GetTimestamp();
            var decodedActive = new WorldLiquidUpdateEntry[header.LiquidActiveCount];
            int offset = 0;
            for (int i = 0; i < decodedActive.Length; i++)
            {
                int tileIndex = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset, 4));
                int delay = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 4, 4));
                int kill = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 8, 4));
                decodedActive[i] = new WorldLiquidUpdateEntry(tileIndex, delay, kill);
                offset += LiquidActiveEntrySize;
            }

            var decodedBuffered = new int[header.LiquidBufferCount];
            for (int i = 0; i < decodedBuffered.Length; i++)
            {
                decodedBuffered[i] = BinaryPrimitives.ReadInt32LittleEndian(
                    payload.AsSpan(offset, LiquidBufferEntrySize));
                offset += LiquidBufferEntrySize;
            }
            decodeTicks = ElapsedTicks(start);

            active = decodedActive;
            buffered = decodedBuffered;
            return RuntimeWorldSnapshotLoadResult.Loaded;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            return RuntimeWorldSnapshotLoadResult.IoError;
        }
    }

    private static RuntimeWorldSnapshotLoadDiagnostic TryReadTilesParallel(
        SafeFileHandle handle,
        long tilePayloadOffset,
        TileShardDescriptor[] shards,
        WorldTile[] destination,
        RuntimeWorldCacheReadOptions readOptions,
        out long wallTicks,
        out long ioTicks,
        out long hashTicks)
    {
        long wallStart = Stopwatch.GetTimestamp();
        long ioAggregate = 0;
        long hashAggregate = 0;
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
                    destination,
                    out long shardIoTicks,
                    out long shardHashTicks);
                Interlocked.Add(ref ioAggregate, shardIoTicks);
                Interlocked.Add(ref hashAggregate, shardHashTicks);
                if (result == RuntimeWorldSnapshotLoadResult.Loaded)
                    return;

                int encodedFailure = ((int)result << 16) | (shardIndex & 0xFFFF);
                Interlocked.CompareExchange(ref failure, encodedFailure, 0);
                state.Stop();
            });

        wallTicks = ElapsedTicks(wallStart);
        ioTicks = ioAggregate;
        hashTicks = hashAggregate;

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
        WorldTile[] destination,
        out long ioTicks,
        out long hashTicks)
    {
        ioTicks = 0;
        hashTicks = 0;
        try
        {
            int tileStart = checked((int)shard.TileStart);
            Span<WorldTile> tiles = destination.AsSpan(tileStart, shard.TileCount);
            Span<byte> bytes = MemoryMarshal.AsBytes(tiles);
            long fileOffset = checked(tilePayloadOffset + (shard.TileStart * TileRecordSize));

            long start = Stopwatch.GetTimestamp();
            bool read = ReadExactlyAt(handle, bytes, fileOffset);
            ioTicks = ElapsedTicks(start);
            if (!read)
                return RuntimeWorldSnapshotLoadResult.Truncated;

            start = Stopwatch.GetTimestamp();
            ulong hash = RuntimeWorldIntegrity.Hash64(bytes);
            hashTicks = ElapsedTicks(start);
            return hash == shard.Hash
                ? RuntimeWorldSnapshotLoadResult.Loaded
                : RuntimeWorldSnapshotLoadResult.PayloadHashMismatch;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            return RuntimeWorldSnapshotLoadResult.IoError;
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
            shards[shardIndex] = new TileShardDescriptor(tileStart, shardTileCount, 0);
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

        shard = expected with { Hash = BinaryPrimitives.ReadUInt64LittleEndian(source[16..]) };
        return true;
    }

    private static long ElapsedTicks(long start) => Stopwatch.GetElapsedTime(start).Ticks;

    private readonly record struct ProfileHeader(
        long SourceLength,
        long SourceLastWriteTimeUtcTicks,
        int WorldFormatVersion,
        int Width,
        int Height,
        long TileCount,
        int ShardCount,
        long TilePayloadOffset,
        long ShardTableOffset,
        int LiquidActiveCount,
        int LiquidBufferCount,
        long LiquidPayloadOffset,
        ulong LiquidHash,
        int PreparedPayloadLength,
        long PreparedPayloadOffset,
        ulong PreparedHash);

    private readonly record struct TileShardDescriptor(long TileStart, int TileCount, ulong Hash);
}
