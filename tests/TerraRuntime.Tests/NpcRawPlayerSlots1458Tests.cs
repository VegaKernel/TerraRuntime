using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;
using System.IO.Compression;
using System.Text.Json;

namespace TerraRuntime.Tests;

public sealed class NpcRawPlayerSlots1458Tests
{
    [Fact]
    public void Bound_player_spawn_animation_final_disconnect_and_slot_ABA_have_owned_lifetimes()
    {
        var players = new PlayerAuthority(null, null);
        var lookup = new RuntimePlayerSnapshotLookup(players, null);
        var raw = new RuntimeNpcRawPlayerSlots1458(lookup, () => players.MembershipSerial);
        players.BindNpcRawPlayerSlots(raw);
        Assert.True(raw.TryCapture(0, out var constructor));
        Assert.Equal(VanillaNpcRawPlayer1458.Constructor(0), constructor.Facts);
        var pool = new PlayerSlotPool(1);
        using (var first = Join(players, pool, 800))
        {
            Assert.True(raw.TryCapture(0, out var attached));
            Assert.True(attached.Facts.Active);
            Assert.False(raw.IsCurrent(in constructor));
            Assert.True(players.TryApply(new PlayerItemAnimationRuntimeCommand(first.Connection, .25f, 9)));
            Assert.False(raw.IsCurrent(in attached));
            Assert.True(raw.TryCapture(0, out var animated));
            Assert.Equal(9, animated.Facts.ItemAnimation);
            Assert.True(players.TryApply(new PlayerDisconnectRuntimeCommand(first.Connection)));
            Assert.False(raw.IsCurrent(in animated));
            Assert.True(raw.TryCapture(0, out var reset));
            Assert.Equal(VanillaNpcRawPlayer1458.Constructor(0), reset.Facts);
            Assert.NotEqual(constructor.LifetimeRevision, reset.LifetimeRevision);
            Assert.False(raw.TryReset(first.Connection.Player));
        }
        using var second = Join(players, pool, 801);
        Assert.True(raw.TryCapture(0, out var rejoined));
        Assert.True(rejoined.Facts.Active);
        Assert.False(raw.IsCurrent(in constructor));
        Assert.True(raw.TryCapture(byte.MaxValue, out var sentinel));
        Assert.Equal(VanillaNpcRawPlayer1458.Constructor(byte.MaxValue), sentinel.Facts);
    }

    [Fact]
    public async Task Actual_transfer_detach_resets_source_and_attach_owns_destination_slot()
    {
        var source = new PlayerAuthority(null, null);
        var destination = new PlayerAuthority(null, null);
        var sourceRaw = new RuntimeNpcRawPlayerSlots1458(new RuntimePlayerSnapshotLookup(source, null), () => source.MembershipSerial);
        var destinationRaw = new RuntimeNpcRawPlayerSlots1458(new RuntimePlayerSnapshotLookup(destination, null), () => destination.MembershipSerial);
        source.BindNpcRawPlayerSlots(sourceRaw);
        destination.BindNpcRawPlayerSlots(destinationRaw);
        using var joined = Join(source, new PlayerSlotPool(1), 810);
        Assert.True(sourceRaw.TryCapture(0, out var before));
        var detached = new TaskCompletionSource<RuntimePlayerTransferState?>();
        Assert.True(source.TryApply(new PlayerTransferDetachRuntimeCommand(joined.Connection, detached)));
        var transfer = Assert.IsType<RuntimePlayerTransferState>(await detached.Task);
        Assert.True(sourceRaw.TryCapture(0, out var reset));
        Assert.Equal(VanillaNpcRawPlayer1458.Constructor(0), reset.Facts);
        Assert.False(sourceRaw.IsCurrent(in before));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(811), new(new(0), new(40)));
        var attached = new TaskCompletionSource<bool>();
        Assert.True(destination.TryApply(new PlayerTransferAttachRuntimeCommand(connection, transfer, 10, 10, true, false, attached)));
        Assert.True(await attached.Task);
        Assert.True(destinationRaw.TryCapture(0, out var after));
        Assert.True(after.Facts.Active);
        Assert.Equal(connection.Player, after.LivePlayer!.Value.Player);
        Assert.Equal(before.Facts.PositionX, after.Facts.PositionX);
        Assert.Equal(before.Facts.PositionY, after.Facts.PositionY);
    }

    [Fact]
    public void Bound_server_player_create_despawn_reuse_and_foreign_peer_change_invalidate_capture()
    {
        var pool = new PlayerSlotPool(2);
        var identities = new ServerPlayerSlotRegistry(pool);
        var states = new ServerPlayerStateStore(identities, 2);
        var bots = new ServerPlayerAuthority(states, identities);
        var players = new PlayerAuthority(null, null);
        var raw = new RuntimeNpcRawPlayerSlots1458(new RuntimePlayerSnapshotLookup(players, bots),
            () => players.MembershipSerial, () => bots.MembershipSerial);
        players.BindNpcRawPlayerSlots(raw);
        bots.BindNpcRawPlayerSlots(raw);
        var id = new ServerPlayerId("test:npc-raw");
        var first = bots.Create(id, 10, 20);
        Assert.True(first.IsCreated);
        Assert.True(raw.TryCapture(first.Player.Slot.Value, out var live));
        Assert.True(live.Facts.Active);
        Assert.Equal(10f, live.Facts.PositionX);
        var peer = bots.Create(new("test:npc-raw-peer"), 30, 40);
        Assert.True(peer.IsCreated);
        Assert.False(raw.IsCurrent(in live));
        Assert.True(bots.Despawn(id));
        Assert.True(raw.TryCapture(first.Player.Slot.Value, out var reset));
        Assert.Equal(VanillaNpcRawPlayer1458.Constructor(first.Player.Slot.Value), reset.Facts);
        var second = bots.Create(id, 50, 60);
        Assert.True(second.IsCreated);
        Assert.Equal(first.Player.Slot, second.Player.Slot);
        Assert.NotEqual(first.Player.Generation, second.Player.Generation);
        Assert.False(raw.TryReset(first.Player));
        Assert.False(raw.IsCurrent(in reset));
    }

    [Fact]
    public void Unbound_custom_active_snapshot_is_rejected_and_callbacks_cannot_replace_capture_baseline()
    {
        var lookup = new MutableLookup();
        var raw = new RuntimeNpcRawPlayerSlots1458(lookup);
        Assert.False(raw.TryCapture(0, out _));
        Assert.True(raw.TryAttach(lookup.Player.Player));
        Assert.True(raw.TryCapture(0, out var before));
        lookup.Callback = () =>
        {
            Assert.True(raw.TryReset(lookup.Player.Player));
            Assert.True(raw.TryAttach(lookup.Player.Player));
        };
        Assert.False(raw.TryCapture(0, out _));
        Assert.False(raw.IsCurrent(in before));
    }

    [Theory]
    [InlineData(0, false, -1, -1)]
    [InlineData(7, false, -1, -1)]
    [InlineData(255, false, -1, -1)]
    [InlineData(7, true, 0, 1)]
    public void No_living_refresh_preserves_valid_raw_slot_and_dead_facing(ushort target, bool dead, int dx, int dy)
    {
        byte slot = target == 255 ? (byte)0 : (byte)target;
        var raw = VanillaNpcRawPlayer1458.Constructor(slot) with { Dead = dead };
        Assert.True(VanillaNpcUnoccupiedTarget1458.TryRefresh(target, 0, 1, 1000, 1200, 24, 18, in raw, out var result));
        Assert.Equal(slot, result.Target);
        Assert.Equal(dx, result.DirectionX);
        Assert.Equal(dy, result.DirectionY);
    }

    public static IEnumerable<object[]> DifficultyCases()
    {
        using var input = typeof(NpcRawPlayerSlots1458Tests).Assembly.GetManifestResourceStream("NpcDifficultyWire1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        int index = 0;
        foreach (var row in json.RootElement.EnumerateArray()) yield return [index++, row.GetRawText()];
    }
    [Theory]
    [MemberData(nameof(DifficultyCases))]
    public void Original_packet23_in_all_modes_Good_and_explicit_overrides_carries_actual_difficulty(int index, string capturedJson)
    {
        _ = index;
        using var document = JsonDocument.Parse(capturedJson);
        var row = document.RootElement;
        var source = row.GetProperty("state");
        var simulation = NpcSimulationState.Initial with
        {
            Life = source.GetProperty("life").GetInt32(), LifeMax = source.GetProperty("lifeMax").GetInt32(),
            DirectionX = source.GetProperty("direction").GetInt32(), DirectionY = source.GetProperty("directionY").GetInt32(),
            SpriteDirection = row.GetProperty("spriteDirection").GetInt32(),
            HitboxOverride = new(source.GetProperty("width").GetInt32(), source.GetProperty("height").GetInt32()),
            SpawnDifficulty = row.GetProperty("difficulty").GetSingle()
        };
        var snapshot = new NpcSnapshot(new(0, new(1)), new(1), 1, 1,
            source.GetProperty("x").GetSingle(), source.GetProperty("y").GetSingle(), 0, 0, 0, default, simulation);
        Assert.True(RuntimeNpcPacketProjection.TryCreate(in snapshot, RuntimeNpcSyncKind.Spawn, out var projection));
        Assert.True(TerrariaNpcUpdateEncoder.TryEncode(in projection, out var actual));
        Assert.Equal(Convert.FromHexString(sourceFrame(row)), actual);
        Assert.False(TerrariaNpcUpdateEncoder.TryEncode(projection with { SpawnDifficulty = float.NaN }, out _));
        Assert.False(TerrariaNpcUpdateEncoder.TryEncode(projection with { SpawnDifficulty = 0f }, out _));
        static string sourceFrame(JsonElement row) => row.GetProperty("frames")[0].GetString()!;
    }

    public static IEnumerable<object[]> TrackingCases()
    {
        using var input = typeof(NpcRawPlayerSlots1458Tests).Assembly.GetManifestResourceStream("NpcInactiveLifecycle1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        int index = 0;
        foreach (var scenario in json.RootElement.EnumerateArray())
            foreach (var phase in scenario.GetProperty("phases").EnumerateArray())
            {
                var raw = phase.GetProperty("player");
                if (raw.GetProperty("active").GetBoolean() && !raw.GetProperty("dead").GetBoolean())
                    continue;
                yield return [index++, phase.GetRawText()];
            }
    }

    [Theory]
    [MemberData(nameof(TrackingCases))]
    public void Genuine_TargetClosest_on_inactive_slots_keeps_geometry_or_explicitly_rejects_unowned_metadata(int index, string capturedJson)
    {
        _ = index;
        using var document = JsonDocument.Parse(capturedJson);
        var phase = document.RootElement;
        var before = phase.GetProperty("freshBeforeTracking");
        var after = phase.GetProperty("freshAfterTracking");
        byte slot = checked((byte)after.GetProperty("target").GetInt32());
        var player = slot == 0 ? phase.GetProperty("player0") : phase.GetProperty("player");
        var facts = new VanillaNpcRawPlayer1458(slot, player.GetProperty("active").GetBoolean(),
            player.GetProperty("dead").GetBoolean(), player.GetProperty("ghost").GetBoolean(),
            player.GetProperty("x").GetSingle(), player.GetProperty("y").GetSingle(),
            player.GetProperty("width").GetInt32(), player.GetProperty("height").GetInt32(),
            player.GetProperty("aggro").GetInt32(), player.GetProperty("noAggro").GetBoolean(),
            player.GetProperty("itemAnimation").GetInt32());
        bool accepted = VanillaNpcUnoccupiedTarget1458.TryRefresh(checked((ushort)before.GetProperty("target").GetInt32()),
            before.GetProperty("direction").GetInt32(), before.GetProperty("directionY").GetInt32(),
            before.GetProperty("x").GetSingle(), before.GetProperty("y").GetSingle(),
            before.GetProperty("width").GetInt32(), before.GetProperty("height").GetInt32(), in facts, out var actual);
        if (facts.Aggro < 0 || facts.NoAggro || facts.Ghost)
        {
            Assert.False(accepted);
            return;
        }
        Assert.True(accepted);
        Assert.Equal(slot, actual.Target);
        Assert.Equal(after.GetProperty("direction").GetInt32(), actual.DirectionX);
        Assert.Equal(after.GetProperty("directionY").GetInt32(), actual.DirectionY);
    }

    private static Joined Join(PlayerAuthority authority, PlayerSlotPool pool, long source)
    {
        Assert.True(pool.TryAcquireConnection(out var lease));
        var session = new PlayerJoinSession(lease!);
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(source), session.Handle);
        Assert.True(authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Slot, 10, 10, 0, 0, 0, 0, 0))));
        return new(session, connection);
    }
    private sealed record Joined(PlayerJoinSession Session, ConnectionHandle Connection) : IDisposable
    {
        public void Dispose() => Session.Dispose();
    }
    private sealed class MutableLookup : IRuntimePlayerSlotSnapshotLookup
    {
        internal Action? Callback;
        internal PlayerStateSnapshot Player = new(new(new(0), new(1)), new(1), 0, 0, 0, 0, 0, 0,
            1000, 1238, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot state)
        {
            state = Player;
            var callback = Callback;
            Callback = null;
            callback?.Invoke();
            return slot.Value == 0;
        }
    }
}
