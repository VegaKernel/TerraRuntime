using System.Buffers.Binary;

namespace TerraRuntime.World;

public enum RuntimeWorldPreservedSectionsLoadResult : byte
{
    Loaded = 0,
    NotFound = 1,
    InvalidCacheHeader = 2,
    SourceLengthMismatch = 3,
    SourceNewer = 4,
    WorldFormatMismatch = 5,
    DimensionsMismatch = 6,
    InvalidEmbeddedWorld = 7,
    IoError = 8
}

public readonly record struct RuntimeWorldPreservedSectionsLoadDiagnostic(
    RuntimeWorldPreservedSectionsLoadResult Result)
{
    public bool IsLoaded => Result == RuntimeWorldPreservedSectionsLoadResult.Loaded;
}

/// <summary>
/// Recovers only the opaque .wld sections required by the authoritative tile/chest save slice from the canonical
/// world image embedded in a runtime-world cache. This deliberately skips the embedded tile/chest payloads and keeps
/// warm startup independent of reads from the source .wld file.
/// </summary>
public static class RuntimeWorldSnapshotPreservedSections
{
    private const int CacheHeaderSize = 128;
    private static ReadOnlySpan<byte> CacheMagic => "TRWCACHE"u8;

    public static RuntimeWorldPreservedSectionsLoadDiagnostic TryLoad(
        string cachePath,
        RuntimeWorldSourceStamp sourceStamp,
        WorldFileData world,
        out WorldFilePreservedSections? preserved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        ArgumentNullException.ThrowIfNull(world);
        preserved = null;

        if (!File.Exists(cachePath))
            return new RuntimeWorldPreservedSectionsLoadDiagnostic(RuntimeWorldPreservedSectionsLoadResult.NotFound);

        try
        {
            using var stream = new FileStream(
                cachePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.RandomAccess);

            Span<byte> header = stackalloc byte[CacheHeaderSize];
            stream.ReadExactly(header);
            if (!header[..CacheMagic.Length].SequenceEqual(CacheMagic) ||
                BinaryPrimitives.ReadInt32LittleEndian(header[8..]) != CacheHeaderSize)
            {
                return new RuntimeWorldPreservedSectionsLoadDiagnostic(
                    RuntimeWorldPreservedSectionsLoadResult.InvalidCacheHeader);
            }

            long cachedSourceLength = BinaryPrimitives.ReadInt64LittleEndian(header[16..]);
            long cachedSourceWriteTicks = BinaryPrimitives.ReadInt64LittleEndian(header[24..]);
            long canonicalLength = BinaryPrimitives.ReadInt64LittleEndian(header[32..]);
            int cachedWorldFormat = BinaryPrimitives.ReadInt32LittleEndian(header[72..]);
            int cachedWidth = BinaryPrimitives.ReadInt32LittleEndian(header[76..]);
            int cachedHeight = BinaryPrimitives.ReadInt32LittleEndian(header[80..]);

            if (cachedSourceLength <= 0 ||
                cachedSourceWriteTicks < 0 ||
                canonicalLength != cachedSourceLength ||
                canonicalLength > int.MaxValue ||
                canonicalLength > stream.Length - CacheHeaderSize)
            {
                return new RuntimeWorldPreservedSectionsLoadDiagnostic(
                    RuntimeWorldPreservedSectionsLoadResult.InvalidCacheHeader);
            }

            if (sourceStamp.Length != cachedSourceLength)
            {
                return new RuntimeWorldPreservedSectionsLoadDiagnostic(
                    RuntimeWorldPreservedSectionsLoadResult.SourceLengthMismatch);
            }

            if (sourceStamp.LastWriteTimeUtcTicks > cachedSourceWriteTicks)
            {
                return new RuntimeWorldPreservedSectionsLoadDiagnostic(
                    RuntimeWorldPreservedSectionsLoadResult.SourceNewer);
            }

            if (cachedWorldFormat != world.Envelope.FormatVersion)
            {
                return new RuntimeWorldPreservedSectionsLoadDiagnostic(
                    RuntimeWorldPreservedSectionsLoadResult.WorldFormatMismatch);
            }

            if (cachedWidth != world.Header.Dimensions.WidthTiles ||
                cachedHeight != world.Header.Dimensions.HeightTiles)
            {
                return new RuntimeWorldPreservedSectionsLoadDiagnostic(
                    RuntimeWorldPreservedSectionsLoadResult.DimensionsMismatch);
            }

            if (!WorldFilePreservedSections.TryCapture(
                    stream,
                    CacheHeaderSize,
                    canonicalLength,
                    world.Envelope,
                    out preserved) ||
                preserved is null)
            {
                preserved = null;
                return new RuntimeWorldPreservedSectionsLoadDiagnostic(
                    RuntimeWorldPreservedSectionsLoadResult.InvalidEmbeddedWorld);
            }

            return new RuntimeWorldPreservedSectionsLoadDiagnostic(RuntimeWorldPreservedSectionsLoadResult.Loaded);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or ObjectDisposedException or OverflowException)
        {
            preserved = null;
            return new RuntimeWorldPreservedSectionsLoadDiagnostic(RuntimeWorldPreservedSectionsLoadResult.IoError);
        }
    }
}
