using TerraRuntime.Core;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Players;

public sealed partial class ServerPlayerStateStore
{
    public bool TrySpawn(
        ServerPlayerId id,
        float positionX,
        float positionY,
        out PlayerStateSnapshot snapshot)
    {
        if (!id.IsAssigned ||
            !float.IsFinite(positionX) ||
            !float.IsFinite(positionY) ||
            !identities.TryGet(id, out ServerPlayerSlotBinding binding) ||
            binding.Player.Slot.Value >= states.Length)
        {
            snapshot = default;
            return false;
        }

        int slot = binding.Player.Slot.Value;
        ServerPlayerRuntimeState? current = states[slot];
        if (current is not null && current.Player == binding.Player)
        {
            snapshot = default;
            return false;
        }

        var state = new ServerPlayerRuntimeState
        {
            Id = id,
            Player = binding.Player,
            Revision = 1,
            PositionX = positionX,
            PositionY = positionY
        };
        states[slot] = state;
        snapshot = state.CaptureSnapshot();
        return true;
    }

    public bool TryRemove(PlayerHandle player, out PlayerStateSnapshot removed)
    {
        if (!TryGetState(player, out ServerPlayerRuntimeState? state))
        {
            removed = default;
            return false;
        }

        removed = state.CaptureSnapshot();
        states[player.Slot.Value] = null;
        return true;
    }
}
