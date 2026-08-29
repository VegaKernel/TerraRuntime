using System.Buffers;
using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class RuntimeTileManipulationReplicationRegistryTests
{
    [Fact]
    public void Committed_packet17_is_relayed_only_to_other_playing_peers()
    {
        var replication = new RuntimeTileManipulationReplicationRegistry();
        GameCommandSourceId sourceA = GameCommandSourceId.FromConnection(801);
        GameCommandSourceId sourceB = GameCommandSourceId.FromConnection(802);
        GameCommandSourceId sourceJoining = GameCommandSourceId.FromConnection(803);
        TerrariaConnectionOutboundQueue outboundA = CreateOutbound();
        TerrariaConnectionOutboundQueue outboundB = CreateOutbound();
        TerrariaConnectionOutboundQueue outboundJoining = CreateOutbound();
        Assert.True(replication.TryRegister(sourceA, outboundA));
        Assert.True(replication.TryRegister(sourceB, outboundB));
        Assert.True(replication.TryRegister(sourceJoining, outboundJoining));

        ConnectionHandle playerA = Connection(sourceA, slot: 1, generation: 1);
        ConnectionHandle playerB = Connection(sourceB, slot: 2, generation: 1);
        PlayerSpawnCommitRequest spawnA = Spawn(playerA.Player.Slot);
        PlayerSpawnCommitRequest spawnB = Spawn(playerB.Player.Slot);
        replication.PlayerSpawned(playerA, in spawnA);
        replication.PlayerSpawned(playerB, in spawnB);

        var committed = new TerrariaTileManipulationState(
            Action: (byte)TerrariaTileManipulationAction.PlaceTile,
            TileX: 15,
            TileY: 16,
            Data: 0,
            Style: 0);

        Assert.True(replication.TryPublishCommitted(sourceA, in committed));

        Assert.Equal(0, outboundA.QueuedFrames);
        Assert.Equal(1, outboundB.QueuedFrames);
        Assert.Equal(0, outboundJoining.QueuedFrames);
        TerrariaFrame frame = DequeueFrame(outboundB);
        Assert.Equal(
            TerrariaTileManipulationDecodeResult.Decoded,
            TerrariaTileManipulationCodec.TryDecode(in frame, out TerrariaTileManipulationState relayed));
        Assert.Equal(committed, relayed);
        Assert.Equal(1, replication.RelayedFrames);
        Assert.Equal(0, replication.RejectedFrames);
        Assert.Equal(0, replication.EncodeFailures);
    }

    [Fact]
    public void Disconnect_removes_playing_generation_from_live_relay()
    {
        var replication = new RuntimeTileManipulationReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(804);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));
        ConnectionHandle player = Connection(source, slot: 3, generation: 7);
        PlayerSpawnCommitRequest spawn = Spawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);
        replication.PlayerDisconnected(player);

        var committed = new TerrariaTileManipulationState(1, 20, 20, 0, 0);
        Assert.True(replication.TryPublishCommitted(GameCommandSourceId.FromConnection(999), in committed));

        Assert.Equal(0, outbound.QueuedFrames);
        Assert.Equal(0, replication.RelayedFrames);
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 8, maxQueuedBytes: 8_192, maxFrameBytes: 1_024));

    private static ConnectionHandle Connection(GameCommandSourceId source, byte slot, ulong generation) =>
        new(source, new PlayerHandle(new PlayerSlotId(slot), new PlayerSessionGeneration(generation)));

    private static PlayerSpawnCommitRequest Spawn(PlayerSlotId slot) =>
        new(slot, 100, 100, 0, 0, 0, 0, 0);

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
