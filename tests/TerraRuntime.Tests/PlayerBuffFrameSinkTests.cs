using System.Buffers;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class PlayerBuffFrameSinkTests
{
    [Fact]
    public void Pre_spawn_packet_50_discards_claimed_player_and_posts_assigned_generation()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(5101);
        using PlayerBootstrapFrameSink bootstrap = CreateAssignedBootstrap(source);
        var ingress = new CapturingBuffIngress();
        var sink = new PlayerBuffFrameSink(source, bootstrap, new ContinuingSink(), ingress);
        TerrariaFrame frame = Decode(TerrariaPlayerBuffCodec1458.Encode(
            player: 199,
            [VanillaBuffIds.OnFire, VanillaBuffIds.Slow]));

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in frame));
        Assert.True(ingress.Called);
        Assert.Equal(source, ingress.Connection.Source);
        Assert.Equal(bootstrap.AssignedPlayerHandle, ingress.Connection.Player);
        Assert.Equal([VanillaBuffIds.OnFire, VanillaBuffIds.Slow], ingress.Buffs);
    }

    [Fact]
    public void Malformed_packet_50_stops_as_protocol_error()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(5102);
        using PlayerBootstrapFrameSink bootstrap = CreateAssignedBootstrap(source);
        var sink = new PlayerBuffFrameSink(source, bootstrap, new ContinuingSink(), new CapturingBuffIngress());
        TerrariaFrame malformed = Frame(TerrariaMessageId.PlayerBuffs, [0, 24, 0]);

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(in malformed));
        Assert.Equal(PlayerBuffFrameStopReason.MalformedBuffSnapshot, sink.StopReason);
        Assert.Equal(TerrariaFrameRejectionCategory.MalformedProtocol, sink.RejectionCategory);
    }

    [Fact]
    public void Backpressure_drops_replaceable_buff_snapshot_without_disconnect()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(5103);
        using PlayerBootstrapFrameSink bootstrap = CreateAssignedBootstrap(source);
        var sink = new PlayerBuffFrameSink(source, bootstrap, new ContinuingSink(), new RejectingBuffIngress());
        TerrariaFrame frame = Decode(TerrariaPlayerBuffCodec1458.Encode(0, [VanillaBuffIds.OnFire]));

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in frame));
        Assert.Equal(PlayerBuffFrameStopReason.None, sink.StopReason);
    }

    private static PlayerBootstrapFrameSink CreateAssignedBootstrap(GameCommandSourceId source)
    {
        var bootstrap = new PlayerBootstrapFrameSink(
            new PlayerSlotPool(1),
            new TerrariaConnectionOutboundQueue(
                new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 8_192, maxFrameBytes: 2_048)),
            PlayerBootstrapPacketSet.CreateForTesting(
                new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
                Array.Empty<ReadOnlyMemory<byte>>(),
                new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf }),
            source,
            new CommittingSpawnIngress());
        TerrariaFrame hello = Frame(
            TerrariaMessageId.Hello,
            [11, (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a', (byte)'3', (byte)'2', (byte)'6']);
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(in hello));
        Assert.NotNull(bootstrap.AssignedPlayerHandle);
        return bootstrap;
    }

    private static TerrariaFrame Decode(byte[] encoded)
    {
        var input = new ReadOnlySequence<byte>(encoded);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref input, out TerrariaFrame frame));
        return frame;
    }

    private static TerrariaFrame Frame(TerrariaMessageId id, byte[] payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)id,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));

    private sealed class CommittingSpawnIngress : IPlayerSpawnCommitIngress
    {
        public bool TryPost(GameCommandSourceId source, PlayerJoinSession session, in PlayerSpawnCommitRequest request) =>
            session.TryCommitSpawn(request.ClaimedSlot) == PlayerSpawnCommitResult.Committed;
    }

    private sealed class ContinuingSink : ITerrariaFrameSink
    {
        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue;
    }

    private sealed class CapturingBuffIngress : IPlayerBuffNetworkIngress
    {
        public bool Called { get; private set; }
        public ConnectionHandle Connection { get; private set; }
        public BuffTypeId[] Buffs { get; private set; } = [];

        public bool TryPost(ConnectionHandle connection, ReadOnlyMemory<BuffTypeId> buffTypes)
        {
            Called = true;
            Connection = connection;
            Buffs = buffTypes.ToArray();
            return true;
        }
    }

    private sealed class RejectingBuffIngress : IPlayerBuffNetworkIngress
    {
        public bool TryPost(ConnectionHandle connection, ReadOnlyMemory<BuffTypeId> buffTypes) => false;
    }
}
