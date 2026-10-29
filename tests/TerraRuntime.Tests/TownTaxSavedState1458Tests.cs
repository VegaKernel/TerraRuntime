using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class TownTaxSavedState1458Tests
{
    [Fact]
    public void Tax_creation_does_not_rescue_but_accepted_AI_marks_before_its_first_observer()
    {
        var fixture = new Fixture();
        Assert.False(fixture.Saved);
        fixture.Observer.Observed.Clear();
        var summary = fixture.Tick();
        Assert.Equal(0, summary.RejectedCommits);
        Assert.True(fixture.Saved);
        Assert.NotEmpty(fixture.Observer.Observed);
        Assert.All(fixture.Observer.Observed, saved => Assert.True(saved));
    }

    [Fact]
    public void Refused_resident_phase_does_not_mark_the_rescue_fact()
    {
        var fixture = new Fixture();
        Assert.True(fixture.Npcs.TryGetActive(0, out var before));
        var imported = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
            before.VelocityX, before.VelocityY, before.Target, before.Ai, before.Simulation with {
                BreathCounter = 0, WetCount = 0, LavaWet = false, HoneyWet = false, ShimmerWet = false,
                ShimmerTransparency = .375f, TeleportTime = 0f, TeleportStyle = 0 });
        Assert.True(fixture.Npcs.TryUpdate(before.Handle, in imported, out before));
        fixture.Observer.Observed.Clear();
        var random = fixture.Random.SourceRandom.Clone();
        Assert.Equal(1, fixture.Tick().RejectedCommits);
        Assert.False(fixture.Saved);
        Assert.Empty(fixture.Observer.Observed);
        Assert.True(fixture.Random.SourceRandom.HasSameState(random));
        Assert.True(fixture.Npcs.TryGet(before.Handle, out var after));
        Assert.Equal(before, after);
    }

    private sealed class Observer(RuntimeWorldProgressionMutations progression) : INpcStateCommitSink
    {
        internal readonly List<bool> Observed = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) =>
            Observed.Add((progression.CaptureSnapshot().RescuedTownNpcs & RuntimeTownRescueFacts1458.TaxCollector) != 0);
    }

    private sealed class Fixture
    {
        private readonly RuntimeWorldProgressionMutations progression = new();
        private readonly RuntimeTownNpcSchedule1458 schedule;
        private readonly RuntimeTownNpcCombat1458 combat;
        private readonly RuntimeNpcBuffStatus1458 status;
        internal readonly Observer Observer;
        internal readonly RuntimeNpcStore Npcs;
        internal readonly SystemVanillaNpcRandom Random = new(1458);
        internal bool Saved => (progression.CaptureSnapshot().RescuedTownNpcs & RuntimeTownRescueFacts1458.TaxCollector) != 0;

        internal Fixture()
        {
            var tiles = new WorldTileStore(new WorldDimensions(128, 160));
            for (int x = 1; x < 127; x++)
                tiles.Set(x, 100, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            var resident = new WorldTownNpc(VanillaNpcIds.TaxCollector.Value, "Oracle", 1023f, 1560f,
                true, 64, 100, 0, false);
            var town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [resident], []), [], tiles.Dimensions);
            Observer = new Observer(progression);
            Npcs = new RuntimeNpcStore(commitSink: Observer);
            Assert.True(town.TryReserveRuntimeSlots(Npcs));
            town.BindRuntimeNames(Npcs);
            schedule = new RuntimeTownNpcSchedule1458(town, Npcs, tiles, new NpcRuntimeTownScheduleRandom1458(Random));
            combat = new RuntimeTownNpcCombat1458(town, Npcs, new RuntimeProjectileStore(), tiles, default,
                progression, false, false, new NpcRuntimeTownCombatRandom1458(Random));
            status = new RuntimeNpcBuffStatus1458(Npcs);
        }

        internal RuntimeTownNpcCombatTickSummary1458 Tick()
        {
            var conditions = new RuntimeTownNpcScheduleConditions1458(true, false, false, false, false);
            status.BeginWorldTick();
            return schedule.Tick(in conditions, [], status, combat);
        }
    }
}
