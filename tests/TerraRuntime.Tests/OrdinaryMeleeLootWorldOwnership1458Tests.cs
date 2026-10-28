using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class OrdinaryMeleeLootWorldOwnership1458Tests
{
    [Theory]
    [InlineData(181)]
    [InlineData(464)]
    [InlineData(489)]
    public void Missing_required_world_fact_rejects_actual_lethal_transaction_without_side_effects(int type)
    {
        using var f = new Fixture(type);
        var pipeline = f.Pipeline(null, () => null);
        AssertRejected(f, pipeline);
    }

    [Theory]
    [InlineData(181, false)]
    [InlineData(181, true)]
    [InlineData(464, false)]
    [InlineData(464, true)]
    [InlineData(489, false)]
    [InlineData(489, true)]
    public void Known_false_and_true_conditions_both_allow_the_complete_lethal_owner(int type, bool condition)
    {
        using var f = new Fixture(type);
        var clock = Clock(bloodMoon: condition, wind: condition ? .4f : 0f);
        // Unrelated missing DontStarve provenance must not fence the wind or
        // Blood Moon tables; only Face Monster consumes that conditional fact.
        var pipeline = f.Pipeline(clock, () => type == 181 ? condition : null);
        AssertKilled(f, pipeline);
    }

    [Theory]
    [InlineData(181, false)]
    [InlineData(181, true)]
    [InlineData(464, false)]
    [InlineData(464, true)]
    [InlineData(489, false)]
    [InlineData(489, true)]
    public void World_fact_changed_after_detached_preview_cannot_commit_hp_items_or_shared_entropy(int type, bool change)
    {
        using var f = new Fixture(type);
        var clock = Clock(bloodMoon: true, wind: .4f);
        int lowTileReads = 0;
        bool dontStarve = true;
        bool? CaptureLowTiles()
        {
            lowTileReads++;
            // Mutate through a DIFFERENT retained provider so omitting the
            // world comparison cannot also suppress the actual world change.
            // LowTiles itself remains false before and after this callback.
            if (change && lowTileReads == 2)
            {
                if (type == 181) dontStarve = false;
                else if (type == 464) clock.SetWindSpeedTarget(.39f);
                else clock.SetBloodMoonActive(false);
            }
            return false;
        }
        var pipeline = f.Pipeline(clock, () => dontStarve, CaptureLowTiles);
        if (change) AssertRejected(f, pipeline);
        else AssertKilled(f, pipeline);
        Assert.True(lowTileReads >= 2, "the test must cross the retained provider boundary");
        if (change)
        {
            if (type == 181) Assert.False(dontStarve);
            else if (type == 464) Assert.Equal(.39f, clock.WindSpeedTarget);
            else Assert.False(clock.BloodMoonActive);
        }
    }

    [Fact]
    public void Kite_condition_reads_target_wind_in_both_directions_at_the_source_threshold()
    {
        using var calm = new Fixture(464);
        using var below = new Fixture(464);
        using var positive = new Fixture(464);
        using var negative = new Fixture(464);
        AssertKilled(calm, calm.Pipeline(Clock(wind: 0f), () => null));
        AssertKilled(below, below.Pipeline(Clock(wind: MathF.BitDecrement(.4f)), () => null));
        var positiveClock = Clock();
        positiveClock.SetWindSpeedTarget(.4f);
        var negativeClock = Clock();
        negativeClock.SetWindSpeedTarget(-.4f);
        // Current wind stays zero in these two clocks: the source predicate
        // reads abs(Main.windSpeedTarget), not the smoothed current speed.
        Assert.Equal(0f, positiveClock.WindSpeedCurrent);
        Assert.Equal(0f, negativeClock.WindSpeedCurrent);
        AssertKilled(positive, positive.Pipeline(positiveClock, () => null));
        AssertKilled(negative, negative.Pipeline(negativeClock, () => null));
        Assert.True(calm.Random.HasSameState(below.Random));
        Assert.Equal(calm.Drops(), below.Drops());
        Assert.True(positive.Random.HasSameState(negative.Random));
        Assert.Equal(positive.Drops(), negative.Drops());
        Assert.False(calm.Random.HasSameState(positive.Random), "windy table must execute its additional kite roll");
    }

    [Fact]
    public void Bloody_tear_offer_requires_a_blood_moon_at_night()
    {
        using var ordinaryNight = new Fixture(489);
        using var ordinaryDay = new Fixture(489);
        using var bloodNight = new Fixture(489);
        AssertKilled(ordinaryNight, ordinaryNight.Pipeline(Clock(), () => null));
        AssertKilled(ordinaryDay, ordinaryDay.Pipeline(Clock(day: true, bloodMoon: true), () => null));
        AssertKilled(bloodNight, bloodNight.Pipeline(Clock(bloodMoon: true), () => null));
        Assert.True(ordinaryNight.Random.HasSameState(ordinaryDay.Random));
        Assert.Equal(ordinaryNight.Drops(), ordinaryDay.Drops());
        Assert.False(ordinaryNight.Random.HasSameState(bloodNight.Random),
            "the night Blood Moon must execute its additional Bloody Tear roll");
    }

    private static RuntimeWorldClock Clock(bool day = false, bool bloodMoon = false, float wind = 0f) =>
        new(0, day, (VanillaMoonPhase)0, 0, 0, bloodMoonActive: bloodMoon, windSpeedCurrent: wind);

    private static void AssertRejected(Fixture f, RuntimeNpcNetworkCombatPipeline pipeline)
    {
        var before = f.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            pipeline.TryStrikeEnvironment(f.Npc.Handle, 100_000));
        Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var after));
        Assert.Equal(f.Npc, after);
        Assert.True(before.HasSameState(f.Random));
        Assert.Equal(0, f.Items.ActiveCount);
    }

    private static void AssertKilled(Fixture f, RuntimeNpcNetworkCombatPipeline pipeline)
    {
        var before = f.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
            pipeline.TryStrikeEnvironment(f.Npc.Handle, 100_000));
        Assert.False(f.Npcs.TryGet(f.Npc.Handle, out _));
        Assert.False(before.HasSameState(f.Random));
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly PlayerAuthority Players = new(null, null);
        internal readonly PlayerJoinSession Session;
        internal readonly NpcSnapshot Npc;

        internal Fixture(int type)
        {
            var pool = new PlayerSlotPool(1);
            Assert.True(pool.TryAcquireConnection(out var lease));
            Session = new(lease!);
            Session.ObserveWorldRequest();
            Session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9092), Session.Handle);
            Players.TryApply(new PlayerSpawnRuntimeCommand(connection, Session,
                new(Session.Slot, 40, 30, 0, 0, 0, 0, 0)));
            Assert.True(Npcs.TrySpawnVanilla(new(checked((short)type), checked((short)type),
                600, 400, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = 100, LifeMax = 100, MoneyValue = 0f }), out Npc));
        }

        internal RuntimeNpcNetworkCombatPipeline Pipeline(RuntimeWorldClock? clock, Func<bool?> worldSource,
            Func<bool?>? lowTiles = null)
        {
            var pipeline = new RuntimeNpcNetworkCombatPipeline(Npcs, Items,
                new RuntimePlayerSnapshotLookup(Players, null), Players, () => 0, null,
                new(Items), null, clock, new(), false, false, lootRandom: Random, npcSpecificLowTiles: lowTiles);
            pipeline.BindSpecificLootWorldSource(worldSource);
            return pipeline;
        }

        internal WorldItemSnapshot[] Drops()
        {
            var active = new WorldItemSnapshot[Items.Capacity];
            return active[..Items.CopyActive(active)];
        }

        public void Dispose() => Session.Dispose();
    }
}
