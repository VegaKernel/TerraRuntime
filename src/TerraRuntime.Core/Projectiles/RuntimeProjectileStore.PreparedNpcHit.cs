using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Core.Projectiles;

public sealed partial class RuntimeProjectileStore
{
    internal bool TryPrepareNpcHit(in ProjectileSnapshot expected, out NpcHitPreparation? preparation)
    {
        preparation = null;
        if (!IsCurrentHandleCandidate(expected.Handle))
            return false;

        ref readonly SlotState state = ref _slots[expected.Handle.Slot];
        if (!state.Active || state.Generation != expected.Handle.Generation.Value ||
            Capture(expected.Handle.Slot, in state) != expected ||
            !VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(state.Update.Type, out _))
        {
            return false;
        }

        preparation = new NpcHitPreparation(this, expected.Handle.Slot);
        return preparation.CanAdopt;
    }

    internal sealed class NpcHitPreparation
    {
        private readonly RuntimeProjectileStore owner;
        private readonly ushort slot;
        private readonly SlotState before;
        private readonly SlotState after;
        private readonly ProjectileSnapshot resetSnapshot;
        private readonly ProjectileSnapshot hitSnapshot;
        private readonly bool reset;
        private readonly bool despawn;
        private bool adopted;
        private bool published;

        internal NpcHitPreparation(RuntimeProjectileStore owner, ushort slot)
        {
            this.owner = owner;
            this.slot = slot;
            before = owner._slots[slot];
            CanAdopt = TryProjectNpcHit(slot, in before, out after, out reset, out despawn,
                out resetSnapshot, out hitSnapshot);
        }

        internal bool CanAdopt { get; }

        internal bool IsCurrent => CanAdopt && !adopted && Same(in owner._slots[slot], in before);

        internal bool TryAdoptUnpublished(out ProjectileSnapshot committed, out bool despawned)
        {
            committed = default;
            despawned = false;
            if (!IsCurrent)
                return false;

            owner._slots[slot] = after;
            if (despawn)
                owner._activeCount--;
            adopted = true;
            committed = hitSnapshot;
            despawned = despawn;
            return true;
        }

        internal bool TryPublish()
        {
            if (!adopted || published)
                return false;

            // Consume before callbacks. A retired/replaced generation cannot publish a stale continuation.
            published = true;
            if (!Same(in owner._slots[slot], in after))
                return false;
            if (reset)
            {
                owner._commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in resetSnapshot);
                if (!Same(in owner._slots[slot], in after))
                    return true;
            }
            if (despawn)
                owner._commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Despawn, in hitSnapshot);
            return true;
        }

        private static bool Same(in SlotState left, in SlotState right) =>
            left.Active == right.Active && left.Generation == right.Generation &&
            left.Revision == right.Revision && left.Update == right.Update &&
            left.Lifecycle == right.Lifecycle && left.CombatTrusted == right.CombatTrusted &&
            left.CombatTrustedOwner == right.CombatTrustedOwner && left.SourceNpc == right.SourceNpc;
    }

    // Shared shadow projection; the live-hit token and detached simulation use the same semantics.
    private static bool TryProjectNpcHit(ushort slot, in SlotState before, out SlotState after,
        out bool reset, out bool despawn, out ProjectileSnapshot resetSnapshot, out ProjectileSnapshot hitSnapshot)
    {
        after = default;
        reset = false;
        despawn = false;
        resetSnapshot = default;
        hitSnapshot = default;
        if (!before.Active || !VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(before.Update.Type, out int initial))
            return false;
        var state = before;
        var planned = state;
        reset = VanillaProjectileNpcCombatFacts.ShouldResetReleasedControlledMagicTargetAfterNpcHit(
            state.Update.Type, state.Update.Ai.Ai0) && state.Update.Ai.Ai1 != -1f;
        if (reset)
        {
            if (!TryAdvance(ref planned.Revision))
                return false;
            planned.Update = planned.Update with
            {
                Ai = new ProjectileAiState(planned.Update.Ai.Ai0, -1f, planned.Update.Ai.Ai2),
            };
            resetSnapshot = Capture(slot, in planned);
        }

        int remaining = planned.Lifecycle.PenetrateOverride ?? initial;
        despawn = remaining >= 0 && remaining <= 1;
        if (despawn)
        {
            hitSnapshot = Capture(slot, in planned);
            planned.Active = false;
            planned.Revision = 0;
            planned.Update = default;
            planned.Lifecycle = default;
            planned.CombatTrusted = false;
            planned.CombatTrustedOwner = default;
        }
        else
        {
            if (remaining > 1)
            {
                if (!TryAdvance(ref planned.Revision))
                    return false;
                planned.Lifecycle = planned.Lifecycle with { PenetrateOverride = remaining - 1 };
            }
            hitSnapshot = Capture(slot, in planned);
        }
        after = planned;
        return true;
    }
}
