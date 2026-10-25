using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Projectiles;

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
        out SpawnPreparation? preparation) =>
        TryPrepareVanillaSpawn(in update, timeLeftOverride, null, out preparation);

    internal bool TryPrepareVanillaSpawn(
        in ProjectileStateUpdate update,
        int? timeLeftOverride,
        GemStaffFeatures1458? gemStaffFeatures,
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

        if (!TryFinalizePreparedSpawnLifecycle(in update, timeLeftOverride, gemStaffFeatures, ref lifecycle))
            return false;

        preparation = new SpawnPreparation(this, slot, in update, in lifecycle);
        return true;
    }

    private static bool TryFinalizePreparedSpawnLifecycle(in ProjectileStateUpdate update,
        int? timeLeftOverride, GemStaffFeatures1458? gemStaffFeatures, ref ProjectileLifecycleState lifecycle)
    {
        if (gemStaffFeatures is not { } features)
        {
            if (timeLeftOverride is int time) lifecycle = lifecycle with { TimeLeft = time };
            return true;
        }
        // FinalizeNewProjectile owns this once, inside the same prepared birth as payment.
        // A later packet update cannot acquire or refresh these private lifecycle fields.
        if (timeLeftOverride is not null ||
            !VanillaGemStaffProjectile1458.TryDecodeFeatures(update.Ai.Ai0, out var encoded) || encoded != features ||
            !VanillaGemStaffProjectile1458.TryFinalize(update.Type, features,
                new GemStaffFinalize1458(1, 1, lifecycle.TimeLeft, 0), out var finalized)) return false;
        lifecycle = lifecycle with { TimeLeft = finalized.TimeLeft, PenetrateOverride = finalized.Penetration,
            GemStaffFeatures = features, GemStaff = GemStaffRuntimeState1458.Constructor };
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
