using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    private void ApplyPlayerItemAnimation(PlayerItemAnimationRuntimeCommand command)
    {
        if (!TerrariaPlayerItemAnimationCodec1458.IsValid(command.Rotation, command.Animation) ||
            !membership.TryGet(command.Connection, out RuntimePlayerMember? member) ||
            !member.TryAdvanceRevision())
            return;
        member.ItemRotation = command.Rotation;
        member.ItemAnimation = member.IsDead ? 0 : command.Animation;
        events?.PlayerItemAnimationUpdated(command.Connection, member.ItemRotation, checked((short)member.ItemAnimation));
    }

    internal void TickItemAnimation()
    {
        foreach (RuntimePlayerMember member in membership.Members)
        {
            if (member.IsDead) membership.TrySetTalkNpc(member.Connection, TerrariaNpcTalkCodec.NoNpc);
            if (member.ItemAnimation <= 0 || !member.TryAdvanceRevision()) continue;
            int decrement = 1;
            if ((member.ControlFlags & (1 << 5)) == 0 &&
                inventory.TryGet(member.Connection, member.SelectedItem, out RuntimePlayerInventoryItem item) &&
                item.ItemType == VanillaItemIds.Revolver) decrement++;
            member.ItemAnimation = (member.IsDead || GetBuffDuration(member.Connection.Player, VanillaBuffIds.Frozen) > 0 ||
                GetBuffDuration(member.Connection.Player, VanillaBuffIds.Webbed) > 0 || GetBuffDuration(member.Connection.Player, VanillaBuffIds.Stoned) > 0 ||
                GetBuffDuration(member.Connection.Player, VanillaBuffIds.Shimmer) > 0) ? 0 : Math.Max(0, member.ItemAnimation - decrement);
        }
    }
}
