using global::Multiplicity.Packets;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Protocol.Multiplicity;

public enum WorldTownNpcSyncPacketEncodeResult : byte
{
    Encoded = 0,
    InvalidNpcSlot = 1,
    InvalidNpcNetId = 2,
    NonFinitePosition = 3,
    FrameTooLarge = 4
}

/// <summary>
/// Encodes the packet 23 baseline used by Terraria section synchronization for one persisted town NPC.
/// The world file stores inert town-NPC persistence rather than live simulation state, so the bootstrap
/// baseline intentionally uses the vanilla world-load defaults: generation zero, zero velocity/AI and full life.
/// </summary>
public static class WorldTownNpcSyncPacketEncoder
{
    public static WorldTownNpcSyncPacketEncodeResult TryEncode(
        int npcSlot,
        WorldTownNpc npc,
        out ReadOnlyMemory<byte> frame)
    {
        ArgumentNullException.ThrowIfNull(npc);
        frame = default;

        if ((uint)npcSlot > byte.MaxValue)
            return WorldTownNpcSyncPacketEncodeResult.InvalidNpcSlot;

        NpcNetId netIdentity = npc.NetIdentity;
        if (netIdentity.Value < short.MinValue || netIdentity.Value > short.MaxValue)
            return WorldTownNpcSyncPacketEncodeResult.InvalidNpcNetId;
        if (!float.IsFinite(npc.X) || !float.IsFinite(npc.Y))
            return WorldTownNpcSyncPacketEncodeResult.NonFinitePosition;

        short netId = checked((short)netIdentity.Value);
        var packet = new NpcUpdate
        {
            NpcSlot = checked((byte)npcSlot),
            Generation = 0,
            PositionX = npc.X,
            PositionY = npc.Y,
            VelocityX = 0f,
            VelocityY = 0f,
            Target = byte.MaxValue,
            Flags = NpcUpdateFlags.LifeIsFull,
            ExtraFlags = NpcUpdateExtraFlags.None,
            NpcNetId = netId,
            NpcType = NpcUpdate.NpcTypeFromNetId(netId),
            LifeBytes = 0
        };

        if (!packet.TrySerialize(out byte[] encoded))
            return WorldTownNpcSyncPacketEncodeResult.FrameTooLarge;

        frame = encoded;
        return WorldTownNpcSyncPacketEncodeResult.Encoded;
    }
}
