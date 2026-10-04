using System.Buffers;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class PlayerLuckFactors1458Tests
{
    // Original executable calls Player.RecalculateLuck, not a reimplementation of the equations.
    public static IEnumerable<object[]> OriginalCalculations()
    {
        using JsonDocument document = ReadFixture("player-luck-factors-official-1458");
        foreach (JsonElement row in document.RootElement.EnumerateArray())
            yield return [new VanillaPlayerLuckComponents1458(row.GetProperty("ladyBugTime").GetInt32(),
                row.GetProperty("torchLuck").GetSingle(), row.GetProperty("luckPotion").GetByte(),
                (row.GetProperty("mode").GetInt32() & 1) != 0, (row.GetProperty("mode").GetInt32() & 2) != 0,
                row.GetProperty("equipment").GetSingle(), row.GetProperty("coinLuck").GetSingle(),
                row.GetProperty("kiteLuckLevel").GetByte()), row.GetProperty("mode").GetInt32(), row.GetProperty("luck").GetSingle()];
    }

    [Theory, MemberData(nameof(OriginalCalculations))]
    public void Calculation_matches_original_Player(VanillaPlayerLuckComponents1458 components, int mode, float expected) =>
        Assert.Equal(expected, VanillaPlayerLuckFacts1458.Recalculate(components, (mode & 4) != 0, (mode & 4) != 0, (mode & 8) != 0));

    [Fact]
    public void Wire_matches_original_SendData134()
    {
        using JsonDocument document = ReadFixture("player-luck-wire-official-1458");
        foreach (JsonElement row in document.RootElement.EnumerateArray())
        {
            var factors = new VanillaPlayerLuckComponents1458(row.GetProperty("lady").GetInt32(), row.GetProperty("torch").GetSingle(),
                row.GetProperty("potion").GetByte(), row.GetProperty("garden").GetBoolean(), row.GetProperty("broken").GetBoolean(),
                row.GetProperty("equipment").GetSingle(), row.GetProperty("coin").GetSingle(), row.GetProperty("kite").GetByte());
            byte[] original = Convert.FromHexString(row.GetProperty("hex").GetString()!);
            Assert.Equal(original, TerrariaPlayerLuckFactorsCodec1458.Encode(row.GetProperty("slot").GetByte(), factors));
            TerrariaFrame frame = Decode(original);
            Assert.Equal(TerrariaPlayerLuckFactorsDecodeResult.Decoded, TerrariaPlayerLuckFactorsCodec1458.TryDecode(in frame, out byte slot, out var decoded));
            Assert.Equal(row.GetProperty("slot").GetByte(), slot); Assert.Equal(factors, decoded);
        }
    }

    [Fact]
    public void Actual_ingress_rewrites_forged_identity_and_rejects_stale_generations()
    {
        using var bootstrap = Bootstrap(); using var fixture = new Fixture();
        var commands = new CommandIngress(fixture.Authority);
        var sink = new PlayerLuckFactorsFrameSink(Source, bootstrap, new ContinuingSink(), new RuntimePlayerLuckFactorsNetworkIngress(commands));
        var factors = Factors; TerrariaFrame frame = Decode(TerrariaPlayerLuckFactorsCodec1458.Encode(199, factors));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in frame));
        Assert.Equal(fixture.Connection, commands.Last!.Connection);
        Assert.Equal(factors, fixture.State.LuckComponents);
        Assert.Equal(VanillaPlayerLuckFacts1458.Recalculate(factors, false, false, false), fixture.State.Luck);
        PlayerStateSnapshot before = fixture.State;
        fixture.Authority.TryApply(new PlayerLuckFactorsRuntimeCommand(fixture.Connection with { Source = GameCommandSourceId.FromConnection(99) }, default));
        fixture.Authority.TryApply(new PlayerLuckFactorsRuntimeCommand(new ConnectionHandle(Source,
            new PlayerHandle(fixture.Connection.Player.Slot, new PlayerSessionGeneration(99))), default));
        Assert.Equal(before, fixture.State);
    }

    [Theory]
    [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)] [InlineData(float.NegativeInfinity)]
    public void Non_finite_input_rejects_without_mutation(float invalid)
    {
        using var fixture = new Fixture(); fixture.Set(Factors); var before = fixture.State;
        foreach (var factors in new[] { Factors with { TorchLuck = invalid }, Factors with { CoinLuck = invalid }, Factors with { EquipmentBasedLuckBonus = invalid } })
            fixture.Set(factors);
        Assert.Equal(before, fixture.State);
    }

    [Fact]
    public void Missing_world_lantern_owner_rejects_instead_of_fabricating_flags()
    {
        using var fixture = new Fixture(unknownWorld: true); fixture.Set(Factors);
        Assert.Null(fixture.State.LuckComponents); Assert.Equal(0f, fixture.State.Luck);
    }

    [Fact]
    public void Source_timer_decay_and_same_generation_spawn_preserve_components()
    {
        using var fixture = new Fixture(); fixture.Set(Factors);
        fixture.Authority.TickPlayerLuck();
        var next = VanillaPlayerLuckFacts1458.AdvanceRemoteFactors(Factors, 1);
        Assert.Equal(next, fixture.State.LuckComponents);
        fixture.Authority.TryApply(new PlayerRespawnRuntimeCommand(fixture.Connection,
            new PlayerSpawnCommitRequest(fixture.Session.Slot, 12, 12, 0, 1, 0, 0, 0)));
        Assert.Equal(next, fixture.State.LuckComponents);
    }

    [Fact]
    public void Relay_excludes_sender_and_requires_current_generation()
    {
        var registry = new RuntimeConnectionRegistry();
        var first = new ConnectionHandle(Source, new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(1)));
        var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(7002), new PlayerHandle(new PlayerSlotId(1), new PlayerSessionGeneration(1)));
        var firstQueue = Queue(); var peerQueue = Queue();
        Assert.True(registry.TryRegister(first.Source, firstQueue)); Assert.True(registry.TryRegister(peer.Source, peerQueue));
        var a = new PlayerSpawnCommitRequest(first.Player.Slot, 10, 10, 0, 0, 0, 0, 0); var b = a with { ClaimedSlot = peer.Player.Slot };
        registry.PlayerSpawned(first, in a); registry.PlayerSpawned(peer, in b);
        Drain(firstQueue); Drain(peerQueue);
        registry.PlayerLuckFactorsUpdated(first, Factors);
        Assert.Equal(0, firstQueue.QueuedFrames); Assert.Equal(1, peerQueue.QueuedFrames);
        Assert.True(Inner(peerQueue).TryRead(out OutboundFrame frame));
        Assert.Equal(TerrariaPlayerLuckFactorsCodec1458.Encode(0, Factors), frame.Bytes.ToArray());
        registry.PlayerLuckFactorsUpdated(first with { Player = new PlayerHandle(first.Player.Slot, new PlayerSessionGeneration(2)) }, Factors);
        Assert.Equal(0, peerQueue.QueuedFrames);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Transfer_recalculates_retained_components_against_destination_lantern_owner(bool lanterns)
    {
        using var fixture = new Fixture(); fixture.Set(Factors);
        var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
        fixture.Authority.TryApply(new PlayerTransferDetachRuntimeCommand(fixture.Connection, detach));
        var transfer = Assert.IsType<RuntimePlayerTransferState>(await detach.Task);
        var destination = new PlayerAuthority(null, null, lanternsUp: lanterns);
        var attached = new TaskCompletionSource<bool>();
        destination.TryApply(new PlayerTransferAttachRuntimeCommand(fixture.Connection, transfer, 10, 10, false, true, attached));
        Assert.True(await attached.Task);
        Assert.True(destination.TryCapture(fixture.Connection.Player, out var state));
        Assert.Equal(Factors, state.LuckComponents);
        Assert.Equal(VanillaPlayerLuckFacts1458.Recalculate(Factors, false, lanterns, false), state.Luck);
    }

    [Fact]
    public void Finite_components_with_overflowing_combined_luck_reject_without_revision_change()
    {
        using var fixture = new Fixture(); fixture.Set(Factors); var before = fixture.State;
        fixture.Set(Factors with { TorchLuck = float.MaxValue, EquipmentBasedLuckBonus = float.MaxValue });
        Assert.Equal(before, fixture.State);
    }

    [Fact]
    public void Dead_player_does_not_age_remote_luck_components()
    {
        using var fixture = new Fixture(); fixture.Set(Factors);
        fixture.Authority.TryApply(new PlayerHealthRuntimeCommand(fixture.Connection,
            new PlayerHealthCommitRequest(fixture.Session.Slot, 0, 100)));
        Assert.True(fixture.State.IsDead);
        var before = fixture.State;
        fixture.Authority.TickPlayerLuck();
        Assert.Equal(before, fixture.State);
    }

    private static JsonDocument ReadFixture(string name)
    {
        using Stream resource = typeof(PlayerLuckFactors1458Tests).Assembly.GetManifestResourceStream($"TerraRuntime.Tests.Fixtures.{name}.json.gz")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress); return JsonDocument.Parse(gzip);
    }
    private static readonly VanillaPlayerLuckComponents1458 Factors = new(43200, .375f, 2, true, false, .125f, 249001f, 3);
    private static readonly GameCommandSourceId Source = GameCommandSourceId.FromConnection(7001);
    private static TerrariaConnectionOutboundQueue Queue() => new(new OutboundQueueOptions(64, 16384, 2048));
    private static BoundedOutboundQueue Inner(TerrariaConnectionOutboundQueue queue) =>
        (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(queue)!;
    private static void Drain(TerrariaConnectionOutboundQueue queue) { while (Inner(queue).TryRead(out _)) { } }
    private static TerrariaFrame Decode(byte[] bytes)
    { var sequence = new ReadOnlySequence<byte>(bytes); Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out var frame)); return frame; }
    private static PlayerBootstrapFrameSink Bootstrap()
    {
        var bootstrap = new PlayerBootstrapFrameSink(new PlayerSlotPool(1), Queue(),
            PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 }, Array.Empty<ReadOnlyMemory<byte>>(), new byte[] { 3, 0, 49 }), Source, new SpawnIngress());
        var frame = Decode(new byte[] { 15, 0, 1, 11, 84, 101, 114, 114, 97, 114, 105, 97, 51, 50, 54 }); bootstrap.OnFrame(in frame); return bootstrap;
    }
    private sealed class SpawnIngress : IPlayerSpawnCommitIngress
    { public bool TryPost(GameCommandSourceId source, PlayerJoinSession session, in PlayerSpawnCommitRequest request) => session.TryCommitSpawn(request.ClaimedSlot) == PlayerSpawnCommitResult.Committed; }
    private sealed class ContinuingSink : ITerrariaFrameSink { public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue; }
    private sealed class CommandIngress(PlayerAuthority authority) : IGameCommandIngress<RuntimeCommand>
    {
        public PlayerLuckFactorsRuntimeCommand? Last;
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command) { Last = Assert.IsType<PlayerLuckFactorsRuntimeCommand>(command); return authority.TryApply(command); }
    }
    private sealed class Fixture : IDisposable
    {
        public PlayerAuthority Authority { get; }
        public PlayerJoinSession Session { get; }
        public ConnectionHandle Connection { get; }
        public PlayerStateSnapshot State { get { Assert.True(Authority.TryCapture(Connection.Player, out var state)); return state; } }
        public Fixture(bool unknownWorld = false)
        {
            Authority = new(null, null, lanternsUp: unknownWorld ? null : false);
            var slots = new PlayerSlotPool(1); Assert.True(slots.TryAcquireConnection(out var lease)); Session = new(lease!);
            Session.ObserveWorldRequest(); Session.ObserveSectionRequest(); Connection = new(Source, Session.Handle);
            Authority.TryApply(new PlayerSpawnRuntimeCommand(Connection, Session, new PlayerSpawnCommitRequest(Session.Slot, 10, 10, 0, 0, 0, 0, 0)));
        }
        public void Set(VanillaPlayerLuckComponents1458 factors) => Authority.TryApply(new PlayerLuckFactorsRuntimeCommand(Connection, factors));
        public void Dispose() => Session.Dispose();
    }
}
