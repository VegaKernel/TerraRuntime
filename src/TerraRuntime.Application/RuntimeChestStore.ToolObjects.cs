using TerraRuntime.World;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeChestStore
{
    internal bool TryCaptureSourceContainerPermission(int x, int y, out SourceContainerPermission? permission)
    {
        permission = null;
        if (craftingRevision == ulong.MaxValue || !TryCaptureAt(x, y, out var chest) ||
            custodyQuantity[chest.SlotId] is not { } quantity || custodyCopper[chest.SlotId] is not { } copper ||
            quantity.Length != chest.Items.Length || copper.Length != chest.Items.Length)
            return false;
        permission = new(this, chest, quantity, copper);
        return permission.IsCurrentOwned;
    }
    internal sealed class SourceContainerPermission
    {
        private readonly RuntimeChestStore owner;
        private readonly WorldChest chest;
        private readonly ulong revision;
        private readonly WorldChestItem[] items;
        private readonly short[] quantity;
        private readonly long[] copper;
        internal SourceContainerPermission(RuntimeChestStore owner, WorldChest chest, short[] quantity, long[] copper)
        {
            this.owner = owner; this.chest = chest; revision = owner.craftingRevision;
            items = (WorldChestItem[])chest.Items.Clone(); this.quantity = (short[])quantity.Clone(); this.copper = (long[])copper.Clone();
            CanDestroy = true;
            foreach (var item in items) if (item.ItemType > 0 && item.Stack > 0) { CanDestroy = false; break; }
        }
        // Source Chest.CanDestroyChest ignores open-session ownership; SERVER34 does not.
        internal bool CanDestroy { get; }
        internal bool CanRemoveClosed => IsCurrentOwned && owner.CanRemoveAt(chest.X, chest.Y);
        internal short ChestId => chest.SlotId;
        internal int AnchorX => chest.X;
        internal int AnchorY => chest.Y;
        internal bool IsCurrentOwned => owner.craftingRevision == revision &&
            owner.TryCaptureAt(chest.X, chest.Y, out var current) && ReferenceEquals(chest, current) &&
            current.Items.AsSpan().SequenceEqual(items) &&
            owner.custodyQuantity[chest.SlotId] is { } q && q.AsSpan().SequenceEqual(quantity) &&
            owner.custodyCopper[chest.SlotId] is { } c && c.AsSpan().SequenceEqual(copper);
    }

    internal ToolObjectRemovalPreparation? TryPrepareToolObjectRemovals() =>
        craftingRevision == ulong.MaxValue ? null : new(this);

    /// <summary>One action's complete empty-container metadata removals. No tile or observer writes.</summary>
    internal sealed class ToolObjectRemovalPreparation
    {
        private readonly RuntimeChestStore owner;
        private readonly ulong beforeRevision;
        private readonly List<(short Slot, WorldChest Chest, WorldChestItem[] Items, short[] Quantity, long[] Copper)> removals = [];
        private bool adopted;
        internal ToolObjectRemovalPreparation(RuntimeChestStore owner)
        { this.owner = owner; beforeRevision = owner.craftingRevision; }
        internal bool TryStage(int x, int y)
        {
            if (!IsCurrent || removals.Count == ToolWorldTileOverlay1458.MaximumChangedCells ||
                beforeRevision > ulong.MaxValue - (ulong)removals.Count - 1 ||
                !owner.TryGetRemovableChest(x, y, out short slot, out WorldChest chest) ||
                removals.Exists(row => row.Slot == slot)) return false;
            var quantity = owner.custodyQuantity[slot];
            var copper = owner.custodyCopper[slot];
            if (quantity is null || copper is null || quantity.Length != chest.Items.Length || copper.Length != chest.Items.Length)
                return false;
            // Empty physical metadata must not discard hidden funded custody.
            for (int index = 0; index < quantity.Length; index++)
                if (quantity[index] != 0 || copper[index] != 0) return false;
            removals.Add((slot, chest, (WorldChestItem[])chest.Items.Clone(), (short[])quantity.Clone(), (long[])copper.Clone()));
            return true;
        }
        internal bool IsCurrent
        {
            get
            {
                if (adopted || owner.craftingRevision != beforeRevision) return false;
                foreach (var row in removals)
                    if (!owner.TryGetRemovableChest(row.Chest.X, row.Chest.Y, out short slot, out var current) ||
                        slot != row.Slot || !ReferenceEquals(current, row.Chest) ||
                        !current.Items.AsSpan().SequenceEqual(row.Items) ||
                        owner.custodyQuantity[slot] is not { } quantity || !quantity.AsSpan().SequenceEqual(row.Quantity) ||
                        owner.custodyCopper[slot] is not { } copper || !copper.AsSpan().SequenceEqual(row.Copper)) return false;
                return true;
            }
        }
        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent) return false;
            foreach (var row in removals)
            {
                owner.chestByCoordinates.Remove(GetCoordinateKey(row.Chest.X, row.Chest.Y));
                owner.chests[row.Slot] = null;
                owner.custodyQuantity[row.Slot] = null;
                owner.custodyCopper[row.Slot] = null;
                owner.owners[row.Slot] = default;
                owner.craftingRevision++;
            }
            adopted = true;
            return true;
        }
        internal bool IsAcceptedImageCurrent
        {
            get
            {
                if (!adopted || owner.craftingRevision != beforeRevision + (ulong)removals.Count) return false;
                foreach (var row in removals)
                    if (owner.chests[row.Slot] is not null || owner.chestByCoordinates.ContainsKey(GetCoordinateKey(row.Chest.X, row.Chest.Y)))
                        return false;
                return true;
            }
        }
        internal bool HasSameSourceEffects(ToolObjectRemovalPreparation other)
        {
            if (!ReferenceEquals(owner, other.owner) || beforeRevision != other.beforeRevision || removals.Count != other.removals.Count) return false;
            for (int index = 0; index < removals.Count; index++)
            {
                var a = removals[index]; var b = other.removals[index];
                if (a.Slot != b.Slot || !ReferenceEquals(a.Chest, b.Chest) ||
                    !a.Items.AsSpan().SequenceEqual(b.Items) || !a.Quantity.AsSpan().SequenceEqual(b.Quantity) ||
                    !a.Copper.AsSpan().SequenceEqual(b.Copper)) return false;
            }
            return true;
        }
    }
}
