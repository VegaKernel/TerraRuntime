using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;
using System.Text.Json;
using System.Reflection;
using TerraRuntime.Network;
using System.IO.Compression;

namespace TerraRuntime.Tests;

public sealed class SlimeContainedDeath1458Tests
{
    [Theory]
    [InlineData(1)] [InlineData(59)] [InlineData(147)] [InlineData(184)] [InlineData(537)]
    public void Standard_composition_publishes_physical_creation_then_selected_owner(int type)
    {
        var registry = new RuntimeWorldItemReplicationRegistry();
        var items = new RuntimeWorldItemStore(registry);
        var npcs = new RuntimeNpcStore();
        var random = new SystemWorldItemSpawnRandom(1458);
        var state = new ServerRuntimeState(npcs: npcs, worldItems: items, worldItemSpawnRandom: random,
            worldItemReplication: registry, townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458));
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(813), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, 62, 62, 0, 0, 0, 0, 0)));
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 65536, 1024));
        Assert.True(registry.TryRegister(connection.Source, outbound));
        registry.PlayerSpawned(connection, new(session.Slot, 62, 62, 0, 0, 0, 0, 0));
        var queue = Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound));
        while (queue.TryRead(out _)) { }
        var body = NpcSimulationState.Initial with { MoneyValue = 0, ExtraMoneyValue = 0 };
        Assert.True(npcs.TrySpawnVanilla(new(type, checked((short)type), 1000.75f, 1000.25f,
            0, 0, 0, new(0, 2, 0, 0), body), out var npc));
        var graph = Assert.IsType<ServerRuntimeComposition>(typeof(ServerRuntimeState)
            .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state));
        Assert.True(graph.Npcs.TryStrikeBotPlayerMelee(session.Handle, npc.Handle, 100_000, 0, false, 0, 1));
        var packets = new List<byte[]>();
        while (queue.TryRead(out var frame)) if (frame.Bytes.Span[2] is 21 or 22) packets.Add(frame.Bytes.ToArray());
        Assert.True(packets.Count >= 2); Assert.Equal(21, packets[0][2]); Assert.Equal(22, packets[1][2]);
        Assert.Equal((byte)session.Slot.Value, packets[1][5]);
        Assert.Equal(0, packets[0][3]); Assert.Equal(0, packets[1][3]);
        Assert.True(items.TryGetActive(0, out var item)); Assert.Equal(2, item.ItemNetId);
        Assert.Equal(session.Slot.Value, item.OwnerPlayerId);
        var after = random.SourceRandom.Clone();
        Assert.False(graph.Npcs.TryStrikeBotPlayerMelee(session.Handle, npc.Handle, 100_000, 0, false, 0, 1));
        Assert.True(after.HasSameState(random.SourceRandom)); Assert.False(queue.TryRead(out _));
    }
    public static IEnumerable<object[]> OriginalDeaths()
    {
        // Preserve the standalone loot callback oracle. Lava's lethal producer additionally runs
        // dedicated HitEffect (netMode 2), then the semantic full-NPCLoot harness (netMode 0).
        // This composed reference does not claim whole checkDead or dedicated transport parity.
        foreach (var row in SlimeContainedLoot1458Tests.Rows("death"))
            if (((JsonElement)row[0]).GetProperty("type").GetInt32() != 59)
                yield return row;
        foreach (var row in SlimeContainedLoot1458Tests.Rows("lava-composed-death"))
            yield return row;
    }

    [Theory, MemberData(nameof(OriginalDeaths))]
    public void Actual_lethal_ingress_matches_original_imported_money_heals_and_next_rng(JsonElement row)
    {
        var fixture = new Fixture(row.GetProperty("seed").GetInt32());
        var npc = fixture.Spawn(row.GetProperty("type").GetInt32(), row.GetProperty("held").GetSingle(),
            row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32());
        var sourceBody = new NpcStateUpdate(npc.Type, npc.NetId, 1000.75f, 1000.25f, 0, 0,
            0, npc.Ai, npc.Simulation);
        Assert.True(fixture.Npcs.TryUpdate(npc.Handle, in sourceBody, out npc));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, fixture.Hit(npc));
        var actual = fixture.Items();
        Assert.Equal(row.GetProperty("drops").GetArrayLength(), actual.Length);
        int index = 0;
        foreach (var expected in row.GetProperty("drops").EnumerateArray())
        {
            var item = actual[index++];
            var state = new WorldItemDropStateUpdate(item.PositionX, item.PositionY, item.VelocityX,
                item.VelocityY, item.Stack, item.Prefix, item.Ownership, item.ItemNetId,
                item.Shimmered, item.ShimmerTime, item.EnemyGrabDelayTime);
            SlimeContainedLoot1458Tests.Equal(expected, in state);
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), fixture.Random.Next());
    }
    [Theory]
    [InlineData(1)] [InlineData(59)] [InlineData(147)] [InlineData(184)] [InlineData(537)]
    public void Actual_environment_death_materializes_content_before_specific_rewards_once(int type)
    {
        var fixture = new Fixture(1458);
        var npc = fixture.Spawn(type, 2);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, fixture.Hit(npc));
        var items = fixture.Items();
        Assert.Equal(2, items[0].ItemNetId);
        Assert.InRange(items[0].Stack, 10, 25);
        if (type != 59) Assert.Equal(23, items[1].ItemNetId);
        Assert.False(fixture.Npcs.TryGet(npc.Handle, out _));
        var after = fixture.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, fixture.Hit(npc));
        Assert.True(after.HasSameState(fixture.Random));
        Assert.Equal(items, fixture.Items());
    }

    [Theory]
    [InlineData(5000)]
    public void Unknown_source_eligible_content_rejects_before_npc_items_and_rng(int held)
    {
        var fixture = new Fixture(1458);
        var npc = fixture.Spawn(1, held);
        var before = fixture.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, fixture.Hit(npc));
        Assert.True(fixture.Npcs.TryGet(npc.Handle, out var unchanged)); Assert.Equal(npc, unchanged);
        Assert.True(before.HasSameState(fixture.Random)); Assert.Empty(fixture.Items());
    }

    [Theory]
    [InlineData(false, false, 1)] [InlineData(false, false, 42)] [InlineData(false, false, 1458)]
    [InlineData(true, false, 1)] [InlineData(true, false, 42)] [InlineData(true, false, 1458)]
    [InlineData(true, true, 1)] [InlineData(true, true, 42)] [InlineData(true, true, 1458)]
    public void Remix_lava_slime_prepared_projectile_death_requires_no_lava_tile_producer(bool expert, bool master, int seed)
    {
        foreach (bool lowTiles in new[] { false, true })
            AssertRemixLavaDeath(expert, master, seed, lowTiles);
    }

    private static void AssertRemixLavaDeath(bool expert, bool master, int seed, bool lowTiles)
    {
        var fixture = new Fixture(seed, expert: expert, master: master, remix: true, lowTiles: lowTiles);
        var npc = fixture.Spawn(59);
        var before = fixture.Random.Clone();
        var projectile = new ProjectileSnapshot(new(0, new(1)), new(1), new(1), 0,
            npc.PositionX, npc.PositionY, 1, 0, default, 0, 32767, 0, 32767);
        int publications = 0;
        // No WorldTileStore is supplied: source HitEffect's Remix guard must
        // exclude the producer even in Expert/Master, before any terrain read.
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, fixture.Pipeline.TryStrikePreparedProjectile(
            in projectile, in npc, 1, 32767, 0, false, before, before.Clone(), () => true,
            _ => publications++));
        Assert.Equal(1, publications);
        Assert.False(fixture.Npcs.TryGet(npc.Handle, out _));
        Assert.NotEmpty(fixture.Items());
        var accepted = fixture.Random.Clone();
        var items = fixture.Items();
        Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, fixture.Pipeline.TryStrikePreparedProjectile(
            in projectile, in npc, 1, 32767, 0, false, accepted, accepted.Clone(), () => true,
            _ => publications++));
        Assert.Equal(1, publications);
        Assert.True(accepted.HasSameState(fixture.Random));
        Assert.Equal(items, fixture.Items());
    }

    public static IEnumerable<object[]> RemixLavaHitEffects()
    {
        using var resource = typeof(SlimeContainedDeath1458Tests).Assembly.GetManifestResourceStream(
            "TerraRuntime.Tests.Fixtures.slime-lava-remix-hit-effect-official.json.gz")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.Clone()];
    }

    [Theory, MemberData(nameof(RemixLavaHitEffects))]
    public void Remix_lethal_hit_effect_matches_original_all_rng_cells_without_terrain(JsonElement row)
    {
        int mode = row.GetProperty("mode").GetInt32();
        var fixture = new Fixture(row.GetProperty("seed").GetInt32(), expert: mode > 0, master: mode == 2,
            remix: true, lowTiles: row.GetProperty("lowTiles").GetBoolean());
        var npc = fixture.Spawn(59);
        typeof(RuntimeNpcNetworkCombatPipeline).GetMethod("ExecuteNpcDeathHitEffects",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Pipeline, [npc]);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var expected = row.GetProperty("rng");
        Assert.Equal(expected.GetProperty("inext").GetUInt32(),
            (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", fields)!.GetValue(fixture.Random)!);
        Assert.Equal(expected.GetProperty("SeedArray").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", fields)!.GetValue(fixture.Random)!);
        Assert.True(row.GetProperty("terrainNotAccessed").GetBoolean());
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void Non_remix_lava_producer_still_refuses_without_owned_world_tile_death(bool expert, bool lowTiles)
    {
        var fixture = new Fixture(1458, expert: expert, lowTiles: lowTiles);
        var npc = fixture.Spawn(59);
        var before = fixture.Random.Clone();
        var projectile = new ProjectileSnapshot(new(0, new(1)), new(1), new(1), 0,
            npc.PositionX, npc.PositionY, 1, 0, default, 0, 32767, 0, 32767);
        int publications = 0;
        Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, fixture.Pipeline.TryStrikePreparedProjectile(
            in projectile, in npc, 1, 32767, 0, false, before, before.Clone(), () => true,
            _ => publications++));
        Assert.Equal(0, publications);
        Assert.True(fixture.Npcs.TryGet(npc.Handle, out var retained));
        Assert.Equal(npc, retained);
        Assert.True(before.HasSameState(fixture.Random));
        Assert.Empty(fixture.Items());
    }

    [Fact]
    public void Held_allocation_claim_rejects_before_content_rng_then_retry_materializes_once()
    {
        var fixture = new Fixture(1458); var npc = fixture.Spawn(1, 3347);
        Assert.True(fixture.Store.TryReserveDropSlot(out var held)); var before = fixture.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, fixture.Hit(npc));
        Assert.True(before.HasSameState(fixture.Random)); Assert.True(fixture.Npcs.TryGet(npc.Handle, out _));
        Assert.True(fixture.Store.TryReleaseDropReservation(in held));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, fixture.Hit(npc));
        Assert.Equal(3347, fixture.Items()[0].ItemNetId); Assert.InRange(fixture.Items()[0].Stack, 3, 13);
    }

    internal sealed class Fixture : IRuntimePlayerSlotSnapshotLookup
    {
        internal RuntimeNpcStore Npcs { get; } = new();
        internal RuntimeWorldItemStore Store { get; } = new();
        internal VanillaUnifiedRandom1458 Random { get; }
        internal RuntimeWorldClock Clock { get; } = new(0, true, 0, 0, 1);
        internal RuntimeWorldProgressionMutations Progression { get; } = new();
        internal RuntimeNpcNetworkCombatPipeline Pipeline { get; }
        internal PlayerHandle Player { get; } = new(new(0), new(1));
        internal Fixture(int seed, VanillaMechBossSpawnersContext1458 mechanicalLootBaseline = default,
            bool expert = false, bool master = false, bool remix = false, bool lowTiles = false)
        {
            Random = new(seed);
            Pipeline = new(Npcs, Store, this, new PlayerAuthority(null, null), static () => 0, null,
                new(Store), null, Clock, Progression, expert, master, lootRandom: Random,
                seasonalItemContext: () => default, mechanicalLootBaseline: mechanicalLootBaseline,
                lootRemixWorld: remix, skyblockLowTiles: lowTiles, npcSpecificLowTiles: () => lowTiles);
        }
        internal NpcSnapshot Spawn(int type, float held = 2, int width = 0, int height = 0, float value = 0)
        {
            var simulation = NpcSimulationState.Initial with { MoneyValue = value, ExtraMoneyValue = 0 };
            if (width != 0) simulation = simulation with { HitboxOverride = new(width, height) };
            Assert.True(Npcs.TrySpawnVanilla(new(type, checked((short)type), 16000.75f, 1000.25f,
                0, 0, 0, new(0, held, 0, 0), simulation), out var npc));
            return npc;
        }
        internal RuntimeTownNpcMeleeDamageResult1458 Hit(in NpcSnapshot npc) =>
            Pipeline.TryStrikeEnvironment(npc.Handle, 100_000);
        internal WorldItemSnapshot[] Items()
        {
            var items = new WorldItemSnapshot[Store.Capacity]; return items[..Store.CopyActive(items)];
        }
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = default(PlayerStateSnapshot) with { Player = Player, Revision = new(1),
                PositionX = 16000, PositionY = 1000, HasHealth = true, Life = 400, MaxLife = 400,
                HasMana = true, Mana = 200, MaxMana = 200 };
            return slot == Player.Slot;
        }
    }
}
