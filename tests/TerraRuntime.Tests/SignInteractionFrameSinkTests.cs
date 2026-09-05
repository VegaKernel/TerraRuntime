using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class SignInteractionFrameSinkTests
{
    [Fact]
    public void Playing_session_routes_sign_frames_with_exact_connection_identity()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(951);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingIngress();
        var inner = new CountingSink();
        var sink = new SignInteractionFrameSink(source, bootstrap, inner, ingress);

        TerrariaFrame read = SignRead(120, 45);
        TerrariaFrame update = SignState(new TerrariaSignState(3, 120, 45, "TerraRuntime", 77, 1));

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in read));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in update));

        Assert.Equal(2, ingress.Count);
        Assert.Equal(source, ingress.Connection.Source);
        Assert.Equal(new PlayerSlotId(0), ingress.Connection.Player.Slot);
        Assert.Equal(new TerrariaSignReadRequest(120, 45), ingress.ReadRequest);
        Assert.Equal(new TerrariaSignState(3, 120, 45, "TerraRuntime", 77, 1), ingress.State);
        Assert.Equal(0, inner.Count);
        Assert.Equal(SignInteractionFrameStopReason.None, sink.StopReason);
    }

    [Fact]
    public void Sign_packet_before_playing_stops_connection()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(952);
        using PlayerBootstrapFrameSink bootstrap = CreateBootstrap(source);
        var ingress = new CapturingIngress();
        var sink = new SignInteractionFrameSink(source, bootstrap, new CountingSink(), ingress);
        TerrariaFrame read = SignRead(10, 20);

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(in read));
        Assert.Equal(SignInteractionFrameStopReason.InvalidJoinState, sink.StopReason);
        Assert.Equal(0, ingress.Count);
    }

    [Fact]
    public void Malformed_sign_packet_stops_before_ingress()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(953);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingIngress();
        var sink = new SignInteractionFrameSink(source, bootstrap, new CountingSink(), ingress);
        TerrariaFrame malformed = Frame(TerrariaMessageId.RequestSign, new byte[3]);

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(in malformed));
        Assert.Equal(SignInteractionFrameStopReason.MalformedSignPacket, sink.StopReason);
        Assert.Equal(0, ingress.Count);
    }

    [Fact]
    public void Sign_read_backpressure_is_retryable_and_does_not_stop_connection()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(954);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var sink = new SignInteractionFrameSink(source, bootstrap, new CountingSink(), new RejectingIngress());
        TerrariaFrame read = SignRead(10, 20);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in read));
        Assert.Equal(SignInteractionFrameStopReason.None, sink.StopReason);
    }

    [Fact]
    public void Sign_update_backpressure_remains_connection_stopping_because_mutation_is_persistent()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(956);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var sink = new SignInteractionFrameSink(source, bootstrap, new CountingSink(), new RejectingIngress());
        TerrariaFrame update = SignState(new TerrariaSignState(3, 120, 45, "TerraRuntime", 77, 1));

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(in update));
        Assert.Equal(SignInteractionFrameStopReason.GameIngressBackpressure, sink.StopReason);
    }

    [Fact]
    public void Unrelated_frame_is_delegated_without_touching_sign_ingress()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(955);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingIngress();
        var inner = new CountingSink();
        var sink = new SignInteractionFrameSink(source, bootstrap, inner, ingress);
        TerrariaFrame frame = Frame(TerrariaMessageId.PlayerControls, []);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in frame));
        Assert.Equal(1, inner.Count);
        Assert.Equal(0, ingress.Count);
    }

    private static TerrariaFrame SignRead(short x, short y)
    {
        byte[] payload = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(0, 2), x);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(2, 2), y);
        return Frame(TerrariaMessageId.RequestSign, payload);
    }

    private static TerrariaFrame SignState(TerrariaSignState state)
    {
        byte[] encoded = TerrariaSignCodec.EncodeState(in state);
        var buffer = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        Assert.True(buffer.IsEmpty);
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
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), 100);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), 200);
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

    private sealed class CountingSink : ITerrariaFrameSink
    {
        public int Count { get; private set; }

        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)
        {
            Count++;
            return TerrariaFrameSinkResult.Continue;
        }
    }

    private sealed class CapturingIngress : ISignNetworkIngress
    {
        public int Count { get; private set; }
        public ConnectionHandle Connection { get; private set; }
        public TerrariaSignReadRequest ReadRequest { get; private set; }
        public TerrariaSignState State { get; private set; }

        public bool TryPostRead(ConnectionHandle connection, in TerrariaSignReadRequest request)
        {
            Count++;
            Connection = connection;
            ReadRequest = request;
            return true;
        }

        public bool TryPostUpdate(ConnectionHandle connection, in TerrariaSignState state)
        {
            Count++;
            Connection = connection;
            State = state;
            return true;
        }
    }

    private sealed class RejectingIngress : ISignNetworkIngress
    {
        public bool TryPostRead(ConnectionHandle connection, in TerrariaSignReadRequest request) => false;

        public bool TryPostUpdate(ConnectionHandle connection, in TerrariaSignState state) => false;
    }
}
