using System.Buffers;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class ProjectileLifecycleFrameSinkTests
{
    [Fact]
    public void Playing_session_routes_valid_packet27_with_exact_connection_identity()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(701);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingIngress();
        var sink = new ProjectileLifecycleFrameSink(source, bootstrap, new PassthroughSink(), ingress);
        TerrariaProjectileUpdateState state = CreateUpdate(spawner: 0, type: 1, index: 777, generation: 9);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(UpdateFrame(in state)));

        Assert.Equal(1, ingress.UpdateCount);
        Assert.Equal(0, ingress.DestroyCount);
        Assert.Equal(source, ingress.Connection.Source);
        Assert.Equal(new PlayerSlotId(0), ingress.Connection.Player.Slot);
        Assert.Equal(state, ingress.Update);
        Assert.Equal(0, sink.DroppedAuthorityUpdates);
        Assert.Equal(ProjectileLifecycleFrameStopReason.None, sink.StopReason);
    }

    [Fact]
    public void Hostile_or_foreign_spawner_packet27_is_silently_dropped_like_vanilla()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(702);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingIngress();
        var sink = new ProjectileLifecycleFrameSink(source, bootstrap, new PassthroughSink(), ingress);
        TerrariaProjectileUpdateState hostile = CreateUpdate(spawner: 0, type: 31, index: 10, generation: 1);
        TerrariaProjectileUpdateState foreign = CreateUpdate(spawner: 1, type: 1, index: 11, generation: 1);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(UpdateFrame(in hostile)));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(UpdateFrame(in foreign)));

        Assert.Equal(0, ingress.UpdateCount);
        Assert.Equal(2, sink.DroppedAuthorityUpdates);
        Assert.Equal(ProjectileLifecycleFrameStopReason.None, sink.StopReason);
    }

    [Fact]
    public void Packet29_spawner_is_not_prevalidated_because_owner_check_is_authoritative()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(703);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingIngress();
        var sink = new ProjectileLifecycleFrameSink(source, bootstrap, new PassthroughSink(), ingress);
        var state = new TerrariaProjectileDestroyState(
            new TerrariaProjectileKeyState(Spawner: 42, ProjectileIndex: 888, Generation: 7),
            PositionX: 100f,
            PositionY: 200f);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(DestroyFrame(in state)));

        Assert.Equal(1, ingress.DestroyCount);
        Assert.Equal(state, ingress.Destroy);
        Assert.Equal(ProjectileLifecycleFrameStopReason.None, sink.StopReason);
    }

    [Fact]
    public void Projectile_packets_before_playing_stop_connection()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(704);
        using PlayerBootstrapFrameSink bootstrap = CreateBootstrap(source);
        var ingress = new CapturingIngress();
        var sink = new ProjectileLifecycleFrameSink(source, bootstrap, new PassthroughSink(), ingress);
        TerrariaProjectileUpdateState state = CreateUpdate(spawner: 0, type: 1, index: 1, generation: 1);

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(UpdateFrame(in state)));
        Assert.Equal(ProjectileLifecycleFrameStopReason.InvalidJoinState, sink.StopReason);
        Assert.Equal(0, ingress.TotalCount);
    }

    [Fact]
    public void Malformed_projectile_payload_stops_connection()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(705);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var ingress = new CapturingIngress();
        var sink = new ProjectileLifecycleFrameSink(source, bootstrap, new PassthroughSink(), ingress);
        TerrariaFrame malformed = Frame(TerrariaMessageId.ProjectileNew, []);

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(in malformed));
        Assert.Equal(ProjectileLifecycleFrameStopReason.MalformedUpdate, sink.StopReason);
        Assert.Equal(0, ingress.TotalCount);
    }

    [Fact]
    public void Bounded_game_ingress_rejection_stops_connection()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(706);
        using PlayerBootstrapFrameSink bootstrap = CreatePlayingBootstrap(source);
        var sink = new ProjectileLifecycleFrameSink(source, bootstrap, new PassthroughSink(), new RejectingIngress());
        TerrariaProjectileUpdateState state = CreateUpdate(spawner: 0, type: 1, index: 1, generation: 1);

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(UpdateFrame(in state)));
        Assert.Equal(ProjectileLifecycleFrameStopReason.GameIngressBackpressure, sink.StopReason);
    }

    private static TerrariaProjectileUpdateState CreateUpdate(byte spawner, int type, ushort index, ushort generation) =>
        new(
            new TerrariaProjectileKeyState(spawner, index, generation),
            type,
            100f,
            200f,
            1f,
            -2f,
            0f,
            0f,
            0f,
            0,
            25,
            2f,
            25);

    private static TerrariaFrame UpdateFrame(in TerrariaProjectileUpdateState state)
    {
        Assert.True(TerrariaProjectileEncoder.TryEncodeUpdate(in state, out byte[] encoded));
        return ReadFrame(encoded);
    }

    private static TerrariaFrame DestroyFrame(in TerrariaProjectileDestroyState state)
    {
        Assert.True(TerrariaProjectileEncoder.TryEncodeDestroy(in state, out byte[] encoded));
        return ReadFrame(encoded);
    }

    private static TerrariaFrame ReadFrame(ReadOnlyMemory<byte> encoded)
    {
        var buffer = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref buffer, out TerrariaFrame frame));
        Assert.Equal(0, buffer.Length);
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

    private sealed class CapturingIngress : IProjectileNetworkIngress
    {
        public int UpdateCount { get; private set; }
        public int DestroyCount { get; private set; }
        public int TotalCount => UpdateCount + DestroyCount;
        public ConnectionHandle Connection { get; private set; }
        public TerrariaProjectileUpdateState Update { get; private set; }
        public TerrariaProjectileDestroyState Destroy { get; private set; }

        public bool TryPostUpdate(ConnectionHandle connection, in TerrariaProjectileUpdateState state)
        {
            UpdateCount++;
            Connection = connection;
            Update = state;
            return true;
        }

        public bool TryPostDestroy(ConnectionHandle connection, in TerrariaProjectileDestroyState state)
        {
            DestroyCount++;
            Connection = connection;
            Destroy = state;
            return true;
        }
    }

    private sealed class RejectingIngress : IProjectileNetworkIngress
    {
        public bool TryPostUpdate(ConnectionHandle connection, in TerrariaProjectileUpdateState state) => false;
        public bool TryPostDestroy(ConnectionHandle connection, in TerrariaProjectileDestroyState state) => false;
    }
}
