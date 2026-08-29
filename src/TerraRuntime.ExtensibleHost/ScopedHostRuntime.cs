using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.HostContracts;

namespace TerraRuntime.ExtensibleHost;

internal sealed class ScopedHostRuntime : ITerraRuntimeHostRuntime
{
    private readonly ITerraRuntimeHostRuntime source;
    private readonly ScopedNpcActorOperations npcActors;
    private readonly ScopedNpcShopOperations npcShops;
    private int retired;

    public ScopedHostRuntime(ITerraRuntimeHostRuntime source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        npcActors = new ScopedNpcActorOperations(source.NpcActors);
        npcShops = new ScopedNpcShopOperations(source.NpcShops);
    }

    public TerraRuntimeHostRuntimeInfo Info => source.Info;
    public IInterestManagementControl InterestManagement => source.InterestManagement;
    public IPlayerStateSnapshotReader PlayerStates => source.PlayerStates;
    public INpcActorOperations NpcActors => npcActors;
    public INpcShopOperations NpcShops => npcShops;
    public IServerPlayerOperations ServerPlayers => source.ServerPlayers;

    public async ValueTask RetireAsync()
    {
        if (Interlocked.Exchange(ref retired, 1) != 0)
            return;

        List<Exception>? failures = null;
        try
        {
            npcShops.Dispose();
        }
        catch (Exception exception)
        {
            failures = [exception];
        }

        try
        {
            await npcActors.RetireAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures ??= [];
            failures.Add(exception);
        }

        if (failures is { Count: > 0 })
            throw new AggregateException("Trusted host runtime scope retirement failed.", failures);
    }

    private sealed class ScopedNpcActorOperations(INpcActorOperations source) : INpcActorOperations
    {
        private readonly object gate = new();
        private readonly HashSet<ActorControllerId> controllers = [];
        private readonly HashSet<NpcHandle> spawnedActors = [];
        private readonly List<INpcArchetypeRegistration> archetypeRegistrations = [];
        private bool retired;

        public NpcArchetypeRegistrationStatus TryRegisterArchetype(
            NpcArchetypeDescriptor descriptor,
            out INpcArchetypeRegistration? registration)
        {
            lock (gate)
            {
                if (retired)
                {
                    registration = null;
                    return NpcArchetypeRegistrationStatus.RuntimeDetached;
                }

                NpcArchetypeRegistrationStatus result = source.TryRegisterArchetype(descriptor, out registration);
                if (result == NpcArchetypeRegistrationStatus.Registered && registration is not null)
                    archetypeRegistrations.Add(registration);
                return result;
            }
        }

        public async ValueTask<NpcActorSpawnResult> SpawnAsync(
            NpcActorSpawnRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                if (retired)
                    return new NpcActorSpawnResult(NpcActorSpawnStatus.QueueRejected, default);
            }

            NpcActorSpawnResult result = await source.SpawnAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSpawned)
                return result;

            lock (gate)
            {
                if (!retired)
                {
                    spawnedActors.Add(result.Npc);
                    return result;
                }
            }

            await source.DespawnAsync(result.Npc, CancellationToken.None).ConfigureAwait(false);
            return new NpcActorSpawnResult(NpcActorSpawnStatus.QueueRejected, default);
        }

        public async ValueTask<bool> DespawnAsync(
            NpcHandle npc,
            CancellationToken cancellationToken = default)
        {
            if (IsRetired)
                return false;

            bool despawned = await source.DespawnAsync(npc, cancellationToken).ConfigureAwait(false);
            if (despawned)
            {
                lock (gate)
                    spawnedActors.Remove(npc);
            }

            return despawned;
        }

        public async ValueTask<NpcActorAcquireStatus> AcquireAsync(
            NpcHandle npc,
            ActorControllerId controllerId,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                if (retired)
                    return NpcActorAcquireStatus.QueueRejected;
            }

            NpcActorAcquireStatus status = await source
                .AcquireAsync(npc, controllerId, cancellationToken)
                .ConfigureAwait(false);
            if (status != NpcActorAcquireStatus.Acquired)
                return status;

            lock (gate)
            {
                if (!retired)
                {
                    controllers.Add(controllerId);
                    return status;
                }
            }

            await source.ReleaseAsync(npc, controllerId, CancellationToken.None).ConfigureAwait(false);
            return NpcActorAcquireStatus.QueueRejected;
        }

        public ValueTask<bool> SetIntentAsync(
            NpcHandle npc,
            ActorControllerId controllerId,
            NpcActorIntent intent,
            CancellationToken cancellationToken = default) =>
            IsRetired
                ? ValueTask.FromResult(false)
                : source.SetIntentAsync(npc, controllerId, intent, cancellationToken);

        public ValueTask<bool> ReleaseAsync(
            NpcHandle npc,
            ActorControllerId controllerId,
            CancellationToken cancellationToken = default) =>
            IsRetired
                ? ValueTask.FromResult(false)
                : source.ReleaseAsync(npc, controllerId, cancellationToken);

        public async ValueTask<int> ReleaseControllerAsync(
            ActorControllerId controllerId,
            CancellationToken cancellationToken = default)
        {
            if (IsRetired)
                return 0;

            int released = await source
                .ReleaseControllerAsync(controllerId, cancellationToken)
                .ConfigureAwait(false);
            lock (gate)
                controllers.Remove(controllerId);
            return released;
        }

        public async ValueTask RetireAsync()
        {
            ActorControllerId[] capturedControllers;
            NpcHandle[] capturedActors;
            INpcArchetypeRegistration[] capturedArchetypes;
            lock (gate)
            {
                if (retired)
                    return;

                retired = true;
                capturedControllers = controllers.ToArray();
                capturedActors = spawnedActors.ToArray();
                capturedArchetypes = archetypeRegistrations.ToArray();
                controllers.Clear();
                spawnedActors.Clear();
                archetypeRegistrations.Clear();
            }

            List<Exception>? failures = null;
            foreach (ActorControllerId controllerId in capturedControllers)
            {
                try
                {
                    await source
                        .ReleaseControllerAsync(controllerId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures ??= [];
                    failures.Add(exception);
                }
            }

            foreach (NpcHandle npc in capturedActors)
            {
                try
                {
                    await source.DespawnAsync(npc, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures ??= [];
                    failures.Add(exception);
                }
            }

            foreach (INpcArchetypeRegistration registration in capturedArchetypes)
            {
                try
                {
                    registration.Dispose();
                }
                catch (Exception exception)
                {
                    failures ??= [];
                    failures.Add(exception);
                }
            }

            if (failures is { Count: > 0 })
                throw new AggregateException("NPC actor-controller retirement failed.", failures);
        }

        private bool IsRetired
        {
            get
            {
                lock (gate)
                    return retired;
            }
        }
    }

    private sealed class ScopedNpcShopOperations(INpcShopOperations source) : INpcShopOperations, IDisposable
    {
        private readonly object gate = new();
        private readonly List<INpcShopRegistration> registrations = [];
        private bool disposed;

        public NpcShopRegistrationStatus TryRegister(
            NpcShopCatalog catalog,
            out INpcShopRegistration? registration)
        {
            lock (gate)
            {
                if (disposed)
                {
                    registration = null;
                    return NpcShopRegistrationStatus.RuntimeDetached;
                }

                NpcShopRegistrationStatus result = source.TryRegister(catalog, out registration);
                if (result == NpcShopRegistrationStatus.Registered && registration is not null)
                    registrations.Add(registration);
                return result;
            }
        }

        public void Dispose()
        {
            INpcShopRegistration[] captured;
            lock (gate)
            {
                if (disposed)
                    return;

                disposed = true;
                captured = registrations.ToArray();
                registrations.Clear();
            }

            List<Exception>? failures = null;
            foreach (INpcShopRegistration registration in captured)
            {
                try
                {
                    registration.Dispose();
                }
                catch (Exception exception)
                {
                    failures ??= [];
                    failures.Add(exception);
                }
            }

            if (failures is { Count: > 0 })
                throw new AggregateException("NPC shop-registration retirement failed.", failures);
        }
    }
}
