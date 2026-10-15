using System.Security.Cryptography;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed record RuntimeWorldCustodyCheckpoint(RuntimeWorldChestCustodySnapshot Custody,
    WorldPersistenceOwnerLease.Revision Revision);

/// <summary>Authoritative dependencies of the canonical world, not a disposable prepared-state cache.
/// All entry points require the lifetime logical-world lease; publication uses the existing atomic writer.</summary>
internal sealed class RuntimeWorldChestCustodyArtifacts(WorldPersistenceOwnerLease owner,
    WorldFileLoadLimits limits, int maximumWorldBytes)
{
    private const int AnchorLength = 8 + 16 + 4 + 32 + 32;
    private static ReadOnlySpan<byte> AnchorMagic => "TRWCM001"u8;
    private string DirectoryPath => owner.WorldPath + ".custody";
    private string AnchorPath => Path.Combine(DirectoryPath, "managed-v1.anchor");

    internal bool HasManagedAnchor => File.Exists(AnchorPath);

    internal async Task InitializeTrustedImportAsync(WorldFileData expectedWorld, bool explicitInitialImport,
        CancellationToken cancellationToken = default)
    {
        using var ownership = owner.EnterCheckpointWrite();
        if (!explicitInitialImport) throw new InvalidDataException("An explicit initial vanilla import is required.");
        if (File.Exists(AnchorPath)) throw new InvalidDataException("Managed custody cannot be reset as a new full import.");
        if (Directory.Exists(DirectoryPath) && Directory.EnumerateFileSystemEntries(DirectoryPath).Any())
            throw new InvalidDataException("Existing custody artifacts cannot be silently reset after a missing anchor.");
        var canonical = await ReadWorldAsync(owner.WorldPath, cancellationToken).ConfigureAwait(false);
        MatchWorld(expectedWorld, canonical.World);
        var full = RuntimeWorldChestCustodySnapshot.FullImport(canonical.World);
        await WriteArtifactAsync(full, canonical.World, canonical.Hash, cancellationToken).ConfigureAwait(false);
        byte[] anchor = new byte[AnchorLength];
        AnchorMagic.CopyTo(anchor);
        canonical.World.Header.UniqueId.TryWriteBytes(anchor.AsSpan(8, 16));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(anchor.AsSpan(24, 4), canonical.World.Header.WorldId);
        canonical.Hash.CopyTo(anchor, 28);
        SHA256.HashData(anchor.AsSpan(0, AnchorLength - 32)).CopyTo(anchor, AnchorLength - 32);
        owner.EnsureHeld();
        await AtomicSaveFileWriter.WriteAsync(AnchorPath,
            (stream, token) => stream.WriteAsync(anchor, token).AsTask(), cancellationToken).ConfigureAwait(false);
        ValidateAnchor(canonical.World);
    }

    internal async Task<RuntimeWorldChestCustodySnapshot> ReadManagedAsync(WorldFileData expectedWorld,
        CancellationToken cancellationToken = default)
    {
        owner.EnsureHeld();
        ValidateAnchor(expectedWorld);
        var canonical = await ReadWorldAsync(owner.WorldPath, cancellationToken).ConfigureAwait(false);
        MatchWorld(expectedWorld, canonical.World);
        string path = ArtifactPath(canonical.World.Envelope.Revision, canonical.Hash);
        byte[] bytes = await ReadBoundedAsync(path, RuntimeWorldChestCustodyCodec.MaximumEncodedBytes, cancellationToken).ConfigureAwait(false);
        owner.EnsureHeld();
        return RuntimeWorldChestCustodyCodec.Decode(bytes, expectedWorld, canonical.Hash);
    }

    internal async Task ValidateRecoveryCandidateAsync(string candidatePath, CancellationToken token = default)
    {
        owner.EnsureHeld();
        var candidate = await ReadWorldAsync(candidatePath, token).ConfigureAwait(false);
        ValidateAnchor(candidate.World);
        _ = await ReadGenerationAsync(candidate.World, candidate.Hash, token).ConfigureAwait(false);
        owner.EnsureHeld();
    }

    internal async Task ValidateRecoveryBytesAsync(WorldFileData world, byte[] bytes, CancellationToken token)
    {
        owner.EnsureHeld();
        ValidateAnchor(world);
        byte[] hash = SHA256.HashData(bytes);
        string path = ArtifactPath(world.Envelope.Revision, hash);
        byte[] custody = await ReadBoundedAsync(path, RuntimeWorldChestCustodyCodec.MaximumEncodedBytes, token).ConfigureAwait(false);
        _ = RuntimeWorldChestCustodyCodec.Decode(custody, world, hash);
        owner.EnsureHeld();
    }

    internal async Task PrepareCandidateAsync(RuntimeWorldCustodyCheckpoint checkpoint, string candidatePath,
        CancellationToken cancellationToken)
    {
        owner.EnsureCheckpointWrite();
        var revision = checkpoint.Revision;
        owner.ValidateRevision(in revision);
        if (checkpoint.Custody.Revision != checkpoint.Revision.Value)
            throw new InvalidDataException("Snapshot custody and reserved revision disagree.");
        var candidate = await ReadWorldAsync(candidatePath, cancellationToken).ConfigureAwait(false);
        ValidateAnchor(candidate.World);
        var canonical = await ReadWorldAsync(owner.WorldPath, cancellationToken).ConfigureAwait(false);
        if (candidate.World.Envelope.Revision != checkpoint.Revision.Value ||
            candidate.World.Envelope.Revision <= canonical.World.Envelope.Revision)
            throw new InvalidDataException("Canonical checkpoint revision must advance without wrap or replay.");
        if (candidate.World.Header.UniqueId != canonical.World.Header.UniqueId ||
            candidate.World.Header.WorldId != canonical.World.Header.WorldId)
            throw new InvalidDataException("Candidate changed the managed world identity.");
        owner.ValidateRevision(in revision);
        await PruneGenerationsAsync(candidate.World.Envelope.Revision, candidate.Hash, cancellationToken).ConfigureAwait(false);
        await WriteArtifactAsync(checkpoint.Custody, candidate.World, candidate.Hash, cancellationToken).ConfigureAwait(false);
        owner.EnsureHeld();
    }

    // Background maintenance shares the same lifetime lease and structured write scope.
    // At most 64 filenames are inspected in one transaction; a larger/foreign directory
    // fails closed for operator review instead of becoming an unbounded startup sweep.
    internal async Task PruneCommittedAsync(CancellationToken token = default) =>
        await PruneGenerationsAsync(null, null, token).ConfigureAwait(false);

    private async Task PruneGenerationsAsync(uint? preparedRevision, byte[]? preparedHash, CancellationToken token)
    {
        owner.EnsureCheckpointWrite();
        var canonical = await ReadWorldAsync(owner.WorldPath, token).ConfigureAwait(false);
        ValidateAnchor(canonical.World);
        await ReadGenerationAsync(canonical.World, canonical.Hash, token).ConfigureAwait(false);
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ArtifactPath(canonical.World.Envelope.Revision, canonical.Hash)
        };
        string backupPath = owner.WorldPath + ".bak";
        if (File.Exists(backupPath))
        {
            var backup = await ReadWorldAsync(backupPath, token).ConfigureAwait(false);
            ValidateAnchor(backup.World);
            await ReadGenerationAsync(backup.World, backup.Hash, token).ConfigureAwait(false);
            keep.Add(ArtifactPath(backup.World.Envelope.Revision, backup.Hash));
        }
        if (preparedRevision.HasValue && preparedHash is not null)
            keep.Add(ArtifactPath(preparedRevision.Value, preparedHash));
        foreach (string dependency in keep)
            if (!File.Exists(dependency) && (preparedRevision is null || dependency != ArtifactPath(preparedRevision.Value, preparedHash!)))
                throw new InvalidDataException("Current or backup custody dependency is missing; retention cannot proceed.");
        var removable = new List<string>();
        int inspected = 0;
        foreach (string path in Directory.EnumerateFiles(DirectoryPath, "generation-*", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();
            if (++inspected > 64) throw new InvalidDataException("Custody generation retention exceeds its bounded directory budget.");
            string name = Path.GetFileName(path);
            if (name.Length != 88 || !name.StartsWith("generation-", StringComparison.Ordinal) || name[19] != '-' ||
                !name.EndsWith(".bin", StringComparison.Ordinal) || !uint.TryParse(name.AsSpan(11, 8), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out _) || !IsHexHash(name.AsSpan(20, 64)))
                throw new InvalidDataException("Unclassified generation artifact requires explicit operator review.");
            if (!keep.Contains(path)) removable.Add(path);
        }
        owner.EnsureCheckpointWrite();
        foreach (string path in removable) File.Delete(path);
    }

    private async Task<RuntimeWorldChestCustodySnapshot> ReadGenerationAsync(WorldFileData world, byte[] hash, CancellationToken token)
    {
        byte[] bytes = await ReadBoundedAsync(ArtifactPath(world.Envelope.Revision, hash),
            RuntimeWorldChestCustodyCodec.MaximumEncodedBytes, token).ConfigureAwait(false);
        owner.EnsureHeld();
        return RuntimeWorldChestCustodyCodec.Decode(bytes, world, hash);
    }

    private static bool IsHexHash(ReadOnlySpan<char> hash)
    {
        foreach (char digit in hash) if (!Uri.IsHexDigit(digit)) return false;
        return true;
    }

    internal string ArtifactPath(uint revision, ReadOnlySpan<byte> hash) =>
        GetArtifactPath(owner.WorldPath, revision, hash);

    internal static string GetArtifactPath(string worldPath, uint revision, ReadOnlySpan<byte> hash) =>
        Path.Combine(worldPath + ".custody", $"generation-{revision:X8}-{Convert.ToHexString(hash)}.bin");

    private async Task WriteArtifactAsync(RuntimeWorldChestCustodySnapshot snapshot, WorldFileData world, byte[] hash,
        CancellationToken cancellationToken)
    {
        owner.EnsureHeld();
        Directory.CreateDirectory(DirectoryPath);
        byte[] encoded = RuntimeWorldChestCustodyCodec.Encode(snapshot, hash);
        _ = RuntimeWorldChestCustodyCodec.Decode(encoded, world, hash);
        string path = ArtifactPath(snapshot.Revision, hash);
        if (File.Exists(path))
        {
            byte[] existing = await ReadBoundedAsync(path, RuntimeWorldChestCustodyCodec.MaximumEncodedBytes, cancellationToken).ConfigureAwait(false);
            if (!existing.AsSpan().SequenceEqual(encoded))
                throw new InvalidDataException("Immutable custody generation conflicts with existing metadata.");
            return;
        }
        owner.EnsureHeld();
        var options = new AtomicSaveFileWriterOptions(ValidateCandidateAsync: async (candidate, token) =>
        {
            byte[] bytes = await ReadBoundedAsync(candidate, RuntimeWorldChestCustodyCodec.MaximumEncodedBytes, token).ConfigureAwait(false);
            _ = RuntimeWorldChestCustodyCodec.Decode(bytes, world, hash);
            owner.EnsureHeld();
        });
        await AtomicSaveFileWriter.WriteAsync(path, (stream, token) => stream.WriteAsync(encoded, token).AsTask(),
            options, cancellationToken).ConfigureAwait(false);
    }

    private void ValidateAnchor(WorldFileData world)
    {
        owner.EnsureHeld();
        ValidateAnchor(AnchorPath, world);
    }

    internal static void ValidateAnchor(string anchorPath, WorldFileData world)
    {
        // Missing authority is never inferred as an initial-import request.
        using var stream = new FileStream(anchorPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != AnchorLength) throw new InvalidDataException("Managed custody anchor length is invalid.");
        byte[] bytes = new byte[AnchorLength];
        stream.ReadExactly(bytes);
        if (!bytes.AsSpan(0, 8).SequenceEqual(AnchorMagic) ||
            new Guid(bytes.AsSpan(8, 16)) != world.Header.UniqueId ||
            System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24, 4)) != world.Header.WorldId ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes.AsSpan(0, AnchorLength - 32)), bytes.AsSpan(AnchorLength - 32)))
            throw new InvalidDataException("Managed custody anchor identity or checksum is invalid.");
    }

    private async Task<(WorldFileData World, byte[] Hash)> ReadWorldAsync(string path, CancellationToken token)
    {
        if (maximumWorldBytes < 1) throw new InvalidDataException("World byte bound is invalid.");
        byte[] bytes = await ReadBoundedAsync(path, maximumWorldBytes, token).ConfigureAwait(false);
        var diagnostic = WorldFileLoader.TryLoad(bytes, limits, out var world);
        if (!diagnostic.IsLoaded || world is null) throw new InvalidDataException("Custody world binding is not a complete supported world.");
        return (world, SHA256.HashData(bytes));
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, int maximum, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length < 1 || stream.Length > maximum) throw new InvalidDataException("Custody input exceeds its configured byte bound.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Custody input grew while reading.");
        return bytes;
    }

    private static void MatchWorld(WorldFileData expected, WorldFileData actual)
    {
        if (expected.Header.UniqueId != actual.Header.UniqueId || expected.Header.WorldId != actual.Header.WorldId ||
            expected.Envelope.Revision != actual.Envelope.Revision || expected.Chests.Length != actual.Chests.Length)
            throw new InvalidDataException("Loaded world does not match the canonical custody source.");
        for (int i = 0; i < expected.Chests.Length; i++)
        {
            var left = expected.Chests[i]; var right = actual.Chests[i];
            if (left.SlotId != right.SlotId || left.X != right.X || left.Y != right.Y || left.Name != right.Name ||
                !left.Items.AsSpan().SequenceEqual(right.Items))
                throw new InvalidDataException("Loaded chest projection does not match the canonical source.");
        }
    }
}
