using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Application;

internal sealed partial class ServerPlayerAuthority
{
    internal bool TryAdoptToolShapePosition(in PlayerStateSnapshot before, float y,
        out PlayerStateSnapshot accepted)
    {
        accepted = default;
        return states.TryGet(before.Player, out var current) && current == before &&
            states.TrySetMotion(before.Player, before.PositionX, y,
                before.VelocityX, before.VelocityY, out accepted);
    }

    internal void TryPublishToolShapePosition(in PlayerStateSnapshot accepted)
    {
        if (states.TryGet(accepted.Player, out var current) && current == accepted)
            events?.ServerPlayerMoved(in accepted);
    }
}
