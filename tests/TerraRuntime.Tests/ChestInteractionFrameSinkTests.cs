using System.Buffers;
using global::Multiplicity.Packets;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class ChestInteractionFrameSinkTests
{
    [Fact]
    public void Playing_session_routes_chest_frames_with_exact_connection_identity()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(901);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingIngress();
        var sink = new ChestInteractionFrameSink(source, bootstrap, new PassthroughSink(), ingress);

        TerrariaFrame open = Packet(new ChestGetContents { TileX = 10, TileY = 20 });
        TerrariaFrame item = Packet(new ChestItem
        {
            ChestId = 3,
            ItemSlot = 1,
            Stack = 5,
            Prefix = 2,
            ItemNetId = 1
        });
        TerrariaFrame close = Packet(new ChestOpen
        {
            ChestId = -1,
            ChestX = 0,
            ChestY = 0,
            ChestName = string.Empty
        });
        TerrariaFrame lookup = Packet(new ChestName
        {
            ChestId = -1,
            ChestX = 10,
            ChestY = 20,
            HasName = false
        });

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in open));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in item));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in close));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in lookup));

        Assert.Equal(4, ingress.Count);
        Assert.Equal(source, ingress.Connection.Source);
        Assert.Equal(new PlayerSlotId(0), ingress.Connection.Player.Slot);
        Assert.Equal(new TerrariaChestOpenRequest(10, 20), ingress.OpenRequest);
        Assert.Equal(new TerrariaChestItemState(3, 1, 5, 2, 1), ingress.ItemState);
        Assert.Equal((short)-1, ingress.ActiveState.ChestId);
        Assert.Equal(new TerrariaChestNameLookupRequest(-1, 10, 20), ingress.NameLookup);
        Assert.Equal(ChestInteractionFrameStopReason.None, sink.StopReason);
    }

    [Fact]
    public void Chest_packet_before_playing_stops_connection()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(902);
        using PlayerBootstrapFrameSink bootstrap = CreateBootstrap(source);
        var ingress = new CapturingIngress();
        var sink = new ChestInteractionFrameSink(source, bootstrap, new PassthroughSink(), ingress);
        TerrariaFrame open = Packet(new ChestGetContents { TileX = 10, TileY = 20 });

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(in open));
        Assert.Equal(ChestInteractionFrameStopReason.InvalidJoinState, sink.StopReason);
        Assert.Equal(0, ingress.Count);
    }

    [Fact]
    public void Malformed_chest_packet_stops_before_ingress()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(903);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingIngress();
        var sink = new ChestInteractionFrameSink(source, bootstrap, new PassthroughSink(), ingress);
        TerrariaFrame malformed = Frame(TerrariaMessageId.SyncChestItem, new byte[7]);

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(in malformed));
        Assert.Equal(ChestInteractionFrameStopReason.MalformedChestPacket, sink.StopReason);
        Assert.Equal(0, ingress.Count);
    }

    [Fact]
    public void Bounded_chest_ingress_rejection_stops_connection()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(904);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var sink = new ChestInteractionFrameSink(source, bootstrap, new PassthroughSink(), new RejectingIngress());
        TerrariaFrame open = Packet(new ChestGetContents { TileX = 10, TileY = 20 });

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(in open));
        Assert.Equal(ChestInteractionFrameStopReason.GameIngressBackpressure, sink.StopReason);
    }

    private static TerrariaFrame Packet(TerrariaPacket packet)
    {
        using var stream = new MemoryStream();
        packet.ToStream(stream);
        byte[] encoded = stream.ToArray();
        var buffer = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        return frame;
    }

    private static PlayerBootstrapFrameSink CreatePlayingBootstrap(GameCommandSourceId source)
    {
        PlayerBootstrapFrameSink bootstrap = CreateBootstrap(source);
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Hello()));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(TerrariaMessageId.RequestWorldData, [])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(Frame(TerrariaMessageId.SpawnTileData, new byte[9])));
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(PlayerSpawn()));
        Assert.Equal(PlayerJoinState.Playing, bootstrap.JoinState);
        return bootstrap;
    }

    private static PlayerBootstrapFrameSink CreateBootstrap(GameCommandSourceId source) =>
        new(
            new PlayerSlotPool(1),
            new TerrariaConnectionOutboundQueue(
                new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 8_192, maxFrameBytes: 2_048)),
            PlayerBootstrapPacketSet.CreateForTesting(
                new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                Array.Empty<ReadOnlyMemory<byte>>(),
                new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }),
            source,
            new CommittingSpawnIngress());

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

    private sealed class CapturingIngress : IChestNetworkIngress
    {
        public int Count { get; private set; }
        public ConnectionHandle Connection { get; private set; }
        public TerrariaChestOpenRequest OpenRequest { get; private set; }
        public TerrariaChestItemState ItemState { get; private set; }
        public TerrariaActiveChestState ActiveState { get; private set; }
        public TerrariaChestNameLookupRequest NameLookup { get; private set; }

        public bool TryPostOpen(ConnectionHandle connection, in TerrariaChestOpenRequest request)
        {
            Count++;
            Connection = connection;
            OpenRequest = request;
            return true;
        }

        public bool TryPostItem(ConnectionHandle connection, in TerrariaChestItemState state)
        {
            Count++;
            Connection = connection;
            ItemState = state;
            return true;
        }

        public bool TryPostActiveState(ConnectionHandle connection, in TerrariaActiveChestState state)
        {
            Count++;
            Connection = connection;
            ActiveState = state;
            return true;
        }

        public bool TryPostNameLookup(ConnectionHandle connection, in TerrariaChestNameLookupRequest request)
        {
            Count++;
            Connection = connection;
            NameLookup = request;
            return true;
        }
    }

    private sealed class RejectingIngress : IChestNetworkIngress
    {
        public bool TryPostOpen(ConnectionHandle connection, in TerrariaChestOpenRequest request) => false;

        public bool TryPostItem(ConnectionHandle connection, in TerrariaChestItemState state) => false;

        public bool TryPostActiveState(ConnectionHandle connection, in TerrariaActiveChestState state) => false;

        public bool TryPostNameLookup(ConnectionHandle connection, in TerrariaChestNameLookupRequest request) => false;
    }
}
