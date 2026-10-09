using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class SelectedItemCritNpcHit1458Tests
{
    [Fact]
    public void Actual_configured_source_phase_switch_and_private_hit_match_damage_four_offers_and_packet28()
    {
        using var source = Read();
        Assert.Equal(18, source.RootElement.GetArrayLength());
        foreach (var row in source.RootElement.EnumerateArray())
        {
            string mode = row.GetProperty("mode").GetString()!;
            using var f = new SelectedItemCritPhase1458Tests.Fixture(mode.StartsWith("bonus"), 1458);
            f.State.Tick();
            if (mode.Contains("switch"))
            {
                f.Move(1);
                if (mode.EndsWith("with-phase")) f.State.Tick();
            }
            using var arena = new Arena(f, row.GetProperty("hitSeed").GetInt32(), row.GetProperty("launch"));
            arena.Pass.Tick();
            Assert.Equal(1, arena.Pass.CommittedHits);
            Assert.True(arena.Npcs.TryGet(arena.Actor.Handle, out var actor));
            Assert.Equal(row.GetProperty("npcLife").GetInt32(), actor.Simulation.Life);
            Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(x => x.GetString()),
                arena.Drain().Where(x => x[2] == 28).Select(Convert.ToHexString));
            AssertRandom(row.GetProperty("hitRng"), arena.Random);
            Assert.Equal(row.GetProperty("keep").GetBoolean(), f.Projectiles.TryGet(arena.Shot.Handle, out _));
        }
    }

    [Fact]
    public void Unknown_or_tick_callback_changed_retained_crit_refuses_before_damage_and_random()
    {
        using var source = Read();
        var row = source.RootElement[0];
        foreach (bool unknown in new[] { false, true })
        {
            using var f = new SelectedItemCritPhase1458Tests.Fixture(false, 1458);
            f.State.Tick();
            if (unknown) f.Member.ItemPhase = f.Member.ItemPhase!.Value with { DerivedCombat = null };
            using var arena = new Arena(f, row.GetProperty("hitSeed").GetInt32(), row.GetProperty("launch"));
            if (!unknown) arena.BeforeTick = () =>
                f.Member.ItemPhase = f.Member.ItemPhase!.Value with
                {
                    DerivedCombat = f.Member.ItemPhase.Value.DerivedCombat!.Value with { MeleeCrit = 99, RangedCrit = 99, MagicCrit = 99 }
                };
            var random = arena.Random.Clone();
            arena.Pass.Tick();
            Assert.Equal(0, arena.Pass.CommittedHits);
            Assert.True(arena.Npcs.TryGet(arena.Actor.Handle, out var actor));
            Assert.Equal(arena.Actor, actor);
            Assert.True(f.Projectiles.TryGet(arena.Shot.Handle, out var shot));
            Assert.Equal(arena.Shot, shot);
            Assert.True(arena.Random.HasSameState(random));
            Assert.False(arena.Combat.Interactions.HasAnyInteraction(actor.Handle));
            Assert.Empty(arena.Drain());
        }
    }

    [Fact]
    public void Plain_bullet_preserves_existing_non_status_target_admission()
    {
        using var source = Read("SelectedPrefixCritDemonEyeBorn1458");
        using var blueSource = Read();
        Assert.Equal(4, source.RootElement.GetArrayLength());
        foreach (var row in source.RootElement.EnumerateArray())
        {
            string mode = row.GetProperty("mode").GetString()!;
            int seed = row.GetProperty("hitSeed").GetInt32();
            var shotSource = blueSource.RootElement.EnumerateArray().Single(x =>
                x.GetProperty("mode").GetString() == mode && x.GetProperty("hitSeed").GetInt32() == seed);
            using var f = new SelectedItemCritPhase1458Tests.Fixture(mode == "bonus", 1458);
            f.State.Tick();
            // NewProjectile precedes the target setup; its originalDamage comes from the
            // independently captured identical launch in the Blue1 composition.
            using var arena = new Arena(f, seed, shotSource.GetProperty("launch"), npcType: 6);
            arena.Pass.Tick();
            Assert.Equal(1, arena.Pass.CommittedHits);
            Assert.True(arena.Npcs.TryGet(arena.Actor.Handle, out var actor));
            Assert.Equal(row.GetProperty("npcLife").GetInt32(), actor.Simulation.Life);
            Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(x => x.GetString()),
                arena.Drain().Where(x => x[2] == 28).Select(Convert.ToHexString));
            AssertRandom(row.GetProperty("hitRng"), arena.Random);
        }
    }

    [Fact]
    public void Generic_target_lethal_planning_rechecks_crit_after_its_actual_loot_provider()
    {
        using var source = Read();
        var launch = source.RootElement[0].GetProperty("launch");
        foreach (bool change in new[] { false, true })
        {
            using var f = new SelectedItemCritPhase1458Tests.Fixture(false, 1458);
            f.State.Tick();
            using var arena = new Arena(f, 14, launch, npcType: 6, life: 1);
            int calls = 0;
            arena.DuringLoot = () =>
            {
                calls++;
                if (change) f.Member.ItemPhase = f.Member.ItemPhase!.Value with
                {
                    DerivedCombat = f.Member.ItemPhase.Value.DerivedCombat!.Value with { MeleeCrit = 99, RangedCrit = 99, MagicCrit = 99 }
                };
            };
            arena.Pass.Tick();
            Assert.True(calls > 0);
            Assert.Equal(change ? 0 : 1, arena.Pass.CommittedHits);
            if (change)
            {
                Assert.True(arena.Npcs.TryGet(arena.Actor.Handle, out var actor));
                Assert.Equal(arena.Actor, actor);
                Assert.False(arena.Combat.Interactions.HasAnyInteraction(actor.Handle));
                Assert.Empty(arena.Drain());
            }
            else Assert.False(arena.Npcs.TryGet(arena.Actor.Handle, out _));
        }
    }

    private static JsonDocument Read(string resource = "SelectedPrefixCritBornConfigured1458")
    {
        using var stream = typeof(SelectedItemCritNpcHit1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static void AssertRandom(JsonElement source, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(source.GetProperty("cursor").GetUInt32(),
            typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random));
        Assert.Equal(source.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
    }

    private sealed class Arena : IDisposable
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly NpcSnapshot Actor;
        internal readonly ProjectileSnapshot Shot;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeNpcNetworkCombatPipeline Combat;
        internal readonly RuntimeProjectileNpcCombatPass Pass;
        internal Action? BeforeTick;
        internal Action? DuringLoot;
        private readonly TerrariaConnectionOutboundQueue outbound = new(new OutboundQueueOptions(128, 131072, 1024));

        internal Arena(SelectedItemCritPhase1458Tests.Fixture f, int seed, JsonElement launch, int npcType = 1,
            int life = 1000)
        {
            Random = new(seed);
            var registry = new RuntimeNpcReplicationRegistry();
            Npcs = new(capacity: 8, commitSink: registry);
            var actor = new NpcStateUpdate(npcType, (short)npcType, 1800, 1606, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = life, LifeMax = 1000, MoneyValue = 0,
                    ExtraMoneyValue = 0, Immortal = false, ShimmerTransparency = 0 });
            Assert.True(Npcs.TrySpawn(0, actor, out Actor));
            var items = new RuntimeWorldItemStore();
            var lookup = new RuntimePlayerSnapshotLookup(f.Players, null);
            Combat = new(Npcs, items, lookup, f.Players, () => 100, registry, new(items), null,
                new(1000, false, default, 0, 0), new(), false, false, lootRandom: Random,
                seasonalItemContext: () => default,
                npcSpecificLowTiles: () => { DuringLoot?.Invoke(); return false; });
            var status = new RuntimeNpcBuffStatus1458(Npcs, registry.PublishNpcBuffs);
            registry.BindNpcBuffStatus(status);
            var velocity = launch.GetProperty("velocity");
            var shot = new ProjectileStateUpdate(new(launch.GetProperty("type").GetInt32()), 0,
                1800, 1606, velocity.GetProperty("X").GetSingle(), velocity.GetProperty("Y").GetSingle(),
                default, 0, checked((short)launch.GetProperty("damage").GetInt32()), launch.GetProperty("knockBack").GetSingle(),
                checked((short)launch.GetProperty("originalDamage").GetInt32()));
            Assert.True(f.Projectiles.TrySpawn(0, shot, out Shot));
            Assert.True(f.Projectiles.TryMarkCombatTrusted(Shot.Handle, f.Connection.Player));
            Pass = new(f.Projectiles, Npcs, Combat, f.Players,
                () => { BeforeTick?.Invoke(); return 100; }, status: status);
            Assert.True(registry.TryRegister(f.Connection.Source, outbound));
            registry.PlayerSpawned(f.Connection, new(f.Connection.Player.Slot, 100, 103, 0, 0, 0, 0, 0));
            _ = Drain();
        }

        internal byte[][] Drain()
        {
            var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetField("_queue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(outbound)!;
            var frames = new List<byte[]>();
            while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
            return frames.ToArray();
        }

        public void Dispose() { }
    }
}
