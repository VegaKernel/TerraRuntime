using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
namespace TerraRuntime.Core.Players;
public sealed partial class ServerPlayerStateStore
{
    public bool TrySetItemAnimation(PlayerHandle player, float rotation, int animation)
    {
        if (!float.IsFinite(rotation) || animation is < 0 or > short.MaxValue ||
            !TryGetState(player, out ServerPlayerRuntimeState? state) || state.Revision == ulong.MaxValue) return false;
        state.Revision++;
        state.ItemRotation = rotation;
        state.ItemAnimation = state.IsDead ? 0 : animation;
        return true;
    }
    public void TickItemAnimation()
    {
        foreach (ServerPlayerRuntimeState? state in states)
        {
            if (state is null || state.ItemAnimation <= 0 || state.Revision == ulong.MaxValue) continue;
            state.Revision++;
            int decrement = (state.ControlFlags & (1 << 5)) == 0 && state.Items is not null &&
                state.Items.TryGetValue(state.SelectedItem, out ServerPlayerItemState item) &&
                item.ItemType == VanillaItemIds.Revolver ? 2 : 1;
            state.ItemAnimation = state.IsDead ? 0 : Math.Max(0, state.ItemAnimation - decrement);
        }
    }
}
