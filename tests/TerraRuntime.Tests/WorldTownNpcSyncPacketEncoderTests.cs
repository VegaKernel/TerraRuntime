using global::Multiplicity.Packets;
using global::Multiplicity.Packets.Views;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldTownNpcSyncPacketEncoderTests
{
    [Fact]
    public void Encodes_persisted_town_npc_as_vanilla_full_life_packet23_baseline()
    {
        var npc = new WorldTownNpc(
            NetId: 22,
            GivenName: "Andrew",
            X: 1600f,
            Y: 800f,
            Homeless: true,
            HomeTileX: 0,
            HomeTileY: 0,
            TownNpcVariationIndex: null,
            HomelessDespawn: false);

        Assert.Equal(
            WorldTownNpcSyncPacketEncodeResult.Encoded,
            WorldTownNpcSyncPacketEncoder.TryEncode(7, npc, out ReadOnlyMemory<byte> frame));

        Assert.True(frame.Length >= 3);
        Assert.Equal((byte)PacketTypes.NpcUpdate, frame.Span[2]);

        var view = NpcUpdateView.FromPayload(frame.Span[3..]);
        Assert.Equal((byte)7, view.NpcSlot);
        Assert.Equal((byte)0, view.Generation);
        Assert.Equal(1600f, view.PositionX);
        Assert.Equal(800f, view.PositionY);
        Assert.Equal(0f, view.VelocityX);
        Assert.Equal(0f, view.VelocityY);
        Assert.Equal((ushort)byte.MaxValue, view.Target);
        Assert.Equal(NpcUpdateFlags.LifeIsFull, view.Flags);
        Assert.Equal(NpcUpdateExtraFlags.None, view.ExtraFlags);
        Assert.False(view.HasAI0);
        Assert.False(view.HasAI1);
        Assert.False(view.HasAI2);
        Assert.False(view.HasAI3);
        Assert.Equal((short)22, view.NpcNetId);
        Assert.Equal(22, view.NpcType);
        Assert.False(view.HasLifePayload);
        Assert.False(view.HasReleaseOwner);
    }

    [Fact]
    public void Rejects_non_addressable_slots_invalid_net_ids_and_non_finite_positions()
    {
        WorldTownNpc valid = CreateNpc(22, 1f, 2f);
        WorldTownNpc invalidNetId = CreateNpc(short.MaxValue + 1, 1f, 2f);
        WorldTownNpc nonFinite = CreateNpc(22, float.NaN, 2f);

        Assert.Equal(
            WorldTownNpcSyncPacketEncodeResult.InvalidNpcSlot,
            WorldTownNpcSyncPacketEncoder.TryEncode(byte.MaxValue + 1, valid, out _));
        Assert.Equal(
            WorldTownNpcSyncPacketEncodeResult.InvalidNpcNetId,
            WorldTownNpcSyncPacketEncoder.TryEncode(0, invalidNetId, out _));
        Assert.Equal(
            WorldTownNpcSyncPacketEncodeResult.NonFinitePosition,
            WorldTownNpcSyncPacketEncoder.TryEncode(0, nonFinite, out _));
    }

    private static WorldTownNpc CreateNpc(int netId, float x, float y) =>
        new(netId, "npc", x, y, true, 0, 0, null, false);
}
