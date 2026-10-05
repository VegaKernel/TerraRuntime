using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed partial class TownLootSource1458Tests
{
    public static IEnumerable<object[]> Rows(string suffix)
    {
        var assembly = typeof(TownLootSource1458Tests).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(suffix + ".json.gz", StringComparison.Ordinal)))!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }
    public static IEnumerable<object[]> Rules() => Rows("town-rules");
    public static IEnumerable<object[]> Items() => Rows("town-items");
    public static IEnumerable<object[]> Deaths() => Rows("town-deaths").Where(Admitted);
    public static IEnumerable<object[]> Batches() => Rows("town-batches").Where(static values =>
    {
        using var json = JsonDocument.Parse((string)values[0]);
        return !json.RootElement.TryGetProperty("legacy", out var legacy) || !legacy.GetBoolean();
    }).Where(Admitted);
    private static bool Admitted(object[] row)
    { using var json = JsonDocument.Parse((string)row[0]); return json.RootElement.GetProperty("id").GetInt32() != 368; }

    [Theory, MemberData(nameof(Rules))]
    public void Specific_tables_materialize_in_original_order_with_original_rng_in_both_reference_cultures(string raw)
    {
        using var json = JsonDocument.Parse(raw); var row = json.RootElement;
        int id = row.GetProperty("id").GetInt32();
        Assert.True(VanillaTownNpcLootRules1458.TryGet(new(id), out var rules));
        var context = new VanillaTownNpcLootContext1458(row.GetProperty("nameMode").GetString() == "matching",
            row.GetProperty("hard").GetBoolean());
        Assert.True(VanillaTownNpcLootRules1458.TryValidateContext(rules, in context));
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var rolls = new Rolls(random, row.GetProperty("luck").GetSingle());
        var origin = new NpcLootWorldItemOrigin(800 + row.GetProperty("width").GetInt32() / 2,
            800 + row.GetProperty("height").GetInt32() / 2);
        var actual = new List<WorldItemDropStateUpdate>();
        foreach (ref readonly var rule in rules)
        {
            Assert.True(VanillaTownNpcLootRules1458.TryEvaluateRule(in rule, in context, rolls, out bool dropped, out var drop));
            if (!dropped) continue;
            Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(in origin, in drop, rolls, out var item));
            actual.Add(item);
        }
        Equal(row.GetProperty("drops"), actual);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
    }

    [Theory, MemberData(nameof(Items))]
    public void Sparse_drop_defaults_and_natural_prefixes_match_original_without_item_use_admission(string raw)
    {
        using var json = JsonDocument.Parse(raw); var row = json.RootElement; var type = new ItemTypeId(row.GetProperty("id").GetInt32());
        Assert.True(VanillaTownNpcDropCatalog1458.TryGet(type, out var definition));
        Assert.Null(definition.UseTiming); Assert.Null(definition.Placement); Assert.Null(definition.PickTool);
        Assert.True(VanillaDefinitionCatalog.TryGetWorldDrop(type, out var drop));
        var defaults = row.GetProperty("values");
        Assert.Equal(defaults.GetProperty("maxStack").GetInt32(), definition.RuntimeDefaults.MaximumStack);
        Assert.Equal(defaults.GetProperty("width").GetInt32(), drop.Width);
        Assert.Equal(defaults.GetProperty("height").GetInt32(), drop.Height);
        Assert.Equal(defaults.GetProperty("noGravity").GetBoolean(), drop.NoGravity);
        foreach (var capture in row.GetProperty("rolls").EnumerateArray())
        {
            var rng = new VanillaUnifiedRandom1458(capture.GetProperty("seed").GetInt32()); var rolls = new Rolls(rng, 0);
            Assert.True(VanillaNaturalItemPrefixRoller.TryRoll(type, rolls, out var prefix));
            Assert.Equal(capture.GetProperty("prefix").GetInt32(), prefix.Value);
            Assert.Equal(capture.GetProperty("next").GetInt32(), rng.Next());
        }
        Assert.Equal(row.GetProperty("rollablePrefixes").EnumerateArray().Select(static p => p.GetInt32()).Order(),
            VanillaItemPrefixCatalog.GetRollablePrefixes(drop.PrefixFamily).ToArray().Select(static p => p.Value).Order());
        if (row.GetProperty("rollablePrefixes").GetArrayLength() != 0)
        {
            Assert.True(VanillaItemPrefixTable1458.TryGet(type, out var stats));
            foreach (var p in row.GetProperty("rollablePrefixes").EnumerateArray())
                Assert.Equal(row.GetProperty("validPrefixes").EnumerateArray().Any(v => v.GetInt32() == p.GetInt32()),
                    VanillaItemPrefixTable1458.Accepts(in stats, p.GetInt32()));
        }
    }

    [Theory, MemberData(nameof(Deaths))]
    public void Actual_lethal_ingress_matches_full_original_StrikeNPC_including_globals_heals_credit_and_rng(string raw)
    {
        using var json = JsonDocument.Parse(raw); var row = json.RootElement; var f = new Fixture(row, false);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, f.Pipeline.TryStrikeEnvironment(f.Victim.Handle, 9999, 10, 1));
        Check(row, f); Assert.False(f.Npcs.TryGet(f.Victim.Handle, out _));
    }

    [Theory, MemberData(nameof(Batches))]
    public void Ordered_two_Guides_then_selected_reward_victim_matches_actual_original_whole_doll(string raw)
    {
        using var json = JsonDocument.Parse(raw); var row = json.RootElement; var f = new Fixture(row, true);
        var doll = f.Doll();
        Assert.True(f.Pipeline.TryBurnGuideDollBatch(in doll, out int walls)); Assert.Equal(1, walls);
        Check(row, f);
        var survivors = new NpcSnapshot[f.Npcs.Capacity]; int count = f.Npcs.CopyActive(survivors);
        var expected = row.GetProperty("alive"); Assert.Equal(expected.GetArrayLength(), count);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(expected[i].GetProperty("slot").GetInt32(), survivors[i].Handle.Slot);
            Assert.Equal(expected[i].GetProperty("type").GetInt32(), survivors[i].Type);
            Assert.Equal(expected[i].GetProperty("x").GetSingle(), survivors[i].PositionX);
            Assert.Equal(expected[i].GetProperty("y").GetSingle(), survivors[i].PositionY);
        }
        Assert.Equal("item:Remove:267", f.Events.First());
        int first = f.Events.IndexOf("npc:Despawn:22"), wall = f.Events.IndexOf("npc:Spawn:113");
        int last = f.Events.LastIndexOf("npc:Despawn:22");
        Assert.True(first < wall && wall < last);
    }

    private static void Check(JsonElement row, Fixture f)
    {
        var items = new WorldItemSnapshot[f.Items.Capacity]; int count = f.Items.CopyActive(items);
        Equal(row.GetProperty("drops"), items[..count].Select(static i => new WorldItemDropStateUpdate(
            i.PositionX, i.PositionY, i.VelocityX, i.VelocityY, i.Stack, i.Prefix,
            WorldItemOwnershipMode.None, i.ItemNetId, false, 0, 0)).ToList());
        Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
        var credits = f.Prelude.CaptureBestiary().Kills.ToDictionary(static x => x.PersistentId, static x => x.KillCount);
        Assert.Equal(row.GetProperty("bestiary").EnumerateObject().Count(), credits.Count);
        foreach (var pair in row.GetProperty("bestiary").EnumerateObject()) Assert.Equal(pair.Value.GetInt32(), credits[pair.Name]);
    }

    private static void Equal(JsonElement expected, List<WorldItemDropStateUpdate> actual)
    {
        Assert.Equal(expected.GetArrayLength(), actual.Count);
        for (int i = 0; i < actual.Count; i++)
        {
            var e = expected[i]; var a = actual[i];
            Assert.Equal((e.GetProperty("id").GetInt32(), e.GetProperty("stack").GetInt32(), e.GetProperty("prefix").GetInt32()),
                ((int)a.ItemNetId, (int)a.Stack, (int)a.Prefix));
            Assert.Equal((e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(),
                e.GetProperty("vx").GetSingle(), e.GetProperty("vy").GetSingle()),
                (a.PositionX, a.PositionY, a.VelocityX, a.VelocityY));
        }
    }

    internal sealed class Rolls(VanillaUnifiedRandom1458 rng, float luck) : INpcLootRollSource
    {
        public int NextInt32(int min, int max) => rng.Next(min, max);
        public int RollLuck(int range)
        {
            if (luck > 0 && (float)rng.NextDouble() < luck) return rng.Next(rng.Next(range / 2, range));
            if (luck < 0 && (float)rng.NextDouble() < -luck) return rng.Next(rng.Next(range, range * 2));
            return rng.Next(range);
        }
    }

    internal sealed class Fixture : IRuntimePlayerSlotSnapshotLookup, INpcStateCommitSink, IWorldItemStateCommitSink, IDisposable
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeWorldItemStore Items;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeNpcDeathPrelude1458 Prelude = new();
        internal readonly RuntimeWorldProgressionMutations Progression = new();
        internal readonly RuntimeNpcNetworkCombatPipeline Pipeline;
        internal readonly NpcSnapshot Victim;
        internal readonly RuntimeTownNpcStateStore Residents;
        internal readonly List<string> Events = [];
        internal PlayerStateSnapshot Player;
        internal Func<NpcHandle, string?>? NameOverride = null;
        internal RuntimeNpcGlobalLootWorldFacts1458 World;
        internal Action? WorldRead;
        internal RuntimeWorldItemReplicationRegistry? Registry;
        internal TerrariaConnectionOutboundQueue? Outbound;
        private PlayerJoinSession? session;
        internal Fixture(JsonElement row, bool batch, VanillaTownNpcLootLanguage1458? language = VanillaTownNpcLootLanguage1458.English, bool wire = false)
        {
            int id = row.GetProperty("id").GetInt32(); bool injured = row.GetProperty("injured").GetBoolean();
            Random = new(row.GetProperty("seed").GetInt32()); Npcs = new(commitSink: this); Items = new(this);
            Npcs.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(Random));
            Player = default(PlayerStateSnapshot) with { Player = new(new(0), new(1)), Revision = new(1),
                PositionX = 1000, PositionY = 1000, Luck = batch ? 0 : row.GetProperty("luck").GetSingle(),
                Zones = default(PlayerZoneSnapshot1458), HasHealth = true, DerivedLifeMax = 100,
                NpcHealth = new(injured ? 50 : 100, 0, 0, true), NpcLifeCurrent = true,
                HasMana = true, Mana = 200, MaxMana = 200 };
            World = new(400, 400, 1, row.GetProperty("hard").GetBoolean(), false, false, false, 200, 140, true, true);
            var records = new List<WorldTownNpc>();
            if (batch) for (int i = 0; i < 2; i++)
            {
                Assert.True(Npcs.TrySpawn((byte)i, new(22, 22, 800 + 50 * i, 800, 0, 0, 255, default,
                    NpcSimulationState.Initial with { DirectionX = 1 }), out _));
                records.Add(new(22, "Andrew", 800 + 50 * i, 800, true, 0, 0, null, false));
            }
            var b = row.GetProperty("body"); var state = NpcSimulationState.Initial with
            { Life = b.GetProperty("lifeMax").GetInt32(), LifeMax = b.GetProperty("lifeMax").GetInt32(),
                DamageOverride = b.GetProperty("damage").GetInt32(), DefenseOverride = b.GetProperty("defense").GetInt32(),
                MoneyValue = b.GetProperty("value").GetSingle(), Friendly = b.GetProperty("friendly").GetBoolean(),
                HitboxOverride = new(b.GetProperty("width").GetInt32(), b.GetProperty("height").GetInt32()), DirectionX = 1 };
            int slot = batch ? 2 : 0;
            Assert.True(Npcs.TrySpawn((byte)slot, new(id, (short)id, 800 + 50 * slot, 800, 0, 0, 255, default, state), out Victim));
            string mode = row.GetProperty("mode").GetString()!;
            string name = mode == "matching" ? id == 178 ? "Whitney" : "Jim" : mode == "other" ? "NotTheMatchingName" : "";
            records.Add(new(id, name, 800 + 50 * slot, 800, true, 0, 0, null, false));
            Residents = new(new([], records.ToArray(), []), [], new(400, 400)); Residents.BindRuntimeNames(Npcs);
            var tiles = new WorldTileStore(new(400, 400));
            if (batch) for (int x = 150; x <= 170; x++) for (int y = 247; y <= 258; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = x is 150 or 170 || y == 258
                    ? new WorldTile { Type = 1, Flags = WorldTileFlags.Active }
                    : new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Lava };
            var authority = new PlayerAuthority(null, tiles);
            if (wire)
            {
                Registry = new(); Outbound = new(new OutboundQueueOptions(128, 131072, 4096));
                var pool = new PlayerSlotPool(1); Assert.True(pool.TryAcquireConnection(out var lease));
                session = new(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease)); session.ObserveWorldRequest(); session.ObserveSectionRequest();
                var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(23001), session.Handle);
                var spawn = new PlayerSpawnCommitRequest(session.Slot, 63, 65, 0, 0, 0, 0, 0);
                Assert.True(authority.TryApply(new PlayerSpawnRuntimeCommand(connection, session, spawn)));
                Assert.True(authority.TryApply(new PlayerMovementRuntimeCommand(connection, new(session.Slot, 0, 0, 0, 0, 0,
                    1000, 1000, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0))));
                Assert.True(authority.TryApply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, (short)(injured ? 50 : 100), 100))));
                Assert.True(Registry.TryRegister(connection.Source, Outbound)); Registry.PlayerSpawned(connection, in spawn);
                Items.AttachOwnerFactsProvider(new RuntimeWorldItemOwnerFactsProvider1458(authority, tiles, false, false));
            }
            Pipeline = new(Npcs, Items, this, authority, () => 0, null, new(Items), Registry,
                null, Progression, false, false, worldTiles: tiles, lootRandom: Random, deathPrelude: Prelude,
                globalLootWorldSource: () => { WorldRead?.Invoke(); return World; }, requireOwnedPlayerHealth: true,
                townNameSource: h => NameOverride is null ? Residents.CaptureResidentName(h) : NameOverride(h),
                townLootLanguage: language);
            if (!batch && row.GetProperty("interacted").GetBoolean())
                Assert.True(Pipeline.Interactions.TryMark(Victim.Handle, Player.Player));
            Events.Clear();
        }
        internal WorldItemSnapshot Doll()
        {
            Assert.True(Items.TryAllocate(new(2560, 4000, 0, 0, 3, 0, WorldItemOwnershipMode.None, 267,
                false, 0, 0, 255, 0, 255, 0), out var doll)); Events.Clear(); return doll;
        }
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot value) { value = Player; return slot.Value == 0; }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc) => Events.Add($"npc:{kind}:{npc.Type}");
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item)
        { Events.Add($"item:{kind}:{item.ItemNetId}"); Registry?.WorldItemStateCommitted(kind, in item); }
        public void Dispose() => session?.Dispose();
    }
}
