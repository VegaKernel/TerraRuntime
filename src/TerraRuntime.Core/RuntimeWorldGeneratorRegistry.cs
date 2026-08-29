using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Core;

public enum WorldGeneratorRegistrationResult : byte
{
    Registered = 0,
    DuplicateId = 1,
    InvalidProvider = 2
}

public interface IWorldGeneratorRegistrationLease : IDisposable
{
    WorldGeneratorId Id { get; }
    bool IsRetired { get; }
}

public readonly record struct RuntimeWorldGeneratorEntry(
    WorldGeneratorId Id,
    IWorldGenerationProvider Provider);

/// <summary>
/// Immutable, deterministic view of the currently registered world generators. A generation job captures a
/// provider from one snapshot before execution, so retiring a registration never invalidates a job already running
/// against an isolated candidate workspace.
/// </summary>
public sealed class RuntimeWorldGeneratorSnapshot
{
    private readonly RuntimeWorldGeneratorEntry[] entries;

    internal RuntimeWorldGeneratorSnapshot(ulong revision, RuntimeWorldGeneratorEntry[] entries)
    {
        Revision = revision;
        this.entries = entries;
    }

    public ulong Revision { get; }
    public int Count => entries.Length;
    public ReadOnlyMemory<RuntimeWorldGeneratorEntry> Entries => entries;

    public bool TryResolve(WorldGeneratorId id, out IWorldGenerationProvider? provider)
    {
        if (!id.IsAssigned)
        {
            provider = null;
            return false;
        }

        int low = 0;
        int high = entries.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            RuntimeWorldGeneratorEntry entry = entries[middle];
            int comparison = entry.Id.CompareTo(id);
            if (comparison == 0)
            {
                provider = entry.Provider;
                return true;
            }

            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        provider = null;
        return false;
    }
}

/// <summary>
/// Explicit AOT-safe registry for selectable world-generation providers. Registration uses concrete instances and
/// stable IDs only: TerraRuntime never scans assemblies or plugin directories to discover generators.
/// </summary>
public sealed class RuntimeWorldGeneratorRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<WorldGeneratorId, IWorldGenerationProvider> providers = [];
    private RuntimeWorldGeneratorSnapshot published = new(0, []);
    private ulong nextRevision;

    public RuntimeWorldGeneratorSnapshot Snapshot => Volatile.Read(ref published);

    public WorldGeneratorRegistrationResult TryRegister(
        IWorldGenerationProvider provider,
        out IWorldGeneratorRegistrationLease? lease)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lease = null;

        WorldGeneratorId id = provider.Id;
        if (!id.IsAssigned)
            return WorldGeneratorRegistrationResult.InvalidProvider;

        lock (gate)
        {
            if (providers.ContainsKey(id))
                return WorldGeneratorRegistrationResult.DuplicateId;

            providers.Add(id, provider);
            PublishLocked();
            lease = new RegistrationLease(this, id, provider);
            return WorldGeneratorRegistrationResult.Registered;
        }
    }

    public bool TryResolve(WorldGeneratorId id, out IWorldGenerationProvider? provider) =>
        Snapshot.TryResolve(id, out provider);

    private void Retire(WorldGeneratorId id, IWorldGenerationProvider provider)
    {
        lock (gate)
        {
            if (!providers.TryGetValue(id, out IWorldGenerationProvider? current) ||
                !ReferenceEquals(current, provider))
            {
                return;
            }

            providers.Remove(id);
            PublishLocked();
        }
    }

    private bool IsRetired(WorldGeneratorId id, IWorldGenerationProvider provider)
    {
        lock (gate)
            return !providers.TryGetValue(id, out IWorldGenerationProvider? current) || !ReferenceEquals(current, provider);
    }

    private void PublishLocked()
    {
        RuntimeWorldGeneratorEntry[] entries = providers
            .OrderBy(static pair => pair.Key)
            .Select(static pair => new RuntimeWorldGeneratorEntry(pair.Key, pair.Value))
            .ToArray();

        ulong revision = nextRevision == ulong.MaxValue ? 1 : nextRevision + 1;
        nextRevision = revision;
        Volatile.Write(ref published, new RuntimeWorldGeneratorSnapshot(revision, entries));
    }

    private sealed class RegistrationLease : IWorldGeneratorRegistrationLease
    {
        private readonly RuntimeWorldGeneratorRegistry owner;
        private readonly IWorldGenerationProvider provider;
        private int disposed;

        public RegistrationLease(
            RuntimeWorldGeneratorRegistry owner,
            WorldGeneratorId id,
            IWorldGenerationProvider provider)
        {
            this.owner = owner;
            this.provider = provider;
            Id = id;
        }

        public WorldGeneratorId Id { get; }

        public bool IsRetired => Volatile.Read(ref disposed) != 0 && owner.IsRetired(Id, provider);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                owner.Retire(Id, provider);
        }
    }
}
