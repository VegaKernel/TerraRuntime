using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeProjectileReplicationRegistry
{
    internal bool TryPublishFishingActor(RuntimeProjectileStore.SimulationPreparation publication, bool netUpdate)
    {
        if (!publication.CombatTrusted || !publication.TrustedOwner.IsAssigned ||
            !FishingRules1458.IsBobber(publication.Snapshot.Type.Value) || publication.PublicationCount != 1 ||
            !publication.TryGetPublication(0, out var kind, out var snapshot)) return false;
        // Reuse the existing generation-bound netSpam and stale-publication owner.
        var previous = toolActorPublication;
        toolActorPublication = new(publication, kind, snapshot, netUpdate, false);
        try { return publication.TryPublishNext(); }
        finally { toolActorPublication = previous; }
    }
}
