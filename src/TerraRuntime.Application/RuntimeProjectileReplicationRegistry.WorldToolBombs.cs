using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectileReplicationRegistry
{
    // Only the retained WorldGen producer actor enters this scope. Legacy player
    // bombs and NPC-owned bombs keep their existing replication policy.
    internal bool TryPublishWorldToolBombActor(RuntimeProjectileStore.SimulationPreparation publication,
        bool worldBounds = false)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (!publication.CombatTrusted || publication.TrustedOwner.IsAssigned || publication.SourceNpc.IsAssigned ||
            publication.Snapshot.Spawner != byte.MaxValue || !publication.Lifecycle.NpcProjectile ||
            !VanillaWorldToolBomb1458.IsSupported(publication.Snapshot.Type) || publication.PublicationCount != 1 ||
            !publication.TryGetPublication(0, out var kind, out var snapshot) ||
            kind is not (ProjectileStateCommitKind.Update or ProjectileStateCommitKind.Despawn) ||
            worldBounds && kind != ProjectileStateCommitKind.Despawn ||
            kind == ProjectileStateCommitKind.Despawn && !worldBounds && !publication.Lifecycle.WorldToolBombKillInProgress)
            return false;
        var previous = toolActorPublication;
        toolActorPublication = new(publication, kind, snapshot, false, worldBounds);
        try { return publication.TryPublishNext(); }
        finally { toolActorPublication = previous; }
    }

    private bool IsWorldToolBombActorPublication(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot) =>
        VanillaWorldToolBomb1458.IsSupported(snapshot.Type) && IsToolActorPublication(kind, in snapshot) &&
        toolActorPublication!.Value.Preparation.Lifecycle.NpcProjectile &&
        !toolActorPublication.Value.Preparation.TrustedOwner.IsAssigned &&
        !toolActorPublication.Value.Preparation.SourceNpc.IsAssigned;
}
