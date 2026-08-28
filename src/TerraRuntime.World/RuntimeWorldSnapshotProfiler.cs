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
/// Non-invasive diagnostics for the runtime snapshot. Total is measured around the real production loader;
/// component timings come from an isolated second pass so instrumentation cannot perturb startup behavior.
/// ParallelWall is the estimated production critical path: the maximum isolated wall time of tiles,
/// prepared-state verification, and liquid-state verification/decoding, which production executes concurrently.
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

        long productionStart = Stopwatch.GetTimestamp();
        RuntimeWorldSnapshotLoadDiagnostic productionDiagnostic = RuntimeWorldSnapshotCache.TryLoad(
            cachePath,
            sourceStamp,
            limits,
            readOptions,
            out WorldFileData? loadedWorld);
        TimeSpan productionTotal = Stopwatch.GetElapsedTime(productionStart);
        if (!productionDiagnostic.IsLoaded || loadedWorld is null)
            return productionDiagnostic;

        try
        {
            using SafeFileHandle handle = File.OpenHandle(
                cachePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileOptions.RandomAccess);

            long stageStart = Stopwatch.GetTimestamp();
            if (!TryReadLayout(handle, out SnapshotLayout layout))
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidHeader);
            TimeSpan headerTime = Stopwatch.GetElapsedTime(stageStart);

            stageStart = Stopwatch.GetTimestamp();
            WorldTileStore scratchTiles = WorldTileStore.CreateForSnapshotLoad(
                new WorldDimensions(layout.Width, layout.Height));
            TimeSpan allocationTime = Stopwatch.GetElapsedTime(stageStart);

            stageStart = Stopwatch.GetTimestamp();
            if (!TryReadShardTable(handle, layout, out TileShardDescriptor[] shards))
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidShardTable);
            TimeSpan shardTableTime = Stopwatch.GetElapsedTime(stageStart);

            RuntimeWorldSnapshotLoadDiagnostic tileDiagnostic = ProfileTiles(
                handle,
                layout.TilePayloadOffset,
                shards,
                scratchTiles.TileArray,
                readOptions,
                out TimeSpan tileWall,
                out TimeSpan tileIoAggregate,
                out TimeSpan tileHashAggregate);
            if (!tileDiagnostic.IsLoaded)
                return tileDiagnostic;

            byte[] preparedPayload = new byte[layout.PreparedPayloadLength];
            stageStart = Stopwatch.GetTimestamp();
            if (!ReadExactlyAt(handle, preparedPayload, layout.PreparedPayloadOffset))
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Truncated);
            TimeSpan preparedIo = Stopwatch.GetElapsedTime(stageStart);

            stageStart = Stopwatch.GetTimestamp();
            ulong preparedHash = RuntimeWorldIntegrity.Hash64(preparedPayload);
            TimeSpan preparedHashTime = Stopwatch.GetElapsedTime(stageStart);
            if (preparedHash != layout.PreparedHash)
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.PreparedPayloadHashMismatch);

            int liquidPayloadLength = checked(
                (layout.LiquidActiveCount * LiquidActiveEntrySize) +
                (layout.LiquidBufferCount * LiquidBufferEntrySize));
            byte[] liquidPayload = new byte[liquidPayloadLength];
            stageStart = Stopwatch.GetTimestamp();
            if (!ReadExactlyAt(handle, liquidPayload, layout.LiquidPayloadOffset))
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Truncated);
            TimeSpan liquidIo = Stopwatch.GetElapsedTime(stageStart);

            stageStart = Stopwatch.GetTimestamp();
            ulong liquidHash = RuntimeWorldIntegrity.Hash64(liquidPayload);
            TimeSpan liquidHashTime = Stopwatch.GetElapsedTime(stageStart);
            if (liquidHash != layout.LiquidHash)
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.LiquidQueueHashMismatch);

            stageStart = Stopwatch.GetTimestamp();
            DecodeLiquidPayload(
                liquidPayload,
                layout.LiquidActiveCount,
                layout.LiquidBufferCount,
                out WorldLiquidUpdateEntry[] liquidActive,
                out int[] liquidBuffered);
            TimeSpan liquidDecode = Stopwatch.GetElapsedTime(stageStart);

            stageStart = Stopwatch.GetTimestamp();
            bool liquidRestored = scratchTiles.LiquidUpdates.TryRestoreSnapshot(liquidActive, liquidBuffered);
            TimeSpan liquidRestore = Stopwatch.GetElapsedTime(stageStart);
            if (!liquidRestored)
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidLiquidQueue);

            stageStart = Stopwatch.GetTimestamp();
            bool preparedDecoded = RuntimeWorldPreparedStateCodec.TryDecode(
                preparedPayload,
                scratchTiles,
                out WorldFileData? decodedWorld);
            TimeSpan preparedDecode = Stopwatch.GetElapsedTime(stageStart);
            if (!preparedDecoded || decodedWorld is null)
                return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.InvalidPreparedWorld);

            TimeSpan preparedVerifyWall = preparedIo + preparedHashTime;
            TimeSpan liquidVerifyWall = liquidIo + liquidHashTime + liquidDecode;
            TimeSpan estimatedParallelWall = Max(tileWall, preparedVerifyWall, liquidVerifyWall);

            profile = new RuntimeWorldSnapshotLoadProfile(
                productionTotal,
                headerTime,
                allocationTime,
                shardTableTime,
                estimatedParallelWall,
                tileWall,
                tileIoAggregate,
                tileHashAggregate,
                preparedIo,
                preparedHashTime,
                liquidIo,
                liquidHashTime,
                liquidDecode,
                liquidRestore,
                preparedDecode,
                shards.Length,
                layout.TilePayloadLength,
                layout.PreparedPayloadLength,
                liquidPayloadLength);
            world = loadedWorld;
            return productionDiagnostic;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or OverflowException or ArgumentOutOfRangeException)
        {
            world = null;
            profile = default;
            return new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.IoError);
        }
    }

    private static bool TryReadLayout(SafeFileHandle handle, out SnapshotLayout layout)
    {
        layout = default;
        Span<byte> header = stackalloc byte[HeaderSize];
        if (!ReadExactlyAt(handle, header, 0) ||
            !header[..8].SequenceEqual("TRWCACHE"u8) ||
            BinaryPrimitives.ReadInt32LittleEndian(header[8..]) != HeaderSize ||
            BinaryPrimitives.ReadInt32LittleEndian(header[12..]) != TileRecordSize ||
            BinaryPrimitives.ReadInt32LittleEndian(header[84..]) != ShardEntrySize)
        {
            return false;
        }

        long canonicalLength = BinaryPrimitives.ReadInt64LittleEndian(header[32..]);
        int width = BinaryPrimitives.ReadInt32LittleEndian(header[76..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(header[80..]);
        long tileCount = BinaryPrimitives.ReadInt64LittleEndian(header[88..]);
        long tilePayloadLength = BinaryPrimitives.ReadInt64LittleEndian(header[96..]);
        int shardCount = BinaryPrimitives.ReadInt32LittleEndian(header[104..]);
        long tilePayloadOffset = BinaryPrimitives.ReadInt64LittleEndian(header[112..]);
        long shardTableOffset = BinaryPrimitives.ReadInt64LittleEndian(header[120..]);

        if (canonicalLength <= 0 ||
            width <= 0 ||
            height <= 0 ||
            tileCount != checked((long)width * height) ||
            tilePayloadLength != checked(tileCount * TileRecordSize) ||
            tilePayloadOffset != checked(HeaderSize + canonicalLength) ||
            shardCount <= 0 ||
            shardTableOffset != checked(tilePayloadOffset + tilePayloadLength))
        {
            return false;
        }

        long liquidHeaderOffset = checked(shardTableOffset + ((long)shardCount * ShardEntrySize));
        Span<byte> liquidHeader = stackalloc byte[LiquidTrailerHeaderSize];
        if (!ReadExactlyAt(handle, liquidHeader, liquidHeaderOffset) ||
            !liquidHeader[..8].SequenceEqual("LIQSTATE"u8) ||
            BinaryPrimitives.ReadInt32LittleEndian(liquidHeader[8..]) != LiquidActiveEntrySize ||
            BinaryPrimitives.ReadInt32LittleEndian(liquidHeader[12..]) != LiquidBufferEntrySize)
        {
            return false;
        }

        int liquidActiveCount = BinaryPrimitives.ReadInt32LittleEndian(liquidHeader[16..]);
        int liquidBufferCount = BinaryPrimitives.ReadInt32LittleEndian(liquidHeader[20..]);
        long liquidPayloadLength = BinaryPrimitives.ReadInt64LittleEndian(liquidHeader[24..]);
        ulong liquidHash = BinaryPrimitives.ReadUInt64LittleEndian(liquidHeader[32..]);
        long expectedLiquidPayloadLength = checked(
            ((long)liquidActiveCount * LiquidActiveEntrySize) +
            ((long)liquidBufferCount * LiquidBufferEntrySize));
        if (liquidActiveCount < 0 ||
            liquidBufferCount < 0 ||
            liquidPayloadLength != expectedLiquidPayloadLength ||
            liquidPayloadLength > int.MaxValue)
        {
            return false;
        }

        long liquidPayloadOffset = checked(liquidHeaderOffset + LiquidTrailerHeaderSize);
        long preparedHeaderOffset = checked(liquidPayloadOffset + liquidPayloadLength);
        Span<byte> preparedHeader = stackalloc byte[PreparedTrailerHeaderSize];
        if (!ReadExactlyAt(handle, preparedHeader, preparedHeaderOffset) ||
            !preparedHeader[..8].SequenceEqual("PREPARED"u8))
        {
            return false;
        }

        long preparedPayloadLength = BinaryPrimitives.ReadInt64LittleEndian(preparedHeader[8..]);
        ulong preparedHash = BinaryPrimitives.ReadUInt64LittleEndian(preparedHeader[16..]);
        if (preparedPayloadLength <= 0 ||
            preparedPayloadLength > RuntimeWorldPreparedStateCodec.MaximumPayloadBytes ||
            preparedPayloadLength > int.MaxValue)
        {
            return false;
        }

        long preparedPayloadOffset = checked(preparedHeaderOffset + PreparedTrailerHeaderSize);
        if (RandomAccess.GetLength(handle) != checked(preparedPayloadOffset + preparedPayloadLength))
            return false;

        layout = new SnapshotLayout(
            width,
            height,
            tilePayloadLength,
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
        return true;
    }

    private static bool TryReadShardTable(
        SafeFileHandle handle,
        SnapshotLayout layout,
        out TileShardDescriptor[] shards)
    {
        shards = Array.Empty<TileShardDescriptor>();
        byte[] table = new byte[checked(layout.ShardCount * ShardEntrySize)];
        if (!ReadExactlyAt(handle, table, layout.ShardTableOffset))
            return false;

        var decoded = new TileShardDescriptor[layout.ShardCount];
        long expectedTileStart = 0;
        for (int i = 0; i < decoded.Length; i++)
        {
            ReadOnlySpan<byte> entry = table.AsSpan(i * ShardEntrySize, ShardEntrySize);
            long tileStart = BinaryPrimitives.ReadInt64LittleEndian(entry);
            int tileCount = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            int reserved = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            ulong hash = BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]);
            if (tileStart != expectedTileStart || tileCount <= 0 || reserved != 0)
                return false;

            decoded[i] = new TileShardDescriptor(tileStart, tileCount, hash);
            expectedTileStart = checked(expectedTileStart + tileCount);
        }

        if (checked(expectedTileStart * TileRecordSize) != layout.TilePayloadLength)
            return false;

        shards = decoded;
        return true;
    }

    private static RuntimeWorldSnapshotLoadDiagnostic ProfileTiles(
        SafeFileHandle handle,
        long tilePayloadOffset,
        TileShardDescriptor[] shards,
        WorldTile[] destination,
        RuntimeWorldCacheReadOptions readOptions,
        out TimeSpan wall,
        out TimeSpan ioAggregate,
        out TimeSpan hashAggregate)
    {
        long wallStart = Stopwatch.GetTimestamp();
        long ioTicks = 0;
        long hashTicks = 0;
        int failure = 0;
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(readOptions.MaxParallelReads, shards.Length)
        };

        Parallel.For(0, shards.Length, parallelOptions, (shardIndex, state) =>
        {
            if (Volatile.Read(ref failure) != 0)
            {
                state.Stop();
                return;
            }

            TileShardDescriptor shard = shards[shardIndex];
            int tileStart = checked((int)shard.TileStart);
            Span<byte> bytes = MemoryMarshal.AsBytes(destination.AsSpan(tileStart, shard.TileCount));
            long fileOffset = checked(tilePayloadOffset + (shard.TileStart * TileRecordSize));

            long start = Stopwatch.GetTimestamp();
            bool read = ReadExactlyAt(handle, bytes, fileOffset);
            Interlocked.Add(ref ioTicks, Stopwatch.GetElapsedTime(start).Ticks);
            if (!read)
            {
                Interlocked.CompareExchange(ref failure, (int)RuntimeWorldSnapshotLoadResult.Truncated, 0);
                state.Stop();
                return;
            }

            start = Stopwatch.GetTimestamp();
            ulong hash = RuntimeWorldIntegrity.Hash64(bytes);
            Interlocked.Add(ref hashTicks, Stopwatch.GetElapsedTime(start).Ticks);
            if (hash != shard.Hash)
            {
                Interlocked.CompareExchange(ref failure, (int)RuntimeWorldSnapshotLoadResult.PayloadHashMismatch, 0);
                state.Stop();
            }
        });

        wall = Stopwatch.GetElapsedTime(wallStart);
        ioAggregate = TimeSpan.FromTicks(ioTicks);
        hashAggregate = TimeSpan.FromTicks(hashTicks);
        return failure == 0
            ? new RuntimeWorldSnapshotLoadDiagnostic(RuntimeWorldSnapshotLoadResult.Loaded)
            : new RuntimeWorldSnapshotLoadDiagnostic((RuntimeWorldSnapshotLoadResult)failure);
    }

    private static void DecodeLiquidPayload(
        ReadOnlySpan<byte> payload,
        int activeCount,
        int bufferedCount,
        out WorldLiquidUpdateEntry[] active,
        out int[] buffered)
    {
        active = new WorldLiquidUpdateEntry[activeCount];
        int offset = 0;
        for (int i = 0; i < active.Length; i++)
        {
            int tileIndex = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, 4));
            int delay = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset + 4, 4));
            int kill = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset + 8, 4));
            active[i] = new WorldLiquidUpdateEntry(tileIndex, delay, kill);
            offset += LiquidActiveEntrySize;
        }

        buffered = new int[bufferedCount];
        for (int i = 0; i < buffered.Length; i++)
        {
            buffered[i] = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, LiquidBufferEntrySize));
            offset += LiquidBufferEntrySize;
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

    private static TimeSpan Max(TimeSpan first, TimeSpan second, TimeSpan third)
    {
        TimeSpan result = first >= second ? first : second;
        return result >= third ? result : third;
    }

    private readonly record struct SnapshotLayout(
        int Width,
        int Height,
        long TilePayloadLength,
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
