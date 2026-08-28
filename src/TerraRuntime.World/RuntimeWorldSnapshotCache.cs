using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TerraRuntime.World;

/// <summary>
/// Disposable self-contained TerraRuntime startup snapshot. There is intentionally no migration or
/// schema-version mechanism: any incompatible or invalid snapshot is rebuilt from the canonical .wld.
/// </summary>
public static class RuntimeWorldSnapshotCache
{
    private const int HeaderSize = 128;
    private const int TileRecordSize = 16;
    private const int ShardEntrySize = 24;
    private const int LiquidActiveEntrySize = 12;
    private const int LiquidBufferEntrySize = 4;
    private const int LiquidTrailerHeaderSize = 64;
    private const int PreparedTrailerHeaderSize = 32;
    private const int IoBufferSize = 64 * 1024;
    private const int TargetShardBytes = 16 * 1024 * 1024;
    private const int TilesPerShard = TargetShardBytes / TileRecordSize;

    private const WorldTileFlags KnownFlags =
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

    private static readonly bool NativeTileLayoutSupported =
        BitConverter.IsLittleEndian &&
        MemoryMarshal.AsBytes(new WorldTile[1].AsSpan()).Length == TileRecordSize;

    private static ReadOnlySpan<byte> Magic => "TRWCACHE"u8;

    private static ReadOnlySpan<byte> LiquidTrailerMagic => "LIQSTATE"u8;

    private static ReadOnlySpan<byte> PreparedTrailerMagic => "PREPARED"u8;

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

    public static RuntimeWorldSnapshotWriteDiagnostic TryWriteAtomic(
        string cachePath,
        ReadOnlySpan<byte> sourceWorld,
        RuntimeWorldSourceStamp sourceStamp,
        WorldFileData world)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        ArgumentNullException.ThrowIfNull(world);

        if (!NativeTileLayoutSupported || sourceStamp.Length != sourceWorld.Length || sourceWorld.Length == 0)
            return new RuntimeWorldSnapshotWriteDiagnostic(RuntimeWorldSnapshotWriteResult.InvalidWorld);

        long expectedTileCount = (long)world.Header.Dimensions.WidthTiles * world.Header.Dimensions.HeightTiles;
        if (expectedTileCount != world.Tiles.Count || expectedTileCount <= 0)
            return new RuntimeWorldSnapshotWriteDiagnostic(RuntimeWorldSnapshotWriteResult.InvalidWorld);

        ReadOnlySpan<WorldTile> sourceTiles = world.Tiles.Tiles;
        if (!ValidateTiles(sourceTiles))
            return new RuntimeWorldSnapshotWriteDiagnostic(RuntimeWorldSnapshotWriteResult.InvalidWorld);

        WorldLiquidUpdateEntry[] liquidActive = world.Tiles.LiquidUpdates.CaptureActiveSnapshot();
        int[] liquidBuffered = world.Tiles.LiquidUpdates.CaptureBufferSnapshot();
        if (liquidActive.Length > expectedTileCount || liquidBuffered.Length > expectedTileCount)
            return new RuntimeWorldSnapshotWriteDiagnostic(RuntimeWorldSnapshotWriteResult.InvalidWorld);

        byte[] liquidPayload;
        byte[] preparedPayload;
        try
        {
            liquidPayload = EncodeLiquidState(liquidActive, liquidBuffered);
            preparedPayload = RuntimeWorldPreparedStateCodec.Encode(world);
        }
        catch (Exception exception) when (exception is OverflowException or InvalidDataException or ArgumentException)
        {
            return new RuntimeWorldSnapshotWriteDiagnostic(RuntimeWorldSnapshotWriteResult.InvalidWorld);
        }

        long tilePayloadLength;
        long tilePayloadOffset;
        long shardTableOffset;
        long liquidHeaderOffset;
        long liquidPayloadOffset;
        long preparedHeaderOffset;
        long preparedPayloadOffset;
        int shardCount;
        try
        {
            tilePayloadLength = checked(expectedTileCount * TileRecordSize);
            tilePayloadOffset = checked(HeaderSize + sourceWorld.Length);
            shardCount = GetShardCount(expectedTileCount);
            shardTableOffset = checked(tilePayloadOffset + tilePayloadLength);
            liquidHeaderOffset = checked(shardTableOffset + ((long)shardCount * ShardEntrySize));
            liquidPayloadOffset = checked(liquidHeaderOffset + LiquidTrailerHeaderSize);
            preparedHeaderOffset = checked(liquidPayloadOffset + liquidPayload.Length);
            preparedPayloadOffset = checked(preparedHeaderOffset + PreparedTrailerHeaderSize);
            _ = checked(preparedPayloadOffset + preparedPayload.Length);
        }
        catch (OverflowException)
        {
            return new RuntimeWorldSnapshotWriteDiagnostic(RuntimeWorldSnapshotWriteResult.InvalidWorld);
        }

        ulong canonicalHash = RuntimeWorldIntegrity.Hash64(sourceWorld);

        Span<byte> header = stackalloc byte[HeaderSize];
        header.Clear();
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], TileRecordSize);
        BinaryPrimitives.WriteInt64LittleEndian(header[16..], sourceStamp.Length);
        BinaryPrimitives.WriteInt64LittleEndian(header[24..], sourceStamp.LastWriteTimeUtcTicks);
        BinaryPrimitives.WriteInt64LittleEndian(header[32..], sourceWorld.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(header[40..], canonicalHash);
        BinaryPrimitives.WriteInt32LittleEndian(header[72..], world.Envelope.FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[76..], world.Header.Dimensions.WidthTiles);
        BinaryPrimitives.WriteInt32LittleEndian(header[80..], world.Header.Dimensions.HeightTiles);
        BinaryPrimitives.WriteInt32LittleEndian(header[84..], ShardEntrySize);
        BinaryPrimitives.WriteInt64LittleEndian(header[88..], expectedTileCount);
        BinaryPrimitives.WriteInt64LittleEndian(header[96..], tilePayloadLength);
        BinaryPrimitives.WriteInt32LittleEndian(header[104..], shardCount);
        BinaryPrimitives.WriteInt32LittleEndian(header[108..], 0);
        BinaryPrimitives.WriteInt64LittleEndian(header[112..], tilePayloadOffset);
        BinaryPrimitives.WriteInt64LittleEndian(header[120..], shardTableOffset);

        Span<byte> liquidHeader = stackalloc byte[LiquidTrailerHeaderSize];
        liquidHeader.Clear();
        LiquidTrailerMagic.CopyTo(liquidHeader);
        BinaryPrimitives.WriteInt32LittleEndian(liquidHeader[8..], LiquidActiveEntrySize);
        BinaryPrimitives.WriteInt32LittleEndian(liquidHeader[12..], LiquidBufferEntrySize);
        BinaryPrimitives.WriteInt32LittleEndian(liquidHeader[16..], liquidActive.Length);
        BinaryPrimitives.WriteInt32LittleEndian(liquidHeader[20..], liquidBuffered.Length);
        BinaryPrimitives.WriteInt64LittleEndian(liquidHeader[24..], liquidPayload.LongLength);
        BinaryPrimitives.WriteUInt64LittleEndian(liquidHeader[32..], RuntimeWorldIntegrity.Hash64(liquidPayload));

        Span<byte> preparedHeader = stackalloc byte[PreparedTrailerHeaderSize];
        preparedHeader.Clear();
        PreparedTrailerMagic.CopyTo(preparedHeader);
        BinaryPrimitives.WriteInt64LittleEndian(preparedHeader[8..], preparedPayload.LongLength);
        BinaryPrimitives.WriteUInt64LittleEndian(preparedHeader[16..], RuntimeWorldIntegrity.Hash64(preparedPayload));

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

                for (int shardIndex = 0; shardIndex < shards.Length; shardIndex++)
                {
                    TileShardDescriptor shard = shards[shardIndex];
                    ReadOnlySpan<WorldTile> shardTiles = sourceTiles.Slice(
                        checked((int)shard.TileStart),
                        shard.TileCount);
                    ReadOnlySpan<byte> shardBytes = MemoryMarshal.AsBytes(shardTiles);
                    ulong hash = RuntimeWorldIntegrity.Hash64(shardBytes);
                    stream.Write(shardBytes);

                    TileShardDescriptor written = shard with { Hash = hash };
                    EncodeShardEntry(
                        shardTable.AsSpan(shardIndex * ShardEntrySize, ShardEntrySize),
                        written);
                }

                stream.Write(shardTable);
                stream.Write(liquidHeader);
                stream.Write(liquidPayload);
                stream.Write(preparedHeader);
                stream.Write(preparedPayload);
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
        WorldLiquidUpdateEntry[]? liquidActive;
        int[]? liquidBuffered;
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

            RuntimeWorldSnapshotLoadResult liquidResult = TryReadLiquidState(
                handle,
                header,
                out liquidActive,
                out liquidBuffered);
            if (liquidResult != RuntimeWorldSnapshotLoadResult.Loaded ||
                liquidActive is null ||
                liquidBuffered is null)
            {
                return new RuntimeWorldCheckpointSaveDiagnostic(
                    RuntimeWorldCheckpointSaveResult.InvalidCache,
                    (int)liquidResult);
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

            if (!world.Tiles.LiquidUpdates.TryRestoreSnapshot(liquidActive, liquidBuffered))
            {
                return new RuntimeWorldCheckpointSaveDiagnostic(
                    RuntimeWorldCheckpointSaveResult.InvalidCache,
                    (int)RuntimeWorldSnapshotLoadResult.InvalidLiquidQueue);
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

        long sourceLength = BinaryPrimitives.ReadInt64LittleEndian(bytes[16..]);
        long sourceLastWriteTimeUtcTicks = BinaryPrimitives.ReadInt64LittleEndian(bytes[24..]);
        long canonicalLength = BinaryPrimitives.ReadInt64LittleEndian(bytes[32..]);
        ulong canonicalHash = BinaryPrimitives.ReadUInt64LittleEndian(bytes[40..]);
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

        header = new CacheHeader(
            sourceLength,
            sourceLastWriteTimeUtcTicks,
            canonicalLength,
            canonicalHash,
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

    private readonly record struct CacheHeader(
        long SourceLength,
        long SourceLastWriteTimeUtcTicks,
        long CanonicalLength,
        ulong CanonicalHash,
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
    InvalidPreparedWorld = 24
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
