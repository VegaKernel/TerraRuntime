using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TerraRuntime.World;

public static partial class RuntimeWorldSnapshotCache
{
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

        long sourceLength = BinaryPrimitives.ReadInt64LittleEndian(bytes[16..]);
        long sourceLastWriteTimeUtcTicks = BinaryPrimitives.ReadInt64LittleEndian(bytes[24..]);
        long canonicalLength = BinaryPrimitives.ReadInt64LittleEndian(bytes[32..]);
        ulong canonicalHash = BinaryPrimitives.ReadUInt64LittleEndian(bytes[40..]);
        int schemaVersion = BinaryPrimitives.ReadInt32LittleEndian(bytes[48..]);
        int layoutVersion = BinaryPrimitives.ReadInt32LittleEndian(bytes[52..]);
        ulong sourceFingerprintLow = BinaryPrimitives.ReadUInt64LittleEndian(bytes[56..]);
        ulong sourceFingerprintHigh = BinaryPrimitives.ReadUInt64LittleEndian(bytes[64..]);
        int worldFormatVersion = BinaryPrimitives.ReadInt32LittleEndian(bytes[72..]);
        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes[76..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(bytes[80..]);
        long tileCount = BinaryPrimitives.ReadInt64LittleEndian(bytes[88..]);
        long tilePayloadLength = BinaryPrimitives.ReadInt64LittleEndian(bytes[96..]);
        int shardCount = BinaryPrimitives.ReadInt32LittleEndian(bytes[104..]);
        int reserved = BinaryPrimitives.ReadInt32LittleEndian(bytes[108..]);
        long tilePayloadOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes[112..]);
        long shardTableOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes[120..]);

        if (schemaVersion != CurrentSchemaVersion)
        {
            return new RuntimeWorldSnapshotLoadDiagnostic(
                RuntimeWorldSnapshotLoadResult.SchemaVersionMismatch,
                schemaVersion);
        }

        if (layoutVersion != CurrentLayoutVersion)
        {
            return new RuntimeWorldSnapshotLoadDiagnostic(
                RuntimeWorldSnapshotLoadResult.LayoutVersionMismatch,
                layoutVersion);
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]) != HeaderSize ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]) != TileRecordSize ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[84..]) != ShardEntrySize ||
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

        header = new CacheHeader(
            sourceLength,
            sourceLastWriteTimeUtcTicks,
            canonicalLength,
            canonicalHash,
            sourceFingerprintLow,
            sourceFingerprintHigh,
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

    private static RuntimeWorldSnapshotLoadResult TryReadCanonical(
        SafeFileHandle handle,
        CacheHeader header,
        byte[] destination)
    {
        try
        {
            if (!ReadExactlyAt(handle, destination, HeaderSize))
                return RuntimeWorldSnapshotLoadResult.Truncated;

            return RuntimeWorldIntegrity.Hash64(destination) == header.CanonicalHash
                ? RuntimeWorldSnapshotLoadResult.Loaded
                : RuntimeWorldSnapshotLoadResult.CanonicalPayloadHashMismatch;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RuntimeWorldSnapshotLoadResult.IoError;
        }
    }

    private static RuntimeWorldSnapshotLoadResult TryReadPreparedState(
        SafeFileHandle handle,
        CacheHeader header,
        byte[] destination)
    {
        try
        {
            if (destination.Length != header.PreparedPayloadLength ||
                !ReadExactlyAt(handle, destination, header.PreparedPayloadOffset))
            {
                return RuntimeWorldSnapshotLoadResult.Truncated;
            }

            return RuntimeWorldIntegrity.Hash64(destination) == header.PreparedHash
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
        CacheHeader header,
        out WorldLiquidUpdateEntry[]? active,
        out int[]? buffered)
    {
        active = null;
        buffered = null;
        try
        {
            int payloadLength = checked(
                (header.LiquidActiveCount * LiquidActiveEntrySize) +
                (header.LiquidBufferCount * LiquidBufferEntrySize));
            byte[] payload = new byte[payloadLength];
            if (!ReadExactlyAt(handle, payload, header.LiquidPayloadOffset))
                return RuntimeWorldSnapshotLoadResult.Truncated;

            if (RuntimeWorldIntegrity.Hash64(payload) != header.LiquidHash)
                return RuntimeWorldSnapshotLoadResult.LiquidQueueHashMismatch;

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

            active = decodedActive;
            buffered = decodedBuffered;
            return RuntimeWorldSnapshotLoadResult.Loaded;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            return RuntimeWorldSnapshotLoadResult.IoError;
        }
    }

    private static byte[] EncodeLiquidState(
        ReadOnlySpan<WorldLiquidUpdateEntry> active,
        ReadOnlySpan<int> buffered)
    {
        int payloadLength = checked(
            (active.Length * LiquidActiveEntrySize) +
            (buffered.Length * LiquidBufferEntrySize));
        byte[] payload = new byte[payloadLength];
        int offset = 0;

        foreach (WorldLiquidUpdateEntry entry in active)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset, 4), entry.TileIndex);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + 4, 4), entry.Delay);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset + 8, 4), entry.Kill);
            offset += LiquidActiveEntrySize;
        }

        foreach (int tileIndex in buffered)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                payload.AsSpan(offset, LiquidBufferEntrySize),
                tileIndex);
            offset += LiquidBufferEntrySize;
        }

        return payload;
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
        try
        {
            int tileStart = checked((int)shard.TileStart);
            Span<WorldTile> tiles = destination.AsSpan(tileStart, shard.TileCount);
            Span<byte> bytes = MemoryMarshal.AsBytes(tiles);
            long fileOffset = checked(tilePayloadOffset + (shard.TileStart * TileRecordSize));

            if (!ReadExactlyAt(handle, bytes, fileOffset))
                return RuntimeWorldSnapshotLoadResult.Truncated;

            if (RuntimeWorldIntegrity.Hash64(bytes) != shard.Hash)
                return RuntimeWorldSnapshotLoadResult.PayloadHashMismatch;

            // Tiles were semantically validated before snapshot write. The matching integrity digest proves
            // the payload was not changed after validation, so a second semantic scan on warm start is redundant.
            return RuntimeWorldSnapshotLoadResult.Loaded;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            return RuntimeWorldSnapshotLoadResult.IoError;
        }
    }

    private static bool ValidateTiles(ReadOnlySpan<WorldTile> tiles)
    {
        foreach (ref readonly WorldTile tile in tiles)
        {
            if ((tile.Flags & ~KnownFlags) != 0 ||
                tile.Shape > 5 ||
                (byte)tile.LiquidKind > (byte)WorldLiquidKind.Shimmer ||
                tile.Reserved != 0)
            {
                return false;
            }
        }

        return true;
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
        long baseTilesPerShard = tileCount / shardCount;
        int remainder = checked((int)(tileCount % shardCount));
        long tileStart = 0;

        for (int shardIndex = 0; shardIndex < shardCount; shardIndex++)
        {
            int shardTileCount = checked((int)(baseTilesPerShard + (shardIndex < remainder ? 1 : 0)));
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

    private static void EncodeShardEntry(Span<byte> destination, TileShardDescriptor shard)
    {
        destination.Clear();
        BinaryPrimitives.WriteInt64LittleEndian(destination, shard.TileStart);
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], shard.TileCount);
        BinaryPrimitives.WriteInt32LittleEndian(destination[12..], 0);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[16..], shard.Hash);
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

}
