using System.IO.Hashing;

namespace TerraRuntime.World;

/// <summary>
/// Fast non-cryptographic integrity hash for disposable runtime snapshot payloads.
/// Snapshot files are local cache/checkpoint data, so corruption detection matters while
/// cryptographic collision resistance does not justify an extra SHA-256 pass over hundreds of MiB.
/// </summary>
internal static class RuntimeWorldIntegrity
{
    public static ulong Hash64(ReadOnlySpan<byte> data) => XxHash3.HashToUInt64(data);
}
