using System.Buffers.Binary;

namespace TerraRuntime.Core.Worlds;

/// <summary>Lifetime exclusion for one normalized logical world destination. The persistent lock file is not
/// custody metadata; only the live exclusive handle confers ownership. Do not unlink it on release.</summary>
internal sealed class WorldPersistenceOwnerLease : IDisposable
{
    private readonly object gate = new();
    private FileStream? handle;
    private int activeWrites;
    private uint sourceRevision;
    private uint lastReservedRevision;
    private bool revisionInitialized;
    private int ownerThread;

    private WorldPersistenceOwnerLease(string worldPath, FileStream handle)
    {
        WorldPath = worldPath;
        this.handle = handle;
    }

    internal string WorldPath { get; }
    internal bool IsHeld => Volatile.Read(ref handle) is not null;

    internal static WorldPersistenceOwnerLease Acquire(string worldPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldPath);
        string normalized = Path.GetFullPath(worldPath);
        string directory = Path.GetDirectoryName(normalized)
            ?? throw new ArgumentException("World destination has no directory.", nameof(worldPath));
        Directory.CreateDirectory(directory);
        var stream = new FileStream(normalized + ".custody-owner.lease", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough);
        try
        {
            Span<byte> diagnostic = stackalloc byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(diagnostic, 1);
            BinaryPrimitives.WriteInt32LittleEndian(diagnostic[4..], Environment.ProcessId);
            stream.SetLength(0);
            stream.Write(diagnostic);
            stream.Flush(flushToDisk: true);
            return new(normalized, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    internal void InitializeRevision(uint loadedRevision)
    {
        EnsureHeld();
        if (revisionInitialized) throw new InvalidOperationException("World revision is already initialized.");
        sourceRevision = lastReservedRevision = loadedRevision;
        revisionInitialized = true;
    }

    internal Revision ReserveNextRevision()
    {
        EnsureHeld();
        if (!revisionInitialized) throw new InvalidOperationException("Loaded world revision is not initialized.");
        int currentThread = Environment.CurrentManagedThreadId;
        int captured = Interlocked.CompareExchange(ref ownerThread, currentThread, 0);
        if (captured != 0 && captured != currentThread)
            throw new InvalidOperationException("Checkpoint revision reservation belongs to the authoritative owner.");
        if (lastReservedRevision == uint.MaxValue)
            throw new InvalidOperationException("World checkpoint revision is exhausted.");
        return new(this, ++lastReservedRevision);
    }

    // Called only at the existing final-save boundary after the authoritative loop has stopped.
    // No new ordinary capture may run on its previous thread after this explicit handoff.
    internal void TransferRevisionCaptureAfterOwnerStopped()
    {
        EnsureHeld();
        Interlocked.Exchange(ref ownerThread, Environment.CurrentManagedThreadId);
    }

    internal void ValidateRevision(in Revision revision)
    {
        EnsureHeld();
        if (!revisionInitialized || !revision.BelongsTo(this) || revision.Value <= sourceRevision ||
            revision.Value > Volatile.Read(ref lastReservedRevision))
            throw new InvalidOperationException("Checkpoint revision does not belong to this lifetime owner.");
    }

    internal void EnsureHeld()
    {
        ObjectDisposedException.ThrowIf(!IsHeld, this);
    }

    internal void EnsureCheckpointWrite()
    {
        lock (gate)
        {
            EnsureHeld();
            if (activeWrites != 1) throw new InvalidOperationException("Custody publication requires the structured world write scope.");
        }
    }

    internal IDisposable EnterCheckpointWrite()
    {
        lock (gate)
        {
            EnsureHeld();
            if (activeWrites != 0)
                throw new InvalidOperationException("A logical world already has an active checkpoint writer.");
            activeWrites++;
            return new WriteScope(this);
        }
    }

    public void Dispose()
    {
        FileStream? released;
        lock (gate)
        {
            if (handle is null) return;
            if (activeWrites != 0)
                throw new InvalidOperationException("Complete checkpoint writes before releasing world ownership.");
            released = handle;
            handle = null;
        }
        released.Dispose();
    }

    private void EndCheckpointWrite()
    {
        lock (gate) activeWrites--;
    }

    private sealed class WriteScope(WorldPersistenceOwnerLease owner) : IDisposable
    {
        private WorldPersistenceOwnerLease? held = owner;
        public void Dispose() => Interlocked.Exchange(ref held, null)?.EndCheckpointWrite();
    }

    internal readonly struct Revision(WorldPersistenceOwnerLease owner, uint value)
    {
        internal uint Value { get; } = value;
        internal bool BelongsTo(WorldPersistenceOwnerLease expected) => ReferenceEquals(owner, expected);
    }
}
