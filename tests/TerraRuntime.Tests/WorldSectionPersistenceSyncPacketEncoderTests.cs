using global::Multiplicity.Packets;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldSectionPersistenceSyncPacketEncoderTests
{
    [Fact]
    public void Emits_town_npcs_before_chest_frames_and_filters_neighbor_sections()
    {
        var dimensions = new WorldDimensions(400, 300);
        WorldTownNpc[] townNpcs =
        [
            CreateNpc(22, x: 100 * 16f, y: 50 * 16f),
            CreateNpc(17, x: 250 * 16f, y: 50 * 16f)
        ];
        WorldChest[] chests =
        [
            new WorldChest(3, 120, 60, "local", [new WorldChestItem(0, 0, 0)]),
            new WorldChest(4, 250, 60, "neighbor", [new WorldChestItem(1, 1, 0)])
        ];

        Assert.Equal(
            WorldSectionPersistenceSyncPacketEncodeResult.Encoded,
            WorldSectionPersistenceSyncPacketEncoder.TryEncode(
                dimensions,
                townNpcs,
                chests,
                new WorldSectionId(0, 0),
                out ReadOnlyMemory<byte>[] frames));

        Assert.Equal(3, frames.Length);
        Assert.Equal((byte)PacketTypes.NpcUpdate, frames[0].Span[2]);
        Assert.Equal((byte)PacketTypes.SyncChestSize, frames[1].Span[2]);
        Assert.Equal((byte)PacketTypes.ChestItem, frames[2].Span[2]);
        Assert.Equal((byte)0, frames[0].Span[3]);
    }

    [Fact]
    public void Rejects_out_of_world_chest_coordinates_instead_of_misrouting_them()
    {
        var dimensions = new WorldDimensions(200, 150);
        WorldChest[] chests =
        [
            new WorldChest(0, 200, 10, "bad", [])
        ];

        Assert.Equal(
            WorldSectionPersistenceSyncPacketEncodeResult.InvalidChest,
            WorldSectionPersistenceSyncPacketEncoder.TryEncode(
                dimensions,
                Array.Empty<WorldTownNpc>(),
                chests,
                new WorldSectionId(0, 0),
                out ReadOnlyMemory<byte>[] frames));
        Assert.Empty(frames);
    }

    private static WorldTownNpc CreateNpc(int netId, float x, float y) =>
        new(netId, "npc", x, y, true, 0, 0, null, false);
}
