using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Application;

internal sealed partial class RuntimeConnectionRegistry
{
    public void PlayerDamageAvoided(PlayerHandle player, float positionX, float positionY, string text)
    {
        if (!player.IsAssigned || !float.IsFinite(positionX) || !float.IsFinite(positionY) || string.IsNullOrWhiteSpace(text))
            return;

        byte[] frame = TerrariaCombatTextCodec.EncodeString(
            positionX,
            positionY,
            text,
            new TerrariaRgbColor(190, 220, 255));
        _ = BroadcastToPlaying(frame);
    }

    public void PlayerGodModeChanged(PlayerHandle player, bool enabled)
    {
        if (!player.IsAssigned || player.Slot.Value >= _godModeByPlayer.Length)
            return;

        _godModeByPlayer[player.Slot.Value] = enabled;
        byte[] frame = TerrariaCreativeGodModeCodec1458.EncodeSyncOnePlayer(player.Slot, enabled);
        _ = BroadcastToPlaying(frame);
    }
}
