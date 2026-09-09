using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

/// <summary>Exact spawn provenance and bounded retained termination handoff. Client types confer no terrain authority.</summary>
internal sealed class RuntimeFallingBlockProjectiles : IProjectileTerminationCommitSink
{
    internal const int Capacity = 128;
    private readonly HashSet<ProjectileHandle> active = new(Capacity);
    private readonly Queue<ProjectileSnapshot> pending = new(Capacity);
    private readonly List<ProjectileHandle> displaced = new(Capacity);
    public int Count => active.Count + pending.Count;
    public bool CanSpawn => Count < Capacity;
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
    public void ProjectileTerminated(in ProjectileTerminationCommit termination)
    {
        if (!termination.CombatTrusted || !active.Remove(termination.FinalProjectile.Handle) ||
            !VanillaFallingBlock1458.TryGetTile(termination.FinalProjectile.Type, out _)) return;
        // Capacity includes active AND pending; item pressure retains the event and prevents further spawns.
        if (termination.Reason != ProjectileSimulationTerminationReason.WorldBounds) pending.Enqueue(termination.FinalProjectile);
    }
}
