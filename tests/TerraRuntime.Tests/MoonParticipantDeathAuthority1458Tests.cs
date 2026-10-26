using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed partial class MoonBossDeathAuthority1458Tests
{
    private static readonly int[] OrdinaryMoonTypes = Enumerable.Range(305, 11).Concat(new[] { 326, 329, 330 })
        .Concat(Enumerable.Range(338, 6)).Concat(Enumerable.Range(347, 6)).ToArray();

    [Fact]
    public void All_31_participant_complete_death_phases_match_5952_independent_original_1458_rows()
        => VerifyCompleteMoonDeathFixture("MoonParticipantFullDeath1458", 5952);

    [Fact]
    public void All_31_participant_dedicated_hit_effects_match_248_independent_original_1458_rows()
        => VerifyMoonHitFixture("MoonParticipantHit1458", 248);

    [Fact]
    public void Remaining_source_participants_use_retained_terminal_loot_and_credit_in_all_owned_strike_lanes()
    {
        foreach (int type in OrdinaryMoonTypes)
        foreach (int mode in new[] { 0, 1, 2 })
        foreach (string lane in new[] { "environment", "melee", "projectile" })
        {
            // Ordinary participants have no player-centered Master reward. Their
            // captured player census does not need the separate five-boss body owner.
            var f = new Fixture(type, mode, ownPlayer: false);
            var target = f.Spawn(type);
            bool observed = false;
            float expected = OrdinaryPoints(type) * (mode == 2 ? 2.5f : mode == 1 ? 2 : 1);
            f.OnBootstrap = () =>
            {
                observed = true;
                Assert.False(f.Npcs.TryGet(target.Handle, out _));
                Assert.Equal(expected, f.Clock.MoonEventTotalInvasionPoints);
                Assert.False(f.Progression.CaptureSnapshot().HasAny);
                Assert.True(f.Invasion.TryCapture(out var invasion));
                Assert.False(invasion.State.LanternNextNight);
            };
            Assert.True(f.Kill(in target, lane), $"NPC{type}, mode{mode}, {lane}");
            Assert.True(observed);
            Assert.Single(f.Terminals, terminal => terminal == target.Handle);
            var after = f.Random.Clone();
            int count = f.Drops.Count;
            Assert.False(f.Kill(in target, lane));
            Assert.Equal(count, f.Drops.Count);
            Assert.True(f.Random.HasSameState(after));
            Assert.Equal(expected, f.Clock.MoonEventTotalInvasionPoints);
            Assert.Equal(23, Drain(f.First).Last()[2]);
            Assert.DoesNotContain(Drain(f.Join(2)), frame => frame[2] == 23 && frame[3] == target.Handle.Slot);
        }
    }

    [Fact]
    public void Remaining_participants_allow_nested_different_death_without_rewinding_any_accepted_phase()
    {
        foreach (int type in OrdinaryMoonTypes)
        {
            int secondType = type < 338 ? 305 : 338;
            var sequential = new Fixture(type, 2, seed: 31);
            var first = sequential.Spawn(type);
            var second = sequential.Spawn(secondType);
            Assert.True(sequential.Kill(in first, "environment"));
            Assert.True(sequential.Kill(in second, "environment"));

            var nested = new Fixture(type, 2, seed: 31);
            var outer = nested.Spawn(type);
            var inner = nested.Spawn(secondType);
            bool entered = false;
            nested.OnBootstrap = () =>
            {
                if (entered) return;
                entered = true;
                Assert.False(nested.Npcs.TryGet(outer.Handle, out _));
                Assert.True(nested.Kill(in inner, "environment"));
            };
            Assert.True(nested.Kill(in outer, "environment"));
            Assert.True(entered);
            Assert.True(nested.Random.HasSameState(sequential.Random));
            Assert.Equal(sequential.Drops.Select(DropKey).Order(), nested.Drops.Select(DropKey).Order());
            Assert.Equal(sequential.Clock.MoonEventTotalInvasionPoints, nested.Clock.MoonEventTotalInvasionPoints);
            Assert.Equal(sequential.Clock.MoonEventWaveKills, nested.Clock.MoonEventWaveKills);
            Assert.Equal(sequential.Clock.MoonEventWaveNumber, nested.Clock.MoonEventWaveNumber);
            Assert.Equal(2, nested.Terminals.Count);
            Assert.Equal(nested.Clock.MoonEventProgressReportRequired ? 1 : 0,
                Drain(nested.First).Count(frame => frame[2] == 78));
        }
    }

    [Fact]
    public void Elf_copter_requires_known_christmas_and_uses_owned_false_over_saved_true()
    {
        foreach (bool active in new[] { false, true })
        foreach (int mode in new[] { 0, 1, 2 })
        {
            var unknown = new Fixture(341, mode, active: active, representGlobalWorld: false);
            var unknownTarget = unknown.Spawn(341);
            var before = unknown.Random.Clone();
            Assert.False(unknown.Kill(in unknownTarget, "environment"));
            Assert.True(unknown.Npcs.TryGet(unknownTarget.Handle, out var unchanged));
            Assert.Equal(unknownTarget, unchanged);
            Assert.True(unknown.Random.HasSameState(before));
            Assert.Empty(unknown.Drops);
            Assert.Equal(0f, unknown.Clock.MoonEventTotalInvasionPoints);

            foreach (bool christmas in new[] { false, true })
            {
                // No global table is represented in this partial fixture. The clock's
                // exact calendar owner alone proves the NPC341-specific Present rule.
                var known = new Fixture(341, mode, active: active, representGlobalWorld: false,
                    ownedChristmas: christmas);
                var target = known.Spawn(341);
                Assert.True(known.Kill(in target, "environment"));
                Assert.Equal(christmas ? 1 : 0, known.Drops.Count(drop => drop.ItemNetId == 1869));
            }
            // Value=0 excludes the separate global holiday rule, while NPC341's
            // particular Present rule still reads Christmas. This isolates a real
            // represented loaded true from the clock's later owned false.
            var expired = new Fixture(341, mode, active: active,
                christmas: true, ownedChristmas: false);
            var expiredTarget = expired.Spawn(341);
            var zeroValue = new NpcStateUpdate(expiredTarget.Type, expiredTarget.NetId, expiredTarget.PositionX,
                expiredTarget.PositionY, expiredTarget.VelocityX, expiredTarget.VelocityY, expiredTarget.Target,
                expiredTarget.Ai, expiredTarget.Simulation with { MoneyValue = 0 });
            Assert.True(expired.Npcs.TryUpdate(expiredTarget.Handle, in zeroValue, out expiredTarget));
            Assert.True(expired.Kill(in expiredTarget, "environment"));
            Assert.DoesNotContain(expired.Drops, drop => drop.ItemNetId == 1869);
        }
    }

    [Fact]
    public void Missing_required_participant_reward_materialization_refuses_before_hp_rng_items_or_credit()
    {
        foreach (var (type, item) in new[] { (305, 1788), (314, 1790), (315, 1857), (326, 1729),
            (338, 1943), (339, 1944), (340, 1945), (341, 1869) })
        {
            var f = new Fixture(type, 0, ownedChristmas: type == 341 ? true : null);
            var target = f.Spawn(type);
            var delivery = typeof(RuntimeNpcNetworkCombatPipeline).GetField("lootDelivery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Pipeline)!;
            typeof(RuntimeNpcLootDelivery1458).GetField("ownedMaterializer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(delivery, new DeniedMoonItemMaterializer(item));
            var before = f.Random.Clone();
            Assert.False(f.Kill(in target, "environment"), $"NPC{type} must not omit required item{item}.");
            Assert.True(f.Npcs.TryGet(target.Handle, out var unchanged));
            Assert.Equal(target, unchanged);
            Assert.Empty(f.Drops);
            Assert.True(f.Random.HasSameState(before));
            Assert.Equal(0f, f.Clock.MoonEventTotalInvasionPoints);
            Assert.False(f.Progression.CaptureSnapshot().HasAny);
            Assert.Equal(0, f.BootstrapAdoptions);
        }
    }

    [Fact]
    public void Accepted_death_suppresses_the_shared_spawn_cycle_once_before_observers_without_reapplying_during_drain()
    {
        foreach (int type in OrdinaryMoonTypes.Concat(BossTypes))
        {
            var f = new Fixture(type, 0);
            var target = f.Spawn(type);
            Assert.True(f.SpawnCycle.TryBeginCycle());
            bool observed = false;
            f.OnBootstrap = () =>
            {
                observed = true;
                Assert.False(f.Npcs.TryGet(target.Handle, out _));
                Assert.False(f.SpawnCycle.TryBeginCycle());
                Assert.True(f.SpawnCycle.TryBeginCycle());
            };
            Assert.True(f.Kill(in target, "environment"));
            Assert.True(observed);
            Assert.True(f.SpawnCycle.TryBeginCycle());
            Assert.False(f.Kill(in target, "environment"));
            Assert.True(f.SpawnCycle.TryBeginCycle());
        }
    }

    [Fact]
    public void Two_nested_accepted_deaths_share_one_boolean_spawn_suppression_and_refusals_do_not_change_it()
    {
        var f = new Fixture(338, 0);
        var first = f.Spawn(338);
        var second = f.Spawn(340);
        bool entered = false;
        f.OnBootstrap = () =>
        {
            if (entered) return;
            entered = true;
            Assert.True(f.Kill(in second, "environment"));
        };
        Assert.True(f.Kill(in first, "environment"));
        Assert.True(entered);
        Assert.False(f.Kill(in first, "environment"));
        Assert.False(f.Kill(in second, "environment"));
        Assert.False(f.SpawnCycle.TryBeginCycle());
        Assert.True(f.SpawnCycle.TryBeginCycle());

        var rejected = new Fixture(341, 0, representGlobalWorld: false);
        var unknownSeason = rejected.Spawn(341);
        Assert.False(rejected.Kill(in unknownSeason, "environment"));
        Assert.True(rejected.SpawnCycle.TryBeginCycle());
        Assert.True(rejected.Npcs.TryGet(unknownSeason.Handle, out var unchanged));
        Assert.Equal(unknownSeason, unchanged);
    }

    [Fact]
    public void Nonlethal_owned_hit_preserves_spawn_cycle_but_real_checkDead_animation_intercept_suppresses_it()
    {
        var f = new Fixture(338, 0);
        var elf = f.Spawn(338);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Committed,
            f.Pipeline.TryStrikeEnvironment(elf.Handle, 1));
        Assert.True(f.SpawnCycle.TryBeginCycle());
        Assert.True(f.Npcs.TryGet(elf.Handle, out var wounded));
        Assert.True(wounded.Simulation.Life > 0);
        Assert.Equal(0f, f.Clock.MoonEventTotalInvasionPoints);

        var bubble = f.Spawn(VanillaNpcIds.DetonatingBubble.Value);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Committed,
            f.Pipeline.TryStrikeEnvironment(bubble.Handle, 100000));
        Assert.True(f.Npcs.TryGet(bubble.Handle, out var intercepted));
        Assert.True(intercepted.Simulation.Life > 0);
        Assert.True(intercepted.Simulation.DontTakeDamage);
        Assert.False(f.SpawnCycle.TryBeginCycle());
        Assert.True(f.SpawnCycle.TryBeginCycle());
        Assert.Empty(f.Drops);
        Assert.False(f.Progression.CaptureSnapshot().HasAny);
    }

    private static float OrdinaryPoints(int type) => type switch
    {
        315 => 50, 326 or 342 => 2, 329 or 352 => 5, 330 or 351 => 10,
        341 => 20, 343 => 18, 347 => 8, 348 or 349 => 4, 350 => 3, _ => 1
    };

    private sealed class DeniedMoonItemMaterializer(int denied) : INpcLootWorldItemMaterializer
    {
        public bool CanMaterialize(ItemTypeId item) => item.Value != denied && VanillaNpcLootWorldItemMaterializer.Instance.CanMaterialize(item);
        public bool TryMaterialize(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random,
            out WorldItemDropStateUpdate item) => VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(in origin, in drop, random, out item);
    }
}
