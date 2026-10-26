using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PaidBossSummon1458Tests
{
    [Fact]
    public void Source_sampler_uses_top_left_half_open_bounds_ordered_draws_and_final_attempt_space_bypass()
    {
        var player = Caster();
        var roster = Roster(player);
        var tiles = Tiles(150);
        var random = new SampleRandom(166, 48);
        Assert.True(RuntimeBossSummonSpawn1458.TryFind(tiles, in player, roster, random, out int x, out int y));
        Assert.Equal(166 * 16 + 8, x); Assert.Equal(150 * 16, y);
        Assert.Equal(2, random.Draws);
        Assert.Equal(new[] { (166, 334), (48, 152) }, random.Bounds.Take(2));
        // NPC.SpawnOnPlayer's last attempt skips HasTileSpawnSpace. Earlier attempts
        // must reject the same candidate because the source 2x3 rectangle is blocked.
        tiles.Set(165, 149, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        random = new SampleRandom(166, 48);
        Assert.True(RuntimeBossSummonSpawn1458.TryFind(tiles, in player, roster, random, out x, out y));
        Assert.Equal(49_902, random.Draws);
        Assert.Equal(166 * 16 + 8, x); Assert.Equal(150 * 16, y);
        random = new SampleRandom(166, 48);
        Assert.False(RuntimeBossSummonSpawn1458.TryFind(new(new(500, 300)), in player, roster, random, out _, out _));
        Assert.Equal(50_000, random.Draws);
    }

    [Fact]
    public void Source_sampler_retains_candidate_between_on_screen_attempts_and_samples_all_player_slots()
    {
        var player = Caster(); var roster = Roster(player); var tiles = Tiles(100);
        // First candidate is valid but on screen; later samples start inside solid ground.
        // Source retains found=true across the outer retries and accepts on its last attempt.
        var random = new SampleRandom(200, 100, firstY: 60);
        Assert.True(RuntimeBossSummonSpawn1458.TryFind(tiles, in player, roster, random, out int x, out int y));
        Assert.Equal(200 * 16 + 8, x); Assert.Equal(100 * 16, y);
        Assert.Equal(49_902, random.Draws);
        // A second active player covering the otherwise off-screen first candidate changes
        // the acceptance cadence to the final attempt, even though that player is not the caster.
        roster[254] = new(new(254, true, true, false, 166 * 16, 150 * 16, 20, 42, 0, false, 0),
            1, 1, player with { Player = new(new(254), new(1)), PositionX = 166 * 16, PositionY = 150 * 16, IsDead = true });
        random = new SampleRandom(166, 48);
        Assert.True(RuntimeBossSummonSpawn1458.TryFind(Tiles(150), in player, roster, random, out _, out _));
        Assert.Equal(1_000, random.Draws);
    }

    [Fact]
    public void Paid_single_root_boss_birth_and_announcement_follow_debit_once_with_source_lifetime_and_slot()
    {
        foreach (var row in new[] { (Boss: (short)4, Item: 43), (Boss: (short)134, Item: 556), (Boss: (short)127, Item: 557) })
        {
            using var f = new Fixture(); f.Grant(row.Item, 2, 2);
            f.OnBirth = () =>
            {
                Assert.Equal(1, f.Item.Stack); Assert.Equal(1, f.Funded);
                Assert.Equal(1, f.Npcs.ActiveCount);
                f.Request(row.Boss);
            };
            f.OnInventory = () =>
            {
                Assert.Equal(1, f.Item.Stack); Assert.Equal(1, f.Funded);
                Assert.Equal(1, f.Npcs.ActiveCount);
                f.Request(row.Boss);
            };
            f.Request(row.Boss); f.Request(row.Boss);
            Assert.Equal(1, f.Item.Stack); Assert.Equal(1, f.Funded);
            NpcSnapshot[] snapshots = new NpcSnapshot[10]; Assert.Equal(1, f.Npcs.CopyActive(snapshots));
            Assert.Equal(row.Boss, snapshots[0].Type);
            Assert.Equal(1, snapshots[0].Handle.Slot);
            Assert.Equal(VanillaNpcDefinitionCatalog.NewNpcTimeLeft * 20, snapshots[0].Simulation.TimeLeft);
            Assert.Single(f.InventoryNotifications);
            Assert.True(VanillaBossAnnouncementCatalog1458.TryGetSpawn(new(row.Boss), out string key, out string? name));
            var frames = f.Drain();
            Assert.Equal(new byte[] { 23, 82 }, frames.Select(frame => frame[2]).ToArray());
            Assert.Equal(TerrariaBossAnnouncementCodec1458.Encode(key, name), frames[1]);
        }
    }

    [Fact]
    public void Context_provider_reentry_cannot_spend_a_superseded_payment_or_publish_a_speculative_birth()
    {
        foreach (int change in new[] { 0, 4341, 5126 })
        {
            using var f = new Fixture(); f.Grant(43, 2, 2);
            ConnectionHandle observer = change == 0 ? default : f.AddObserver();
            Assert.True(f.Npcs.TryCaptureDeathSpawnContext(out var context)); Assert.NotNull(context);
            var captured = context.GetValueOrDefault();
            int calls = 0;
            f.Npcs.SetVanillaSpawnContextSource(() =>
            {
                if (calls++ == 0)
                {
                    if (change == 0) f.Grant(43, 3, 2);
                    // Mutate the real profile owner without changing public pose/revision:
                    // a raw player-census recapture alone must not authorize the old height.
                    else f.Equip(observer, change, profileOnly: true);
                }
                return captured;
            });
            var before = f.Random.Clone(); f.Request(4);
            Assert.True(calls > 0);
            Assert.Equal(0, f.Npcs.ActiveCount); Assert.Equal(change == 0 ? 3 : 2, f.Item.Stack); Assert.Equal(2, f.Funded);
            Assert.DoesNotContain(43, f.InventoryNotifications); Assert.Empty(f.Drain()); Assert.True(f.Random.HasSameState(before));
        }
    }

    [Fact]
    public void Unfunded_daytime_full_table_and_unrepresented_routes_refuse_without_payment_birth_or_rng()
    {
        foreach (int invalid in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
        {
            using var f = new Fixture(day: invalid == 1, capacity: invalid == 2 ? 1 : 200,
                tiles: invalid == 3 ? new(new(500, 300)) : null,
                gameMode: invalid == 4 ? (byte)3 : invalid == 5 ? null : (byte)0);
            f.Grant(43, 2, (short)(invalid == 0 ? 0 : 2));
            if (invalid == 2)
                Assert.True(f.Npcs.TrySpawn(0, new(1, 1, 10, 10, 0, 0, 0, default, NpcSimulationState.Initial), out _));
            if (invalid is 6 or 7)
            {
                f.Equip(f.AddObserver(), invalid == 6 ? 4341 : 5126);
                f.InventoryNotifications.Clear();
            }
            var before = f.Random.Clone(); int active = f.Npcs.ActiveCount;
            f.Request(4);
            Assert.Equal(active, f.Npcs.ActiveCount); Assert.Equal(2, f.Item.Stack);
            Assert.Equal(invalid == 0 ? 0 : 2, f.Funded); Assert.Empty(f.InventoryNotifications);
            Assert.True(f.Random.HasSameState(before));
        }
        foreach (short type in new short[] { -16, 13, 50, 125, 126, 128, 129, 130, 131, 222, 245, 266, 370, 657, 668 })
        {
            using var f = new Fixture(); f.Grant(43, 2, 2); var before = f.Random.Clone();
            f.Request(type); Assert.Equal(0, f.Npcs.ActiveCount); Assert.Equal(2, f.Funded);
            Assert.True(f.Random.HasSameState(before));
        }
    }

    private static PlayerStateSnapshot Caster() => new(new(new(0), new(1)), new(1), 0, 0, 0, 0, 0, 0,
        4000, 1600, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    private static NpcRawPlayerSlotSnapshot1458[] Roster(PlayerStateSnapshot player)
    {
        var result = new NpcRawPlayerSlotSnapshot1458[255];
        result[0] = new(new(0, true, false, false, player.PositionX, player.PositionY, 20, 42, 0, false, 0), 1, 1, player);
        return result;
    }
    private static WorldTileStore Tiles(int floor)
    {
        var tiles = new WorldTileStore(new(500, 300));
        for (int x = 0; x < 500; x++)
            for (int y = floor; y < 300; y++)
                tiles.Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        return tiles;
    }
    private sealed class SampleRandom(int x, int y, int? firstY = null) : IVanillaNpcRandom
    {
        internal int Draws; internal readonly List<(int, int)> Bounds = [];
        public int NextInt32(int min, int max)
        {
            Bounds.Add((min, max)); Draws++;
            int value = Draws % 2 == 1 ? x : Draws == 2 ? firstY ?? y : y;
            Assert.InRange(value, min, max - 1); return value;
        }
        public double NextDouble() => throw new InvalidOperationException();
    }

    private sealed class Fixture : IDisposable, IRuntimePlayerEventSink, INpcStateCommitSink, INpcBirthRetentionSink
    {
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly RuntimeNpcReplicationRegistry Replication = new();
        internal readonly RuntimeNpcStore Npcs;
        internal readonly ServerRuntimeState State;
        internal readonly ConnectionHandle Connection;
        internal readonly List<int> InventoryNotifications = [];
        internal Action? OnInventory;
        internal Action? OnBirth;
        private readonly RuntimePlayerInventoryStore inventory;
        private readonly PlayerAuthority players;
        private readonly RuntimePlayerTransferProfileStore profiles;
        private readonly PlayerJoinSession session;
        private readonly PlayerSlotPool pool = new(2);
        private readonly List<PlayerJoinSession> observers = [];
        private readonly TerrariaConnectionOutboundQueue queue;
        internal Fixture(bool day = false, int capacity = 200, WorldTileStore? tiles = null, byte? gameMode = 0)
        {
            Npcs = new(capacity, this);
            State = new(playerEvents: this, npcs: Npcs, npcReplication: Replication,
                worldTiles: tiles ?? Tiles(150), worldClock: new(100, day, default, 0, 0),
                townCommerceWorldFacts: RuntimeTownCommerceWorldFacts1458.FromMetadata(new WorldFileRuntimeMetadata())
                    with { GameMode = gameMode },
                naturalSpawnRandom: new SystemVanillaNpcRandom(Random));
            var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State)!;
            players = graph.Players;
            profiles = (RuntimePlayerTransferProfileStore)typeof(PlayerAuthority).GetField("transferProfiles",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(players)!;
            inventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graph.Players)!;
            Assert.True(pool.TryAcquireConnection(out var lease));
            session = new(lease!); session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(1458), session.Handle);
            queue = new(new OutboundQueueOptions(128, 131_072, 4096));
            Assert.True(Replication.TryRegister(Connection.Source, queue));
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 200, 200)));
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 250, 103, 0, 0, 0, 0, 0)));
            Assert.True(graph.Players.TryCapture(Connection.Player, out var caster));
            Assert.Equal(3998f, caster.PositionX); Assert.Equal(1606f, caster.PositionY);
            Replication.PlayerSpawned(Connection, new(session.Slot, 250, 103, 0, 0, 0, 0, 0));
            Drain();
        }
        internal RuntimePlayerInventoryItem Item { get { Assert.True(inventory.TryGet(Connection, 0, out var item)); return item; } }
        internal short Funded { get { Assert.True(inventory.TryGetFundedQuantity(Connection, 0, out var value)); return value; } }
        internal void Grant(int item, short stack, short funded)
        {
            Assert.True(inventory.TrySet(Connection, new(session.Slot, 0, stack, 0, (short)item, 0)));
            var budget = inventory.CaptureFundedQuantities(Connection); budget[0] = funded; inventory.AdoptFundedQuantities(Connection, budget);
        }
        internal void Request(short type) => State.Apply(new ClientBossSummonRuntimeCommand(Connection, type));
        internal ConnectionHandle AddObserver()
        {
            Assert.True(pool.TryAcquireConnection(out var lease));
            var joined = new PlayerJoinSession(lease!); observers.Add(joined);
            joined.ObserveWorldRequest(); joined.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1459), joined.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(connection, new(joined.Slot, 200, 200)));
            State.Apply(new PlayerSpawnRuntimeCommand(connection, joined, new(joined.Slot, 250, 103, 0, 0, 0, 0, 0)));
            return connection;
        }
        internal void Equip(ConnectionHandle connection, int type, bool profileOnly = false)
        {
            var update = new PlayerEquipmentCommitRequest(connection.Player.Slot, 62, 1, 0, (short)type, 0);
            if (profileOnly)
            {
                Assert.True(players.TryCapture(connection.Player, out var before));
                Assert.True(profiles.TrySetEquipment(connection, in update));
                Assert.True(players.TryCapture(connection.Player, out var after));
                Assert.Equal(before, after);
            }
            else State.Apply(new PlayerEquipmentRuntimeCommand(connection, update));
            Assert.True(players.TryCaptureProjectileUse(connection, out var body));
            Assert.Contains(body!.Equipment, equipment => equipment.SlotId == 62 && equipment.ItemNetId == type);
        }
        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
        { InventoryNotifications.Add(request.ItemNetId); OnInventory?.Invoke(); }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot)
        { OnBirth?.Invoke(); Replication.NpcStateCommitted(kind, in snapshot); }
        public void NpcBirthRetained(in NpcSnapshot snapshot)
        { OnBirth?.Invoke(); ((INpcBirthRetentionSink)Replication).NpcBirthRetained(in snapshot); }
        internal List<byte[]> Drain()
        {
            var inner = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
            var result = new List<byte[]>();
            while (inner.TryRead(out var frame)) result.Add(frame.Bytes.ToArray());
            return result;
        }
        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) { }
        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }
        public void PlayerDisconnected(ConnectionHandle connection) { }
        public void Dispose() { foreach (var observer in observers) observer.Dispose(); session.Dispose(); }
    }
}
