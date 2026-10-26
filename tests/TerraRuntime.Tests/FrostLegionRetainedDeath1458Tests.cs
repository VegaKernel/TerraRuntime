using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class FrostLegionRetainedDeath1458Tests
{
    [Fact]
    public void Original_hit_death_events_and_specific_loot_preserve_all_144_rng_rows()
    {
        using var resource = typeof(FrostLegionRetainedDeath1458Tests).Assembly.GetManifestResourceStream("FrostLegionDeath1458");
        Assert.NotNull(resource);
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal("74D01299FCD217286924668C86E9B855EB0BA58209F5C55CC701469503AB130A",
            Convert.ToHexString(SHA256.HashData(bytes.ToArray())));
        using var reference = JsonDocument.Parse(bytes.ToArray());
        var rows = reference.RootElement.GetProperty("rows").EnumerateArray().ToArray();
        Assert.Equal(144, rows.Length);
        foreach (var row in rows)
        {
            int type = row.GetProperty("type").GetInt32(), seed = row.GetProperty("seed").GetInt32();
            var fixture = new BackendFixture(type, seed);
            var dead = fixture.Npc with { Simulation = fixture.Npc.Simulation with { Life = 0 } };
            Invoke(fixture.Pipeline, "ExecuteNpcDeathHitEffects", dead);
            Assert.Equal(row.GetProperty("hitNext").GetInt32(), fixture.Random.Next());
            fixture = new BackendFixture(type, seed);
            Invoke(fixture.Pipeline, "ExecuteOwnedDeathEvents", dead, false);
            Assert.Equal(row.GetProperty("deathNext").GetInt32(), fixture.Random.Next());
            Assert.Equal(row.GetProperty("baselineNext").GetInt32(), row.GetProperty("deathNext").GetInt32());
            var random = new VanillaUnifiedRandom1458(seed);
            var rolls = new CorpusRolls(random);
            Assert.True(VanillaNpcLootRuleCatalog.TryGetNpcSpecificTable(new(type), out var table));
            var rule = Assert.Single(table.Rules.ToArray());
            Assert.True(VanillaNpcLootEvaluator.TryEvaluateRule(in rule,
                new(row.GetProperty("mode").GetInt32() != 0, false), rolls, out bool dropped, out var drop));
            Assert.True(dropped);
            var expected = Assert.Single(row.GetProperty("drops").EnumerateArray().ToArray());
            Assert.Equal(expected[0].GetInt32(), drop.ItemType.Value);
            Assert.Equal(expected[1].GetInt32(), drop.Stack);
            Assert.Equal(row.GetProperty("lootNext").GetInt32(), random.Next());
        }
    }

    [Fact]
    public void Trusted_backend_plan_adopts_frost_credit_loot_rng_and_terminal_before_first_observer()
    {
        foreach (int type in new[] { 143, 144, 145 })
        {
            var fixture = new BackendFixture(type);
            int adopted = 0, drops = 0;
            fixture.OnAdopted = () =>
            {
                adopted++;
                Assert.Empty(fixture.Events);
                Assert.False(fixture.Npcs.TryGet(fixture.Npc.Handle, out _));
                Assert.Equal(11, fixture.State.Size);
                Assert.True(fixture.Items.ActiveCount > 0);
            };
            fixture.OnDrop = () =>
            {
                drops++;
                Assert.Equal(1, adopted);
                Assert.False(fixture.Npcs.TryGet(fixture.Npc.Handle, out _));
                Assert.Equal(11, fixture.State.Size);
                var accepted = fixture.Random.Clone();
                Assert.False(fixture.CompleteBackend(fixture.Npc.Handle));
                Assert.True(fixture.Random.HasSameState(accepted));
            };
            Assert.True(fixture.CompleteBackend(fixture.Npc.Handle));
            Assert.Equal(1, adopted); Assert.True(drops > 0);
            var snow = Assert.Single(fixture.Drops, item => item.ItemNetId == 593);
            Assert.InRange(snow.Stack, 5, 10);
            Assert.Equal(109, fixture.State.Progress); Assert.Equal(5, fixture.State.ProgressIcon);
            Assert.Equal(new[] { 78, 23 }, fixture.Events.TakeLast(2));
            Assert.Single(fixture.Terminals);
            Assert.False(fixture.CompleteBackend(fixture.Npc.Handle));
        }
    }

    [Fact]
    public void Throwing_state_or_loot_observer_drains_accepted_frost_tail_once_without_refund()
    {
        foreach (bool throwAtAdoption in new[] { false, true })
        {
            var baseline = new BackendFixture(145);
            Assert.True(baseline.CompleteBackend(baseline.Npc.Handle));
            var fixture = new BackendFixture(145);
            bool thrown = false;
            void Fail()
            {
                if (thrown) return;
                thrown = true;
                Assert.False(fixture.Npcs.TryGet(fixture.Npc.Handle, out _));
                Assert.Equal(11, fixture.State.Size);
                throw new InvalidOperationException("accepted Frost observer witness");
            }
            if (throwAtAdoption) fixture.OnAdopted = Fail; else fixture.OnDrop = Fail;
            var exception = Assert.Throws<InvalidOperationException>(() => fixture.CompleteBackend(fixture.Npc.Handle));
            Assert.Equal("accepted Frost observer witness", exception.Message);
            Assert.True(thrown);
            Assert.Equal(baseline.Drops, fixture.Drops);
            Assert.True(fixture.Random.HasSameState(baseline.Random));
            Assert.Equal(new[] { 78, 23 }, fixture.Events.TakeLast(2));
            Assert.Single(fixture.Terminals);
            Assert.False(fixture.CompleteBackend(fixture.Npc.Handle));
        }
    }

    [Fact]
    public void Nested_frost_death_preserves_newer_credit_and_rng_and_replacement_generation_survives()
    {
        var sequential = new BackendFixture(143);
        var second = sequential.Spawn(144, 2);
        Assert.True(sequential.CompleteBackend(sequential.Npc.Handle));
        Assert.True(sequential.CompleteBackend(second.Handle));
        var nested = new BackendFixture(143);
        var nestedSecond = nested.Spawn(144, 2);
        NpcSnapshot replacement = default;
        bool entered = false;
        nested.OnDrop = () =>
        {
            if (entered) return;
            entered = true;
            Assert.True(nested.CompleteBackend(nestedSecond.Handle));
            replacement = nested.Spawn(145, nested.Npc.Handle.Slot);
        };
        Assert.True(nested.CompleteBackend(nested.Npc.Handle));
        Assert.True(entered);
        Assert.Equal(10, nested.State.Size);
        Assert.True(nested.Random.HasSameState(sequential.Random));
        Assert.Equal(sequential.Drops.OrderBy(item => item.Handle.Slot), nested.Drops.OrderBy(item => item.Handle.Slot));
        Assert.True(nested.Npcs.TryGet(replacement.Handle, out var survivor));
        Assert.Equal(220, survivor.Simulation.Life);
        Assert.NotEqual(nested.Npc.Handle.Generation, survivor.Handle.Generation);
        Assert.Single(nested.Events, message => message == 78);
        Assert.DoesNotContain(nested.Npc.Handle, nested.Terminals);
        Assert.Single(nested.Terminals);
    }

    [Fact]
    public void Missing_or_possible_spirit_producer_refuses_the_whole_backend_death_before_hp_loot_or_rng()
    {
        foreach (bool? dungeon in new bool?[] { null, true })
        {
            var fixture = new BackendFixture(144, hardmode: true, plantera: dungeon.HasValue ? true : null, dungeon: dungeon);
            var before = fixture.Random.Clone();
            Assert.False(fixture.CompleteBackend(fixture.Npc.Handle));
            Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var current));
            Assert.Equal(fixture.Npc, current);
            Assert.True(fixture.Random.HasSameState(before));
            Assert.Equal(12, fixture.State.Size);
            Assert.Empty(fixture.Drops); Assert.Empty(fixture.Events);
        }
    }

    [Fact]
    public void Source_roster_creation_defaults_do_not_admit_public_combat_for_unrepresented_ai38()
    {
        foreach (int type in new[] { 143, 144, 145 })
        {
            var fixture = new BackendFixture(type);
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), out var definition));
            Assert.False(definition.DefinitionOnly);
            Assert.True(definition.CreationDefaultsOnly);
            Assert.False(definition.SupportsRuntimeLifecycle);
            var before = fixture.Random.Clone();
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
                fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100000));
            Assert.True(fixture.Random.HasSameState(before));
            Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var current));
            Assert.Equal(fixture.Npc, current);
            Assert.Empty(fixture.Drops); Assert.Empty(fixture.Events);
        }
        Assert.False(RuntimeInvasionSpawn1458.IsFrostCohortReady());
    }

    private static object? Invoke(object target, string method, params object?[] args)
    {
        try { return target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args); }
        catch (TargetInvocationException exception) when (exception.InnerException is { } cause)
        { ExceptionDispatchInfo.Capture(cause).Throw(); throw; }
    }

    private sealed class CorpusRolls(VanillaUnifiedRandom1458 random) : INpcLootRollSource
    {
        public int RollLuck(int range) => random.Next(0, range);
        public int NextInt32(int minimum, int maximum) => random.Next(minimum, maximum);
    }

    private sealed class BackendFixture : IRuntimePlayerSlotSnapshotLookup, INpcStateCommitSink, IWorldItemStateCommitSink
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly RuntimeWorldItemStore Items;
        internal readonly RuntimeNpcNetworkCombatPipeline Pipeline;
        internal readonly RuntimeWorldInvasion1458 Owner = new(new(2, 12, 120, 0, 50, 0,
            0, 120, 5, 0, false, false, false, false));
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly NpcSnapshot Npc;
        internal readonly List<int> Events = [];
        internal readonly List<WorldItemSnapshot> Drops = [];
        internal readonly List<NpcHandle> Terminals = [];
        internal Action? OnDrop, OnAdopted;
        private readonly PlayerStateSnapshot player;
        internal InvasionState1458 State { get { Assert.True(Owner.TryCapture(out var state)); return state.State; } }

        internal BackendFixture(int type, int seed = 1, bool hardmode = false, bool? plantera = null, bool? dungeon = false)
        {
            Random = new(seed); Npcs = new(10, this); Items = new(this);
            Npc = Spawn(type, 1);
            player = default(PlayerStateSnapshot) with
            {
                Player = new(new(0), new(1)), Revision = new(1),
                Zones = dungeon is { } known ? new((byte)(known ? 1 : 0), 0, 0, 0, 0, 0) : null,
                HasHealth = true, Life = 400, MaxLife = 400, DerivedLifeMax = 400,
                HasMana = true, Mana = 200, MaxMana = 200, NpcLifeCurrent = true
            };
            Pipeline = new(Npcs, Items, this, new PlayerAuthority(null, null), () => 0,
                null, new(Items), null, new(0, true, 0, 0, 0), new(), false, false,
                lootRandom: Random, lootRemixWorld: false, planteraDownedBaseline: plantera,
                globalLootWorld: new(100, 80, 1, hardmode, false, false, false, 35, 40, false, false),
                invasion: Owner, invasionProgressPublisher: _ => Events.Add(78),
                worldStateAdopted: () => OnAdopted?.Invoke());
            Events.Clear();
        }

        internal NpcSnapshot Spawn(int type, byte slot)
        {
            // Explicit source body for backend proof only. Creation defaults do not
            // remove the public combat/lifecycle barrier for unrepresented AI38.
            var update = new NpcStateUpdate(type, (short)type, 800, 440, 0, 0, 0, default,
                NpcSimulationState.Initial with
                {
                    Life = type == 143 ? 200 : type == 144 ? 240 : 220,
                    LifeMax = type == 143 ? 200 : type == 144 ? 240 : 220,
                    MoneyValue = 400f, ExtraMoneyValue = 0, Midas = false,
                    Friendly = false, Chaseable = true, Immortal = false, TownNpc = false,
                    BaseDamage = type == 143 ? 50 : type == 144 ? 65 : 55,
                    BaseDefense = type == 143 ? 20 : type == 144 ? 26 : 22,
                    BaseLifeMax = type == 143 ? 200 : type == 144 ? 240 : 220,
                    SpawnDifficulty = 1f, KnockBackResist = .6f, Rotation = 0f
                });
            Assert.True(Npcs.TrySpawn(slot, in update, out var npc));
            return npc;
        }

        internal bool CompleteBackend(NpcHandle handle)
        {
            if (!Npcs.TryGet(handle, out var before)) return false;
            var projected = before with { Simulation = before.Simulation with { Life = 0 } };
            if (!(bool)Invoke(Pipeline, "TryAdmitLethalDeath", projected)!) return false;
            var hp = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
                before.VelocityX, before.VelocityY, before.Target, before.Ai, projected.Simulation);
            Assert.True(Npcs.TryUpdateUnpublished(handle, in hp, out var accepted));
            Invoke(Pipeline, "AdoptOrdinaryInvasionOwners", accepted);
            var wire = new TerrariaNpcDamageState(handle.Slot, RuntimeNpcPacketProjection.ToProtocolGeneration(handle.Generation),
                short.MaxValue, 0, 1, 0);
            Invoke(Pipeline, "PublishOrdinaryInvasionDeath", accepted, wire, null);
            return true;
        }

        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot value)
        { value = player; return slot.Value == 0; }
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc)
        { Events.Add(23); if (kind == NpcStateCommitKind.Despawn) Terminals.Add(npc.Handle); }
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot item, int? reservationOffer = null, GameCommandSourceId excludedSource = default)
        { if (kind == WorldItemStateCommitKind.Drop) { Events.Add(21); Drops.Add(item); OnDrop?.Invoke(); } }
    }
}
