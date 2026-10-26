using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class InvasionDeathCredit1458Tests
{
    [Fact]
    public void Ordinary_invasion_deaths_adopt_terminal_credit_and_rng_before_loot_in_each_source_family()
    {
        foreach (int type in new[] { 26, 27, 28, 29, 111, 212, 213, 214, 215, 252 })
        {
            var fixture = new PipelineFixture(type, VanillaInvasionLifecycle1458.GetGroup(type), 1, true);
            int callbacks = 0;
            fixture.OnDrop = () =>
            {
                callbacks++;
                Assert.False(fixture.Npcs.TryGet(fixture.Npc.Handle, out _));
                Assert.True(fixture.Owner.TryCapture(out var accepted));
                Assert.Equal(11, accepted.State.Size);
                var acceptedRandom = fixture.Random.Clone();
                Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
                    fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100000));
                Assert.True(fixture.Random.HasSameState(acceptedRandom));
            };
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100000));
            Assert.True(callbacks > 0);
            Assert.True(fixture.Owner.TryCapture(out var state));
            Assert.Equal(11, state.State.Size);
            Assert.Single(fixture.Terminals);
            Assert.Equal(new[] { 78, 23 }, fixture.Events.TakeLast(2));
        }
    }

    [Fact]
    public void Nested_different_invasion_death_uses_local_journal_and_never_rewinds_newer_credit_or_rng()
    {
        foreach (int type in new[] { 27, 212 })
        {
            int group = VanillaInvasionLifecycle1458.GetGroup(type);
            var sequential = new PipelineFixture(type, group, 1, true);
            var sequentialSecond = SpawnSecond(sequential, type);
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                sequential.Pipeline.TryStrikeEnvironment(sequential.Npc.Handle, 100000));
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                sequential.Pipeline.TryStrikeEnvironment(sequentialSecond.Handle, 100000));

            var nested = new PipelineFixture(type, group, 1, true);
            var second = SpawnSecond(nested, type);
            bool entered = false;
            VanillaUnifiedRandom1458? nestedAcceptedRandom = null;
            nested.OnDrop = () =>
            {
                if (entered) return;
                entered = true;
                Assert.False(nested.Npcs.TryGet(nested.Npc.Handle, out _));
                Assert.True(nested.Owner.TryCapture(out var firstAccepted));
                Assert.Equal(11, firstAccepted.State.Size);
                Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                    nested.Pipeline.TryStrikeEnvironment(second.Handle, 100000));
                Assert.True(nested.Owner.TryCapture(out var secondAccepted));
                Assert.Equal(10, secondAccepted.State.Size);
                nestedAcceptedRandom = nested.Random.Clone();
            };
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                nested.Pipeline.TryStrikeEnvironment(nested.Npc.Handle, 100000));
            Assert.True(entered);
            Assert.NotNull(nestedAcceptedRandom);
            Assert.True(nested.Random.HasSameState(nestedAcceptedRandom));
            Assert.True(nested.Random.HasSameState(sequential.Random));
            Assert.Equal(sequential.Drops.OrderBy(item => item.Handle.Slot),
                nested.Drops.OrderBy(item => item.Handle.Slot));
            Assert.Equal(2, nested.Terminals.Count);
            Assert.Equal(2, nested.Terminals.Distinct().Count());
            Assert.False(nested.Npcs.TryGet(second.Handle, out _));
            // The nested accepted snapshot is the current public progress; the stale
            // outer snapshot must not be published after returning from the observer.
            Assert.Single(nested.Events, message => message == 78);
            Assert.True(nested.Owner.TryCapture(out var final));
            Assert.Equal(10, final.State.Size);
        }
    }

    [Fact]
    public void Throwing_loot_observer_drains_accepted_invasion_rewards_progress_and_terminal_once()
    {
        var baseline = new PipelineFixture(212, 3, 1, true);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
            baseline.Pipeline.TryStrikeEnvironment(baseline.Npc.Handle, 100000));
        var fixture = new PipelineFixture(212, 3, 1, true);
        bool threw = false;
        fixture.OnDrop = () =>
        {
            if (threw) return;
            threw = true;
            Assert.False(fixture.Npcs.TryGet(fixture.Npc.Handle, out _));
            Assert.True(fixture.Owner.TryCapture(out var accepted));
            Assert.Equal(11, accepted.State.Size);
            throw new InvalidOperationException("accepted invasion callback witness");
        };
        var failure = Assert.Throws<InvalidOperationException>(() =>
            fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100000));
        Assert.Equal("accepted invasion callback witness", failure.Message);
        Assert.True(threw);
        Assert.Equal(baseline.Drops, fixture.Drops);
        Assert.True(fixture.Random.HasSameState(baseline.Random));
        Assert.Equal(new[] { 78, 23 }, fixture.Events.TakeLast(2));
        Assert.Single(fixture.Terminals);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100000));
        Assert.Equal(baseline.Drops, fixture.Drops);
    }

    [Fact]
    public void Invasion_dependency_change_during_death_preparation_refuses_without_hp_or_loot_adoption()
    {
        var fixture = new PipelineFixture(27, 1, 1, true);
        var beforeRandom = fixture.Random.Clone();
        bool changed = false;
        fixture.OnPlayerRead = () =>
        {
            if (changed) return;
            changed = true;
            Assert.True(fixture.Owner.TryCapture(out var before));
            var newer = new InvasionTransition1458(before.State with { Size = 8 }, default);
            Assert.True(fixture.Owner.TryAdopt(in before, in newer, out _));
        };
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100000));
        Assert.True(changed);
        Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var current));
        Assert.Equal(fixture.Npc, current);
        Assert.True(fixture.Random.HasSameState(beforeRandom));
        Assert.Empty(fixture.Drops);
        Assert.Empty(fixture.Events);
        Assert.True(fixture.Owner.TryCapture(out var accepted));
        Assert.Equal(8, accepted.State.Size);
    }

    [Fact]
    public void Possible_or_unknown_dungeon_spirit_producer_refuses_before_invasion_death_adoption()
    {
        foreach (bool unknown in new[] { false, true })
        {
            var fixture = new PipelineFixture(212, 3, 1, true, hardmode: true,
                planteraDowned: unknown ? null : true, dungeon: unknown ? null : true);
            var beforeRandom = fixture.Random.Clone();
            Assert.True(fixture.Owner.TryCapture(out var beforeInvasion));
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
                fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100000));
            Assert.True(fixture.Npcs.TryGet(fixture.Npc.Handle, out var current));
            Assert.Equal(fixture.Npc, current);
            Assert.True(fixture.Random.HasSameState(beforeRandom));
            Assert.True(fixture.Owner.IsCurrent(in beforeInvasion));
            Assert.Empty(fixture.Drops);
            Assert.Empty(fixture.Events);
        }
    }

    [Fact]
    public void Dungeon_spirit_exclusion_uses_early_false_facts_and_current_progression()
    {
        foreach (bool nonDungeon in new[] { false, true })
        {
            var fixture = new PipelineFixture(212, 3, 1, true, hardmode: true,
                planteraDowned: nonDungeon, dungeon: !nonDungeon);
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
                fixture.Pipeline.TryStrikeEnvironment(fixture.Npc.Handle, 100000));
            Assert.True(fixture.Owner.TryCapture(out var accepted));
            Assert.Equal(11, accepted.State.Size);
        }
        var progressed = new PipelineFixture(212, 3, 1, true, hardmode: false,
            planteraDowned: false, dungeon: true);
        Assert.True(progressed.Progression.MarkCompleted(VanillaWorldProgressionId.Hardmode));
        Assert.True(progressed.Progression.MarkCompleted(VanillaWorldProgressionId.Plantera));
        var beforeRandom = progressed.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            progressed.Pipeline.TryStrikeEnvironment(progressed.Npc.Handle, 100000));
        Assert.True(progressed.Npcs.TryGet(progressed.Npc.Handle, out var unchanged));
        Assert.Equal(progressed.Npc, unchanged);
        Assert.True(progressed.Random.HasSameState(beforeRandom));
        Assert.Empty(progressed.Drops);
    }

    private static NpcSnapshot SpawnSecond(PipelineFixture fixture, int type)
    {
        var update = new NpcStateUpdate(type, (short)type, 900, 440, 0, 0, 0,
            default, NpcSimulationState.Initial);
        Assert.True(fixture.Npcs.TrySpawnVanilla(in update, out var second, startSlot: 2));
        return second;
    }
}
