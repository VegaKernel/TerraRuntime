using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectileReplicationRegistry
{
    private static bool IsToolActorType1458(TerraRuntime.Contracts.Gameplay.ProjectileTypeId type) =>
        VanillaHeldToolProjectile1458.IsSupported(type) || type.Value == VanillaThrownAxeProjectile1458.ProjectileType ||
        VanillaFallingBlock1458.TryGetTile(type, out _);

    private RuntimeProjectileStore.WorldSpawnPreparation.WorldSpawnPublication? toolWorldBirthPublication;

    internal bool TryPublishToolWorldBirth(RuntimeProjectileStore.WorldSpawnPreparation.WorldSpawnPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        var previous = toolWorldBirthPublication;
        toolWorldBirthPublication = publication;
        try { return publication.TryPublishNext(); }
        finally { toolWorldBirthPublication = previous; }
    }

    private bool IsStaleToolWorldBirth(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot) =>
        kind == ProjectileStateCommitKind.Spawn && toolWorldBirthPublication is { } publication &&
        publication.Births.Contains(snapshot) && !publication.IsPublicationCurrent(in snapshot);

    private readonly ProjectileHandle[] pendingToolUpdates = new ProjectileHandle[MaxProjectileSlots];
    private ToolActorPublication? toolActorPublication;

    internal bool TryPublishToolActor(RuntimeProjectileStore.SimulationPreparation publication,
        bool sourceNetUpdate, bool worldBounds = false)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (!publication.CombatTrusted || !publication.TrustedOwner.IsAssigned ||
            (publication.Snapshot.Type.Value == VanillaThrownAxeProjectile1458.ProjectileType
                ? publication.Lifecycle.ThrownAxe is null || publication.Lifecycle.HeldTool is not null
                : publication.Lifecycle.HeldTool is null || publication.Lifecycle.ThrownAxe is not null) ||
            publication.PublicationCount != 1 ||
            !publication.TryGetPublication(0, out var kind, out var snapshot) ||
            !IsToolActorType1458(snapshot.Type) ||
            kind is not (ProjectileStateCommitKind.Update or ProjectileStateCommitKind.Despawn or ProjectileStateCommitKind.Remove) ||
            worldBounds && kind != ProjectileStateCommitKind.Despawn ||
            sourceNetUpdate && kind != ProjectileStateCommitKind.Update)
            return false;

        var previous = toolActorPublication;
        toolActorPublication = new(publication, kind, snapshot, sourceNetUpdate, worldBounds);
        try { return publication.TryPublishNext(); }
        finally { toolActorPublication = previous; }
    }

    internal bool TryPublishFallingBlockActor(RuntimeProjectileStore.SimulationPreparation publication,
        bool sourceNetUpdate, bool worldBounds = false)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (publication.Snapshot.Spawner != byte.MaxValue ||
            !VanillaFallingBlock1458.TryGetTile(publication.Snapshot.Type, out _) ||
            publication.PublicationCount != 1 ||
            !publication.TryGetPublication(0, out var kind, out var snapshot) ||
            kind is not (ProjectileStateCommitKind.Update or ProjectileStateCommitKind.Despawn) ||
            sourceNetUpdate && kind != ProjectileStateCommitKind.Update ||
            worldBounds && kind != ProjectileStateCommitKind.Despawn) return false;
        var previous = toolActorPublication;
        toolActorPublication = new(publication, kind, snapshot, sourceNetUpdate, worldBounds);
        try { return publication.TryPublishNext(); }
        finally { toolActorPublication = previous; }
    }

    private bool IsToolActorPublication(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot) =>
        toolActorPublication is { } entry && entry.Kind == kind && entry.Snapshot == snapshot;

    private bool IsStaleToolActorPublication(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot) =>
        IsToolActorPublication(kind, in snapshot) && !toolActorPublication!.Value.Preparation.IsPublicationCurrent;

    private bool HasToolActorUpdateIntent(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
    {
        if (kind != ProjectileStateCommitKind.Update || !IsToolActorPublication(kind, in snapshot))
            return false;
        int slot = snapshot.Handle.Slot;
        if (toolActorPublication!.Value.SourceNetUpdate)
            pendingToolUpdates[slot] = snapshot.Handle;
        // Projectile.netUpdate2 retains an unsent explicit update after netSpam throttling.
        // Retain intent, never an obsolete wire frame, and bind it to the full generation.
        return pendingToolUpdates[slot] == snapshot.Handle;
    }

    private bool TryCreateToolActorBoundsDestroy(in ProjectileSnapshot snapshot,
        in TerrariaProjectileKeyState key, out TerrariaProjectileDestroyState state)
    {
        state = default;
        if (!IsToolActorPublication(ProjectileStateCommitKind.Despawn, in snapshot) ||
            !toolActorPublication!.Value.WorldBounds) return false;
        // Source Projectile.Update exits the world before Kill and sends NaN positions.
        // The protocol permits these values; ordinary local mutation still requires finite coordinates.
        state = new(key, float.NaN, float.NaN);
        return true;
    }

    private readonly record struct ToolActorPublication(RuntimeProjectileStore.SimulationPreparation Preparation,
        ProjectileStateCommitKind Kind,
        ProjectileSnapshot Snapshot, bool SourceNetUpdate, bool WorldBounds);
}
