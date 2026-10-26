using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Projectiles;

public sealed partial class RuntimeProjectileStore
{
    internal CraftRemovalPreparation PrepareCraftRemovals(byte spawner, ProjectileTypeId type)
        => new(this, spawner, type);

    // One retained, bounded census on the existing projectile owner. It is not a
    // second command queue. Each scan advance is charged by the caller's journal.
    internal sealed class CraftRemovalPreparation : IDisposable
    {
        private readonly RuntimeProjectileStore owner;
        private readonly byte spawner;
        private readonly ProjectileTypeId type;
        private SlotState[]? before;
        private readonly List<ProjectileSnapshot> removals = [];
        private readonly int activeCount;
        private int scanned;
        private int published;
        private bool adopted;

        internal CraftRemovalPreparation(RuntimeProjectileStore owner, byte spawner, ProjectileTypeId type)
        {
            this.owner = owner;
            this.spawner = spawner;
            this.type = type;
            before = owner._slots.ToArray();
            activeCount = owner._activeCount;
        }

        internal int Count => removals.Count;
        internal ProjectileTypeId Type => type;
        internal bool TryGetCaptured(ushort slot, out ProjectileSnapshot snapshot)
        {
            snapshot = default;
            if (before is null || slot >= scanned || !before[slot].Active) return false;
            snapshot = Capture(slot, in before[slot]);
            return true;
        }

        internal bool MoveNext()
        {
            if (before is null || adopted || scanned >= before.Length) return false;
            int slot = scanned++;
            // Vanilla's source loop excludes physical overflow slot1000.
            if (slot < VanillaPhysicalSlotCount && before[slot].Active &&
                before[slot].Update.Spawner == spawner && before[slot].Update.Type == type)
                removals.Add(Capture((ushort)slot, in before[slot]));
            return true;
        }

        internal bool IsCurrentOwned
        {
            get
            {
                if (before is null || adopted || scanned != before.Length) return false;
                for (int slot = 0; slot < before.Length; slot++)
                {
                    ref readonly SlotState captured = ref before[slot];
                    ref readonly SlotState current = ref owner._slots[slot];
                    if (type.Value == 0)
                    {
                        // Read-only consumers choose their own relevant geometry scope.
                        if (!Same(in captured, in current)) return false;
                        continue;
                    }
                    bool wasSelected = IsSelected(slot, in captured);
                    bool isSelected = IsSelected(slot, in current);
                    if (wasSelected != isSelected) return false;
                    if (wasSelected && (captured.Generation != current.Generation ||
                        captured.CombatTrusted != current.CombatTrusted || captured.CombatTrustedOwner != current.CombatTrustedOwner ||
                        captured.SourceNpc != current.SourceNpc)) return false;
                }
                return true;
            }
        }

        internal bool IsCurrentContainerCensus(ReadOnlySpan<ProjectileTypeId> relevantTypes)
        {
            if (before is null || adopted || scanned != before.Length || relevantTypes.IsEmpty) return false;
            for (int slot = 0; slot < Math.Min(before.Length, VanillaPhysicalSlotCount); slot++)
            {
                ref readonly SlotState captured = ref before[slot];
                ref readonly SlotState current = ref owner._slots[slot];
                bool wasMember = captured.Active && relevantTypes.Contains(captured.Update.Type);
                bool isMember = current.Active && relevantTypes.Contains(current.Update.Type);
                if (wasMember != isMember) return false;
                if (wasMember && (captured.Generation != current.Generation || captured.Update.Type != current.Update.Type ||
                    captured.Update.PositionX != current.Update.PositionX || captured.Update.PositionY != current.Update.PositionY)) return false;
            }
            return true;
        }

        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrentOwned) return false;
            for (int index = 0; index < removals.Count; index++)
            {
                // Source selection does not depend on position/velocity/lifetime.
                // Re-read the full guarded preimage after all membership checks,
                // then adopt without callbacks; packet29 uses this final pose.
                ref SlotState state = ref owner._slots[removals[index].Handle.Slot];
                removals[index] = Capture(removals[index].Handle.Slot, in state);
                state.Active = false;
                state.Revision = 0;
                state.Update = default;
                state.Lifecycle = default;
                state.CombatTrusted = false;
                state.CombatTrustedOwner = default;
            }
            owner._activeCount -= removals.Count;
            adopted = true;
            return true;
        }

        internal bool TryPublishNext()
        {
            if (before is null || !adopted || published >= removals.Count) return false;
            var snapshot = removals[published++];
            ref readonly SlotState state = ref owner._slots[snapshot.Handle.Slot];
            // Consume before callbacks. Reentry cannot resurrect or kill a new generation.
            if (state.Active || state.Generation != snapshot.Handle.Generation.Value) return true;
            owner._commitSink?.ProjectileStateCommitted(ProjectileStateCommitKind.Despawn, in snapshot);
            return true;
        }

        public void Dispose() { before = null; removals.Clear(); }

        private bool IsSelected(int slot, in SlotState state) => slot < VanillaPhysicalSlotCount &&
            state.Active && state.Update.Spawner == spawner && state.Update.Type == type;

        private static bool Same(in SlotState left, in SlotState right) =>
            left.Active == right.Active && left.Generation == right.Generation &&
            left.Revision == right.Revision && left.Update == right.Update &&
            left.Lifecycle == right.Lifecycle && left.CombatTrusted == right.CombatTrusted &&
            left.CombatTrustedOwner == right.CombatTrustedOwner && left.SourceNpc == right.SourceNpc && left.SourceDirection == right.SourceDirection;
    }
}
