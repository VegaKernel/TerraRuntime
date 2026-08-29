using System.Buffers;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class TileManipulationFrameSinkTests
{
    [Fact]
    public void Playing_session_routes_packet17_with_exact_connection_identity()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(801);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingIngress();
        var sink = new TileManipulationFrameSink(source, bootstrap, new PassthroughSink(), ingress);
        var state = new TerrariaTileManipulationState(
            (byte)TerrariaTileManipulationAction.KillTile,
            123,
            456,
            0,
            0);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Packet17(in state)));

        Assert.Equal(1, ingress.Count);
        Assert.Equal(source, ingress.Connection.Source);
        Assert.Equal(new PlayerSlotId(0), ingress.Connection.Player.Slot);
        Assert.Equal(state, ingress.State);
        Assert.Equal(TileManipulationFrameStopReason.None, sink.StopReason);
    }

    [Fact]
    public void Packet17_before_playing_stops_connection()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(802);
        using PlayerBootstrapFrameSink bootstrap = CreateBootstrap(source);
        var ingress = new CapturingIngress();
        var sink = new TileManipulationFrameSink(source, bootstrap, new PassthroughSink(), ingress);
        var state = new TerrariaTileManipulationState(0, 1, 1, 0, 0);

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(Packet17(in state)));
        Assert.Equal(TileManipulationFrameStopReason.InvalidJoinState, sink.StopReason);
        Assert.Equal(0, ingress.Count);
    }

    [Fact]
    public void Malformed_packet17_stops_connection_before_ingress()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(803);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingIngress();
        var sink = new TileManipulationFrameSink(source, bootstrap, new PassthroughSink(), ingress);
        TerrariaFrame malformed = Frame(TerrariaMessageId.TileManipulation, new byte[7]);

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(in malformed));
        Assert.Equal(TileManipulationFrameStopReason.MalformedManipulation, sink.StopReason);
        Assert.Equal(0, ingress.Count);
    }

    [Fact]
    public void Bounded_game_ingress_rejection_stops_connection()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(804);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var sink = new TileManipulationFrameSink(source, bootstrap, new PassthroughSink(), new RejectingIngress());
        var state = new TerrariaTileManipulationState(0, 1, 1, 0, 0);

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(Packet17(in state)));
        Assert.Equal(TileManipulationFrameStopReason.GameIngressBackpressure, sink.StopReason);
    }

    private static TerrariaFrame Packet17(in TerrariaTileManipulationState state)
    {
        Assert.Equal(
            TerrariaTileManipulationEncodeResult.Encoded,
            TerrariaTileManipulationCodec.TryEncode(in state, out byte[] encoded));
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

    private sealed class CapturingIngress : ITileNetworkIngress
    {
        public int Count { get; private set; }
        public ConnectionHandle Connection { get; private set; }
        public TerrariaTileManipulationState State { get; private set; }

        public bool TryPost(ConnectionHandle connection, in TerrariaTileManipulationState state)
        {
            Count++;
            Connection = connection;
            State = state;
            return true;
        }
    }

    private sealed class RejectingIngress : ITileNetworkIngress
    {
        public bool TryPost(ConnectionHandle connection, in TerrariaTileManipulationState state) => false;
    }
}
