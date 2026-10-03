using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class PlayerStealth1458Tests
{
    [Theory]
    [InlineData(7, .25f, "080054070000803E")]
    [InlineData(199, .5f, "080054C70000003F")]
    public void Wire_bytes_match_original_NetMessage_SendData(int player, float stealth, string hex)
    {
        byte[] original = Convert.FromHexString(hex);
        Assert.Equal(original, TerrariaPlayerStealthCodec1458.Encode((byte)player, stealth));
        TerrariaFrame frame = Decode(original);
        Assert.Equal(TerrariaPlayerStealthDecodeResult.Decoded,
            TerrariaPlayerStealthCodec1458.TryDecode(in frame, out byte claimed, out float value));
        Assert.Equal(player, claimed); Assert.Equal(stealth, value);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(4)] [InlineData(6)]
    public void Invalid_payload_length_is_rejected_before_view_access(int length)
    {
        var frame = new TerrariaFrame(checked((ushort)(3 + length)), (byte)TerrariaMessageId.PlayerStealth,
            ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(new byte[length]));
        Assert.Equal(TerrariaPlayerStealthDecodeResult.InvalidPayloadLength,
            TerrariaPlayerStealthCodec1458.TryDecode(in frame, out _, out _));
    }

    [Fact]
    public void Fragmented_payload_uses_the_same_source_projection()
    {
        byte[] bytes = Convert.FromHexString("C70000003F");
        var first = new Segment(bytes.AsMemory(0, 2)); var last = first.Append(bytes.AsMemory(2));
        var payload = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        var frame = new TerrariaFrame(8, (byte)TerrariaMessageId.PlayerStealth, ReadOnlySequence<byte>.Empty, payload);
        Assert.Equal(TerrariaPlayerStealthDecodeResult.Decoded,
            TerrariaPlayerStealthCodec1458.TryDecode(in frame, out byte player, out float value));
        Assert.Equal(199, player); Assert.Equal(.5f, value);
    }

    [Fact]
    public void Queue_backpressure_drops_sample_without_disconnect()
    {
        using var bootstrap = Bootstrap();
        var sink = new PlayerStealthFrameSink(Source, bootstrap, new ContinuingSink(), new RejectingIngress());
        TerrariaFrame frame = Decode(TerrariaPlayerStealthCodec1458.Encode(199, .5f));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in frame));
        Assert.Equal(PlayerStealthFrameStopReason.None, sink.StopReason);
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
    [InlineData(-.01f)] [InlineData(1.01f)]
    public void Invalid_wire_values_stop_before_ingress(float value)
    {
        using var bootstrap = Bootstrap();
        var ingress = new ApplyingIngress(new PlayerAuthority(null, null));
        var sink = new PlayerStealthFrameSink(Source, bootstrap, new ContinuingSink(), ingress);
        byte[] bytes = Convert.FromHexString("0800540700000000");
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), value);
        TerrariaFrame frame = Decode(bytes);
        Assert.Equal(TerrariaFrameSinkResult.Stop, sink.OnFrame(in frame));
        Assert.Equal(TerrariaFrameRejectionCategory.MalformedProtocol, sink.RejectionCategory);
        Assert.Equal(0, ingress.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrariaPlayerStealthCodec1458.Encode(0, value));
    }

    [Fact]
    public void Actual_frame_ingress_rewrites_forged_slot_and_commits_only_authenticated_generation()
    {
        using var bootstrap = Bootstrap();
        using var fixture = new Fixture();
        var authority = fixture.Authority;
        PlayerHandle owner = bootstrap.AssignedPlayerHandle!.Value;
        // Admit the same authenticated lease through the actual bootstrap spawn session.
        var commands = new CommandIngress(authority);
        var ingress = new RuntimePlayerStealthNetworkIngress(commands);
        var sink = new PlayerStealthFrameSink(Source, bootstrap, new ContinuingSink(), ingress);
        TerrariaFrame forged = Decode(TerrariaPlayerStealthCodec1458.Encode(199, .25f));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in forged));
        Assert.Equal(Source, commands.Source);
        Assert.Equal(new ConnectionHandle(Source, owner), commands.Last!.Connection);
        Assert.Equal(.25f, commands.Last.Stealth);
        Assert.Equal(.25f, fixture.State.Stealth);
        fixture.Authority.TryApply(new PlayerStealthRuntimeCommand(
            fixture.Connection with { Source = GameCommandSourceId.FromConnection(999) }, .5f));
        fixture.Authority.TryApply(new PlayerStealthRuntimeCommand(new ConnectionHandle(fixture.Connection.Source,
            new PlayerHandle(fixture.Connection.Player.Slot, new PlayerSessionGeneration(999))), .5f));
        Assert.Equal(.25f, fixture.State.Stealth);
    }

    [Fact]
    public void Missing_projection_defaults_visible_and_respawn_preserves_owned_stealth()
    {
        Assert.Null(default(PlayerStateSnapshot).Stealth);
        using var fixture = new Fixture();
        Assert.Equal(1f, fixture.State.Stealth);
        fixture.Set(0f);
        fixture.Authority.TryApply(new PlayerRespawnRuntimeCommand(fixture.Connection,
            new PlayerSpawnCommitRequest(fixture.Session.Slot, 12, 12, 0, 1, 0, 0, 0)));
        Assert.Equal(0f, fixture.State.Stealth);
    }

    [Fact]
    public void Disconnect_replacement_starts_visible_and_retired_commands_cannot_hide_it()
    {
        using var fixture = new Fixture(); fixture.Set(0f);
        fixture.Authority.TryApply(new PlayerDisconnectRuntimeCommand(fixture.Connection));
        fixture.Session.Dispose(); Assert.True(fixture.Slots.TryAcquireConnection(out var lease));
        using var next = new PlayerJoinSession(lease!); next.ObserveWorldRequest(); next.ObserveSectionRequest();
        var replacement = new ConnectionHandle(Source, next.Handle);
        fixture.Authority.TryApply(new PlayerSpawnRuntimeCommand(replacement, next,
            new PlayerSpawnCommitRequest(next.Slot, 10, 10, 0, 0, 0, 0, 0)));
        fixture.Set(.25f);
        Assert.True(fixture.Authority.TryCapture(replacement.Player, out var state)); Assert.Equal(1f, state.Stealth);
        Assert.NotEqual(fixture.Connection.Player.Generation, next.Handle.Generation);
    }

    [Theory]
    [InlineData(0f)] [InlineData(1f)]
    public void Inclusive_endpoints_are_owned_and_invalid_direct_commands_preserve_revision(float value)
    {
        using var fixture = new Fixture(); fixture.Set(value);
        Assert.Equal(value, fixture.State.Stealth);
        PlayerStateSnapshot before = fixture.State;
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -.1f, 1.1f })
            fixture.Set(invalid);
        Assert.Equal(before, fixture.State);
    }

    [Theory]
    [InlineData(null, 1f)] [InlineData(0f, 0f)] [InlineData(.25f, .25f)]
    public async Task Transfer_retains_owned_projection_and_absence_defaults_visible(float? projected, float expected)
    {
        using var fixture = new Fixture();
        var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
        fixture.Authority.TryApply(new PlayerTransferDetachRuntimeCommand(fixture.Connection, detach));
        RuntimePlayerTransferState transfer = Assert.IsType<RuntimePlayerTransferState>(await detach.Task);
        transfer = transfer with { Player = transfer.Player with { Stealth = projected } };
        var events = new Events(); var destination = new PlayerAuthority(events, null); var complete = new TaskCompletionSource<bool>();
        destination.TryApply(new PlayerTransferAttachRuntimeCommand(fixture.Connection, transfer, 10, 10, false, true, complete));
        Assert.True(await complete.Task);
        Assert.True(destination.TryCapture(fixture.Connection.Player, out var after)); Assert.Equal(expected, after.Stealth);
        if (projected.HasValue) Assert.Equal(new[] { expected }, events.Stealth);
        else Assert.Empty(events.Stealth);
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(-.1f)] [InlineData(1.1f)]
    public async Task Invalid_transfer_projection_rejects_before_admission(float projected)
    {
        using var fixture = new Fixture();
        var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
        fixture.Authority.TryApply(new PlayerTransferDetachRuntimeCommand(fixture.Connection, detach));
        var transfer = Assert.IsType<RuntimePlayerTransferState>(await detach.Task);
        transfer = transfer with { Player = transfer.Player with { Stealth = projected } };
        var destination = new PlayerAuthority(null, null); var complete = new TaskCompletionSource<bool>();
        destination.TryApply(new PlayerTransferAttachRuntimeCommand(fixture.Connection, transfer, 10, 10, false, false, complete));
        Assert.False(await complete.Task); Assert.False(destination.TryCapture(fixture.Connection.Player, out _));
        var retry = new TaskCompletionSource<bool>();
        transfer = transfer with { Player = transfer.Player with { Stealth = 1f } };
        destination.TryApply(new PlayerTransferAttachRuntimeCommand(fixture.Connection, transfer, 10, 10, false, false, retry));
        Assert.True(await retry.Task);
    }

    [Fact]
    public async Task Accepted_Hurt_resets_stealth_but_immune_and_GodMode_rejections_preserve_it()
    {
        var events = new Events(); using var fixture = new Fixture(events);
        fixture.Set(.25f); events.Stealth.Clear();
        Assert.Equal(PlayerDamageCommitResult.Committed, fixture.Hurt(10));
        Assert.Equal(1f, fixture.State.Stealth); Assert.Equal(new[] { 1f }, events.Stealth);
        fixture.Set(.25f); events.Stealth.Clear();
        Assert.Equal(PlayerDamageCommitResult.Rejected, fixture.Hurt(11));
        Assert.Equal(.25f, fixture.State.Stealth); Assert.Empty(events.Stealth);
        var completion = new TaskCompletionSource<bool>();
        fixture.Authority.TryApply(new SetPlayerGodModeRuntimeCommand(fixture.Connection.Player, true, completion));
        Assert.True(await completion.Task);
        Assert.Equal(PlayerDamageCommitResult.AvoidedByGodMode, fixture.Hurt(1000));
        Assert.Equal(.25f, fixture.State.Stealth); Assert.Empty(events.Stealth);
    }

    [Fact]
    public void Accepted_Pvp_Hurt_resets_stealth_and_pvp_immunity_preserves_next_owned_value()
    {
        using var fixture = new Fixture(); fixture.Set(.25f);
        fixture.Authority.TryApply(new PlayerPvpToggleRuntimeCommand(fixture.Connection, true));
        PlayerStateSnapshot attacker = fixture.State with { Player = new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)), Hostile = true };
        Assert.Equal(PlayerDamageCommitResult.Committed, fixture.Authority.TryCommitAuthoritativePvpDamageFromSnapshot(
            10, in attacker, fixture.Connection.Player, DamageSource.FromPlayerItem(attacker.Player), 10, false, 1, out _));
        Assert.Equal(1f, fixture.State.Stealth); fixture.Set(.25f);
        Assert.Equal(PlayerDamageCommitResult.Rejected, fixture.Authority.TryCommitAuthoritativePvpDamageFromSnapshot(
            11, in attacker, fixture.Connection.Player, DamageSource.FromPlayerItem(attacker.Player), 10, false, 1, out _));
        Assert.Equal(.25f, fixture.State.Stealth);
    }

    [Fact]
    public void Updates_relay_only_to_peers_with_exact_generation_without_invented_join_baseline()
    {
        var registry = new RuntimeConnectionRegistry();
        var firstQueue = Queue(); var peerQueue = Queue();
        ConnectionHandle first = new(Source, new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        ConnectionHandle peer = new(GameCommandSourceId.FromConnection(7002),
            new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)));
        Assert.True(registry.TryRegister(first.Source, firstQueue)); Assert.True(registry.TryRegister(peer.Source, peerQueue));
        var a = new PlayerSpawnCommitRequest(first.Player.Slot, 10, 10, 0, 0, 0, 0, 0);
        var b = new PlayerSpawnCommitRequest(peer.Player.Slot, 10, 10, 0, 0, 0, 0, 0);
        registry.PlayerSpawned(first, in a); registry.PlayerSpawned(peer, in b);
        Drain(firstQueue); Drain(peerQueue);
        registry.PlayerStealthUpdated(first, .25f);
        Assert.Equal(0, firstQueue.QueuedFrames); Assert.Equal(1, peerQueue.QueuedFrames);
        Assert.True(Inner(peerQueue).TryRead(out OutboundFrame frame));
        Assert.Equal(TerrariaPlayerStealthCodec1458.Encode(0, .25f), frame.Bytes.ToArray());
        registry.PlayerStealthUpdated(first with { Player = new PlayerHandle(first.Player.Slot, new PlayerSessionGeneration(2)) }, .5f);
        Assert.Equal(0, peerQueue.QueuedFrames);
        registry.PlayerDisconnected(first);
        registry.PlayerStealthUpdated(first, .5f);
        Assert.DoesNotContain(ReadFrames(peerQueue), f => f.Bytes.Span[2] == (byte)TerrariaMessageId.PlayerStealth);
    }

    private static readonly GameCommandSourceId Source = GameCommandSourceId.FromConnection(7001);
    private static TerrariaConnectionOutboundQueue Queue() => new(new OutboundQueueOptions(64, 16384, 2048));
    private static BoundedOutboundQueue Inner(TerrariaConnectionOutboundQueue queue) =>
        (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
    private static void Drain(TerrariaConnectionOutboundQueue queue) { while (Inner(queue).TryRead(out _)) { } }
    private static IEnumerable<OutboundFrame> ReadFrames(TerrariaConnectionOutboundQueue queue)
    { while (Inner(queue).TryRead(out var frame)) yield return frame; }
    private static TerrariaFrame Decode(byte[] bytes)
    { var sequence = new ReadOnlySequence<byte>(bytes); Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out var frame)); return frame; }
    private static PlayerBootstrapFrameSink Bootstrap()
    {
        var bootstrap = new PlayerBootstrapFrameSink(new PlayerSlotPool(1), Queue(),
            PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 }, Array.Empty<ReadOnlyMemory<byte>>(), new byte[] { 3, 0, 49 }),
            Source, new SpawnIngress());
        var frame = Decode(new byte[] { 15, 0, 1, 11, 84, 101, 114, 114, 97, 114, 105, 97, 51, 50, 54 });
        Assert.Equal(TerrariaFrameSinkResult.Continue, bootstrap.OnFrame(in frame)); return bootstrap;
    }
    private sealed class SpawnIngress : IPlayerSpawnCommitIngress
    { public bool TryPost(GameCommandSourceId source, PlayerJoinSession session, in PlayerSpawnCommitRequest request) => session.TryCommitSpawn(request.ClaimedSlot) == PlayerSpawnCommitResult.Committed; }
    private sealed class ContinuingSink : ITerrariaFrameSink
    { public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue; }
    private sealed class RejectingIngress : IPlayerStealthNetworkIngress
    { public bool TryPost(ConnectionHandle connection, float stealth) => false; }
    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;
        public Segment Append(ReadOnlyMemory<byte> memory)
        { var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length }; Next = next; return next; }
    }
    private sealed class ApplyingIngress(PlayerAuthority authority) : IPlayerStealthNetworkIngress
    {
        public int Count; public ConnectionHandle Last; public float Value;
        public bool TryPost(ConnectionHandle connection, float stealth)
        { Count++; Last = connection; Value = stealth; return authority.TryApply(new PlayerStealthRuntimeCommand(connection, stealth)); }
    }
    private sealed class CommandIngress(PlayerAuthority authority) : IGameCommandIngress<RuntimeCommand>
    {
        public GameCommandSourceId Source; public PlayerStealthRuntimeCommand? Last;
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        { Source = source; Last = Assert.IsType<PlayerStealthRuntimeCommand>(command); return authority.TryApply(command); }
    }
    private sealed class Fixture : IDisposable
    {
        public PlayerAuthority Authority { get; }
        public PlayerSlotPool Slots { get; } = new(1);
        public PlayerJoinSession Session { get; }
        public ConnectionHandle Connection { get; }
        public PlayerStateSnapshot State { get { Assert.True(Authority.TryCapture(Connection.Player, out var state)); return state; } }
        public Fixture(IRuntimePlayerEventSink? events = null)
        {
            Authority = new PlayerAuthority(events, null);
            Assert.True(Slots.TryAcquireConnection(out var lease)); Session = new PlayerJoinSession(lease!);
            Session.ObserveWorldRequest(); Session.ObserveSectionRequest(); Connection = new ConnectionHandle(Source, Session.Handle);
            Authority.TryApply(new PlayerSpawnRuntimeCommand(Connection, Session, new PlayerSpawnCommitRequest(Session.Slot, 10, 10, 0, 0, 0, 0, 0)));
            Authority.TryApply(new PlayerHealthRuntimeCommand(Connection, new PlayerHealthCommitRequest(Session.Slot, 100, 100)));
            Authority.TryApply(new PlayerEquipmentRuntimeCommand(Connection,
                new PlayerEquipmentCommitRequest(Session.Slot, VanillaPlayerItemSlotCatalog.ArmorStart, 0, 0, 0, 0)));
        }
        public void Set(float value) => Authority.TryApply(new PlayerStealthRuntimeCommand(Connection, value));
        public PlayerDamageCommitResult Hurt(long tick) => Authority.TryCommitAuthoritativeNpcContactDamage(tick,
            new NpcHandle(1, new NpcGeneration(1)), Connection.Player, 10, 1, VanillaPlayerImmunityChannel1458.General, out _);
        public void Dispose() => Session.Dispose();
    }
    private sealed class Events : IRuntimePlayerEventSink
    {
        public List<float> Stealth { get; } = [];
        public void PlayerStealthUpdated(ConnectionHandle connection, float stealth) => Stealth.Add(stealth);
        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request) { }
        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) { }
        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }
        public void PlayerDisconnected(ConnectionHandle connection) { }
    }
}
