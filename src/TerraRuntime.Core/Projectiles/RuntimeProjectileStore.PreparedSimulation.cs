using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Core.Projectiles;

public sealed partial class RuntimeProjectileStore
{
    internal bool TryPrepareSimulation(in ProjectileSnapshot expected, out SimulationPreparation? preparation)
    {
        preparation = null;
        if (!IsCurrentHandleCandidate(expected.Handle))
            return false;
        ref readonly SlotState current = ref _slots[expected.Handle.Slot];
        if (!current.Active || Capture(expected.Handle.Slot, in current) != expected)
            return false;
        preparation = new(this, expected.Handle.Slot);
        return true;
    }

    internal sealed partial class SimulationPreparation
    {
        private readonly RuntimeProjectileStore owner;
        private readonly ushort slot;
        private readonly SlotState before;
        private SlotState shadow;
        private ProjectileSnapshot terminalSnapshot;
        private ProjectileLifecycleState terminalLifecycle;
        private bool terminalPublicationQueued;
        private readonly List<(ProjectileStateCommitKind Kind, ProjectileSnapshot Snapshot)> publications = [];
        private bool adopted;
        private int published;

        internal SimulationPreparation(RuntimeProjectileStore owner, ushort slot)
        {
            this.owner = owner;
            this.slot = slot;
            before = owner._slots[slot];
            shadow = before;
        }

        internal ProjectileSnapshot Snapshot => shadow.Active ? Capture(slot, in shadow) : terminalSnapshot;
        internal ProjectileLifecycleState Lifecycle => shadow.Active ? shadow.Lifecycle : terminalLifecycle;
        internal bool IsActive => shadow.Active;
        internal bool CombatTrusted => before.CombatTrusted;
        internal PlayerHandle TrustedOwner => before.CombatTrustedOwner;
        internal NpcHandle SourceNpc => before.SourceNpc;
        internal bool IsCurrent => !adopted && Same(in owner._slots[slot], in before);
        internal bool IsPublicationCurrent => adopted && Same(in owner._slots[slot], in shadow);
        internal int PublicationCount => publications.Count;

        internal bool TryStageSourceDirection(int? direction)
        {
            if (adopted || direction is not (null or -1 or 1)) return false;
            shadow.SourceDirection = direction;
            return true;
        }

        internal bool TryStageMotion(in ProjectileStateUpdate update, in ProjectileLifecycleState lifecycle) =>
            TryStageMotion(in update, in lifecycle, false);

        internal bool TryStageGemStaffKill(in ProjectileStateUpdate update, in ProjectileLifecycleState lifecycle)
        {
            if (!shadow.CombatTrusted || !shadow.CombatTrustedOwner.IsAssigned ||
                shadow.Lifecycle.GemStaff is null || shadow.Lifecycle.GemStaffFeatures is null ||
                shadow.Lifecycle.GemStaffKillInProgress || lifecycle.GemStaff is null ||
                lifecycle.GemStaffFeatures != shadow.Lifecycle.GemStaffFeatures ||
                lifecycle.GemStaffUpdateCount != shadow.Lifecycle.GemStaffUpdateCount || lifecycle.TimeLeft != 0 ||
                lifecycle.PenetrateOverride is < -1 ||
                !VanillaGemStaffProjectile1458.IsGemStaffProjectile(shadow.Update.Type)) return false;
            var killing = lifecycle with { GemStaffKillInProgress = true };
            // Source Kill calls area Damage while the generation is still active, including
            // lifetime zero. The enclosing actor removes it after those source effects.
            return TryStageMotion(in update, in killing, true);
        }

        internal bool TryStageGemStaffKilledMotion(in ProjectileStateUpdate update,
            in ProjectileLifecycleState lifecycle)
        {
            if (!shadow.CombatTrusted || !shadow.CombatTrustedOwner.IsAssigned ||
                !shadow.Lifecycle.GemStaffKillInProgress || !lifecycle.GemStaffKillInProgress ||
                lifecycle.TimeLeft != 0 || lifecycle.GemStaff is null ||
                lifecycle.GemStaffFeatures != shadow.Lifecycle.GemStaffFeatures ||
                lifecycle.GemStaffUpdateCount != shadow.Lifecycle.GemStaffUpdateCount) return false;
            return TryStageMotion(in update, in lifecycle, true);
        }

        private bool TryStageMotion(in ProjectileStateUpdate update, in ProjectileLifecycleState lifecycle,
            bool preserveTerminalGem)
        {
            if (adopted || !shadow.Active || !IsValidState(in update) ||
                update.Type != shadow.Update.Type || update.Spawner != shadow.Update.Spawner ||
                lifecycle.PenetrateOverride is < -1 && !(lifecycle.PenetrateOverride == -2 &&
                    shadow.Lifecycle.GemStaffKillInProgress && lifecycle.GemStaffKillInProgress && lifecycle.TimeLeft == 0) ||
                !lifecycle.LocalAi.IsFinite ||
                lifecycle.NetImportant != shadow.Lifecycle.NetImportant ||
                lifecycle.Reflected != shadow.Lifecycle.Reflected)
                return false;
            var planned = shadow;
            if (!TryAdvance(ref planned.Revision))
                return false;
            planned.Update = update;
            planned.Lifecycle = lifecycle;
            if (lifecycle.TimeLeft <= 0 && !preserveTerminalGem)
            {
                terminalSnapshot = Capture(slot, in planned);
                terminalLifecycle = planned.Lifecycle;
                Clear(ref planned);
            }
            shadow = planned;
            return true;
        }

        internal bool TryStageOrdinaryArrowPostHit(in ProjectileStateUpdate update)
        {
            if (adopted || !shadow.Active || shadow.Update.Type.Value is not (1 or 2 or 4 or 5) ||
                !IsValidState(in update) ||
                update != shadow.Update with { Damage = update.Damage, Ai = update.Ai })
                return false;
            var planned = shadow;
            if (!TryAdvance(ref planned.Revision))
                return false;
            planned.Update = update;
            shadow = planned;
            return true;
        }

        internal bool TryStageNpcHit()
        {
            if (adopted || !TryProjectNpcHit(slot, in shadow, out var planned, out bool reset,
                    out bool despawn, out var resetSnapshot, out var hitSnapshot))
                return false;
            if (reset)
                publications.Add((ProjectileStateCommitKind.Update, resetSnapshot));
            terminalLifecycle = shadow.Lifecycle;
            shadow = planned;
            if (despawn)
            {
                terminalSnapshot = hitSnapshot;
                publications.Add((ProjectileStateCommitKind.Despawn, hitSnapshot));
                terminalPublicationQueued = true;
            }
            return true;
        }

        internal bool TryQueuePublication()
        {
            if (adopted || !shadow.Active && terminalPublicationQueued)
                return false;
            var kind = shadow.Active ? ProjectileStateCommitKind.Update :
                VanillaProjectileOwnership.IsServerOwned(before.Update.Spawner)
                    ? ProjectileStateCommitKind.Despawn : ProjectileStateCommitKind.Remove;
            publications.Add((kind, Snapshot));
            if (!shadow.Active)
                terminalPublicationQueued = true;
            return true;
        }

        internal bool TryQueueToolDespawn(ProjectileKillOrigin? sourceKillOrigin = null)
        {
            if (adopted || shadow.Active || terminalPublicationQueued || !before.CombatTrusted ||
                !before.CombatTrustedOwner.IsAssigned ||
                !(terminalLifecycle.HeldTool is not null && VanillaHeldToolProjectile1458.IsSupported(before.Update.Type) ||
                  terminalLifecycle.ThrownAxe is not null && before.Update.Type.Value == VanillaThrownAxeProjectile1458.ProjectileType) ||
                sourceKillOrigin is { IsValid: false }) return false;
            var publication = sourceKillOrigin is { } origin
                ? Snapshot with { PositionX = origin.X, PositionY = origin.Y } : Snapshot;
            publications.Add((ProjectileStateCommitKind.Despawn, publication));
            terminalPublicationQueued = true;
            return true;
        }

        internal bool TryQueueFallingDespawn(ProjectileKillOrigin sourceKillOrigin)
        {
            if (adopted || shadow.Active || terminalPublicationQueued || !sourceKillOrigin.IsValid ||
                !VanillaProjectileOwnership.IsServerOwned(before.Update.Spawner) ||
                !VanillaFallingBlock1458.TryGetTile(before.Update.Type, out _)) return false;
            // Kill may retain an inactive movement tail. Packet29 still belongs
            // to the saved source Kill origin, not that final physical position.
            var publication = Snapshot with { PositionX = sourceKillOrigin.X, PositionY = sourceKillOrigin.Y };
            publications.Add((ProjectileStateCommitKind.Despawn, publication));
            terminalPublicationQueued = true;
            return true;
        }

        internal bool TryQueueGemStaffDespawn() => TryQueueGemStaffDespawn(null);

        internal bool TryQueueGroundCombatDespawn(ProjectileKillOrigin? sourceKillOrigin)
        {
            if (adopted || shadow.Active || terminalPublicationQueued ||
                before.Update.Spawner != byte.MaxValue || !before.SourceNpc.IsAssigned ||
                !VanillaGroundCombatPlayerHit1458.IsSupported(before.Update.Type) ||
                sourceKillOrigin is { IsValid: false }) return false;
            // Extra-update lasers retain one inactive movement tail after lifetime Kill.
            // The final physical image and the saved source packet29 origin are distinct.
            var publication = sourceKillOrigin is { } origin
                ? Snapshot with { PositionX = origin.X, PositionY = origin.Y } : Snapshot;
            publications.Add((ProjectileStateCommitKind.Despawn, publication));
            terminalPublicationQueued = true;
            return true;
        }

        internal bool TryQueueGemStaffDespawn(ProjectileKillOrigin? sourceKillOrigin)
        {
            if (adopted || shadow.Active || terminalPublicationQueued || !before.CombatTrusted ||
                !before.CombatTrustedOwner.IsAssigned || terminalLifecycle.GemStaff is null ||
                terminalLifecycle.GemStaffFeatures is null || sourceKillOrigin is { IsValid: false }) return false;
            // The authoritative gem actor owns the source-local Kill/bounds packet29,
            // including player-owned generations normally removed without server echo.
            var publication = sourceKillOrigin is { } origin
                ? Snapshot with { PositionX = origin.X, PositionY = origin.Y } : Snapshot;
            publications.Add((ProjectileStateCommitKind.Despawn, publication));
            terminalPublicationQueued = true;
            return true;
        }

        internal bool TryGetPublication(int index, out ProjectileStateCommitKind kind, out ProjectileSnapshot snapshot)
        {
            kind = default;
            snapshot = default;
            if ((uint)index >= (uint)publications.Count)
                return false;
            (kind, snapshot) = publications[index];
            return true;
        }

        internal bool TryAdoptUnpublished(out ProjectileSnapshot committed, out bool expired)
        {
            committed = default;
            expired = false;
            if (!IsCurrent)
                return false;
            owner._slots[slot] = shadow;
            if (!shadow.Active)
                owner._activeCount--;
            adopted = true;
            committed = Snapshot;
            expired = !shadow.Active;
            return true;
        }

        internal bool TryPublishNext()
        {
            if (!adopted || published >= publications.Count)
                return false;
            var entry = publications[published++];
            // Consume before callbacks and compare final owned slot, even for historical journal snapshots.
            if (!Same(in owner._slots[slot], in shadow))
                return false;
            owner._commitSink?.ProjectileStateCommitted(entry.Kind, in entry.Snapshot);
            return true;
        }

        private static void Clear(ref SlotState state)
        {
            state.Active = false;
            state.Revision = 0;
            state.Update = default;
            state.Lifecycle = default;
            state.CombatTrusted = false;
            state.CombatTrustedOwner = default;
        }

        private static bool Same(in SlotState left, in SlotState right) =>
            left.Active == right.Active && left.Generation == right.Generation &&
            left.Revision == right.Revision && left.Update == right.Update &&
            left.Lifecycle == right.Lifecycle && left.CombatTrusted == right.CombatTrusted &&
            left.CombatTrustedOwner == right.CombatTrustedOwner && left.SourceNpc == right.SourceNpc && left.SourceDirection == right.SourceDirection;
    }
}
