using System.Buffers;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class PlayerCombatFrameSinkTests
{
    [Fact]
    public void Pvp_toggle_backpressure_drops_replaceable_snapshot_without_stopping_connection()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(4601);
        using var bootstrap = CreateBootstrap(source);
        var sink = new PlayerCombatFrameSink(source, bootstrap, new ContinuingSink(), new RejectingCombatIngress());

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(PvpToggle(claimedPlayer: 99, hostile: true)));
        Assert.Equal(PlayerCombatFrameStopReason.None, sink.StopReason);
        Assert.Equal(TerrariaFrameRejectionCategory.None, sink.RejectionCategory);
    }

    [Fact]
    public void Team_backpressure_drops_replaceable_snapshot_without_stopping_connection()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(4602);
        using var bootstrap = CreateBootstrap(source);
        var sink = new PlayerCombatFrameSink(source, bootstrap, new ContinuingSink(), new RejectingCombatIngress());

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Team(claimedPlayer: 77, team: 3)));
        Assert.Equal(PlayerCombatFrameStopReason.None, sink.StopReason);
        Assert.Equal(TerrariaFrameRejectionCategory.None, sink.RejectionCategory);
    }

    [Fact]
    public void Pvp_hurt_backpressure_remains_connection_stopping_because_hit_is_discrete()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(4603);
        using var bootstrap = CreateBootstrap(source);
        var sink = new PlayerCombatFrameSink(source, bootstrap, new ContinuingSink(), new RejectingCombatIngress());

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        TerrariaPlayerHurtState hurt = new(
            TargetPlayer: 0,
            Reason: new TerrariaPlayerDeathReasonState(
                SourcePlayer: 1,
                SourceNpc: -1,
                SourceProjectileLocalIndex: -1,
                SourceOther: -1,
                SourceProjectileType: 0,
                SourceItemType: 0,
                SourceItemPrefix: 0,
                CustomReason: null),
            Damage: 10,
            HitDirectionWire: 1,
            Flags: 2,
            CooldownCounter: 0);
        Assert.Equal(TerrariaPlayerHurtEncodeResult.Encoded, TerrariaPlayerCombatCodec.TryEncodeHurt(in hurt, out byte[] encoded));

        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(Decode(encoded)));
        Assert.Equal(PlayerCombatFrameStopReason.GameIngressBackpressure, sink.StopReason);
        Assert.Equal(TerrariaFrameRejectionCategory.Backpressure, sink.RejectionCategory);
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
            new AcceptingSpawnIngress());

    private static TerrariaFrame Hello() =>
        Frame(
            TerrariaMessageId.Hello,
            [
                11,
                (byte)'T', (byte)'e', (byte)'r', (byte)'r', (byte)'a', (byte)'r', (byte)'i', (byte)'a',
                (byte)'3', (byte)'2', (byte)'6'
            ]);

    private static TerrariaFrame PvpToggle(byte claimedPlayer, bool hostile) =>
        Frame(TerrariaMessageId.TogglePvp, [claimedPlayer, hostile ? (byte)1 : (byte)0]);

    private static TerrariaFrame Team(byte claimedPlayer, byte team) =>
        Frame(TerrariaMessageId.PlayerTeam, [claimedPlayer, team]);

    private static TerrariaFrame Frame(TerrariaMessageId id, byte[] payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)id,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));

    private static TerrariaFrame Decode(byte[] encoded) =>
        new(
            checked((ushort)encoded.Length),
            encoded[2],
            new ReadOnlySequence<byte>(encoded),
            new ReadOnlySequence<byte>(encoded.AsMemory(3)));

    private sealed class AcceptingSpawnIngress : IPlayerSpawnCommitIngress
    {
        public bool TryPost(GameCommandSourceId source, PlayerJoinSession session, in PlayerSpawnCommitRequest request) => true;
    }

    private sealed class ContinuingSink : ITerrariaFrameSink
    {
        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue;
    }

    private sealed class RejectingCombatIngress : IPlayerCombatNetworkIngress
    {
        public bool TryPostPvpToggle(ConnectionHandle connection, bool hostile) => false;
        public bool TryPostTeam(ConnectionHandle connection, byte team) => false;
        public bool TryPostPvpHit(ConnectionHandle connection, in TerrariaPlayerHurtState state) => false;
    }
}
