using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.HostContracts.WorldGeneration;

namespace TerraRuntime.ExtensibleHost;

/// <summary>
/// CoreCLR-host adapter over TerraRuntime's explicit provider registry. Each loaded host module receives a scoped
/// view so every registration is retired before that module's collectible AssemblyLoadContext is unloaded.
/// </summary>
internal sealed class HostWorldGeneratorRegistry :
    ITerraRuntimeWorldGeneratorRegistry,
    ITerraRuntimeWorldGeneratorSource
{
    private readonly RuntimeWorldGeneratorRegistry registry = new();

    public TerraRuntimeWorldGeneratorRegistrationResult TryRegister(
        IWorldGenerationProvider provider,
        out ITerraRuntimeWorldGeneratorRegistration? registration)
    {
        WorldGeneratorRegistrationResult result = registry.TryRegister(
            provider,
            out IWorldGeneratorRegistrationLease? lease);

        registration = result == WorldGeneratorRegistrationResult.Registered && lease is not null
            ? new Registration(lease)
            : null;

        return result switch
        {
            WorldGeneratorRegistrationResult.Registered => TerraRuntimeWorldGeneratorRegistrationResult.Registered,
            WorldGeneratorRegistrationResult.DuplicateId => TerraRuntimeWorldGeneratorRegistrationResult.DuplicateId,
            _ => TerraRuntimeWorldGeneratorRegistrationResult.InvalidProvider
        };
    }

    public ReadOnlyMemory<WorldGeneratorId> CaptureWorldGeneratorIds()
    {
        ReadOnlySpan<RuntimeWorldGeneratorEntry> entries = registry.Snapshot.Entries.Span;
        if (entries.IsEmpty)
            return ReadOnlyMemory<WorldGeneratorId>.Empty;

        var ids = new WorldGeneratorId[entries.Length];
        for (int index = 0; index < entries.Length; index++)
            ids[index] = entries[index].Id;
        return ids;
    }

    public bool TryResolveWorldGenerator(
        WorldGeneratorId id,
        out IWorldGenerationProvider? provider) =>
        registry.TryResolve(id, out provider);

    public Scope CreateScope() => new(this);

    internal sealed class Scope : ITerraRuntimeWorldGeneratorRegistry, IDisposable
    {
        private readonly HostWorldGeneratorRegistry owner;
        private readonly object gate = new();
        private readonly List<ITerraRuntimeWorldGeneratorRegistration> registrations = [];
        private bool disposed;

        public Scope(HostWorldGeneratorRegistry owner) => this.owner = owner;

        public TerraRuntimeWorldGeneratorRegistrationResult TryRegister(
            IWorldGenerationProvider provider,
            out ITerraRuntimeWorldGeneratorRegistration? registration)
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);

                TerraRuntimeWorldGeneratorRegistrationResult result = owner.TryRegister(provider, out registration);
                if (result == TerraRuntimeWorldGeneratorRegistrationResult.Registered && registration is not null)
                    registrations.Add(registration);
                return result;
            }
        }

        public void Dispose()
        {
            ITerraRuntimeWorldGeneratorRegistration[] snapshot;
            lock (gate)
            {
                if (disposed)
                    return;

                disposed = true;
                snapshot = registrations.ToArray();
                registrations.Clear();
            }

            for (int index = snapshot.Length - 1; index >= 0; index--)
                snapshot[index].Dispose();
        }
    }

    private sealed class Registration : ITerraRuntimeWorldGeneratorRegistration
    {
        private readonly IWorldGeneratorRegistrationLease lease;

        public Registration(IWorldGeneratorRegistrationLease lease) => this.lease = lease;

        public WorldGeneratorId Id => lease.Id;
        public bool IsRetired => lease.IsRetired;
        public void Dispose() => lease.Dispose();
    }
}
