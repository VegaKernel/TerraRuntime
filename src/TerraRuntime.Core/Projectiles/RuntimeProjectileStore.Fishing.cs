using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Core.Projectiles;

public sealed partial class RuntimeProjectileStore
{
    internal sealed partial class SimulationPreparation
    {
        internal bool TryQueueFishingDespawn()
        {
            if (adopted || shadow.Active || terminalPublicationQueued || !before.CombatTrusted ||
                !before.CombatTrustedOwner.IsAssigned || !FishingRules1458.IsBobber(before.Update.Type.Value)) return false;
            publications.Add((ProjectileStateCommitKind.Despawn, Snapshot));
            terminalPublicationQueued = true;
            return true;
        }
    }
}
