using TerraRuntime.Core.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Application;

/// <summary>The lifetime canonical-world persistence owner. The mutable chest slot store remains the
/// funding owner; this object binds its detached generations to the existing checkpoint transaction.</summary>
internal sealed class RuntimeManagedWorldCustody : IDisposable
{
    internal const int MaximumWorldBytes = 1024 * 1024 * 1024;
    internal WorldPersistenceOwnerLease Owner { get; }
    internal RuntimeWorldChestCustodyArtifacts Artifacts { get; }
    internal RuntimeWorldChestCustodySnapshot Loaded { get; }
    private long retentionFailures;
    internal long RetentionFailures => Interlocked.Read(ref retentionFailures);

    private RuntimeManagedWorldCustody(WorldPersistenceOwnerLease owner, RuntimeWorldChestCustodyArtifacts artifacts,
        RuntimeWorldChestCustodySnapshot loaded)
    {
        Owner = owner;
        Artifacts = artifacts;
        Loaded = loaded;
    }

    internal static async Task<RuntimeManagedWorldCustody> OpenAsync(WorldPersistenceOwnerLease owner,
        WorldFileData world, WorldFileLoadLimits limits, bool explicitInitialImport, CancellationToken token = default)
    {
        var artifacts = new RuntimeWorldChestCustodyArtifacts(owner, limits, MaximumWorldBytes);
        if (!artifacts.HasManagedAnchor && explicitInitialImport)
            await artifacts.InitializeTrustedImportAsync(world, true, token).ConfigureAwait(false);
        var loaded = await artifacts.ReadManagedAsync(world, token).ConfigureAwait(false);
        owner.InitializeRevision(world.Envelope.Revision);
        return new(owner, artifacts, loaded);
    }

    internal RuntimeWorldCustodyCheckpoint Capture(RuntimeChestStore store)
    {
        var revision = Owner.ReserveNextRevision();
        var snapshot = store.CaptureCustodySnapshot(Loaded.WorldUniqueId, Loaded.WorldId, revision.Value);
        return new(snapshot, revision);
    }

    internal void OnCommitted()
    {
        // Canonical and dependency publication already succeeded. A maintenance failure
        // must not relabel that committed generation as a failed/refunded world save.
        try { Artifacts.PruneCommittedAsync().GetAwaiter().GetResult(); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            Interlocked.Increment(ref retentionFailures);
        }
    }

    public void Dispose() => Owner.Dispose();
}
