using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Core.Players;

public sealed partial class ServerPlayerStateStore
{
    public bool TrySetHostile(
        PlayerHandle player,
        bool hostile,
        out PlayerStateSnapshot snapshot)
    {
        snapshot = default;
        if (!TryGetState(player, out ServerPlayerRuntimeState? state) ||
            state.Revision == ulong.MaxValue)
        {
            return false;
        }

        if (state.Hostile == hostile)
        {
            snapshot = state.CaptureSnapshot();
            return true;
        }

        state.Revision++;
        state.Hostile = hostile;
        snapshot = state.CaptureSnapshot();
        return true;
    }
}
