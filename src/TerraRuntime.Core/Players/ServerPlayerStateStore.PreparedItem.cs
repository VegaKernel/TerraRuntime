using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Players;

public sealed partial class ServerPlayerStateStore
{
    internal bool TryPrepareItem(
        in PlayerStateSnapshot expected,
        in ServerPlayerItemState oldItem,
        in ServerPlayerItemState next,
        out ItemPreparation? plan)
    {
        plan = null;
        if (!TryGetState(expected.Player, out var state) ||
            state.CaptureSnapshot() != expected || state.Revision == ulong.MaxValue ||
            !TryNormalizeItem(next, out var normalized) || normalized.Slot != oldItem.Slot ||
            !TryGetItem(expected.Player, oldItem.Slot, out var current) || current != oldItem)
        {
            return false;
        }

        plan = new(this, expected, normalized);
        return true;
    }

    internal sealed class ItemPreparation
    {
        private readonly ServerPlayerStateStore owner;
        private readonly ServerPlayerRuntimeState state;
        private readonly PlayerStateSnapshot before;
        private readonly KeyValuePair<short, ServerPlayerItemState>[] original;
        private readonly Dictionary<short, ServerPlayerItemState>? preparedItems;
        private readonly Dictionary<short, ServerPlayerItemState>? originalItems;
        private readonly int acceptedItemCount;
        private PlayerStateSnapshot accepted;
        private bool adopted;

        internal ItemPreparation(ServerPlayerStateStore owner, PlayerStateSnapshot before, ServerPlayerItemState next)
        {
            if (!owner.TryGetState(before.Player, out var captured))
                throw new InvalidOperationException("Prepared server-player source is missing.");
            this.owner = owner;
            state = captured;
            this.before = before;
            Next = next;
            // Retain the bounded owned item dictionary, including empty-slot absence.
            // Exact reference, generation and snapshot revision guards also catch same-value writes.
            originalItems = state.Items;
            original = state.Items?.ToArray() ?? [];
            // Build the complete next dictionary before either producer owner is adopted.
            // Store queries expose values, not a live dictionary identity. Adoption only swaps this reference.
            var working = state.Items is null
                ? new Dictionary<short, ServerPlayerItemState>()
                : new Dictionary<short, ServerPlayerItemState>(state.Items);
            if (next.IsEmpty)
                working.Remove(next.Slot);
            else
                working[next.Slot] = next;
            preparedItems = working.Count == 0 ? null : working;
            acceptedItemCount = working.Count;
        }

        internal ServerPlayerItemState Next { get; }
        internal bool IsCurrent => !adopted && SameActor(before) && SameItems(after: false);
        internal bool IsAcceptedCurrent => adopted && SameActor(accepted) && SameItems(after: true);

        private bool SameActor(PlayerStateSnapshot expected) =>
            owner.TryGetState(before.Player, out var current) && ReferenceEquals(current, state) &&
            state.CaptureSnapshot() == expected;

        private bool SameItems(bool after)
        {
            if (!ReferenceEquals(state.Items, after ? preparedItems : originalItems) ||
                (state.Items?.Count ?? 0) != (after ? acceptedItemCount : original.Length))
                return false;
            foreach (var pair in original)
            {
                if (after && pair.Key == Next.Slot)
                    continue;
                if (state.Items is null || !state.Items.TryGetValue(pair.Key, out var value) || value != pair.Value)
                    return false;
            }
            return !after || (Next.IsEmpty
                ? state.Items?.ContainsKey(Next.Slot) != true
                : state.Items is not null && state.Items.TryGetValue(Next.Slot, out var final) && final == Next);
        }

        internal bool TryAdoptUnpublished()
        {
            if (!IsCurrent)
                return false;
            state.Revision++;
            state.Items = preparedItems;
            accepted = state.CaptureSnapshot();
            adopted = true;
            return true;
        }
    }
}
