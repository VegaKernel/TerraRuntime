using global::Multiplicity.Packets;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>Source packet65 entity teleport; the operation and current-position flag share one typed encoder.</summary>
public static class TerrariaEntityTeleportCodec1458
{
    public static bool TryEncode(short entitySlot, bool npc, float positionX, float positionY, byte style,
        bool useCurrentPosition, out byte[] frame)
    {
        if (entitySlot < 0 || (npc ? !TerrariaNpcCatchCodec.IsValidNpcSlot(entitySlot) : entitySlot > byte.MaxValue) ||
            !float.IsFinite(positionX) || !float.IsFinite(positionY))
        {
            frame = [];
            return false;
        }
        frame = new Teleport
        {
            Operation = npc ? TeleportOperation.Npc : TeleportOperation.Player,
            UseCurrentEntityPosition = useCurrentPosition,
            TargetId = entitySlot,
            X = positionX,
            Y = positionY,
            TeleportStyle = style,
            HasExtraInfo = false
        }.ToArray();
        return true;
    }
}
