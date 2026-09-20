using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class TempleDoorUnlock1458Tests
{
    [Fact]
    public void Packet52_matches_official_wire_layout_and_round_trips()
    {
        var expected = new TerrariaLockAndUnlockState(Action: 2, TileX: -123, TileY: 456);
        byte[] expectedBytes = [0x08, 0x00, 0x34, 0x02, 0x85, 0xFF, 0xC8, 0x01];

        Assert.True(TerrariaLockAndUnlockCodec.TryEncode(in expected, out byte[] encoded));
        Assert.Equal(expectedBytes, encoded);

        TerrariaFrame frame = Decode(encoded);
        Assert.Equal(
            TerrariaLockAndUnlockDecodeResult.Decoded,
            TerrariaLockAndUnlockCodec.TryDecode(in frame, out TerrariaLockAndUnlockState actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Action2_consumes_first_ordinary_inventory_key_unlocks_door_and_replicates_source_boundaries()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        PlaceLockedTempleDoor(tiles, 40, 50);
        var replication = new RuntimeTileManipulationReplicationRegistry();
        var state = new ServerRuntimeState(worldTiles: tiles, tileManipulationReplication: replication);
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreatePlayingSession(slots);
        ConnectionHandle owner = new(GameCommandSourceId.FromConnection(145801), session.Handle);
        ConnectionHandle observer = new(
            GameCommandSourceId.FromConnection(145802),
            new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)));
        TerrariaConnectionOutboundQueue ownerOutbound = Outbound();
        TerrariaConnectionOutboundQueue observerOutbound = Outbound();
        Assert.True(replication.TryRegister(owner.Source, ownerOutbound));
        Assert.True(replication.TryRegister(observer.Source, observerOutbound));

        PlayerSpawnCommitRequest ownerSpawn = Spawn(owner.Player.Slot);
        state.Apply(new PlayerSpawnRuntimeCommand(owner, session, ownerSpawn));
        Assert.Equal(PlayerSpawnCommitResult.Committed, state.LastSpawnCommitResult);
        replication.PlayerSpawned(owner, in ownerSpawn);
        PlayerSpawnCommitRequest observerSpawn = Spawn(observer.Player.Slot);
        replication.PlayerSpawned(observer, in observerSpawn);

        // Player.cs scans slots 0..57 in ascending order. Slot 58 is the mouse item and must never win.
        SetItem(state, owner, 9, stack: 2);
        SetItem(state, owner, 17, stack: 3);
        SetItem(state, owner, 58, stack: 9);

        var unlock = new TerrariaLockAndUnlockState(Action: 2, TileX: 40, TileY: 51);
        state.Apply(new ClientTempleDoorUnlockRuntimeCommand(owner, unlock));

        Assert.Equal((short)648, tiles.Get(40, 50).FrameY);
        Assert.Equal((short)666, tiles.Get(40, 51).FrameY);
        Assert.Equal((short)684, tiles.Get(40, 52).FrameY);
        Assert.True(state.TryCapturePlayerInventoryItem(owner.Player, 9, out RuntimePlayerInventoryItem firstKey));
        Assert.Equal((short)1, firstKey.Stack);
        Assert.True(state.TryCapturePlayerInventoryItem(owner.Player, 17, out RuntimePlayerInventoryItem laterKey));
        Assert.Equal((short)3, laterKey.Stack);
        Assert.True(state.TryCapturePlayerInventoryItem(owner.Player, 58, out RuntimePlayerInventoryItem mouseKey));
        Assert.Equal((short)9, mouseKey.Stack);
        Assert.Equal(1, state.AppliedClientTileManipulations);

        // The initiator receives only the packet-20 resync square; the observer receives packet 52 then packet 20.
        Assert.Equal(1, ownerOutbound.QueuedFrames);
        Assert.Equal((byte)TerrariaMessageId.TileSquare, Dequeue(ownerOutbound).MessageId);
        Assert.Equal(2, observerOutbound.QueuedFrames);
        TerrariaFrame echoed = Dequeue(observerOutbound);
        Assert.Equal(
            TerrariaLockAndUnlockDecodeResult.Decoded,
            TerrariaLockAndUnlockCodec.TryDecode(in echoed, out TerrariaLockAndUnlockState echoedState));
        Assert.Equal(unlock, echoedState);
        Assert.Equal((byte)TerrariaMessageId.TileSquare, Dequeue(observerOutbound).MessageId);
    }

    [Fact]
    public void Missing_ordinary_inventory_key_leaves_door_and_mouse_item_unchanged()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        PlaceLockedTempleDoor(tiles, 40, 50);
        var state = new ServerRuntimeState(worldTiles: tiles);
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreatePlayingSession(slots);
        ConnectionHandle owner = new(GameCommandSourceId.FromConnection(145803), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(owner, session, Spawn(owner.Player.Slot)));
        SetItem(state, owner, 58, stack: 1);

        state.Apply(new ClientTempleDoorUnlockRuntimeCommand(
            owner,
            new TerrariaLockAndUnlockState(Action: 2, TileX: 40, TileY: 50)));

        Assert.Equal((short)594, tiles.Get(40, 50).FrameY);
        Assert.Equal((short)612, tiles.Get(40, 51).FrameY);
        Assert.Equal((short)630, tiles.Get(40, 52).FrameY);
        Assert.True(state.TryCapturePlayerInventoryItem(owner.Player, 58, out RuntimePlayerInventoryItem mouseKey));
        Assert.Equal((short)1, mouseKey.Stack);
        Assert.Equal(1, state.RejectedClientTileManipulations);
    }

    [Fact]
    public void Packet19_action0_uses_closed_door_transform_and_relays_only_to_other_peers()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        PlaceClosedDoor(tiles, 40, 50);
        var replication = new RuntimeTileManipulationReplicationRegistry();
        var state = new ServerRuntimeState(worldTiles: tiles, tileManipulationReplication: replication);
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreatePlayingSession(slots);
        ConnectionHandle owner = new(GameCommandSourceId.FromConnection(145804), session.Handle);
        ConnectionHandle observer = new(GameCommandSourceId.FromConnection(145805),
            new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)));
        TerrariaConnectionOutboundQueue ownerOutbound = Outbound();
        TerrariaConnectionOutboundQueue observerOutbound = Outbound();
        Assert.True(replication.TryRegister(owner.Source, ownerOutbound));
        Assert.True(replication.TryRegister(observer.Source, observerOutbound));
        PlayerSpawnCommitRequest ownerSpawn = Spawn(owner.Player.Slot);
        state.Apply(new PlayerSpawnRuntimeCommand(owner, session, ownerSpawn));
        replication.PlayerSpawned(owner, in ownerSpawn);
        PlayerSpawnCommitRequest observerSpawn = Spawn(observer.Player.Slot);
        replication.PlayerSpawned(observer, in observerSpawn);

        var open = new TerrariaDoorToggleState((byte)TerrariaDoorToggleAction.OpenDoor, 40, 51, 1);
        state.Apply(new ClientDoorOpenRuntimeCommand(owner, open));

        for (int row = 0; row < 3; row++)
        {
            Assert.Equal(VanillaTileIds.OpenDoor, tiles.Get(40, 50 + row).TileType);
            Assert.Equal(VanillaTileIds.OpenDoor, tiles.Get(41, 50 + row).TileType);
        }
        Assert.Equal(0, ownerOutbound.QueuedFrames);
        Assert.Equal(1, observerOutbound.QueuedFrames);
        TerrariaFrame echoed = Dequeue(observerOutbound);
        Assert.Equal(TerrariaDoorToggleDecodeResult.Decoded,
            TerrariaDoorToggleCodec.TryDecode(in echoed, out TerrariaDoorToggleState echoedState));
        Assert.Equal(open, echoedState);
    }

    [Fact]
    public void Packet19_action1_forced_close_restores_one_column_and_relays_only_to_other_peers()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        PlaceClosedDoor(tiles, 40, 50);
        var replication = new RuntimeTileManipulationReplicationRegistry();
        var state = new ServerRuntimeState(worldTiles: tiles, tileManipulationReplication: replication);
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreatePlayingSession(slots);
        ConnectionHandle owner = new(GameCommandSourceId.FromConnection(145806), session.Handle);
        ConnectionHandle observer = new(GameCommandSourceId.FromConnection(145807),
            new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)));
        TerrariaConnectionOutboundQueue ownerOutbound = Outbound();
        TerrariaConnectionOutboundQueue observerOutbound = Outbound();
        Assert.True(replication.TryRegister(owner.Source, ownerOutbound));
        Assert.True(replication.TryRegister(observer.Source, observerOutbound));
        PlayerSpawnCommitRequest ownerSpawn = Spawn(owner.Player.Slot);
        state.Apply(new PlayerSpawnRuntimeCommand(owner, session, ownerSpawn));
        replication.PlayerSpawned(owner, in ownerSpawn);
        PlayerSpawnCommitRequest observerSpawn = Spawn(observer.Player.Slot);
        replication.PlayerSpawned(observer, in observerSpawn);

        state.Apply(new ClientDoorOpenRuntimeCommand(owner,
            new TerrariaDoorToggleState((byte)TerrariaDoorToggleAction.OpenDoor, 40, 51, 1)));
        Assert.Equal(1, observerOutbound.QueuedFrames);
        _ = Dequeue(observerOutbound);

        var close = new TerrariaDoorToggleState((byte)TerrariaDoorToggleAction.CloseDoor, 40, 51, -1);
        state.Apply(new ClientDoorCloseRuntimeCommand(owner, close));

        for (int row = 0; row < 3; row++)
        {
            WorldTile retained = tiles.Get(40, 50 + row);
            WorldTile removed = tiles.Get(41, 50 + row);
            Assert.Equal(VanillaTileIds.ClosedDoor, retained.TileType);
            Assert.True(retained.IsActive);
            Assert.InRange(retained.FrameX, (short)0, (short)36);
            Assert.False(removed.IsActive);
        }
        Assert.Equal(0, ownerOutbound.QueuedFrames);
        Assert.Equal(1, observerOutbound.QueuedFrames);
        TerrariaFrame echoed = Dequeue(observerOutbound);
        Assert.Equal(TerrariaDoorToggleDecodeResult.Decoded,
            TerrariaDoorToggleCodec.TryDecode(in echoed, out TerrariaDoorToggleState echoedState));
        Assert.Equal(close, echoedState);
        Assert.Equal(2, state.AppliedClientTileManipulations);
    }

    private static void SetItem(ServerRuntimeState state, ConnectionHandle connection, short slot, short stack)
    {
        state.Apply(new PlayerEquipmentRuntimeCommand(connection, new PlayerEquipmentCommitRequest(
            connection.Player.Slot,
            slot,
            stack,
            Prefix: 0,
            ItemNetId: checked((short)VanillaPlanteraItemIds.TempleKey.Value),
            ItemFlags: 0)));
    }

    private static void PlaceLockedTempleDoor(WorldTileStore tiles, int x, int topY)
    {
        for (int offset = 0; offset < 3; offset++)
        {
            var tile = new WorldTile
            {
                Type = checked((ushort)VanillaTileIds.ClosedDoor.Value),
                FrameY = checked((short)(594 + offset * 18)),
                Flags = WorldTileFlags.Active
            };
            tiles.Set(x, topY + offset, in tile);
        }
    }

    private static void PlaceClosedDoor(WorldTileStore tiles, int x, int topY)
    {
        for (int offset = 0; offset < 3; offset++)
        {
            var tile = new WorldTile
            {
                Type = checked((ushort)VanillaTileIds.ClosedDoor.Value),
                FrameY = checked((short)(offset * 18)),
                Flags = WorldTileFlags.Active
            };
            tiles.Set(x, topY + offset, in tile);
        }
    }

    private static PlayerJoinSession CreatePlayingSession(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        return session;
    }

    private static PlayerSpawnCommitRequest Spawn(PlayerSlotId slot) => new(slot, 20, 20, 0, 0, 0, 0, 0);

    private static TerrariaConnectionOutboundQueue Outbound() =>
        new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 8_192, maxFrameBytes: 2_048));

    private static TerrariaFrame Decode(byte[] packet)
    {
        var sequence = new ReadOnlySequence<byte>(packet);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
        Assert.Equal(0, sequence.Length);
        return frame;
    }

    private static TerrariaFrame Dequeue(TerrariaConnectionOutboundQueue outbound)
    {
        PropertyInfo property = typeof(TerrariaConnectionOutboundQueue).GetProperty(
            "InnerQueue",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Outbound queue internal contract changed.");
        var queue = Assert.IsType<BoundedOutboundQueue>(property.GetValue(outbound));
        Assert.True(queue.TryRead(out OutboundFrame frame));
        return Decode(frame.Bytes.ToArray());
    }
}
