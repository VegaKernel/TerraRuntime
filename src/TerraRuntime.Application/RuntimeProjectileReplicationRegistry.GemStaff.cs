using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectileReplicationRegistry
{
    private readonly ProjectileHandle[] pendingGemStaffUpdates = new ProjectileHandle[MaxProjectileSlots];
    private GemStaffPublication? gemStaffPublication;

    internal bool TryPublishGemStaffActor(RuntimeProjectileStore.SimulationPreparation publication,
        bool sourceNetUpdate, bool worldBounds = false)
    {
        ArgumentNullException.ThrowIfNull(publication);
        if (!publication.CombatTrusted || !publication.TrustedOwner.IsAssigned ||
            publication.Lifecycle.GemStaff is null || publication.Lifecycle.GemStaffFeatures is null ||
            publication.PublicationCount != 1 ||
            !publication.TryGetPublication(0, out var kind, out var snapshot) ||
            !VanillaGemStaffProjectile1458.IsGemStaffProjectile(snapshot.Type) ||
            kind is not (ProjectileStateCommitKind.Update or ProjectileStateCommitKind.Despawn or ProjectileStateCommitKind.Remove) ||
            worldBounds && kind != ProjectileStateCommitKind.Despawn ||
            sourceNetUpdate && kind != ProjectileStateCommitKind.Update)
            return false;

        var previous = gemStaffPublication;
        gemStaffPublication = new(publication, kind, snapshot, sourceNetUpdate, worldBounds);
        try { return publication.TryPublishNext(); }
        finally { gemStaffPublication = previous; }
    }

    private bool IsGemStaffPublication(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot) =>
        gemStaffPublication is { } entry && entry.Kind == kind && entry.Snapshot == snapshot;

    private bool IsStaleGemStaffPublication(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot) =>
        IsGemStaffPublication(kind, in snapshot) && !gemStaffPublication!.Value.Preparation.IsPublicationCurrent;

    private bool HasGemStaffUpdateIntent(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot)
    {
        if (kind != ProjectileStateCommitKind.Update || !IsGemStaffPublication(kind, in snapshot))
            return false;
        int slot = snapshot.Handle.Slot;
        if (gemStaffPublication!.Value.SourceNetUpdate)
            pendingGemStaffUpdates[slot] = snapshot.Handle;
        // Projectile.netUpdate2 retains an unsent explicit update after netSpam throttling.
        // Retain intent, never an obsolete wire frame, and bind it to the full generation.
        return pendingGemStaffUpdates[slot] == snapshot.Handle;
    }

    private bool TryCreateGemStaffBoundsDestroy(in ProjectileSnapshot snapshot,
        in TerrariaProjectileKeyState key, out TerrariaProjectileDestroyState state)
    {
        state = default;
        if (!IsGemStaffPublication(ProjectileStateCommitKind.Despawn, in snapshot) ||
            !gemStaffPublication!.Value.WorldBounds) return false;
        // Source Projectile.Update exits the world before Kill and sends NaN positions.
        // The protocol permits these values; ordinary local mutation still requires finite coordinates.
        state = new(key, float.NaN, float.NaN);
        return true;
    }

    private readonly record struct GemStaffPublication(RuntimeProjectileStore.SimulationPreparation Preparation,
        ProjectileStateCommitKind Kind,
        ProjectileSnapshot Snapshot, bool SourceNetUpdate, bool WorldBounds);
}
