using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Core.Npcs;

public sealed partial class RuntimeNpcStore
{
    internal bool TryCreateAiSpawnPreview(in NpcSnapshot parent, VanillaUnifiedRandom1458 random,
        out AiSpawnPreview? preview)
    {
        preview = null;
        if (mutationSerialExhausted || !MatchesSource(in parent) || !IsSpawnRandomSource(random))
            return false;

        ulong capturedSerial = mutationSerial;
        var beforeRandom = random.Clone();
        var context = _spawnContext?.Invoke();
        if (context is { IsValid: false } || mutationSerial != capturedSerial ||
            !MatchesSource(in parent) || !IsSpawnRandomSource(random) || !random.HasSameState(beforeRandom))
            return false;

        var afterRandom = beforeRandom.Clone();
        preview = new(this, parent, context, random, beforeRandom, afterRandom);
        return true;
    }

    // One Hive attempt creates at most one child. This preview owns a detached physical slot table,
    // the source creation context and the cloned shared stream; it never owns an arbitrary sink.
    internal sealed class AiSpawnPreview
    {
        private readonly RuntimeNpcStore owner;
        private readonly ulong beforeSerial;
        private RuntimeNpcStore? detached;
        private readonly NpcSnapshot parent;
        private readonly VanillaNpcSpawnContext? context;
        private readonly VanillaUnifiedRandom1458 liveRandom;
        private readonly VanillaUnifiedRandom1458 beforeRandom;
        private bool completed;
        private NpcSnapshot? child;
        private NpcSnapshot? transformCheckpoint;

        internal AiSpawnPreview(RuntimeNpcStore owner, NpcSnapshot parent,
            VanillaNpcSpawnContext? context, VanillaUnifiedRandom1458 liveRandom,
            VanillaUnifiedRandom1458 beforeRandom, VanillaUnifiedRandom1458 afterRandom)
        {
            this.owner = owner;
            this.parent = parent;
            this.context = context;
            this.liveRandom = liveRandom;
            this.beforeRandom = beforeRandom;
            Random = afterRandom;
            beforeSerial = owner.mutationSerial;
        }

        internal VanillaUnifiedRandom1458 Random { get; }

        internal NpcSnapshot? TransformCheckpoint => transformCheckpoint;

        // Transform shares this preview's captured context and random stream, but
        // never runs NewNPC's allocation/Good-world prelude or a second AI phase.
        internal bool TryStageTransform(in NpcSnapshot working, NpcTypeId target,
            in NpcAiState suppliedTransformAi, VanillaNpcBehaviorContext behaviorContext,
            out NpcSnapshot transformed)
        {
            transformed = default;
            if (completed || transformCheckpoint is not null || working.Handle != parent.Handle ||
                context is not { IsValid: true } captured || !IsStageCurrent() ||
                !RuntimeNpcStateOwnershipPolicy.TryMaterializeTransform(in working, target, in captured,
                    in suppliedTransformAi, behaviorContext, out var checkpoint) || !IsStageCurrent())
                return false;
            transformed = checkpoint;
            transformCheckpoint = transformed;
            return true;
        }

        private bool IsStageCurrent()
        {
            if (completed || owner.mutationSerialExhausted || !owner.IsSpawnRandomSource(liveRandom) ||
                !liveRandom.HasSameState(beforeRandom)) return false;
            return owner.mutationSerial == beforeSerial && owner.MatchesSource(in parent) ||
                beforeSerial < ulong.MaxValue && parent.Revision.Value < ulong.MaxValue && IsCurrentOwnedState(
                    parent with { Revision = new(parent.Revision.Value + 1) });
        }

        internal bool IsBeforeCurrent() =>
            !completed && !owner.mutationSerialExhausted && owner.mutationSerial == beforeSerial &&
            owner.MatchesSource(in parent) && owner.IsSpawnRandomSource(liveRandom) &&
            liveRandom.HasSameState(beforeRandom) && owner._spawnContext?.Invoke() == context &&
            owner.mutationSerial == beforeSerial && owner.IsSpawnRandomSource(liveRandom) &&
            liveRandom.HasSameState(beforeRandom);

        internal bool TryStageHiveChild(in NpcAiSpawnIntent intent, out NpcSnapshot birth)
        {
            birth = default;
            if (completed || child is not null ||
                intent.Type != TerraRuntime.Contracts.Gameplay.VanillaNpcIds.Bee &&
                intent.Type != TerraRuntime.Contracts.Gameplay.VanillaNpcIds.SmallBee)
                return false;

            if (detached is null)
            {
                if (!IsBeforeCurrent())
                    return false;
                detached = new RuntimeNpcStore(owner.Capacity);
                owner._slots.CopyTo(detached._slots, 0);
                detached._activeCount = owner._activeCount;
                detached._spawnRandom = new SystemVanillaNpcRandom(Random);
                if (context is { } captured)
                    detached._spawnContext = () => captured;
            }

            if (!detached.TrySpawnIntent(in intent, out birth))
                return false;

            child = birth;
            return true;
        }

        internal bool TryFinishHiveChild(in NpcSnapshot birth, float velocityX, float velocityY, bool wet)
        {
            if (completed || child != birth || (detached is null || !detached.MatchesSource(in birth)) ||
                !float.IsFinite(velocityX) || !float.IsFinite(velocityY))
                return false;

            ref SlotState state = ref detached!._slots[birth.Handle.Slot];
            state.Update = state.Update with
            {
                VelocityX = velocityX,
                VelocityY = velocityY,
                Ai = state.Update.Ai with { Ai1 = 60f },
                Simulation = state.Update.Simulation with
                {
                    Wet = wet,
                    LocalAi = state.Update.Simulation.LocalAi with { Ai0 = 60f },
                    CanBeReplacedByOtherNpcs = true
                }
            };
            state.BirthPending = true;
            detached!.MarkSlotMutation();
            child = Capture(birth.Handle.Slot, in state);
            return true;
        }

        internal bool IsCurrent(in NpcSnapshot acceptedParent) => IsCurrent(in acceptedParent, direct: false);

        private bool IsCurrent(in NpcSnapshot acceptedParent, bool direct)
        {
            if (!IsCurrentOwnedState(in acceptedParent, direct) || owner._spawnContext?.Invoke() != context)
                return false;
            return IsCurrentOwnedState(in acceptedParent, direct);
        }

        // No caller-provided callbacks belong after the final captured-owner guard.
        private bool IsCurrentOwnedState(in NpcSnapshot acceptedParent, bool direct = false)
        {
            ulong offset = direct ? 0UL : 1UL;
            if (completed || owner.mutationSerialExhausted || !direct && beforeSerial == ulong.MaxValue ||
                owner.mutationSerial != beforeSerial + offset || acceptedParent.Handle != parent.Handle ||
                acceptedParent.Revision.Value != parent.Revision.Value + offset ||
                !owner.MatchesSource(in acceptedParent) || !owner.IsSpawnRandomSource(liveRandom) ||
                !liveRandom.HasSameState(beforeRandom))
                return false;

            var expected = new NpcStateUpdate(parent.Type, parent.NetId, parent.PositionX, parent.PositionY,
                parent.VelocityX, parent.VelocityY, parent.Target, parent.Ai, parent.Simulation);
            var retained = owner._slots[parent.Handle.Slot].Update;
            if (direct) retained = retained with { RequiresImmediateSync = false,
                VanillaNetworkPhase = false, RequiresLowPrioritySync = false };
            return retained == expected &&
                owner.mutationSerial == beforeSerial + offset && owner.IsSpawnRandomSource(liveRandom) &&
                liveRandom.HasSameState(beforeRandom);
        }

        internal bool TryAdopt(in NpcSnapshot acceptedParent, in NpcStateUpdate finalParent,
            Func<bool> ownedInputsCurrent,
            out NpcSnapshot final) => TryAdoptCore(in acceptedParent, in finalParent, ownedInputsCurrent, direct: false, out final);

        // Powder and caught-frog teleport begin from an unchanged live occupation,
        // rather than the AI executor's identical-state placeholder revision.
        internal bool TryAdoptDirect(in NpcStateUpdate finalParent, Func<bool> ownedInputsCurrent,
            out NpcSnapshot final) => TryAdoptCore(in parent, in finalParent, ownedInputsCurrent, direct: true, out final);

        private bool TryAdoptCore(in NpcSnapshot acceptedParent, in NpcStateUpdate finalParent,
            Func<bool> ownedInputsCurrent, bool direct, out NpcSnapshot final)
        {
            final = default;
            ArgumentNullException.ThrowIfNull(ownedInputsCurrent);
            // The public spawn-context provider may reenter other domain owners.
            // Sample it before validating those owners, then adopt without calling it again.
            if (direct && child is not null || !IsValid(in finalParent) ||
                transformCheckpoint is { } checkpoint &&
                    (finalParent.Type != checkpoint.Type || finalParent.NetId != checkpoint.NetId ||
                     finalParent.Simulation.HitboxOverride != checkpoint.Simulation.HitboxOverride ||
                     finalParent.Simulation.Scale != checkpoint.Simulation.Scale) ||
                direct && transformCheckpoint is null &&
                    (finalParent.Type != parent.Type || finalParent.NetId != parent.NetId) ||
                !IsCurrent(in acceptedParent, direct) ||
                !ownedInputsCurrent() || !IsCurrentOwnedState(in acceptedParent, direct))
                return false;

            ref SlotState parentState = ref owner._slots[parent.Handle.Slot];
            if (parentState.Revision == ulong.MaxValue)
                return false;

            NpcSnapshot? replacedPending = null;
            if (child is { } born)
            {
                int slot = born.Handle.Slot;
                if (slot == parent.Handle.Slot || (detached is null || !detached._slots[slot].BirthPending))
                    return false;
                if (owner._slots[slot].Active && owner._slots[slot].BirthPending)
                    replacedPending = Capture((byte)slot, in owner._slots[slot]);
                if (!owner._slots[slot].Active)
                    owner._activeCount++;
                owner._slots[slot] = detached!._slots[slot];
            }

            parentState.Update = transformCheckpoint is not null ? finalParent :
                RuntimeNpcStateOwnershipPolicy.PreserveUnownedUpdateState(in finalParent, in parentState.Update);
            if (direct) parentState.Update = parentState.Update with { RequiresImmediateSync = false,
                VanillaNetworkPhase = false, RequiresLowPrioritySync = false };
            parentState.Revision++;
            owner.MarkSlotMutation();
            final = Capture(parent.Handle.Slot, in parentState);
            liveRandom.CopyStateFrom(Random);
            completed = true;

            // Replacement's old generation is announced before any later child update. Adopt all
            // owned state and the stream first so observers cannot invalidate a half-finished birth.
            if (replacedPending is { } previous)
                owner._commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in previous);
            if (child is { } retainedChild && owner._commitSink is INpcBirthRetentionSink retained)
                retained.NpcBirthRetained(in retainedChild);
            return true;
        }

    }
}
