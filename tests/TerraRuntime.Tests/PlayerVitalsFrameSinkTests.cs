using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class PlayerVitalsFrameSinkTests
{
    [Fact]
    public void Health_discards_forged_wire_player_id_and_uses_assigned_handle()
    {
        var healthIngress = new CapturingHealthIngress();
        var manaIngress = new CapturingManaIngress();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(42);
        using var bootstrap = CreateBootstrap(source);
        var sink = new PlayerVitalsFrameSink(source, bootstrap, healthIngress, manaIngress);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Health(claimedPlayerId: 99, life: 123, maxLife: 200)));

        Assert.Equal(PlayerVitalsStopReason.None, sink.StopReason);
        Assert.Equal(1, healthIngress.PostCount);
        Assert.Equal(source, healthIngress.Connection.Source);
        Assert.Equal(new PlayerSlotId(0), healthIngress.Connection.Player.Slot);
        Assert.True(healthIngress.Connection.Player.Generation.IsAssigned);
        Assert.Equal(new PlayerSlotId(0), healthIngress.Request.PlayerSlot);
        Assert.Equal((short)123, healthIngress.Request.Life);
        Assert.Equal((short)200, healthIngress.Request.MaxLife);
    }

    [Fact]
    public void Mana_discards_forged_wire_player_id_and_uses_assigned_handle()
    {
        var healthIngress = new CapturingHealthIngress();
        var manaIngress = new CapturingManaIngress();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(43);
        using var bootstrap = CreateBootstrap(source);
        var sink = new PlayerVitalsFrameSink(source, bootstrap, healthIngress, manaIngress);

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Mana(claimedPlayerId: 77, mana: 40, maxMana: 80)));

        Assert.Equal(PlayerVitalsStopReason.None, sink.StopReason);
        Assert.Equal(1, manaIngress.PostCount);
        Assert.Equal(source, manaIngress.Connection.Source);
        Assert.Equal(new PlayerSlotId(0), manaIngress.Connection.Player.Slot);
        Assert.Equal(new PlayerSlotId(0), manaIngress.Request.PlayerSlot);
        Assert.Equal((short)40, manaIngress.Request.Mana);
        Assert.Equal((short)80, manaIngress.Request.MaxMana);
    }

    [Fact]
    public void Health_ingress_backpressure_drops_replaceable_snapshot_without_stopping_connection()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(4401);
        using var bootstrap = CreateBootstrap(source);
        var sink = new PlayerVitalsFrameSink(
            source,
            bootstrap,
            new RejectingHealthIngress(),
            new CapturingManaIngress());

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal(
            TerrariaFrameSinkResult.Continue,
            sink.OnFrame(Health(claimedPlayerId: 0, life: 123, maxLife: 200)));
        Assert.Equal(PlayerVitalsStopReason.None, sink.StopReason);
        Assert.Equal(TerrariaFrameRejectionCategory.None, sink.RejectionCategory);
    }

    [Fact]
    public void Mana_ingress_backpressure_drops_replaceable_snapshot_without_stopping_connection()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(4402);
        using var bootstrap = CreateBootstrap(source);
        var sink = new PlayerVitalsFrameSink(
            source,
            bootstrap,
            new CapturingHealthIngress(),
            new RejectingManaIngress());

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal(
            TerrariaFrameSinkResult.Continue,
            sink.OnFrame(Mana(claimedPlayerId: 0, mana: 40, maxMana: 80)));
        Assert.Equal(PlayerVitalsStopReason.None, sink.StopReason);
        Assert.Equal(TerrariaFrameRejectionCategory.None, sink.RejectionCategory);
    }

    [Fact]
    public void Malformed_health_stops_the_vitals_layer()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(44);
        using var bootstrap = CreateBootstrap(source);
        var sink = new PlayerVitalsFrameSink(
            source,
            bootstrap,
            new CapturingHealthIngress(),
            new CapturingManaIngress());

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal(
            TerrariaFrameSinkResult.Stop,
            sink.OnFrame(Frame(TerrariaMessageId.PlayerHp, new byte[4])));
        Assert.Equal(PlayerVitalsStopReason.MalformedHealth, sink.StopReason);
        Assert.Equal(TerrariaFrameRejectionCategory.MalformedProtocol, sink.RejectionCategory);
    }

    [Fact]
    public void Delegated_bootstrap_invalid_state_is_exposed_as_rejection_category()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(45);
        using var bootstrap = CreateBootstrap(source);
        var sink = new PlayerVitalsFrameSink(
            source,
            bootstrap,
            new CapturingHealthIngress(),
            new CapturingManaIngress());

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(Hello()));

        Assert.Equal(PlayerVitalsStopReason.None, sink.StopReason);
        Assert.Equal(PlayerBootstrapStopReason.InvalidJoinState, bootstrap.StopReason);
        Assert.Equal(TerrariaFrameRejectionCategory.InvalidState, sink.RejectionCategory);
    }

    [Fact]
    public void Delegated_malformed_bootstrap_packet_is_exposed_as_malformed_protocol()
    {
        GameCommandSourceId source = GameCommandSourceId.FromConnection(46);
        using var bootstrap = CreateBootstrap(source);
        var sink = new PlayerVitalsFrameSink(
            source,
            bootstrap,
            new CapturingHealthIngress(),
            new CapturingManaIngress());

        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(Hello()));
        Assert.Equal(
            TerrariaFrameSinkResult.Stop,
            sink.OnFrame(Frame(TerrariaMessageId.SyncPlayer, Array.Empty<byte>())));

        Assert.Equal(PlayerVitalsStopReason.None, sink.StopReason);
        Assert.Equal(PlayerBootstrapStopReason.MalformedPlayerAppearance, bootstrap.StopReason);
        Assert.Equal(TerrariaFrameRejectionCategory.MalformedProtocol, sink.RejectionCategory);
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

    private static TerrariaFrame Health(byte claimedPlayerId, short life, short maxLife)
    {
        byte[] payload = new byte[5];
        payload[0] = claimedPlayerId;
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), life);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), maxLife);
        return Frame(TerrariaMessageId.PlayerHp, payload);
    }

    private static TerrariaFrame Mana(byte claimedPlayerId, short mana, short maxMana)
    {
        byte[] payload = new byte[5];
        payload[0] = claimedPlayerId;
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), mana);
        BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), maxMana);
        return Frame(TerrariaMessageId.PlayerMana, payload);
    }

    private static TerrariaFrame Frame(TerrariaMessageId id, byte[] payload) =>
        new(
            checked((ushort)(TerrariaFrameDecoderOptions.MinimumFrameLength + payload.Length)),
            (byte)id,
            ReadOnlySequence<byte>.Empty,
            new ReadOnlySequence<byte>(payload));

    private sealed class AcceptingSpawnIngress : IPlayerSpawnCommitIngress
    {
        public bool TryPost(
            GameCommandSourceId source,
            PlayerJoinSession session,
            in PlayerSpawnCommitRequest request) => true;
    }

    private sealed class RejectingHealthIngress : IPlayerHealthIngress
    {
        public bool TryPost(ConnectionHandle connection, in PlayerHealthCommitRequest request) => false;
    }

    private sealed class RejectingManaIngress : IPlayerManaIngress
    {
        public bool TryPost(ConnectionHandle connection, in PlayerManaCommitRequest request) => false;
    }

    private sealed class CapturingHealthIngress : IPlayerHealthIngress
    {
        public int PostCount { get; private set; }
        public ConnectionHandle Connection { get; private set; }
        public PlayerHealthCommitRequest Request { get; private set; }

        public bool TryPost(ConnectionHandle connection, in PlayerHealthCommitRequest request)
        {
            PostCount++;
            Connection = connection;
            Request = request;
            return true;
        }
    }

    private sealed class CapturingManaIngress : IPlayerManaIngress
    {
        public int PostCount { get; private set; }
        public ConnectionHandle Connection { get; private set; }
        public PlayerManaCommitRequest Request { get; private set; }

        public bool TryPost(ConnectionHandle connection, in PlayerManaCommitRequest request)
        {
            PostCount++;
            Connection = connection;
            Request = request;
            return true;
        }
    }
}
