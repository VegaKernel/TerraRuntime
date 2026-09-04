using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime;

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
}
