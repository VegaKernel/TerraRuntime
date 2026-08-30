using System.Buffers;
using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeGroundFighterDoorOpeningSinkTests
{
    [Fact]
    public void Authoritative_normal_door_mutation_publishes_packet19_to_every_playing_peer()
    {
        var tiles = new WorldTileStore(new WorldDimensions(40, 40));
        for (int row = 0; row < 3; row++)
        {
            var door = new WorldTile
            {
                Type = checked((ushort)VanillaTileIds.ClosedDoor.Value),
                FrameX = 0,
                FrameY = checked((short)(row * 18)),
                Flags = WorldTileFlags.Active
            };
            tiles.Set(10, 10 + row, in door);
        }

        var replication = new RuntimeTileManipulationReplicationRegistry();
        TerrariaConnectionOutboundQueue outboundA = CreateOutbound();
        TerrariaConnectionOutboundQueue outboundB = CreateOutbound();
        TerrariaConnectionOutboundQueue joining = CreateOutbound();
        GameCommandSourceId sourceA = GameCommandSourceId.FromConnection(901);
        GameCommandSourceId sourceB = GameCommandSourceId.FromConnection(902);
        GameCommandSourceId sourceJoining = GameCommandSourceId.FromConnection(903);
        Assert.True(replication.TryRegister(sourceA, outboundA));
        Assert.True(replication.TryRegister(sourceB, outboundB));
        Assert.True(replication.TryRegister(sourceJoining, joining));
        MarkPlaying(replication, sourceA, slot: 1);
        MarkPlaying(replication, sourceB, slot: 2);

        var sink = new RuntimeGroundFighterDoorOpeningSink(tiles, replication);
        var intent = new VanillaGroundFighterDoorOpeningIntent(10, 11, 1, VanillaTileIds.ClosedDoor);

        Assert.True(sink.TryOpen(in intent));
        Assert.Equal(VanillaTileIds.OpenDoor, tiles.Get(10, 11).TileType);
        Assert.Equal(VanillaTileIds.OpenDoor, tiles.Get(11, 11).TileType);
        Assert.Equal(1, outboundA.QueuedFrames);
        Assert.Equal(1, outboundB.QueuedFrames);
        Assert.Equal(0, joining.QueuedFrames);
        Assert.Equal(2, replication.RelayedFrames);

        AssertDoorPacket(DequeueFrame(outboundA));
        AssertDoorPacket(DequeueFrame(outboundB));
    }

    private static void AssertDoorPacket(TerrariaFrame frame)
    {
        Assert.Equal(
            TerrariaDoorToggleDecodeResult.Decoded,
            TerrariaDoorToggleCodec.TryDecode(in frame, out TerrariaDoorToggleState state));
        Assert.Equal((byte)TerrariaDoorToggleAction.OpenDoor, state.Action);
        Assert.Equal((short)10, state.TileX);
        Assert.Equal((short)11, state.TileY);
        Assert.Equal(1, state.DirectionX);
    }

    private static void MarkPlaying(
        RuntimeTileManipulationReplicationRegistry replication,
        GameCommandSourceId source,
        byte slot)
    {
        var connection = new ConnectionHandle(
            source,
            new PlayerHandle(new PlayerSlotId(slot), new PlayerSessionGeneration(1)));
        var spawn = new PlayerSpawnCommitRequest(new PlayerSlotId(slot), 100, 100, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(connection, in spawn);
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 8, maxQueuedBytes: 8_192, maxFrameBytes: 1_024));

    private static TerrariaFrame DequeueFrame(TerrariaConnectionOutboundQueue outbound)
    {
        PropertyInfo property = typeof(TerrariaConnectionOutboundQueue).GetProperty(
            "InnerQueue",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Outbound queue internal contract changed.");
        var queue = Assert.IsType<BoundedOutboundQueue>(property.GetValue(outbound));
        Assert.True(queue.TryRead(out OutboundFrame outboundFrame));
        var sequence = new ReadOnlySequence<byte>(outboundFrame.Bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.Equal(0, sequence.Length);
        return frame;
    }
}
