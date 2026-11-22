using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Projectiles;

public sealed partial class RuntimeProjectileStore
{
    internal sealed partial class SimulationPreparation
    {
        private static bool IsWorldBombRole(int type, bool npcProjectile) =>
            npcProjectile ? type is 28 or 75 : type == 164;

        internal bool TryStageWorldToolBombKill(in ProjectileStateUpdate update, in ProjectileLifecycleState lifecycle)
        {
            int type = shadow.Update.Type.Value;
            if (!IsCurrent || !shadow.CombatTrusted || shadow.CombatTrustedOwner.IsAssigned || shadow.SourceNpc.IsAssigned ||
                shadow.Update.Spawner != byte.MaxValue || !IsWorldBombRole(type, shadow.Lifecycle.NpcProjectile) ||
                shadow.Lifecycle.WorldToolBombKillInProgress || lifecycle.NpcProjectile != shadow.Lifecycle.NpcProjectile ||
                lifecycle.TimeLeft != 0 || lifecycle.PenetrateOverride is < -1 ||
                lifecycle.GemStaff is not null || lifecycle.HeldTool is not null || lifecycle.ThrownAxe is not null ||
                update.OriginalDamage != shadow.Update.OriginalDamage ||
                update.Damage != (type == 164 ? 250 : type == 28 ? 100 : 60)) return false;
            var killing = lifecycle with { WorldToolBombKillInProgress = true };
            return TryStageMotion(in update, in killing, true);
        }

        internal bool TryStageWorldToolBombKilledMotion(in ProjectileStateUpdate update, in ProjectileLifecycleState lifecycle)
        {
            if (!IsCurrent || !shadow.CombatTrusted || shadow.CombatTrustedOwner.IsAssigned || shadow.SourceNpc.IsAssigned ||
                shadow.Update.Spawner != byte.MaxValue || shadow.Update.Type.Value is not (28 or 75) ||
                !shadow.Lifecycle.NpcProjectile || !shadow.Lifecycle.WorldToolBombKillInProgress ||
                !lifecycle.NpcProjectile || !lifecycle.WorldToolBombKillInProgress || lifecycle.TimeLeft != 0 ||
                lifecycle.PenetrateOverride != shadow.Lifecycle.PenetrateOverride) return false;
            return TryStageMotion(in update, in lifecycle, true);
        }
        internal bool TryQueueWorldToolBombDespawn(in ProjectileKillOrigin sourceKillOrigin)
        {
            if (adopted || shadow.Active || terminalPublicationQueued || !before.CombatTrusted ||
                before.CombatTrustedOwner.IsAssigned || before.SourceNpc.IsAssigned || before.Update.Spawner != byte.MaxValue ||
                !IsWorldBombRole(before.Update.Type.Value, terminalLifecycle.NpcProjectile) ||
                !terminalLifecycle.WorldToolBombKillInProgress || !sourceKillOrigin.IsValid) return false;
            var publication = Snapshot with { PositionX = sourceKillOrigin.X, PositionY = sourceKillOrigin.Y };
            publications.Add((ProjectileStateCommitKind.Despawn, publication));
            terminalPublicationQueued = true;
            return true;
        }
    }
}
