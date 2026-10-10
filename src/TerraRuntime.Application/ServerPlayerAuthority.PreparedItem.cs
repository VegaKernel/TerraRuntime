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
        out PreparedItem? plan)
    {
        plan = null;
        if (!TryGetPlayer(id, out var player) || player != expected.Player ||
            !states.TryPrepareItem(expected, oldItem, next, out var state))
        {
            return false;
        }
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

        internal bool IsCurrent =>
            owner.TryGetPlayer(id, out var current) && current == player && state.IsCurrent;

        internal bool IsAcceptedCurrent =>
            owner.TryGetPlayer(id, out var current) && current == player && state.IsAcceptedCurrent;

        internal bool TryAdoptUnpublished() => IsCurrent && state.TryAdoptUnpublished();

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
