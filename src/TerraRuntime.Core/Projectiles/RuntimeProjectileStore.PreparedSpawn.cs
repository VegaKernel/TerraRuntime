using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Projectiles;

public sealed partial class RuntimeProjectileStore
{
    /// <summary>
    /// Captures allocation and defaults without advancing a generation or invoking the commit sink. The
    /// application can finish all item-use admission before adopting and publishing the first projectile.
    /// </summary>
    internal bool TryPrepareVanillaSpawn(
        in ProjectileStateUpdate update,
        int? timeLeftOverride,
        out SpawnPreparation? preparation)
    {
        preparation = null;
        if (timeLeftOverride is <= 0 ||
            !IsValidState(in update) ||
            !TryCreateLifecycle(update.Type, out ProjectileLifecycleState lifecycle) ||
            !TrySelectVanillaAllocationSlot(out ushort slot) ||
            _slots[slot].Generation == ulong.MaxValue)
        {
            return false;
        }

        if (timeLeftOverride is int sourceTimeLeft)
            lifecycle = lifecycle with { TimeLeft = sourceTimeLeft };

        preparation = new SpawnPreparation(this, slot, in update, in lifecycle);
        return true;
    }

    internal sealed class SpawnPreparation
    {
        private readonly RuntimeProjectileStore owner;
        private readonly ushort slot;
        private readonly SlotState baseline;
        private readonly ProjectileStateUpdate update;
        private readonly ProjectileLifecycleState lifecycle;
        private ProjectileSnapshot committed;
        private bool adopted;
        private bool published;

        internal SpawnPreparation(
            RuntimeProjectileStore owner,
            ushort slot,
            in ProjectileStateUpdate update,
            in ProjectileLifecycleState lifecycle)
        {
            this.owner = owner;
            this.slot = slot;
            baseline = owner._slots[slot];
            this.update = update;
            this.lifecycle = lifecycle;
        }

        internal bool IsCurrent
        {
            get
            {
                if (adopted ||
                    !owner.TrySelectVanillaAllocationSlot(out ushort selected) ||
                    selected != slot)
                {
                    return false;
                }

                ref readonly SlotState current = ref owner._slots[slot];
                return current.Generation != ulong.MaxValue &&
                    current.Active == baseline.Active &&
                    current.Generation == baseline.Generation &&
                    current.Revision == baseline.Revision &&
                    current.Update == baseline.Update &&
                    current.Lifecycle == baseline.Lifecycle &&
                    current.CombatTrusted == baseline.CombatTrusted &&
                    current.CombatTrustedOwner == baseline.CombatTrustedOwner &&
                    current.SourceNpc == baseline.SourceNpc;
            }
        }

        internal bool TryCommitUnpublished(out ProjectileSnapshot snapshot)
        {
            snapshot = default;
            if (!IsCurrent)
                return false;

            ref SlotState state = ref owner._slots[slot];
            // IsCurrent already proved this increment cannot fail; this tail has no external callbacks.
            state.Generation++;
            bool wasActive = state.Active;
            InitializeSlot(ref state, in update, in lifecycle);
            if (!wasActive)
                owner._activeCount++;

            committed = Capture(slot, in state);
            adopted = true;
            snapshot = committed;
            return true;
        }

        internal bool TryPublish(in ProjectileSnapshot snapshot)
        {
            if (!adopted || published || snapshot != committed ||
                !owner.TryGet(snapshot.Handle, out ProjectileSnapshot current) || current != committed)
            {
                return false;
            }

            // Consume publication before entering a potentially reentrant sink.
            published = true;
            owner._commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in snapshot);
            return true;
        }
    }
}
