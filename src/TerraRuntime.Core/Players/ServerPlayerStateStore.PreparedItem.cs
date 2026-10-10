using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Core.Players;

public sealed partial class ServerPlayerStateStore
{
    internal bool TryPrepareItem(
        in PlayerStateSnapshot expected,
        in ServerPlayerItemState oldItem,
        in ServerPlayerItemState next,
        out ItemPreparation? plan,
        ServerPlayerVitalsState? vitals = null)
    {
        plan = null;
        if (!TryGetState(expected.Player, out var state) ||
            state.CaptureSnapshot() != expected || state.Revision == ulong.MaxValue ||
            !TryNormalizeItem(next, out var normalized) || normalized.Slot != oldItem.Slot ||
            !TryGetItem(expected.Player, oldItem.Slot, out var current) || current != oldItem)
        {
            return false;
        }

        ServerPlayerVitalsState? normalizedVitals = null;
        if (vitals is { } value)
        {
            var health = new PlayerHealthCommitRequest(expected.Player.Slot, value.Life, value.MaxLife);
            var acceptedHealth = VanillaVitalsRules.NormalizeHealth(health);
            normalizedVitals = value with { Life = acceptedHealth.Life, MaxLife = acceptedHealth.MaxLife };
        }
        plan = new(this, expected, normalized, normalizedVitals);
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

        internal ItemPreparation(ServerPlayerStateStore owner, PlayerStateSnapshot before, ServerPlayerItemState next, ServerPlayerVitalsState? vitals)
        {
            if (!owner.TryGetState(before.Player, out var captured))
                throw new InvalidOperationException("Prepared server-player source is missing.");
            this.owner = owner;
            state = captured;
            this.before = before;
            Next = next;
            Vitals = vitals;
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
        internal ServerPlayerVitalsState? Vitals { get; }
        internal bool IsCurrent => !adopted && SameActor(before) && SameItems(after: false);
        internal bool IsAcceptedCurrent => adopted && SameActor(accepted) && SameItems(after: true);

        // After adoption, unrelated component writes must not strand a still-current notification.
        internal bool IsAcceptedItemCurrent => adopted && SameIdentity() &&
            owner.TryGetItem(before.Player, Next.Slot, out var item) && item == Next;

        internal bool IsAcceptedVitalsCurrent => adopted && Vitals.HasValue && SameIdentity() &&
            state.HasHealth == accepted.HasHealth && state.Life == accepted.Life &&
            state.MaxLife == accepted.MaxLife && state.BaseLifeMax == accepted.BaseLifeMax &&
            state.IsDead == accepted.IsDead && state.HasMana == accepted.HasMana &&
            state.Mana == accepted.Mana && state.MaxMana == accepted.MaxMana;

        private bool SameIdentity() => owner.TryGetState(before.Player, out var current) && ReferenceEquals(current, state);

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
            if (Vitals is { } vitals)
            {
                state.HasHealth = true;
                state.Life = vitals.Life;
                state.MaxLife = vitals.MaxLife;
                state.BaseLifeMax = vitals.MaxLife;
                state.IsDead = vitals.Life <= 0;
                state.HasMana = true;
                state.Mana = vitals.Mana;
                state.MaxMana = vitals.MaxMana;
            }
            accepted = state.CaptureSnapshot();
            adopted = true;
            return true;
        }
    }
}
