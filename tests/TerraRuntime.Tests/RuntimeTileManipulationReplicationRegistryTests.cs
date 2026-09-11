using TerraRuntime.Contracts.Gameplay;
using System.Buffers;
using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

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
    public void Authoritative_correction_targets_only_the_origin_with_packet20_tile_square()
    {
        var replication = new RuntimeTileManipulationReplicationRegistry();
        GameCommandSourceId sourceA = GameCommandSourceId.FromConnection(805);
        GameCommandSourceId sourceB = GameCommandSourceId.FromConnection(806);
        TerrariaConnectionOutboundQueue outboundA = CreateOutbound();
        TerrariaConnectionOutboundQueue outboundB = CreateOutbound();
        Assert.True(replication.TryRegister(sourceA, outboundA));
        Assert.True(replication.TryRegister(sourceB, outboundB));
        ConnectionHandle playerA = Connection(sourceA, slot: 4, generation: 1);
        ConnectionHandle playerB = Connection(sourceB, slot: 5, generation: 1);
        PlayerSpawnCommitRequest spawnA = Spawn(playerA.Player.Slot);
        PlayerSpawnCommitRequest spawnB = Spawn(playerB.Player.Slot);
        replication.PlayerSpawned(playerA, in spawnA);
        replication.PlayerSpawned(playerB, in spawnB);

        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        WorldTile authoritative = tiles.Get(10, 10);
        Assert.True(authoritative.TrySetWallType(VanillaWallIds.Stone));
        tiles.Set(10, 10, in authoritative);

        Assert.True(replication.TryPublishAuthoritativeCorrection(sourceA, tiles, 10, 10));

        Assert.Equal(1, outboundA.QueuedFrames);
        Assert.Equal(0, outboundB.QueuedFrames);
        TerrariaFrame correction = DequeueFrame(outboundA);
        Assert.Equal((byte)TerrariaMessageId.TileSquare, correction.MessageId);
        Assert.Equal(1, replication.RelayedFrames);
    }

    [Fact]
    public void Server_authored_tile_square_is_relayed_to_every_playing_peer()
    {
        var replication = new RuntimeTileManipulationReplicationRegistry();
        GameCommandSourceId sourceA = GameCommandSourceId.FromConnection(807);
        GameCommandSourceId sourceB = GameCommandSourceId.FromConnection(808);
        TerrariaConnectionOutboundQueue outboundA = CreateOutbound();
        TerrariaConnectionOutboundQueue outboundB = CreateOutbound();
        Assert.True(replication.TryRegister(sourceA, outboundA));
        Assert.True(replication.TryRegister(sourceB, outboundB));
        ConnectionHandle playerA = Connection(sourceA, slot: 6, generation: 1);
        ConnectionHandle playerB = Connection(sourceB, slot: 7, generation: 1);
        PlayerSpawnCommitRequest spawnA = Spawn(playerA.Player.Slot);
        PlayerSpawnCommitRequest spawnB = Spawn(playerB.Player.Slot);
        replication.PlayerSpawned(playerA, in spawnA);
        replication.PlayerSpawned(playerB, in spawnB);

        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        WorldTile merge = tiles.Get(20, 20);
        merge.Flags |= WorldTileFlags.Active;
        Assert.True(merge.TrySetTileType(VanillaTileIds.Obsidian));
        tiles.Set(20, 20, in merge);

        Assert.True(replication.TryPublishTileSquareToAll(tiles, 20, 20));

        Assert.Equal(1, outboundA.QueuedFrames);
        Assert.Equal(1, outboundB.QueuedFrames);
        Assert.Equal((byte)TerrariaMessageId.TileSquare, DequeueFrame(outboundA).MessageId);
        Assert.Equal((byte)TerrariaMessageId.TileSquare, DequeueFrame(outboundB).MessageId);
        Assert.Equal(2, replication.RelayedFrames);
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

    [Fact]
    public void Liquid_updates_are_coalesced_and_drain_through_the_fixed_authoritative_tick_budget()
    {
        var replication = new RuntimeTileManipulationReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(809);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));
        ConnectionHandle player = Connection(source, slot: 8, generation: 1);
        PlayerSpawnCommitRequest spawn = Spawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);

        for (short index = 0; index < 20; index++)
        {
            var state = new TerrariaLiquidState((short)(20 + index), 30, (byte)(10 + index), 0);
            Assert.True(replication.TryPublishLiquidToAll(in state));
        }
        var replacement = new TerrariaLiquidState(20, 30, 250, 0);
        Assert.True(replication.TryPublishLiquidToAll(in replacement));

        replication.FlushPendingLiquids();

        Assert.Equal(RuntimeTileManipulationReplicationRegistry.MaxLiquidFramesPerAuthoritativeTick, outbound.QueuedFrames);
        Assert.Equal(4, replication.PendingLiquidUpdates);
        Assert.Equal(1, replication.CoalescedLiquidUpdates);
        Assert.Equal(RuntimeTileManipulationReplicationRegistry.MaxLiquidFramesPerAuthoritativeTick, replication.EmittedLiquidUpdates);
        Assert.Equal(
            TerrariaLiquidDecodeResult.Decoded,
            TerrariaLiquidCodec.TryDecode(DequeueFrame(outbound), out TerrariaLiquidState first));
        Assert.Equal(replacement, first);
    }

    [Fact]
    public void Liquid_backlog_has_a_hard_bound_and_evicts_the_oldest_pending_coordinate()
    {
        var replication = new RuntimeTileManipulationReplicationRegistry();
        for (int index = 0; index <= RuntimeTileManipulationReplicationRegistry.MaxPendingLiquidUpdates; index++)
        {
            var state = new TerrariaLiquidState((short)(index & short.MaxValue), (short)(index >> 15), 1, 0);
            Assert.True(replication.TryPublishLiquidToAll(in state));
        }

        Assert.Equal(RuntimeTileManipulationReplicationRegistry.MaxPendingLiquidUpdates, replication.PendingLiquidUpdates);
        Assert.Equal(1, replication.DroppedLiquidUpdates);
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 64, maxQueuedBytes: 8_192, maxFrameBytes: 1_024));

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
