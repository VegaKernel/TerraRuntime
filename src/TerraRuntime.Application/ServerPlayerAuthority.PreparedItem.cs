using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Players;

namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    internal bool TryPrepareItemMutation(
        ServerPlayerId id,
        in PlayerStateSnapshot expected,
        in ServerPlayerItemState oldItem,
        in ServerPlayerItemState next,
        out PreparedItem? plan,
        ServerPlayerVitalsState? vitals = null)
    {
        plan = null;
        if (!TryGetPlayer(id, out var player) || player != expected.Player ||
            !states.TryPrepareItem(expected, oldItem, next, out var state, vitals))
        {
            return false;
        }
        // This producer does not own the separate death-transition buff reset path.
        // Validate the normalized state supplied by the shared player owner.
        if (state!.Vitals is { } normalizedVitals && expected.IsDead != (normalizedVitals.Life <= 0))
            return false;
        plan = new(this, id, player, state);
        return true;
    }

    internal bool TryPrepareItemMutations(
        ServerPlayerId id,
        in PlayerStateSnapshot expected,
        ReadOnlySpan<ServerPlayerItemState> oldItems,
        ReadOnlySpan<ServerPlayerItemState> nextItems,
        out PreparedItem? plan)
    {
        plan = null;
        if (!TryGetPlayer(id, out var player) || player != expected.Player ||
            !states.TryPrepareItems(expected, oldItems, nextItems, out var state))
            return false;
        plan = new(this, id, player, state!);
        return true;
    }

    internal sealed class PreparedItem(
        ServerPlayerAuthority owner,
        ServerPlayerId id,
        PlayerHandle player,
        ServerPlayerStateStore.ItemPreparation state)
    {
        private bool published;
        private bool vitalsPublished;

        internal bool IsCurrent =>
            owner.TryGetPlayer(id, out var current) && current == player && state.IsCurrent;

        internal bool IsAcceptedCurrent =>
            owner.TryGetPlayer(id, out var current) && current == player && state.IsAcceptedCurrent;

        internal bool TryAdoptUnpublished() => IsCurrent && state.TryAdoptUnpublished();

        internal bool TryPublishConsumable()
        {
            try
            {
                return TryPublishItemComponent();
            }
            finally
            {
                TryPublishVitalsComponent();
            }
        }

        private bool OwnsIdentity => owner.TryGetPlayer(id, out var current) && current == player;

        private bool TryPublishItemComponent()
        {
            if (published || !OwnsIdentity || !state.IsAcceptedItemCurrent)
                return false;
            published = true;
            owner.events?.ServerPlayerItemUpdated(player, state.Next);
            return true;
        }

        private bool TryPublishVitalsComponent()
        {
            if (vitalsPublished || !OwnsIdentity || !state.IsAcceptedVitalsCurrent || state.Vitals is not { } vitals)
                return false;
            vitalsPublished = true;
            owner.events?.ServerPlayerVitalsUpdated(player, vitals);
            return true;
        }

        internal bool TryPublishItems()
        {
            if (published || !IsAcceptedCurrent)
                return false;
            published = true; // A reentrant or throwing observer cannot replay this accepted journal.
            PublishItemsFrom(0);
            return true;
        }

        private void PublishItemsFrom(int firstIndex)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo? failure = null;
            for (int index = firstIndex; index < state.ItemCount; index++)
            {
                try
                {
                    if (OwnsIdentity && state.IsAcceptedItemCurrentAt(index))
                        owner.events?.ServerPlayerItemUpdated(player, state.GetNext(index));
                }
                catch (Exception exception)
                {
                    // Consume the complete journal once; keep publishing other still-current components.
                    // Propagate the first observer failure after the bounded notification tail.
                    failure ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception);
                }
            }
            failure?.Throw();
        }

        internal bool TryPublish()
        {
            if (published || !IsAcceptedCurrent)
                return false;
            published = true; // Consume before any reentrant or throwing observer.
            var next = state.Next;
            owner.events?.ServerPlayerItemUpdated(player, next);
            return true;
        }
    }
}
