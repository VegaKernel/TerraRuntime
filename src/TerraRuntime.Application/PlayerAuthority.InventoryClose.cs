using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;

namespace TerraRuntime.Application;

internal sealed partial class PlayerAuthority
{
    internal bool TryPrepareInventoryClose(ConnectionHandle connection,
        in WorldItemDropStateUpdate proposal, out CursorReturnPreparation? preparation)
    {
        preparation = null;
        if (proposal.Ownership != WorldItemOwnershipMode.ReserveForLocalPlayer ||
            !TryPrepareCursorReturn(connection, true, out var prepared,
                requireFavorite: false, animateResidual: false) ||
            prepared is null || prepared.Residual.IsEmpty ||
            !membership.TryGet(connection, out var member))
            return false;

        var state = member.CaptureSnapshot();
        var body = state.HasMount
            ? VanillaPlayerMountHitbox1458.Resolve(state.MountType)
            : (Width: 20f, Height: 42f);
        var item = prepared.Residual;
        var expected = new WorldItemDropStateUpdate(
            state.PositionX + body.Width / 2f - VanillaWorldItemOwner1458.PhysicalWidth / 2f,
            state.PositionY + body.Height / 2f - VanillaWorldItemOwner1458.PhysicalHeight / 2f,
            proposal.VelocityX, proposal.VelocityY, item.Stack, checked((byte)item.Prefix.Value),
            WorldItemOwnershipMode.ReserveForLocalPlayer, checked((short)item.ItemType.Value),
            false, 0, 100);
        if (proposal != expected)
            return false;

        preparation = prepared;
        return true;
    }
}
