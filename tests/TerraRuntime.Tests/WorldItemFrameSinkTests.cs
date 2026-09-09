using System.Buffers;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class WorldItemFrameSinkTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(399)]
    public void Compact_removal_routes_the_authenticated_player_and_physical_slot(short slot)
    {
        var source = GameCommandSourceId.FromConnection(915);
        using var bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingWorldItemIngress();
        var sink = new WorldItemFrameSink(source, bootstrap, new PassthroughSink(), ingress);
        byte[] payload = [(byte)(slot & 255), (byte)(slot >> 8)];
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(TerrariaMessageId.WorldItemRemove, payload)));
        Assert.Equal(1, ingress.RemoveCount);
        Assert.Equal(slot, ingress.Slot);
        Assert.Equal(source, ingress.Connection.Source);
        Assert.Equal(bootstrap.AssignedPlayerHandle, ingress.Connection.Player);
    }

    [Fact]
    public void Compact_removal_before_spawn_rejects_and_queue_pressure_does_not_disconnect()
    {
        var source = GameCommandSourceId.FromConnection(916);
        using var joining = CreateBootstrap(source, new CommittingSpawnIngress());
        var ingress = new CapturingWorldItemIngress();
        var sink = new WorldItemFrameSink(source, joining, new PassthroughSink(), ingress);
        var frame = Frame(TerrariaMessageId.WorldItemRemove, [0, 0]);
        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(frame));
        Assert.Equal(WorldItemFrameStopReason.InvalidJoinState, sink.StopReason);
        Assert.Equal(0, ingress.TotalCount);
        using var playing = CreatePlayingBootstrap(source);
        var pressured = new WorldItemFrameSink(source, playing, new PassthroughSink(), new RejectingWorldItemIngress());
        Assert.Equal(TerrariaFrameSinkResult.Continue, pressured.OnFrame(frame));
        Assert.Equal(WorldItemFrameStopReason.None, pressured.StopReason);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(255, true)]
    public void Release_packet39_decodes_source_boolean_without_accepting_a_new_owner(byte wireBoolean, bool forced)
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(910);
        using var bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingWorldItemIngress();
        var sink = new WorldItemFrameSink(source, bootstrap, new PassthroughSink(), ingress);
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(TerrariaMessageId.ReleaseWorldItem, [143, 1, wireBoolean])));
        Assert.Equal(1, ingress.ReleaseCount);
        Assert.Equal(399, ingress.Slot);
        Assert.Equal(forced, ingress.ForceServer);
        Assert.Equal(source, ingress.Connection.Source);
        Assert.Equal(0, ingress.OwnerCount);
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0, 0 })]
    [InlineData(new byte[] { 0, 0, 1, 0 })]
    [InlineData(new byte[] { 144, 1, 1 })]
    [InlineData(new byte[] { 255, 255, 1 })]
    public void Release_packet39_rejects_malformed_or_nonphysical_slot(byte[] payload)
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(911);
        using var bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingWorldItemIngress();
        var sink = new WorldItemFrameSink(source, bootstrap, new PassthroughSink(), ingress);
        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(Frame(TerrariaMessageId.ReleaseWorldItem, payload)));
        Assert.Equal(WorldItemFrameStopReason.MalformedRelease, sink.StopReason);
        Assert.Equal(TerrariaFrameRejectionCategory.MalformedProtocol, sink.RejectionCategory);
        Assert.Equal(0, ingress.TotalCount);
    }

    [Fact]
    public void Playing_session_routes_allocate_drop_remove_and_ignores_server_only_owner_packet()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(901);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingWorldItemIngress();
        var sink = new WorldItemFrameSink(source, bootstrap, new PassthroughSink(), ingress);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(ItemDrop(itemIndex: 400, stack: 3, itemNetId: 1)));
        Assert.Equal(1, ingress.AllocateCount);
        Assert.Equal(source, ingress.Connection.Source);
        Assert.Equal(new PlayerSlotId(0), ingress.Connection.Player.Slot);
        Assert.Equal((short)3, ingress.Drop.Stack);
        Assert.Equal((short)1, ingress.Drop.ItemNetId);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(ItemDrop(itemIndex: 12, stack: 4, itemNetId: 2)));
        Assert.Equal(1, ingress.DropCount);
        Assert.Equal((short)12, ingress.Slot);
        Assert.Equal((short)4, ingress.Drop.Stack);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(ItemOwner(itemIndex: 12, ownerPlayerId: 0, grabDelayPlayer: 0)));
        Assert.Equal(0, ingress.OwnerCount);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(ItemRemoval(itemIndex: 12)));
        Assert.Equal(1, ingress.RemoveCount);
        Assert.Equal((short)12, ingress.Slot);
        Assert.Equal(WorldItemFrameStopReason.None, sink.StopReason);
    }

    [Fact]
    public void World_item_packets_before_playing_are_rejected()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(902);
        using PlayerBootstrapFrameSink bootstrap = CreateBootstrap(source, new CommittingSpawnIngress());
        var ingress = new CapturingWorldItemIngress();
        var sink = new WorldItemFrameSink(source, bootstrap, new PassthroughSink(), ingress);

        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Hello()));
        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(ItemDrop(itemIndex: 400, stack: 1, itemNetId: 1)));
        Assert.Equal(WorldItemFrameStopReason.InvalidJoinState, sink.StopReason);
        Assert.Equal(0, ingress.TotalCount);
    }

    [Fact]
    public void Packet22_from_client_is_ignored_in_server_mode()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(903);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingWorldItemIngress();
        var sink = new WorldItemFrameSink(source, bootstrap, new PassthroughSink(), ingress);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(ItemOwner(itemIndex: 8, ownerPlayerId: 7, grabDelayPlayer: 9)));
        Assert.Equal(WorldItemFrameStopReason.None, sink.StopReason);
        Assert.Equal(0, ingress.OwnerCount);
    }

    [Fact]
    public void Malformed_packet22_payload_is_ignored_in_server_mode()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(905);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingWorldItemIngress();
        var sink = new WorldItemFrameSink(source, bootstrap, new PassthroughSink(), ingress);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Frame(TerrariaMessageId.WorldItemOwner, [])));
        Assert.Equal(WorldItemFrameStopReason.None, sink.StopReason);
        Assert.Equal(0, ingress.TotalCount);
    }

    [Fact]
    public void New_item_sentinel_cannot_be_used_as_a_remove_slot()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(904);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingWorldItemIngress();
        var sink = new WorldItemFrameSink(source, bootstrap, new PassthroughSink(), ingress);

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(ItemRemoval(itemIndex: 400)));
        Assert.Equal(WorldItemFrameStopReason.MalformedDrop, sink.StopReason);
        Assert.Equal(0, ingress.RemoveCount);
    }


    [Fact]
    public void World_item_backpressure_drops_mutations_without_stopping_connection()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(906);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var sink = new WorldItemFrameSink(source, bootstrap, new PassthroughSink(), new RejectingWorldItemIngress());

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(ItemDrop(itemIndex: 400, stack: 3, itemNetId: 1)));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(ItemDrop(itemIndex: 12, stack: 4, itemNetId: 2)));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(ItemRemoval(itemIndex: 12)));
        Assert.Equal(WorldItemFrameStopReason.None, sink.StopReason);
        Assert.Equal(TerrariaFrameRejectionCategory.None, sink.RejectionCategory);
    }

    private static PlayerBootstrapFrameSink CreatePlayingBootstrap(GameCommandSourceId source)
    {
        var spawnIngress = new CommittingSpawnIngress();
        PlayerBootstrapFrameSink bootstrap = CreateBootstrap(source, spawnIngress);
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Hello()));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(TerrariaMessageId.RequestWorldData, [])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(TerrariaMessageId.SpawnTileData, new byte[9])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(PlayerSpawn()));
        Assert.Equal(PlayerJoinState.Playing, bootstrap.JoinState);
        return bootstrap;
    }

    private static PlayerBootstrapFrameSink CreateBootstrap(
        GameCommandSourceId source,
        IPlayerSpawnCommitIngress spawnIngress) =>
        new(
            new PlayerSlotPool(1),
            new TerrariaConnectionOutboundQueue(
                new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 8_192, maxFrameBytes: 2_048)),
            PlayerBootstrapPacketSet.CreateForTesting(
                new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                Array.Empty<ReadOnlyMemory<byte>>(),
                new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }),
            source,
            spawnIngress);

    private static TerrariaFrame ItemDrop(short itemIndex, short stack, short itemNetId)
    {
        var state = new TerrariaWorldItemState(
            ItemIndex: itemIndex,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 1.5f,
            VelocityY: -2.5f,
            Stack: stack,
            Prefix: 3,
            ItemNetId: itemNetId,
            Ownership: TerrariaWorldItemOwnership.ReserveForLocalPlayer,
            Shimmered: true,
            ShimmerTime: 4.5f,
            EnemyGrabDelayTime: 9,
            OwnerPlayerId: 0,
            TimeToKeepReservation: 60,
            GrabDelayPlayer: 0,
            GrabDelayTime: 30);
        Assert.Equal(
            TerrariaWorldItemBootstrapEncodeResult.Encoded,
            TerrariaWorldItemBootstrapEncoder.TryEncode(in state, out ReadOnlyMemory<byte> itemFrame, out _));
        return ReadFrame(itemFrame);
    }

    private static TerrariaFrame ItemOwner(short itemIndex, byte ownerPlayerId, byte grabDelayPlayer)
    {
        var state = new TerrariaWorldItemState(
            ItemIndex: itemIndex,
            PositionX: 101f,
            PositionY: 202f,
            VelocityX: 0f,
            VelocityY: 0f,
            Stack: 1,
            Prefix: 0,
            ItemNetId: 1,
            Ownership: TerrariaWorldItemOwnership.None,
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0,
            OwnerPlayerId: ownerPlayerId,
            TimeToKeepReservation: 60,
            GrabDelayPlayer: grabDelayPlayer,
            GrabDelayTime: 30);
        Assert.Equal(
            TerrariaWorldItemBootstrapEncodeResult.Encoded,
            TerrariaWorldItemBootstrapEncoder.TryEncode(in state, out _, out ReadOnlyMemory<byte> ownerFrame));
        return ReadFrame(ownerFrame);
    }

    private static TerrariaFrame ItemRemoval(short itemIndex)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(itemIndex);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write(0f);
            writer.Write((short)0);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)0);
        }

        return Frame(TerrariaMessageId.WorldItemDrop, stream.ToArray());
    }

    private static TerrariaFrame ReadFrame(ReadOnlyMemory<byte> encoded)
    {
        var buffer = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        Assert.Equal(0, buffer.Length);
        return frame;
    }

    private static TerrariaFrame Hello() =>
        Frame(
            TerrariaMessageId.Hello,
            [
                11,
                (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a',
                (byte)'3', (byte)'2', (byte)'6'
            ]);

    private static TerrariaFrame PlayerSpawn()
    {
        byte[] payload = new byte[TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];
        payload[0] = 0;
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), 100);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), 200);
        return Frame(TerrariaMessageId.PlayerSpawn, payload);
    }

    private static TerrariaFrame Frame(TerrariaMessageId id, byte[] payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)id,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));

    private sealed class CommittingSpawnIngress : IPlayerSpawnCommitIngress
    {
        public bool TryPost(
            GameCommandSourceId source,
            PlayerJoinSession session,
            in PlayerSpawnCommitRequest request) =>
            session.TryCommitSpawn(request.ClaimedSlot) == PlayerSpawnCommitResult.Committed;
    }

    private sealed class PassthroughSink : ITerrariaFrameSink
    {
        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue;
    }

    private sealed class CapturingWorldItemIngress : IWorldItemIngress
    {
        public int AllocateCount { get; private set; }
        public int DropCount { get; private set; }
        public int RemoveCount { get; private set; }
        public int OwnerCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public bool ForceServer { get; private set; }
        public int TotalCount => AllocateCount + DropCount + RemoveCount + OwnerCount + ReleaseCount;
        public ConnectionHandle Connection { get; private set; }
        public short Slot { get; private set; }
        public WorldItemDropStateUpdate Drop { get; private set; }
        public WorldItemOwnerStateUpdate Owner { get; private set; }

        public bool TryPostAllocate(ConnectionHandle connection, in WorldItemDropStateUpdate state)
        {
            AllocateCount++;
            Connection = connection;
            Drop = state;
            return true;
        }

        public bool TryPostRelease(ConnectionHandle connection, short slot, bool forceServer)
        {
            ReleaseCount++;
            Connection = connection;
            Slot = slot;
            ForceServer = forceServer;
            return true;
        }

        public bool TryPostDrop(ConnectionHandle connection, short slot, in WorldItemDropStateUpdate state)
        {
            DropCount++;
            Connection = connection;
            Slot = slot;
            Drop = state;
            return true;
        }

        public bool TryPostRemove(ConnectionHandle connection, short slot)
        {
            RemoveCount++;
            Connection = connection;
            Slot = slot;
            return true;
        }

        public bool TryPostOwner(ConnectionHandle connection, short slot, in WorldItemOwnerStateUpdate state)
        {
            OwnerCount++;
            Connection = connection;
            Slot = slot;
            Owner = state;
            return true;
        }
    }
    private sealed class RejectingWorldItemIngress : IWorldItemIngress
    {
        public bool TryPostAllocate(ConnectionHandle connection, in WorldItemDropStateUpdate state) => false;
        public bool TryPostDrop(ConnectionHandle connection, short slot, in WorldItemDropStateUpdate state) => false;
        public bool TryPostRemove(ConnectionHandle connection, short slot) => false;
        public bool TryPostOwner(ConnectionHandle connection, short slot, in WorldItemOwnerStateUpdate state) => false;
        public bool TryPostRelease(ConnectionHandle connection, short slot, bool forceServer) => false;
    }

}
