using global::Multiplicity.Packets;
using TerraRuntime.Protocol;

namespace TerraRuntime.Protocol.Multiplicity;

/// <summary>
/// Serializes authoritative NPC state through Multiplicity's protocol-326 packet 23 model.
/// Flag and life-width semantics follow TerrariaServer 1.4.5.8 NetMessage.SendData case 23.
/// </summary>
public static class TerrariaNpcUpdateEncoder
{
    public static bool TryEncode(in TerrariaNpcUpdateState state, out byte[] bytes)
    {
        if (!state.IsValid)
        {
            bytes = [];
            return false;
        }

        NpcUpdateFlags flags = NpcUpdateFlags.None;
        if (state.DirectionX > 0)
            flags |= NpcUpdateFlags.DirectionXPositive;
        if (state.DirectionY > 0)
            flags |= NpcUpdateFlags.DirectionYPositive;
        if (state.SpriteDirection > 0)
            flags |= NpcUpdateFlags.SpriteDirectionPositive;
        if (state.Life == state.LifeMax)
            flags |= NpcUpdateFlags.LifeIsFull;

        NpcUpdateExtraFlags extraFlags = state.SpawnNeedsSyncing
            ? NpcUpdateExtraFlags.SpawnNeedsSyncing
            : NpcUpdateExtraFlags.None;

        var packet = new NpcUpdate
        {
            NpcSlot = state.NpcSlot,
            Generation = state.Generation,
            PositionX = state.PositionX,
            PositionY = state.PositionY,
            VelocityX = state.VelocityX,
            VelocityY = state.VelocityY,
            Target = state.Target,
            Flags = flags,
            ExtraFlags = extraFlags,
            NpcNetId = state.NpcNetId,
            NpcType = NpcUpdate.NpcTypeFromNetId(state.NpcNetId),
            Life = state.Life,
            LifeBytes = state.Life == state.LifeMax ? (byte)0 : GetVanillaLifeWidth(state.LifeMax)
        };
        packet.AI[0] = state.Ai0;
        packet.AI[1] = state.Ai1;
        packet.AI[2] = state.Ai2;
        packet.AI[3] = state.Ai3;

        using var stream = new MemoryStream(packet.GetLength() + TerrariaPacket.PacketHeaderLength);
        packet.ToStream(stream);
        bytes = stream.ToArray();
        return true;
    }

    private static byte GetVanillaLifeWidth(int lifeMax)
    {
        if (lifeMax > short.MaxValue)
            return sizeof(int);
        if (lifeMax > sbyte.MaxValue)
            return sizeof(short);
        return sizeof(sbyte);
    }
}
