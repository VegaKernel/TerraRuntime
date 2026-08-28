using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldGlobalTownNpcBootstrapPacketEncoderTests
{
    [Fact]
    public void Encodes_packet23_then_empty_packet54_for_each_persisted_town_npc()
    {
        WorldTownNpc[] npcs =
        [
            CreateNpc(22, 1600f, 800f),
            CreateNpc(17, 3200f, 1200f)
        ];

        Assert.Equal(
            WorldGlobalTownNpcBootstrapPacketEncodeResult.Encoded,
            WorldGlobalTownNpcBootstrapPacketEncoder.TryEncode(npcs, out ReadOnlyMemory<byte>[] frames));

        Assert.Equal(4, frames.Length);
        Assert.Equal((byte)PacketTypes.NpcUpdate, frames[0].Span[2]);
        Assert.Equal((byte)PacketTypes.NpcUpdateBuff, frames[1].Span[2]);
        Assert.Equal((byte)PacketTypes.NpcUpdate, frames[2].Span[2]);
        Assert.Equal((byte)PacketTypes.NpcUpdateBuff, frames[3].Span[2]);

        var firstUpdate = NpcUpdateView.FromPayload(frames[0].Span[3..]);
        var firstBuffs = NpcUpdateBuffView.FromPayload(frames[1].Span[3..]);
        Assert.Equal((byte)0, firstUpdate.NpcSlot);
        Assert.Equal((short)0, firstBuffs.NpcId);
        Assert.Empty(firstBuffs.Buffs);

        var secondUpdate = NpcUpdateView.FromPayload(frames[2].Span[3..]);
        var secondBuffs = NpcUpdateBuffView.FromPayload(frames[3].Span[3..]);
        Assert.Equal((byte)1, secondUpdate.NpcSlot);
        Assert.Equal((short)1, secondBuffs.NpcId);
        Assert.Empty(secondBuffs.Buffs);
    }

    [Fact]
    public void Empty_persistence_produces_no_global_npc_frames()
    {
        Assert.Equal(
            WorldGlobalTownNpcBootstrapPacketEncodeResult.Encoded,
            WorldGlobalTownNpcBootstrapPacketEncoder.TryEncode([], out ReadOnlyMemory<byte>[] frames));
        Assert.Empty(frames);
    }

    private static WorldTownNpc CreateNpc(int netId, float x, float y) =>
        new(netId, "npc", x, y, true, 0, 0, null, false);
}
