using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Core.Players;

public sealed partial class ServerPlayerStateStore
{
    internal readonly record struct ItemUsePresentation(byte SelectedItem, bool UseItem, int Direction, float Rotation, int Animation);
    internal bool TryPrepareItem(
        in PlayerStateSnapshot expected,
        in ServerPlayerItemState oldItem,
        in ServerPlayerItemState next,
        out ItemPreparation? plan,
        ServerPlayerVitalsState? vitals = null, ItemUsePresentation? presentation = null)
    {
        return TryPrepareItems(expected, [oldItem], [next], out plan, vitals, presentation);
    }

    // Use the existing complete bounded player slot space; callers own narrower producer policies.
    internal const int MaximumPreparedItemChanges = VanillaPlayerItemSlotCatalog.Count;

    internal bool TryPrepareItems(
        in PlayerStateSnapshot expected,
        ReadOnlySpan<ServerPlayerItemState> oldItems,
        ReadOnlySpan<ServerPlayerItemState> nextItems,
        out ItemPreparation? plan,
        ServerPlayerVitalsState? vitals = null, ItemUsePresentation? presentation = null)
    {
        plan = null;
        if (oldItems.Length == 0 || oldItems.Length > MaximumPreparedItemChanges ||
            oldItems.Length != nextItems.Length || !TryGetState(expected.Player, out var state) ||
            state.CaptureSnapshot() != expected || state.Revision == ulong.MaxValue)
            return false;

        if (presentation is { } use && (use.SelectedItem >= 10 || use.Direction is not (-1 or 1) ||
            !float.IsFinite(use.Rotation) || use.Animation is < 1 or > short.MaxValue || expected.IsDead))
            return false;
        var normalizedItems = new ServerPlayerItemState[nextItems.Length];
        for (int i = 0; i < nextItems.Length; i++)
        {
            if (!TryNormalizeItem(nextItems[i], out var normalized) || normalized.Slot != oldItems[i].Slot ||
                !TryGetItem(expected.Player, oldItems[i].Slot, out var current) || current != oldItems[i])
                return false;
            for (int previous = 0; previous < i; previous++)
                if (normalizedItems[previous].Slot == normalized.Slot)
                    return false;
            normalizedItems[i] = normalized;
        }
        ServerPlayerVitalsState? normalizedVitals = null;
        if (vitals is { } value)
        {
            var health = new PlayerHealthCommitRequest(expected.Player.Slot, value.Life, value.MaxLife);
            var acceptedHealth = VanillaVitalsRules.NormalizeHealth(health);
            normalizedVitals = value with { Life = acceptedHealth.Life, MaxLife = acceptedHealth.MaxLife };
        }
        plan = new(this, expected, normalizedItems, normalizedVitals, presentation);
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
        private readonly KeyValuePair<short, ServerPlayerItemState>[] acceptedItems;
        private readonly ServerPlayerItemState[] nextItems;
        private PlayerStateSnapshot accepted;
        private bool adopted;

        internal ItemPreparation(ServerPlayerStateStore owner, PlayerStateSnapshot before, ServerPlayerItemState[] nextItems, ServerPlayerVitalsState? vitals, ItemUsePresentation? presentation)
        {
            if (!owner.TryGetState(before.Player, out var captured))
                throw new InvalidOperationException("Prepared server-player source is missing.");
            this.owner = owner;
            state = captured;
            this.before = before;
            this.nextItems = nextItems;
            Vitals = vitals;
            Presentation = presentation;
            // Retain the bounded owned item dictionary, including empty-slot absence.
            // Exact reference, generation and snapshot revision guards also catch same-value writes.
            originalItems = state.Items;
            original = state.Items?.ToArray() ?? [];
            // Build the complete next dictionary before either producer owner is adopted.
            // Store queries expose values, not a live dictionary identity. Adoption only swaps this reference.
            var working = state.Items is null
                ? new Dictionary<short, ServerPlayerItemState>()
                : new Dictionary<short, ServerPlayerItemState>(state.Items);
            foreach (var next in nextItems)
            {
                if (next.IsEmpty)
                    working.Remove(next.Slot);
                else
                    working[next.Slot] = next;
            }
            preparedItems = working.Count == 0 ? null : working;
            acceptedItemCount = working.Count;
            acceptedItems = working.ToArray();
        }

        internal ServerPlayerItemState Next => nextItems[0];
        internal int ItemCount => nextItems.Length;
        internal ServerPlayerItemState GetNext(int index) => nextItems[index];
        internal ServerPlayerVitalsState? Vitals { get; }
        internal ItemUsePresentation? Presentation { get; }
        internal PlayerStateSnapshot AcceptedSnapshot => accepted;
        internal bool IsAcceptedMovementCurrent => adopted && Presentation.HasValue && SameIdentity() &&
            state.PositionX == accepted.PositionX && state.PositionY == accepted.PositionY &&
            state.VelocityX == accepted.VelocityX && state.VelocityY == accepted.VelocityY &&
            state.ControlFlags == accepted.ControlFlags && state.SelectedItem == accepted.SelectedItem;
        internal bool IsAcceptedPresentationCurrent => adopted && Presentation.HasValue && SameIdentity() &&
            state.ItemRotation == accepted.ItemRotation && state.ItemAnimation == accepted.ItemAnimation;
        internal bool IsCurrent => !adopted && SameActor(before) && SameItems(after: false);
        internal bool IsAcceptedCurrent => adopted && SameActor(accepted) && SameItems(after: true);

        // After adoption, unrelated component writes must not strand a still-current notification.
        internal bool IsAcceptedItemCurrent => IsAcceptedItemCurrentAt(0);

        internal bool IsAcceptedItemCurrentAt(int index) => adopted && SameIdentity() &&
            (uint)index < (uint)nextItems.Length &&
            owner.TryGetItem(before.Player, nextItems[index].Slot, out var item) && item == nextItems[index];

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
            if (after)
            {
                if (preparedItems is null)
                    return state.Items is null;
                foreach (var pair in acceptedItems)
                    if (state.Items is null || !state.Items.TryGetValue(pair.Key, out var value) || value != pair.Value)
                        return false;
                return true;
            }
            foreach (var pair in original)
                if (state.Items is null || !state.Items.TryGetValue(pair.Key, out var value) || value != pair.Value)
                    return false;
            return true;
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
            if (Presentation is { } presentation)
            {
                state.SelectedItem = presentation.SelectedItem;
                byte controls = (byte)(state.ControlFlags & ~((1 << 5) | (1 << 6)));
                if (presentation.UseItem) controls |= 1 << 5;
                if (presentation.Direction > 0) controls |= 1 << 6;
                state.ControlFlags = controls;
                state.ItemRotation = presentation.Rotation;
                state.ItemAnimation = presentation.Animation;
            }
            accepted = state.CaptureSnapshot();
            adopted = true;
            return true;
        }
    }
}
