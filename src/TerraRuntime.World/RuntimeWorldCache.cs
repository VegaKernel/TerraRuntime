using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace TerraRuntime.World;

/// <summary>
/// Disposable startup cache for already-decoded runtime world state. The canonical .wld remains the
/// source of truth; any cache validation failure must fall back to the .wld loader.
/// </summary>
public static class RuntimeWorldCache
{
    private const int HeaderSize = 96;
    private const int HashSize = 32;
    private const int TileRecordSize = 16;
    private const int ShardEntrySize = 48;
    private const int IoBufferSize = 64 * 1024;
    private const int TargetShardBytes = 16 * 1024 * 1024;
    private const int TilesPerShard = TargetShardBytes / TileRecordSize;

    private static ReadOnlySpan<byte> Magic => "TRWCACHE"u8;

    public const int SchemaVersion = 2;

    public static string GetCachePath(string worldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        return Path.ChangeExtension(worldPath, ".runtime-world");
    }

    public static RuntimeWorldCacheLoadDiagnostic TryLoad(
        string cachePath,
        ReadOnlySpan<byte> sourceWorld,
        WorldFileLoadLimits limits,
        out WorldFileData? world) =>
        TryLoad(cachePath, sourceWorld, limits, RuntimeWorldCacheReadOptions.Default, out world);

    public static RuntimeWorldCacheLoadDiagnostic TryLoad(
        string cachePath,
        ReadOnlySpan<byte> sourceWorld,
        WorldFileLoadLimits limits,
        RuntimeWorldCacheReadOptions readOptions,
        out WorldFileData? world)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        limits.Validate();
        readOptions.Validate();
        world = null;

        if (!File.Exists(cachePath))
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.NotFound);

        WorldFileEnvelopeParseResult envelopeResult = WorldFileEnvelopeParser.TryParse(
            sourceWorld,
            out WorldFileEnvelope? envelope,
            out _);
        if (envelopeResult != WorldFileEnvelopeParseResult.Parsed || envelope is null)
        {
            return new RuntimeWorldCacheLoadDiagnostic(
                RuntimeWorldCacheLoadResult.InvalidCanonicalEnvelope,
                (int)envelopeResult);
        }

        WorldFileHeaderParseResult headerResult = WorldFileHeaderParser.TryParse(
            sourceWorld,
            envelope,
            out WorldFileHeader? header);
        if (headerResult != WorldFileHeaderParseResult.Parsed || header is null)
        {
            return new RuntimeWorldCacheLoadDiagnostic(
                RuntimeWorldCacheLoadResult.InvalidCanonicalHeader,
                (int)headerResult);
        }

        long expectedTileCount = (long)header.Dimensions.WidthTiles * header.Dimensions.HeightTiles;
        if (expectedTileCount > limits.MaxTileCount)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.TileBudgetExceeded);

        RuntimeWorldCacheLoadDiagnostic tileCacheDiagnostic;
        WorldTileStore? tiles;
        try
        {
            tileCacheDiagnostic = TryReadTilesParallel(
                cachePath,
                sourceWorld,
                envelope,
                header,
                expectedTileCount,
                readOptions,
                out tiles);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.IoError);
        }

        if (!tileCacheDiagnostic.IsLoaded || tiles is null)
            return tileCacheDiagnostic;

        var core = new WorldFileCore(envelope, header, tiles);
        WorldFileLoadDiagnostic canonicalDiagnostic = WorldFileLoader.TryLoadPreparedCore(
            sourceWorld,
            limits,
            core,
            out world);
        if (!canonicalDiagnostic.IsLoaded || world is null)
        {
            world = null;
            return new RuntimeWorldCacheLoadDiagnostic(
                RuntimeWorldCacheLoadResult.InvalidCanonicalWorld,
                ((int)canonicalDiagnostic.Stage << 16) | (canonicalDiagnostic.StageResultCode & 0xFFFF));
        }

        return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.Loaded);
    }

    public static RuntimeWorldCacheWriteDiagnostic TryWriteAtomic(
        string cachePath,
        ReadOnlySpan<byte> sourceWorld,
        WorldFileData world)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        ArgumentNullException.ThrowIfNull(world);

        long expectedTileCount = (long)world.Header.Dimensions.WidthTiles * world.Header.Dimensions.HeightTiles;
        if (expectedTileCount != world.Tiles.Count || expectedTileCount < 0)
            return new RuntimeWorldCacheWriteDiagnostic(RuntimeWorldCacheWriteResult.InvalidWorld);

        long payloadLength;
        try
        {
            payloadLength = checked(expectedTileCount * TileRecordSize);
        }
        catch (OverflowException)
        {
            return new RuntimeWorldCacheWriteDiagnostic(RuntimeWorldCacheWriteResult.InvalidWorld);
        }

        int shardCount = GetShardCount(expectedTileCount);
        Span<byte> sourceHash = stackalloc byte[HashSize];
        SHA256.HashData(sourceWorld, sourceHash);

        Span<byte> header = stackalloc byte[HeaderSize];
        header.Clear();
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], SchemaVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], HeaderSize);
        BinaryPrimitives.WriteInt64LittleEndian(header[16..], sourceWorld.Length);
        sourceHash.CopyTo(header[24..56]);
        BinaryPrimitives.WriteInt32LittleEndian(header[56..], world.Envelope.FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[60..], world.Header.Dimensions.WidthTiles);
        BinaryPrimitives.WriteInt32LittleEndian(header[64..], world.Header.Dimensions.HeightTiles);
        BinaryPrimitives.WriteInt32LittleEndian(header[68..], TileRecordSize);
        BinaryPrimitives.WriteInt64LittleEndian(header[72..], expectedTileCount);
        BinaryPrimitives.WriteInt64LittleEndian(header[80..], payloadLength);
        BinaryPrimitives.WriteInt32LittleEndian(header[88..], shardCount);
        BinaryPrimitives.WriteInt32LittleEndian(header[92..], ShardEntrySize);

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

                ReadOnlySpan<WorldTile> source = world.Tiles.Tiles;
                for (int shardIndex = 0; shardIndex < shards.Length; shardIndex++)
                {
                    TileShardDescriptor shard = shards[shardIndex];
                    byte[] hash = WriteTileShard(
                        stream,
                        source,
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
            return new RuntimeWorldCacheWriteDiagnostic(RuntimeWorldCacheWriteResult.Written);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new RuntimeWorldCacheWriteDiagnostic(RuntimeWorldCacheWriteResult.IoError);
        }
        finally
        {
            if (!replaced)
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // The canonical .wld is untouched. A stale temporary cache is harmless and disposable.
                }
            }
        }
    }

    private static RuntimeWorldCacheLoadDiagnostic TryReadTilesParallel(
        string cachePath,
        ReadOnlySpan<byte> sourceWorld,
        WorldFileEnvelope envelope,
        WorldFileHeader header,
        long expectedTileCount,
        RuntimeWorldCacheReadOptions readOptions,
        out WorldTileStore? tiles)
    {
        tiles = null;

        using SafeFileHandle handle = File.OpenHandle(
            cachePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.RandomAccess);

        Span<byte> cacheHeader = stackalloc byte[HeaderSize];
        if (!ReadExactlyAt(handle, cacheHeader, 0))
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.Truncated);

        if (!cacheHeader[..Magic.Length].SequenceEqual(Magic))
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.InvalidMagic);

        int schemaVersion = BinaryPrimitives.ReadInt32LittleEndian(cacheHeader[8..]);
        if (schemaVersion != SchemaVersion)
        {
            return new RuntimeWorldCacheLoadDiagnostic(
                RuntimeWorldCacheLoadResult.UnsupportedSchema,
                schemaVersion);
        }

        if (BinaryPrimitives.ReadInt32LittleEndian(cacheHeader[12..]) != HeaderSize)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.InvalidHeader);

        long sourceLength = BinaryPrimitives.ReadInt64LittleEndian(cacheHeader[16..]);
        if (sourceLength != sourceWorld.Length)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.SourceLengthMismatch);

        int worldFormatVersion = BinaryPrimitives.ReadInt32LittleEndian(cacheHeader[56..]);
        if (worldFormatVersion != envelope.FormatVersion)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.WorldFormatMismatch);

        int width = BinaryPrimitives.ReadInt32LittleEndian(cacheHeader[60..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(cacheHeader[64..]);
        if (width != header.Dimensions.WidthTiles || height != header.Dimensions.HeightTiles)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.DimensionsMismatch);

        if (BinaryPrimitives.ReadInt32LittleEndian(cacheHeader[68..]) != TileRecordSize)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.TileLayoutMismatch);

        long tileCount = BinaryPrimitives.ReadInt64LittleEndian(cacheHeader[72..]);
        if (tileCount != expectedTileCount || tileCount < 0)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.TileCountMismatch);

        long payloadLength = BinaryPrimitives.ReadInt64LittleEndian(cacheHeader[80..]);
        if (tileCount > long.MaxValue / TileRecordSize || payloadLength != tileCount * TileRecordSize)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.PayloadLengthMismatch);

        int shardCount = BinaryPrimitives.ReadInt32LittleEndian(cacheHeader[88..]);
        int expectedShardCount = GetShardCount(tileCount);
        if (shardCount != expectedShardCount || shardCount <= 0)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.InvalidShardTable);

        if (BinaryPrimitives.ReadInt32LittleEndian(cacheHeader[92..]) != ShardEntrySize)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.InvalidShardTable);

        long shardTableOffset;
        long expectedFileLength;
        try
        {
            shardTableOffset = checked(HeaderSize + payloadLength);
            expectedFileLength = checked(shardTableOffset + ((long)shardCount * ShardEntrySize));
        }
        catch (OverflowException)
        {
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.PayloadLengthMismatch);
        }

        if (RandomAccess.GetLength(handle) != expectedFileLength)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.PayloadLengthMismatch);

        Span<byte> computedSourceHash = stackalloc byte[HashSize];
        SHA256.HashData(sourceWorld, computedSourceHash);
        if (!CryptographicOperations.FixedTimeEquals(computedSourceHash, cacheHeader[24..56]))
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.SourceHashMismatch);

        byte[] shardTable = new byte[checked(shardCount * ShardEntrySize)];
        if (!ReadExactlyAt(handle, shardTable, shardTableOffset))
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.Truncated);

        TileShardDescriptor[] shards = CreateShardLayout(tileCount);
        for (int shardIndex = 0; shardIndex < shards.Length; shardIndex++)
        {
            ReadOnlySpan<byte> entry = shardTable.AsSpan(shardIndex * ShardEntrySize, ShardEntrySize);
            TileShardDescriptor expected = shards[shardIndex];
            if (!TryDecodeShardEntry(entry, expected, out TileShardDescriptor decoded))
            {
                return new RuntimeWorldCacheLoadDiagnostic(
                    RuntimeWorldCacheLoadResult.InvalidShardTable,
                    shardIndex);
            }

            shards[shardIndex] = decoded;
        }

        try
        {
            tiles = new WorldTileStore(header.Dimensions);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.TileStorageUnsupported);
        }

        WorldTile[] destination = tiles.TileArray;
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

                RuntimeWorldCacheLoadResult result = TryReadTileShard(
                    handle,
                    shards[shardIndex],
                    destination);
                if (result == RuntimeWorldCacheLoadResult.Loaded)
                    return;

                int encodedFailure = ((int)result << 16) | (shardIndex & 0xFFFF);
                Interlocked.CompareExchange(ref failure, encodedFailure, 0);
                state.Stop();
            });

        if (failure != 0)
        {
            tiles = null;
            return new RuntimeWorldCacheLoadDiagnostic(
                (RuntimeWorldCacheLoadResult)((failure >> 16) & 0xFF),
                failure & 0xFFFF);
        }

        return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.Loaded);
    }

    private static RuntimeWorldCacheLoadResult TryReadTileShard(
        SafeFileHandle handle,
        TileShardDescriptor shard,
        WorldTile[] destination)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(IoBufferSize);
        try
        {
            using IncrementalHash payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int remainingTiles = shard.TileCount;
            int tileIndex = checked((int)shard.TileStart);
            long fileOffset = checked(HeaderSize + (shard.TileStart * TileRecordSize));

            while (remainingTiles > 0)
            {
                int recordCount = Math.Min(remainingTiles, IoBufferSize / TileRecordSize);
                int byteCount = recordCount * TileRecordSize;
                Span<byte> chunk = buffer.AsSpan(0, byteCount);
                if (!ReadExactlyAt(handle, chunk, fileOffset))
                    return RuntimeWorldCacheLoadResult.Truncated;

                payloadHasher.AppendData(chunk);
                for (int recordIndex = 0; recordIndex < recordCount; recordIndex++)
                {
                    ReadOnlySpan<byte> record = chunk.Slice(recordIndex * TileRecordSize, TileRecordSize);
                    if (!TryDecodeTile(record, out WorldTile tile))
                        return RuntimeWorldCacheLoadResult.InvalidTileData;

                    destination[tileIndex + recordIndex] = tile;
                }

                remainingTiles -= recordCount;
                tileIndex += recordCount;
                fileOffset += byteCount;
            }

            byte[] computedHash = payloadHasher.GetHashAndReset();
            return CryptographicOperations.FixedTimeEquals(computedHash, shard.Hash)
                ? RuntimeWorldCacheLoadResult.Loaded
                : RuntimeWorldCacheLoadResult.PayloadHashMismatch;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return RuntimeWorldCacheLoadResult.IoError;
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
            shards[shardIndex] = new TileShardDescriptor(
                tileStart,
                shardTileCount,
                new byte[HashSize]);
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

    private readonly record struct TileShardDescriptor(
        long TileStart,
        int TileCount,
        byte[] Hash);
}

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

public enum RuntimeWorldCacheLoadResult : byte
{
    Loaded = 0,
    NotFound = 1,
    IoError = 2,
    InvalidMagic = 3,
    UnsupportedSchema = 4,
    InvalidHeader = 5,
    SourceLengthMismatch = 6,
    SourceHashMismatch = 7,
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
    InvalidShardTable = 21
}

public readonly record struct RuntimeWorldCacheLoadDiagnostic(
    RuntimeWorldCacheLoadResult Result,
    int DetailCode = 0)
{
    public bool IsLoaded => Result == RuntimeWorldCacheLoadResult.Loaded;
}

public enum RuntimeWorldCacheWriteResult : byte
{
    Written = 0,
    InvalidWorld = 1,
    IoError = 2
}

public readonly record struct RuntimeWorldCacheWriteDiagnostic(RuntimeWorldCacheWriteResult Result)
{
    public bool IsWritten => Result == RuntimeWorldCacheWriteResult.Written;
}
