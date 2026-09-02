using System.Runtime.InteropServices;

namespace TerraRuntime.World;

/// <summary>
/// Disposable self-contained TerraRuntime startup snapshot. There is intentionally no migration or
/// schema-version mechanism: any incompatible or invalid snapshot is rebuilt from the canonical .wld.
/// </summary>
public static partial class RuntimeWorldSnapshotCache
{
    public const int CurrentSchemaVersion = 1;
    public const int CurrentLayoutVersion = 1;

    private const int HeaderSize = 128;
    private const int TileRecordSize = 16;
    private const int ShardEntrySize = 24;
    private const int LiquidActiveEntrySize = 12;
    private const int LiquidBufferEntrySize = 4;
    private const int LiquidTrailerHeaderSize = 64;
    private const int PreparedTrailerHeaderSize = 32;
    private const int IoBufferSize = 64 * 1024;
    private const int TargetShardBytes = 8 * 1024 * 1024;
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

    private readonly record struct CacheHeader(
        long SourceLength,
        long SourceLastWriteTimeUtcTicks,
        long CanonicalLength,
        ulong CanonicalHash,
        ulong SourceFingerprintLow,
        ulong SourceFingerprintHigh,
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

