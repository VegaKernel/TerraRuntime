using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;

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
    private const int IoBufferSize = 64 * 1024;
    private const long ReservedHeaderValue = 0;

    private static ReadOnlySpan<byte> Magic => "TRWCACHE"u8;

    public const int SchemaVersion = 1;

    public static string GetCachePath(string worldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        return Path.ChangeExtension(worldPath, ".runtime-world");
    }

    public static RuntimeWorldCacheLoadDiagnostic TryLoad(
        string cachePath,
        ReadOnlySpan<byte> sourceWorld,
        WorldFileLoadLimits limits,
        out WorldFileData? world)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        limits.Validate();
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
        {
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.TileBudgetExceeded);
        }

        WorldTileStore? tiles;
        RuntimeWorldCacheLoadDiagnostic tileCacheDiagnostic;
        try
        {
            using var stream = new FileStream(
                cachePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                IoBufferSize,
                FileOptions.SequentialScan);
            tileCacheDiagnostic = TryReadTiles(
                stream,
                sourceWorld,
                envelope,
                header,
                expectedTileCount,
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
        if (expectedTileCount != world.Tiles.Count)
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
        BinaryPrimitives.WriteInt64LittleEndian(header[88..], ReservedHeaderValue);

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
                WriteTilePayload(stream, world.Tiles);
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

    private static RuntimeWorldCacheLoadDiagnostic TryReadTiles(
        FileStream stream,
        ReadOnlySpan<byte> sourceWorld,
        WorldFileEnvelope envelope,
        WorldFileHeader header,
        long expectedTileCount,
        out WorldTileStore? tiles)
    {
        tiles = null;

        Span<byte> cacheHeader = stackalloc byte[HeaderSize];
        try
        {
            stream.ReadExactly(cacheHeader);
        }
        catch (EndOfStreamException)
        {
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.Truncated);
        }

        if (!cacheHeader[..Magic.Length].SequenceEqual(Magic))
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.InvalidMagic);

        int schemaVersion = BinaryPrimitives.ReadInt32LittleEndian(cacheHeader[8..]);
        if (schemaVersion != SchemaVersion)
        {
            return new RuntimeWorldCacheLoadDiagnostic(
                RuntimeWorldCacheLoadResult.UnsupportedSchema,
                schemaVersion);
        }

        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(cacheHeader[12..]);
        if (headerSize != HeaderSize)
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

        int tileRecordSize = BinaryPrimitives.ReadInt32LittleEndian(cacheHeader[68..]);
        if (tileRecordSize != TileRecordSize)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.TileLayoutMismatch);

        long tileCount = BinaryPrimitives.ReadInt64LittleEndian(cacheHeader[72..]);
        if (tileCount != expectedTileCount || tileCount < 0)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.TileCountMismatch);

        long payloadLength = BinaryPrimitives.ReadInt64LittleEndian(cacheHeader[80..]);
        if (tileCount > long.MaxValue / TileRecordSize || payloadLength != tileCount * TileRecordSize)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.PayloadLengthMismatch);

        if (BinaryPrimitives.ReadInt64LittleEndian(cacheHeader[88..]) != ReservedHeaderValue)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.InvalidHeader);

        long expectedFileLength;
        try
        {
            expectedFileLength = checked(HeaderSize + payloadLength + HashSize);
        }
        catch (OverflowException)
        {
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.PayloadLengthMismatch);
        }

        if (stream.Length != expectedFileLength)
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.PayloadLengthMismatch);

        Span<byte> computedSourceHash = stackalloc byte[HashSize];
        SHA256.HashData(sourceWorld, computedSourceHash);
        if (!CryptographicOperations.FixedTimeEquals(computedSourceHash, cacheHeader[24..56]))
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.SourceHashMismatch);

        try
        {
            tiles = new WorldTileStore(header.Dimensions);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.TileStorageUnsupported);
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(IoBufferSize);
        try
        {
            using IncrementalHash payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<WorldTile> destination = tiles.Tiles;
            int tileIndex = 0;
            while (tileIndex < destination.Length)
            {
                int recordCount = Math.Min(destination.Length - tileIndex, IoBufferSize / TileRecordSize);
                int byteCount = recordCount * TileRecordSize;
                Span<byte> chunk = buffer.AsSpan(0, byteCount);
                try
                {
                    stream.ReadExactly(chunk);
                }
                catch (EndOfStreamException)
                {
                    tiles = null;
                    return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.Truncated);
                }

                payloadHasher.AppendData(chunk);
                for (int recordIndex = 0; recordIndex < recordCount; recordIndex++)
                {
                    ReadOnlySpan<byte> record = chunk.Slice(recordIndex * TileRecordSize, TileRecordSize);
                    if (!TryDecodeTile(record, out WorldTile tile))
                    {
                        tiles = null;
                        return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.InvalidTileData);
                    }

                    destination[tileIndex + recordIndex] = tile;
                }

                tileIndex += recordCount;
            }

            Span<byte> storedPayloadHash = stackalloc byte[HashSize];
            try
            {
                stream.ReadExactly(storedPayloadHash);
            }
            catch (EndOfStreamException)
            {
                tiles = null;
                return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.Truncated);
            }

            byte[] computedPayloadHash = payloadHasher.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(computedPayloadHash, storedPayloadHash))
            {
                tiles = null;
                return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.PayloadHashMismatch);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new RuntimeWorldCacheLoadDiagnostic(RuntimeWorldCacheLoadResult.Loaded);
    }

    private static void WriteTilePayload(Stream stream, WorldTileStore tiles)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(IoBufferSize);
        try
        {
            using IncrementalHash payloadHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            ReadOnlySpan<WorldTile> source = tiles.Tiles;
            int tileIndex = 0;
            while (tileIndex < source.Length)
            {
                int recordCount = Math.Min(source.Length - tileIndex, IoBufferSize / TileRecordSize);
                int byteCount = recordCount * TileRecordSize;
                Span<byte> chunk = buffer.AsSpan(0, byteCount);
                for (int recordIndex = 0; recordIndex < recordCount; recordIndex++)
                {
                    Span<byte> record = chunk.Slice(recordIndex * TileRecordSize, TileRecordSize);
                    EncodeTile(record, source[tileIndex + recordIndex]);
                }

                payloadHasher.AppendData(chunk);
                stream.Write(chunk);
                tileIndex += recordCount;
            }

            byte[] payloadHash = payloadHasher.GetHashAndReset();
            stream.Write(payloadHash);
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
    InvalidCanonicalWorld = 20
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
