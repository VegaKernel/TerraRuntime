using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TerraRuntime.World;

public static partial class RuntimeWorldSnapshotCache
{
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
        RuntimeWorldSourceFingerprint sourceFingerprint =
            RuntimeWorldSourceFingerprint.FromBytes(sourceWorld, sourceStamp);

        Span<byte> header = stackalloc byte[HeaderSize];
        header.Clear();
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], TileRecordSize);
        BinaryPrimitives.WriteInt64LittleEndian(header[16..], sourceStamp.Length);
        BinaryPrimitives.WriteInt64LittleEndian(header[24..], sourceStamp.LastWriteTimeUtcTicks);
        BinaryPrimitives.WriteInt64LittleEndian(header[32..], sourceWorld.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(header[40..], canonicalHash);
        BinaryPrimitives.WriteInt32LittleEndian(header[48..], CurrentSchemaVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[52..], CurrentLayoutVersion);
        sourceFingerprint.WriteHash(header[56..72]);
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

}
