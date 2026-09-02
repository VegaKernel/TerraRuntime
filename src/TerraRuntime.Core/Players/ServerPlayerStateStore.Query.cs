using TerraRuntime.Core;
using System.Diagnostics.CodeAnalysis;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Players;

public sealed partial class ServerPlayerStateStore
{
    public bool TryGet(ServerPlayerId id, out PlayerStateSnapshot snapshot)
    {
        if (!identities.TryGet(id, out ServerPlayerSlotBinding binding))
        {
            snapshot = default;
            return false;
        }

        return TryGet(binding.Player, out snapshot);
    }

    public bool TryGet(PlayerHandle player, out PlayerStateSnapshot snapshot)
    {
        if (!player.IsAssigned ||
            player.Slot.Value >= states.Length ||
            !identities.TryGet(player, out ServerPlayerSlotBinding binding) ||
            binding.Player != player)
        {
            snapshot = default;
            return false;
        }

        ServerPlayerRuntimeState? state = states[player.Slot.Value];
        if (state is null || state.Player != player)
        {
            snapshot = default;
            return false;
        }

        snapshot = state.CaptureSnapshot();
        return true;
    }

    /// <summary>
    /// Copies live server-owned player snapshots in ascending wire-slot order without allocating. Stale state whose
    /// identity lease has already been released is deliberately skipped even if its storage slot has not been cleared.
    /// </summary>
    public int CopySnapshots(Span<PlayerStateSnapshot> destination)
    {
        int written = 0;
        for (int slot = 0; slot < states.Length && written < destination.Length; slot++)
        {
            ServerPlayerRuntimeState? state = states[slot];
            if (state is null ||
                !identities.TryGet(state.Player, out ServerPlayerSlotBinding binding) ||
                binding.Player != state.Player)
            {
                continue;
            }

            destination[written++] = state.CaptureSnapshot();
        }

        return written;
    }

    public bool TryGet(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
    {
        if (slot.Value >= states.Length)
        {
            snapshot = default;
            return false;
        }

        ServerPlayerRuntimeState? state = states[slot.Value];
        if (state is null)
        {
            snapshot = default;
            return false;
        }

        return TryGet(state.Player, out snapshot);
    }

    private bool TryGetState(
        PlayerHandle player,
        [NotNullWhen(true)] out ServerPlayerRuntimeState? state)
    {
        if (!player.IsAssigned ||
            player.Slot.Value >= states.Length ||
            !identities.TryGet(player, out ServerPlayerSlotBinding binding) ||
            binding.Player != player)
        {
            state = null;
            return false;
        }

        state = states[player.Slot.Value];
        return state is not null && state.Player == player;
    }
}
