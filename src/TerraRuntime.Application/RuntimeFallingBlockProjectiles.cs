using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

/// <summary>Exact spawn provenance and bounded retained termination handoff. Client types confer no terrain authority.</summary>
internal sealed class RuntimeFallingBlockProjectiles : IProjectileTerminationCommitSink
{
    internal const int Capacity = RuntimeProjectileStore.MaximumProtocolAddressableCapacity;
    private readonly HashSet<ProjectileHandle> active = new(Capacity);
    private readonly Queue<ProjectileSnapshot> pending = new(Capacity);
    private readonly List<ProjectileHandle> displaced = new(Capacity);
    public int Count => active.Count + pending.Count;
    public bool CanSpawn => Count < Capacity;
    internal bool Contains(ProjectileHandle handle) => active.Contains(handle);
    public void Register(ProjectileHandle handle) => active.Add(handle);
    public void Forget(ProjectileHandle handle) => active.Remove(handle);
    public bool TryPeek(out ProjectileSnapshot value) => pending.TryPeek(out value);
    public void Complete() => pending.Dequeue();
    public void ForgetDisplaced(RuntimeProjectileStore store)
    {
        // Vanilla full-pool replacement has no Kill callback. Do not retain claims for dead generations.
        if (active.Count == 0) return;
        displaced.Clear();
        foreach (var handle in active) if (!store.TryGet(handle, out _)) displaced.Add(handle);
        foreach (var handle in displaced) active.Remove(handle);
    }
    internal bool TryPrepareSourceBirths(RuntimeProjectileStore.WorldSpawnPreparation world,
        out SourceBirthPreparation? preparation)
    {
        preparation = null;
        var capturedActive = active.ToArray();
        var capturedPending = pending.ToArray();
        var final = new HashSet<ProjectileHandle>();
        foreach (var handle in capturedActive)
            if (world.TryGetProjected(handle, out _)) final.Add(handle);
        foreach (var birth in world.Births)
            if (VanillaFallingBlock1458.TryGetTile(birth.Type, out _) &&
                world.TryGetProjected(birth.Handle, out var current) && current.Type == birth.Type) final.Add(birth.Handle);
        if (final.Count + capturedPending.Length > Capacity || !world.IsCurrentOwned) return false;
        preparation = new(this, world, capturedActive, capturedPending, final.ToArray());
        return preparation.IsCurrentOwned;
    }

    internal sealed class SourceBirthPreparation(RuntimeFallingBlockProjectiles owner,
        RuntimeProjectileStore.WorldSpawnPreparation world, ProjectileHandle[] beforeActive,
        ProjectileSnapshot[] beforePending, ProjectileHandle[] afterActive)
    {
        private bool adopted;
        private bool IsRegistryCurrent => !adopted && owner.active.SetEquals(beforeActive) &&
            owner.pending.SequenceEqual(beforePending);
        internal bool IsCurrentOwned => IsRegistryCurrent && world.IsCurrentOwned;
        internal bool TryAdoptUnpublished()
        {
            if (!IsRegistryCurrent || !world.IsAcceptedImageCurrent) return false;
            adopted = true;
            owner.active.Clear();
            owner.active.UnionWith(afterActive);
            return true;
        }
    }

    public void ProjectileTerminated(in ProjectileTerminationCommit termination)
    {
        if (!termination.CombatTrusted || !active.Remove(termination.FinalProjectile.Handle) ||
            !VanillaFallingBlock1458.TryGetTile(termination.FinalProjectile.Type, out _)) return;
        // Capacity includes active AND pending; item pressure retains the event and prevents further spawns.
        if (termination.Reason != ProjectileSimulationTerminationReason.WorldBounds) pending.Enqueue(termination.FinalProjectile);
    }
}
