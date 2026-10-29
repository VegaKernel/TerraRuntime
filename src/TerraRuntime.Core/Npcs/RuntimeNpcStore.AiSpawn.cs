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
        private NpcSnapshot? publicationParent, replacementPublication, birthPublication;

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

            return TryStageChild(in intent, out birth);
        }

        // AI075's NewNPC allocation is evaluated before following the new mount.
        // The physical slot table and all NewNPC entropy stay in this same preview.
        internal bool TryStageScutlixMount(in NpcAiSpawnIntent intent, out NpcSnapshot birth)
        {
            birth = default;
            if (completed || child is not null || parent.TypeIdentity != VanillaNpcIds.ScutlixRider ||
                intent.Type != VanillaNpcIds.Scutlix || intent.StartSlot != parent.Handle.Slot ||
                intent.Target != byte.MaxValue || intent.InitialAi != default || intent.InitialLocalAi != default)
                return false;
            if (!TryStageChild(in intent, out birth)) return false;
            ref SlotState state = ref detached!._slots[birth.Handle.Slot];
            state.BirthPending = true;
            detached.MarkSlotMutation();
            child = birth = Capture(birth.Handle.Slot, in state);
            return true;
        }

        internal bool TryStageMartianChild(in NpcAiSpawnIntent intent, out NpcSnapshot birth)
        {
            birth = default;
            bool shield = parent.TypeIdentity == VanillaNpcIds.MartianOfficer && intent.Type == VanillaNpcIds.ForceBubble;
            bool turret = parent.TypeIdentity == VanillaNpcIds.MartianEngineer && intent.Type == VanillaNpcIds.MartianTurret;
            if (completed || child is not null || (!shield && !turret) || intent.NetIdOverride is not null ||
                intent.StartSlot != (shield ? parent.Handle.Slot : 0) || !TryStageChild(in intent, out birth)) return false;
            ref SlotState state = ref detached!._slots[birth.Handle.Slot];
            state.BirthPending = true;
            detached.MarkSlotMutation();
            child = birth = Capture(birth.Handle.Slot, in state);
            return true;
        }

        // Wet belongs to NewNPC's original birth body; a later source AI assignment
        // can then change placement/ai without running allocation or wet collision again.
        internal bool TryFinishMartianChild(in NpcSnapshot birth, float positionX, float positionY,
            in NpcAiState ai, bool wet, out NpcSnapshot finished)
        {
            finished = default;
            bool shield = parent.TypeIdentity == VanillaNpcIds.MartianOfficer && birth.TypeIdentity == VanillaNpcIds.ForceBubble;
            bool turret = parent.TypeIdentity == VanillaNpcIds.MartianEngineer && birth.TypeIdentity == VanillaNpcIds.MartianTurret;
            if (completed || child != birth || (!shield && !turret) || !float.IsFinite(positionX) ||
                !float.IsFinite(positionY) || !ai.IsValidFor(birth.TypeIdentity) ||
                detached is null || !detached.MatchesSource(in birth)) return false;
            ref SlotState state = ref detached._slots[birth.Handle.Slot];
            state.Update = state.Update with { PositionX = positionX, PositionY = positionY, Ai = ai,
                Simulation = state.Update.Simulation with { Wet = wet } };
            detached.MarkSlotMutation();
            child = finished = Capture(birth.Handle.Slot, in state);
            return true;
        }

        private bool TryStageChild(in NpcAiSpawnIntent intent, out NpcSnapshot birth)
        {
            birth = default;

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

        internal bool TryFinishScutlixMount(in NpcSnapshot birth, bool wet, out NpcSnapshot finished)
        {
            finished = default;
            if (completed || child != birth || birth.TypeIdentity != VanillaNpcIds.Scutlix ||
                detached is null || !detached.MatchesSource(in birth)) return false;
            ref SlotState state = ref detached._slots[birth.Handle.Slot];
            state.Update = state.Update with { Simulation = state.Update.Simulation with { Wet = wet } };
            detached.MarkSlotMutation();
            child = finished = Capture(birth.Handle.Slot, in state);
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
            out NpcSnapshot final) => TryAdoptCore(in acceptedParent, in finalParent, ownedInputsCurrent,
                direct: false, publish: true, out final);

        // Retained multi-owner phases adopt status/projectiles before exposing any birth observer.
        internal bool TryAdoptUnpublished(in NpcSnapshot acceptedParent, in NpcStateUpdate finalParent,
            Func<bool> ownedInputsCurrent, out NpcSnapshot final) =>
            TryAdoptCore(in acceptedParent, in finalParent, ownedInputsCurrent, direct: false, publish: false, out final);

        // Powder and caught-frog teleport begin from an unchanged live occupation,
        // rather than the AI executor's identical-state placeholder revision.
        internal bool TryAdoptDirect(in NpcStateUpdate finalParent, Func<bool> ownedInputsCurrent,
            out NpcSnapshot final) => TryAdoptCore(in parent, in finalParent, ownedInputsCurrent,
                direct: true, publish: true, out final);

        private bool TryAdoptCore(in NpcSnapshot acceptedParent, in NpcStateUpdate finalParent,
            Func<bool> ownedInputsCurrent, bool direct, bool publish, out NpcSnapshot final)
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

            publicationParent = final;
            replacementPublication = replacedPending;
            birthPublication = child;
            if (publish) PublishBirths();
            return true;
        }

        internal void PublishBirths()
        {
            if (publicationParent is not { } accepted) return;
            var previous = replacementPublication;
            var birth = birthPublication;
            // Consume before callbacks. Nested publication cannot replay an earlier generation's journal.
            publicationParent = replacementPublication = birthPublication = null;
            if (!owner.MatchesSource(in accepted)) return;
            if (birth is { } born && !owner.MatchesSource(in born)) return;
            if (previous is { } replaced)
                owner._commitSink?.NpcStateCommitted(NpcStateCommitKind.Spawn, in replaced);
            if (birth is { } retainedChild && owner.MatchesSource(in accepted) &&
                owner.MatchesSource(in retainedChild) && owner._commitSink is INpcBirthRetentionSink retained)
                retained.NpcBirthRetained(in retainedChild);
        }

    }
}
