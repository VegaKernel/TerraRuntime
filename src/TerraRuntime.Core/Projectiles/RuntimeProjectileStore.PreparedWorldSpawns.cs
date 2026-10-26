using System.Buffers;
using System.Runtime.InteropServices;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Projectiles;

namespace TerraRuntime.Core.Projectiles;

public sealed partial class RuntimeProjectileStore
{
    internal WorldSpawnPreparation CreateWorldSpawnPreparation() => new(this);

    /// <summary>One bounded source WorldGen action's incrementally staged NewProjectile calls.</summary>
    internal sealed class WorldSpawnPreparation : IDisposable
    {
        internal const int MaximumCalls = 4096;
        private readonly RuntimeProjectileStore owner;
        private readonly SlotState[] baseline;
        private readonly SlotState[] working;
        private readonly int baselineActiveCount;
        private int activeCount;
        private readonly List<ProjectileSnapshot> births = [];
        private readonly HashSet<ushort> changed = [];
        private SimulationPreparation? sourceActor;
        private WorldSpawnActorImage1458? sourceActorViewBefore;
        private WorldSpawnActorImage1458? sourceActorView;
        private WorldSpawnActorImage1458 actorBefore;
        private WorldSpawnActorImage1458 actorAfter;
        private bool failed;
        private bool adopted;
        private bool disposed;

        internal WorldSpawnPreparation(RuntimeProjectileStore owner)
        {
            this.owner = owner;
            baseline = ArrayPool<SlotState>.Shared.Rent(owner.Capacity);
            working = ArrayPool<SlotState>.Shared.Rent(owner.Capacity);
            owner._slots.CopyTo(baseline, 0); owner._slots.CopyTo(working, 0);
            baselineActiveCount = activeCount = owner._activeCount;
        }
        internal bool HasSameSourceEffects(WorldSpawnPreparation other)
        {
            if (!ReferenceEquals(owner, other.owner) || failed || disposed || adopted ||
                other.failed || other.disposed || other.adopted || activeCount != other.activeCount ||
                baselineActiveCount != other.baselineActiveCount || !changed.SetEquals(other.changed) ||
                sourceActorViewBefore != other.sourceActorViewBefore || sourceActorView != other.sourceActorView ||
                (sourceActor is null) != (other.sourceActor is null) || actorBefore != other.actorBefore || actorAfter != other.actorAfter ||
                !CollectionsMarshal.AsSpan(births).SequenceEqual(CollectionsMarshal.AsSpan(other.births))) return false;
            for (int index = 0; index < owner.Capacity; index++)
                if (!SameSlot(in baseline[index], in other.baseline[index]) || !SameSlot(in working[index], in other.working[index])) return false;
            return true;
        }

        internal bool TrySetSourceActorView(SimulationPreparation phaseActor)
        {
            if (!phaseActor.TryCaptureWorldSpawnActor(out var before, out var view) ||
                !phaseActor.HasWorldSpawnProjection(owner, in before, in view)) return Fail();
            return TrySetSourceActorView(in before, in view);
        }
        internal bool TrySetSourceActorView(in ProjectileSnapshot expected, in ProjectileStateUpdate update,
            in ProjectileLifecycleState lifecycle, bool sourceActive)
        {
            if (!expected.Handle.IsAssigned || expected.Handle.Slot >= owner.Capacity || !IsValidState(in update)) return Fail();
            ushort slot = expected.Handle.Slot;
            ref readonly var original = ref baseline[slot];
            if (Capture(slot, in original) != expected || update.Type != original.Update.Type ||
                update.Spawner != original.Update.Spawner || lifecycle.NpcProjectile != original.Lifecycle.NpcProjectile ||
                lifecycle.HeldTool != original.Lifecycle.HeldTool || lifecycle.ThrownAxe != original.Lifecycle.ThrownAxe) return Fail();
            var before = CaptureActorImage(slot, in original);
            var view = before with { Active = sourceActive, Update = update, Lifecycle = lifecycle };
            return TrySetSourceActorView(in before, in view);
        }
        private bool TrySetSourceActorView(in WorldSpawnActorImage1458 before, in WorldSpawnActorImage1458 view)
        {
            if (!IsCurrentOwned || sourceActor is not null || before.Slot >= owner.Capacity ||
                before.Slot != view.Slot || before.Generation != view.Generation || changed.Contains(before.Slot) ||
                CaptureActorImage(before.Slot, in baseline[before.Slot]) != before ||
                sourceActorViewBefore is { } held && held != before) return Fail();
            sourceActorViewBefore = before; sourceActorView = view;
            ref var state = ref working[view.Slot];
            activeCount += (view.Active ? 1 : 0) - (state.Active ? 1 : 0);
            ApplyActorImage(ref state, in view);
            return IsCurrentOwned;
        }
        private static void ApplyActorImage(ref SlotState state, in WorldSpawnActorImage1458 image)
        {
            state.Active = image.Active; state.Generation = image.Generation; state.Revision = image.Revision;
            state.Update = image.Update; state.Lifecycle = image.Lifecycle;
            state.CombatTrusted = image.CombatTrusted; state.CombatTrustedOwner = image.TrustedOwner;
            state.SourceNpc = image.SourceNpc; state.SourceDirection = image.SourceDirection;
        }

        internal bool TryBindSourceActor(SimulationPreparation actor)
        {
            if (sourceActor is not null || !IsCurrentOwned ||
                !actor.TryCaptureWorldSpawnActor(out var before, out var after) ||
                !actor.HasWorldSpawnProjection(owner, in before, in after) ||
                before.Slot >= owner.Capacity || changed.Contains(before.Slot) ||
                CaptureActorImage(before.Slot, in baseline[before.Slot]) != before ||
                sourceActorViewBefore is { } held && held != before)
                return Fail();
            // A source full-pool child may select the updating actor. That needs
            // its own source-qualified displacement path; never overwrite such
            // a replacement with an old actor afterimage.
            sourceActor = actor; actorBefore = before; actorAfter = after;
            ref var final = ref working[after.Slot];
            activeCount += (after.Active ? 1 : 0) - (final.Active ? 1 : 0);
            ApplyActorImage(ref final, in after);
            return IsCurrentOwned;
        }

        internal bool TryGetProjected(ProjectileHandle handle, out ProjectileSnapshot snapshot)
        {
            snapshot = default;
            if (disposed || !handle.IsAssigned || handle.Slot >= owner.Capacity) return false;
            ref readonly var state = ref working[handle.Slot];
            if (!state.Active || state.Generation != handle.Generation.Value) return false;
            snapshot = Capture(handle.Slot, in state);
            return true;
        }

        internal bool TryHasProjectedFallingDuplicate(ProjectileTypeId type, float centerX, float centerY, out bool duplicate)
        {
            duplicate = false;
            if (disposed || !TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(type, out var definition)) return false;
            for (int index = 0; index < Math.Min(owner.Capacity, VanillaPhysicalSlotCount); index++)
            {
                ref readonly var slot = ref working[index];
                if (!slot.Active || slot.Update.Spawner != byte.MaxValue || slot.Update.Type != type ||
                    Math.Abs(slot.Lifecycle.TimeLeft - 3600L) >= 60) continue;
                float dx = slot.Update.PositionX + definition.Width * .5f - centerX;
                float dy = slot.Update.PositionY + definition.Height * .5f - centerY;
                float distance = OperatingSystem.IsWindows() ? (float)Math.Sqrt((double)dx * dx + (double)dy * dy) :
                    (float)Math.Sqrt(dx * dx + dy * dy);
                if (distance < 4f) { duplicate = true; return true; }
            }
            return true;
        }

        internal bool IsAcceptedImageCurrent
        {
            get
            {
                if (!adopted || disposed || owner._activeCount != activeCount) return false;
                for (int index = 0; index < owner.Capacity; index++)
                {
                    if (sourceActor is not null && index == actorAfter.Slot)
                    { if (CaptureActorImage((ushort)index, in owner._slots[index]) != actorAfter) return false; }
                    else if (!SameSlot(in working[index], in owner._slots[index])) return false;
                }
                return true;
            }
        }

        internal ReadOnlySpan<ProjectileSnapshot> Births => CollectionsMarshal.AsSpan(births);
        internal bool IsCurrentOwned
        {
            get
            {
                if (failed || adopted || disposed) return false;
                bool actorAdopted = false;
                if (sourceActor is not null)
                {
                    if (!sourceActor.HasWorldSpawnProjection(owner, in actorBefore, in actorAfter)) return false;
                    actorAdopted = sourceActor.IsPublicationCurrent;
                    if (!actorAdopted && !sourceActor.IsCurrent) return false;
                }
                int actorDelta = actorAdopted ? (actorAfter.Active ? 1 : 0) - (actorBefore.Active ? 1 : 0) : 0;
                if (owner._activeCount != baselineActiveCount + actorDelta) return false;
                for (int index = 0; index < owner.Capacity; index++)
                {
                    if (actorAdopted && index == actorAfter.Slot)
                    {
                        if (CaptureActorImage((ushort)index, in owner._slots[index]) != actorAfter) return false;
                    }
                    else if (!SameSlot(in baseline[index], in owner._slots[index])) return false;
                }
                return true;
            }
        }
        internal bool TryStage(in ProjectileStateUpdate update, bool npcProjectile,
            out ProjectileSnapshot birth)
        {
            birth = default;
            if (!IsCurrentOwned || sourceActor is not null || births.Count == MaximumCalls || update.Spawner != byte.MaxValue ||
                !IsWorldToolType(update.Type) || !IsValidState(in update) ||
                !TryCreateLifecycle(update.Type, out var lifecycle) ||
                !TrySelectVanillaAllocationSlot(working.AsSpan(0, owner.Capacity), out ushort slot) ||
                working[slot].Generation == ulong.MaxValue) return Fail();
            // Dedicated WorldGen births use source-local owner255, including NewProjectile fuse overrides.
            if (VanillaExplosiveProjectileFacts1458.TryGetPlayerOwnedSpawnTimeLeftOverride(update.Type, out int sourceTimeLeft))
                lifecycle = lifecycle with { TimeLeft = sourceTimeLeft };
            lifecycle = lifecycle with { NpcProjectile = npcProjectile };
            ref var next = ref working[slot];
            if (!next.Active) activeCount++;
            next.Generation++;
            InitializeSlot(ref next, in update, in lifecycle);
            // The actual WorldGen producer, not the untrusted packet, owns this birth.
            next.CombatTrusted = true;
            birth = Capture(slot, in next);
            births.Add(birth); changed.Add(slot);
            return true;
        }
        internal bool TryFinishSourceCall(in ProjectileSnapshot expected, in ProjectileStateUpdate after,
            out ProjectileSnapshot final)
        {
            final = default;
            if (!IsCurrentOwned || sourceActor is not null || births.Count == 0 || births[^1] != expected ||
                !TryGetProjected(expected.Handle, out var current) || current != expected ||
                !IsValidState(in after) || after.Type != expected.Type || after.Spawner != byte.MaxValue) return Fail();
            ref var state = ref working[expected.Handle.Slot];
            state.Update = after;
            final = Capture(expected.Handle.Slot, in state);
            // NewProjectile's immediate packet27 retains its PRE-caller tuple;
            // only the detached final slot receives source post-call writes.
            return true;
        }

        internal bool TryAdoptUnpublished(out WorldSpawnPublication? publication)
        {
            publication = null;
            if (!IsCurrentOwned || sourceActorView.HasValue && sourceActor is null ||
                sourceActor is not null && !sourceActor.IsPublicationCurrent) return false;
            publication = new(this);
            foreach (ushort index in changed) owner._slots[index] = working[index];
            owner._activeCount = activeCount;
            adopted = true;
            return true;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ArrayPool<SlotState>.Shared.Return(baseline);
            ArrayPool<SlotState>.Shared.Return(working);
        }
        private static WorldSpawnActorImage1458 CaptureActorImage(ushort slot, in SlotState state) =>
            new(slot, state.Active, state.Generation, state.Revision, state.Update, state.Lifecycle,
                state.CombatTrusted, state.CombatTrustedOwner, state.SourceNpc, state.SourceDirection);

        private bool Fail() { failed = true; return false; }
        private static bool IsWorldToolType(ProjectileTypeId type) => type.Value is
            28 or 31 or 39 or 56 or 67 or 71 or 75 or 179 or 241 or 411 or 412 or 413 or 414 or
            655 or 727 or 736 or 737 or 738 or 812;
        private static bool SameSlot(in SlotState before, in SlotState current) =>
            before.Active == current.Active && before.Generation == current.Generation &&
            before.Revision == current.Revision && before.Update == current.Update &&
            before.Lifecycle == current.Lifecycle && before.CombatTrusted == current.CombatTrusted &&
            before.CombatTrustedOwner == current.CombatTrustedOwner && before.SourceNpc == current.SourceNpc &&
            before.SourceDirection == current.SourceDirection;

        internal sealed class WorldSpawnPublication
        {
            private readonly RuntimeProjectileStore owner;
            private readonly Dictionary<ushort, SlotState> final;
            private readonly ProjectileSnapshot[] births;
            private int next;
            private bool publishing;
            internal WorldSpawnPublication(WorldSpawnPreparation preparation)
            {
                owner = preparation.owner;
                final = new();
                foreach (ushort index in preparation.changed) final.Add(index, preparation.working[index]);
                births = preparation.births.ToArray();
            }
            internal ReadOnlySpan<ProjectileSnapshot> Births => births;
            // Source NewProjectile can publish a pre-caller tuple different
            // from the accepted live slot. Compare the retained final image,
            // including private lineage, again inside a composed observer.
            internal bool IsPublicationCurrent(in ProjectileSnapshot birth) =>
                final.TryGetValue(birth.Handle.Slot, out var expected) &&
                SameSlot(in expected, in owner._slots[birth.Handle.Slot]);
            internal bool TryPublishNext()
            {
                if (publishing || next == births.Length) return false;
                var birth = births[next++];
                if (!final.TryGetValue(birth.Handle.Slot, out var expected) ||
                    !SameSlot(in expected, in owner._slots[birth.Handle.Slot])) return true;
                publishing = true;
                try { owner._commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, in birth); }
                finally { publishing = false; }
                return true;
            }
        }
    }
}
