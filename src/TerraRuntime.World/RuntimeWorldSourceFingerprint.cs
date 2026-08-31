using System.Buffers.Binary;
using System.Security.Cryptography;

namespace TerraRuntime.World;

/// <summary>
/// Strong content fingerprint for deciding whether a disposable runtime image still belongs to the canonical .wld.
/// The stored digest is the first 128 bits of SHA-256: enough to make accidental or practical collision-based stale
/// cache acceptance negligible while fitting the runtime-cache header's existing reserved bytes.
/// </summary>
public readonly record struct RuntimeWorldSourceFingerprint(
    long Length,
    long LastWriteTimeUtcTicks,
    ulong HashLow,
    ulong HashHigh)
{
    private const int DigestBytes = 32;
    internal const int StoredHashBytes = 16;
    private const int IoBufferSize = 64 * 1024;
    private const int StableReadAttempts = 2;

    public static bool TryCapture(string worldPath, out RuntimeWorldSourceFingerprint fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);

        for (int attempt = 0; attempt < StableReadAttempts; attempt++)
        {
            try
            {
                var before = new FileInfo(worldPath);
                before.Refresh();
                if (!before.Exists || before.Length <= 0)
                {
                    fingerprint = default;
                    return false;
                }

                byte[] digest;
                using (var stream = new FileStream(
                    worldPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    IoBufferSize,
                    FileOptions.SequentialScan))
                {
                    if (stream.Length != before.Length)
                        continue;

                    digest = SHA256.HashData(stream);
                }

                var after = new FileInfo(worldPath);
                after.Refresh();
                if (!after.Exists ||
                    after.Length != before.Length ||
                    after.LastWriteTimeUtc.Ticks != before.LastWriteTimeUtc.Ticks)
                {
                    continue;
                }

                fingerprint = FromDigest(
                    before.Length,
                    before.LastWriteTimeUtc.Ticks,
                    digest);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                fingerprint = default;
                return false;
            }
        }

        fingerprint = default;
        return false;
    }

    internal static RuntimeWorldSourceFingerprint FromBytes(
        ReadOnlySpan<byte> source,
        RuntimeWorldSourceStamp sourceStamp)
    {
        Span<byte> digest = stackalloc byte[DigestBytes];
        SHA256.HashData(source, digest);
        return FromDigest(sourceStamp.Length, sourceStamp.LastWriteTimeUtcTicks, digest);
    }

    internal void WriteHash(Span<byte> destination)
    {
        if (destination.Length < StoredHashBytes)
            throw new ArgumentException("The fingerprint destination is too small.", nameof(destination));

        BinaryPrimitives.WriteUInt64LittleEndian(destination, HashLow);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], HashHigh);
    }

    private static RuntimeWorldSourceFingerprint FromDigest(
        long length,
        long lastWriteTimeUtcTicks,
        ReadOnlySpan<byte> digest)
    {
        if (digest.Length < StoredHashBytes)
            throw new ArgumentException("The SHA-256 digest is truncated.", nameof(digest));

        return new RuntimeWorldSourceFingerprint(
            length,
            lastWriteTimeUtcTicks,
            BinaryPrimitives.ReadUInt64LittleEndian(digest),
            BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]));
    }
}
