using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core;

public sealed partial class RuntimeProjectileStore
{
    public bool TryUpdate(ProjectileHandle handle, in ProjectileStateUpdate update, out ProjectileSnapshot snapshot)
    {
        if (!IsCurrentHandleCandidate(handle) || !IsValidState(in update))
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[handle.Slot];
        if (!state.Active ||
            state.Generation != handle.Generation.Value ||
            state.Update.Spawner != update.Spawner)
        {
            snapshot = default;
            return false;
        }

        ProjectileLifecycleState previousLifecycle = state.Lifecycle;
        ProjectileLifecycleState lifecycle = previousLifecycle;
        if (state.Update.Type != update.Type)
        {
            if (!TryCreateLifecycle(update.Type, out lifecycle))
            {
                snapshot = default;
                return false;
            }

            lifecycle = lifecycle with { Liquid = previousLifecycle.Liquid };
        }

        if (!TryAdvance(ref state.Revision))
        {
            snapshot = default;
            return false;
        }

        state.Update = update;
        state.Lifecycle = lifecycle;
        snapshot = Capture(handle.Slot, in state);
        _commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in snapshot);
        return true;
    }

    /// <summary>
    /// Commits the final state of one completed local simulation pass. Positive timeLeft updates runtime
    /// lifecycle and publishes exactly one Update commit for the whole world tick. A non-positive timeLeft
    /// atomically removes the projectile with the final simulated state: player-owned generations use the
    /// silent Remove path, while owner 255 follows the dedicated-server Kill path and publishes Despawn.
    /// Spawner/owner is generation identity and cannot be rewritten by simulation.
    /// </summary>
    public bool TryCommitSimulationStep(
        ProjectileHandle handle,
        in ProjectileStateUpdate update,
        int timeLeft,
        out ProjectileSnapshot snapshot,
        out bool expired) =>
        TryCommitSimulationStep(
            handle,
            in update,
            timeLeft,
            liquidState: null,
            out snapshot,
            out expired);

    /// <summary>
    /// Commits a simulation result together with runtime-only liquid history. A null liquid state preserves
    /// the current lifecycle so existing state steppers cannot accidentally erase authoritative wet flags.
    /// </summary>
    public bool TryCommitSimulationStep(
        ProjectileHandle handle,
        in ProjectileStateUpdate update,
        int timeLeft,
        ProjectileLiquidState? liquidState,
        out ProjectileSnapshot snapshot,
        out bool expired) =>
        TryCommitSimulationStepCore(
            handle,
            in update,
            timeLeft,
            liquidState,
            publishPositiveUpdate: true,
            out snapshot,
            out expired);

    /// <summary>
    /// Commits one positive-lifetime local extraUpdate without publishing an intermediate packet-27 state.
    /// The authoritative store must expose this state before Projectile.Damage/reflection runs, because those
    /// interactions can despawn or mutate the exact generation before the next local subupdate. Terminal
    /// commits are never silent: removal/despawn still publishes immediately.
    /// </summary>
    internal bool TryCommitSimulationSubupdate(
        ProjectileHandle handle,
        in ProjectileStateUpdate update,
        int timeLeft,
        ProjectileLiquidState? liquidState,
        out ProjectileSnapshot snapshot,
        out bool expired) =>
        TryCommitSimulationStepCore(
            handle,
            in update,
            timeLeft,
            liquidState,
            publishPositiveUpdate: false,
            out snapshot,
            out expired);

    private bool TryCommitSimulationStepCore(
        ProjectileHandle handle,
        in ProjectileStateUpdate update,
        int timeLeft,
        ProjectileLiquidState? liquidState,
        bool publishPositiveUpdate,
        out ProjectileSnapshot snapshot,
        out bool expired)
    {
        expired = false;
        if (!IsCurrentHandleCandidate(handle) || !IsValidState(in update))
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[handle.Slot];
        if (!state.Active ||
            state.Generation != handle.Generation.Value ||
            state.Update.Spawner != update.Spawner)
        {
            snapshot = default;
            return false;
        }

        if (timeLeft <= 0)
        {
            state.Update = update;
            snapshot = Capture(handle.Slot, in state);
            state.Active = false;
            state.Revision = 0;
            state.Update = default;
            state.Lifecycle = default;
            state.CombatTrusted = false;
            state.CombatTrustedOwner = default;
            _activeCount--;
            expired = true;

            ProjectileStateCommitKind kind = VanillaProjectileOwnership.IsServerOwned(snapshot.Spawner)
                ? ProjectileStateCommitKind.Despawn
                : ProjectileStateCommitKind.Remove;
            _commitSink?.ProjectileStateCommitted(kind, in snapshot);
            return true;
        }

        ProjectileLifecycleState previousLifecycle = state.Lifecycle;
        ProjectileLifecycleState lifecycle = previousLifecycle;
        if (state.Update.Type != update.Type)
        {
            if (!TryCreateLifecycle(update.Type, out lifecycle))
            {
                snapshot = default;
                return false;
            }

            lifecycle = lifecycle with { Liquid = previousLifecycle.Liquid };
        }

        lifecycle = lifecycle with
        {
            TimeLeft = timeLeft,
            Liquid = liquidState ?? lifecycle.Liquid,
            OldVelocityX = state.Update.VelocityX,
            OldVelocityY = state.Update.VelocityY
        };
        if (!TryAdvance(ref state.Revision))
        {
            snapshot = default;
            return false;
        }

        state.Update = update;
        state.Lifecycle = lifecycle;
        snapshot = Capture(handle.Slot, in state);
        if (publishPositiveUpdate)
            _commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in snapshot);
        return true;
    }


    /// <summary>
    /// Marks one exact generation as eligible for server-authoritative combat side effects. This is runtime-only trust
    /// metadata: it does not change packet 27 state, revision, or replication. Only server-owned command paths call it.
    /// </summary>
    public bool TryMarkCombatTrusted(ProjectileHandle handle, PlayerHandle owner = default)
    {
        if (!IsCurrentHandleCandidate(handle))
            return false;

        ref SlotState state = ref _slots[handle.Slot];
        if (!state.Active || state.Generation != handle.Generation.Value)
            return false;

        bool playerOwned = VanillaProjectileOwnership.IsPlayerOwned(state.Update.Spawner);
        if (playerOwned)
        {
            if (!owner.IsAssigned || owner.Slot.Value != state.Update.Spawner)
                return false;
        }
        else if (owner.IsAssigned)
        {
            return false;
        }

        state.CombatTrusted = true;
        state.CombatTrustedOwner = owner;
        return true;
    }

    /// <summary>
    /// Applies one source-backed NPC penetration consumption after a committed projectile hit. Infinite penetration
    /// remains active; the last positive penetration despawns the exact generation. Runtime-only remaining
    /// penetration advances the revision without publishing a redundant wire update.
    /// </summary>
    public bool TryConsumeCombatHitPenetration(
        ProjectileHandle handle,
        out bool despawned,
        out ProjectileSnapshot snapshot) =>
        TryConsumeNpcHitPenetration(handle, out despawned, out snapshot);

    public bool TryConsumeNpcHitPenetration(
        ProjectileHandle handle,
        out bool despawned,
        out ProjectileSnapshot snapshot)
    {
        despawned = false;
        if (!IsCurrentHandleCandidate(handle))
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[handle.Slot];
        if (!state.Active || state.Generation != handle.Generation.Value ||
            !VanillaProjectileNpcCombatFacts.TryGetInitialPenetration(state.Update.Type, out int initial))
        {
            snapshot = default;
            return false;
        }

        int remaining = state.Lifecycle.PenetrateOverride ?? initial;
        if (remaining < 0)
        {
            snapshot = Capture(handle.Slot, in state);
            return true;
        }
        if (remaining <= 1)
        {
            despawned = TryDespawn(handle, out snapshot);
            return despawned;
        }

        if (!TryAdvance(ref state.Revision))
        {
            snapshot = default;
            return false;
        }
        state.Lifecycle = state.Lifecycle with { PenetrateOverride = remaining - 1 };
        snapshot = Capture(handle.Slot, in state);
        return true;
    }

    public bool TryDespawn(ProjectileHandle handle, out ProjectileSnapshot finalSnapshot) =>
        TryRemoveCore(
            handle,
            ProjectileStateCommitKind.Despawn,
            overridePosition: false,
            positionX: 0f,
            positionY: 0f,
            out finalSnapshot);

    /// <summary>
    /// Removes one exact generation from authoritative state without declaring a packet-29 network destroy.
    /// Replication sinks still receive the final snapshot so they can clear baseline and exact wire identity.
    /// </summary>
    public bool TryRemove(ProjectileHandle handle, out ProjectileSnapshot finalSnapshot) =>
        TryRemoveCore(
            handle,
            ProjectileStateCommitKind.Remove,
            overridePosition: false,
            positionX: 0f,
            positionY: 0f,
            out finalSnapshot);

    /// <summary>
    /// Atomically applies packet-29's final finite position and despawns the exact generation without
    /// publishing an intermediate Update commit. Replication therefore observes one final Despawn snapshot,
    /// matching vanilla's position assignment followed by Projectile.Kill rather than inventing packet 27.
    /// </summary>
    public bool TryDespawnAt(
        ProjectileHandle handle,
        float positionX,
        float positionY,
        out ProjectileSnapshot finalSnapshot)
    {
        if (!float.IsFinite(positionX) || !float.IsFinite(positionY))
        {
            finalSnapshot = default;
            return false;
        }

        return TryRemoveCore(
            handle,
            ProjectileStateCommitKind.Despawn,
            overridePosition: true,
            positionX,
            positionY,
            out finalSnapshot);
    }

    /// <summary>
    /// Atomically applies the source-backed NPC.ReflectProjectile mutation to one exact projectile generation.
    /// Owner/spawner and original damage remain generation identity; reflection only changes current velocity,
    /// current damage and runtime-only reflected/penetration state.
    /// </summary>
    public bool TryReflect(
        ProjectileHandle handle,
        float velocityX,
        float velocityY,
        short damage,
        out ProjectileSnapshot snapshot)
    {
        if (!IsCurrentHandleCandidate(handle) ||
            !float.IsFinite(velocityX) ||
            !float.IsFinite(velocityY) ||
            damage < 0)
        {
            snapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[handle.Slot];
        if (!state.Active ||
            state.Generation != handle.Generation.Value ||
            state.Lifecycle.Reflected ||
            !TryAdvance(ref state.Revision))
        {
            snapshot = default;
            return false;
        }

        state.Update = state.Update with
        {
            VelocityX = velocityX,
            VelocityY = velocityY,
            Damage = damage
        };
        state.Lifecycle = state.Lifecycle with
        {
            Reflected = true,
            PenetrateOverride = 1
        };
        snapshot = Capture(handle.Slot, in state);
        _commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Update, in snapshot);
        return true;
    }

    private bool TryRemoveCore(
        ProjectileHandle handle,
        ProjectileStateCommitKind commitKind,
        bool overridePosition,
        float positionX,
        float positionY,
        out ProjectileSnapshot finalSnapshot)
    {
        if (commitKind is not ProjectileStateCommitKind.Despawn and not ProjectileStateCommitKind.Remove)
            throw new ArgumentOutOfRangeException(nameof(commitKind));

        if (!IsCurrentHandleCandidate(handle))
        {
            finalSnapshot = default;
            return false;
        }

        ref SlotState state = ref _slots[handle.Slot];
        if (!state.Active || state.Generation != handle.Generation.Value)
        {
            finalSnapshot = default;
            return false;
        }

        if (overridePosition)
        {
            state.Update = state.Update with
            {
                PositionX = positionX,
                PositionY = positionY
            };
        }

        finalSnapshot = Capture(handle.Slot, in state);
        state.Active = false;
        state.Revision = 0;
        state.Update = default;
        state.Lifecycle = default;
        state.CombatTrusted = false;
        state.CombatTrustedOwner = default;
        _activeCount--;
        _commitSink?.ProjectileStateCommitted(commitKind, in finalSnapshot);
        return true;
    }
}
