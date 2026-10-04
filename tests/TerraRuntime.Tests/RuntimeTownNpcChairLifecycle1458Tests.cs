using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcChairLifecycle1458Tests
{
    [Theory]
    [InlineData(15, true, 3f, true, false)]
    [InlineData(497, true, 3f, true, false)]
    [InlineData(15, false, 3f, true, false)]
    [InlineData(497, false, 3f, true, false)]
    [InlineData(15, true, 3f, false, false)]
    [InlineData(15, true, 1f, true, true)]
    [InlineData(497, true, 1f, false, true)]
    [InlineData(15, false, 0f, true, true)]
    [InlineData(0, false, 300f, true, true)]
    [InlineData(1, true, 300f, false, true)]
    public void Seated_timer_and_chair_removal_commit_once_with_ordered_standing_rng(
        int chair, bool active, float timer, bool daytime, bool expectedStanding)
    {
        var sink = new Sink();
        var fixture = new Fixture(sink, chair, active, timer);
        var random = new OrderedRandom(25, 37);
        var schedule = new RuntimeTownNpcSchedule1458(fixture.Town, fixture.Npcs, fixture.Tiles, random);
        sink.Commits.Clear();
        var conditions = new RuntimeTownNpcScheduleConditions1458(daytime, false, false, false, false);

        new TownNpcTestPhase1458(fixture.Town, fixture.Npcs, fixture.Tiles, schedule).Tick(in conditions, []);

        Assert.True(fixture.Npcs.TryGetActive(0, out NpcSnapshot after));
        Assert.Equal(.4f, after.VelocityX);
        Assert.Equal(0f, after.VelocityY);
        Assert.Equal(expectedStanding ? 0f : 5f, after.Ai.Ai0);
        Assert.Equal(expectedStanding ? 85f : timer - 1f, after.Ai.Ai1);
        Assert.Equal(expectedStanding ? 0f : 17f, after.Ai.Ai2);
        Assert.Equal(23f, after.Ai.Ai3);
        Assert.Equal(expectedStanding ? 67f : 9f, after.Simulation.LocalAi.Ai3);
        Assert.Equal(expectedStanding ? 2 : 0, random.Calls);
        Assert.Equal(expectedStanding ? NpcStateCommitKind.ForcedUpdate : NpcStateCommitKind.Update,
            Assert.Single(sink.Commits));
        Assert.Equal(fixture.Initial.PositionX + .4f, after.PositionX);
        Assert.Equal(fixture.Initial.PositionY, after.PositionY);
    }

    [Fact]
    public void Losing_housing_does_not_freeze_an_existing_chair_timer()
    {
        var sink = new Sink();
        var fixture = new Fixture(sink, 15, true, 3f);
        Assert.True(fixture.Town.TryKickOut(0, out _));
        var random = new OrderedRandom(25, 37);
        var schedule = new RuntimeTownNpcSchedule1458(fixture.Town, fixture.Npcs, fixture.Tiles, random);
        sink.Commits.Clear();
        var day = new RuntimeTownNpcScheduleConditions1458(true, false, false, false, false);
        new TownNpcTestPhase1458(fixture.Town, fixture.Npcs, fixture.Tiles, schedule).Tick(in day, []);
        Assert.True(fixture.Npcs.TryGetActive(0, out NpcSnapshot after));
        Assert.Equal(2f, after.Ai.Ai1);
        Assert.Equal(5f, after.Ai.Ai0);
        Assert.Equal(0, random.Calls);
        Assert.Equal(NpcStateCommitKind.Update, Assert.Single(sink.Commits));
    }

    [Fact]
    public void A_replaced_town_slot_cannot_use_an_old_residents_schedule()
    {
        var sink = new Sink();
        var fixture = new Fixture(sink, 0, false, 300f);
        Assert.True(fixture.Npcs.TryDespawn(fixture.Initial.Handle));
        var replacement = new NpcStateUpdate(18, 18, fixture.Initial.PositionX, fixture.Initial.PositionY,
            .5f, 0f, 255, fixture.Initial.Ai, fixture.Initial.Simulation);
        Assert.True(fixture.Npcs.TrySpawn(0, in replacement, out NpcSnapshot current));
        var random = new OrderedRandom(25, 37);
        var schedule = new RuntimeTownNpcSchedule1458(fixture.Town, fixture.Npcs, fixture.Tiles, random);
        sink.Commits.Clear();
        var night = new RuntimeTownNpcScheduleConditions1458(false, false, false, false, false);
        new TownNpcTestPhase1458(fixture.Town, fixture.Npcs, fixture.Tiles, schedule).Tick(in night, []);
        Assert.True(fixture.Npcs.TryGet(current.Handle, out NpcSnapshot after));
        Assert.Equal(current, after);
        Assert.Empty(sink.Commits);
        Assert.Equal(0, random.Calls);
    }

    private sealed class Fixture
    {
        public Fixture(Sink sink, int chair, bool active, float timer)
        {
            Tiles = new WorldTileStore(new WorldDimensions(100, 80));
            for (int x = 0; x < 100; x++) Tiles.Set(x, 30, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            Tiles.Set(40, 29, new WorldTile { Type = checked((ushort)chair),
                Flags = active ? WorldTileFlags.Active : default });
            Town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [
                new WorldTownNpc(17, "Merchant", 639f, 440f, false, 40, 30, null, false) ], []),
                [new WorldTownRoom(17, 40, 30)], Tiles.Dimensions);
            Npcs = new RuntimeNpcStore(commitSink: sink);
            Assert.True(Town.TryReserveRuntimeSlots(Npcs));
            Assert.True(Npcs.TryGetActive(0, out NpcSnapshot initial));
            var update = new NpcStateUpdate(initial.Type, initial.NetId, initial.PositionX, initial.PositionY,
                .5f, 0f, initial.Target, new NpcAiState(5f, timer, 17f, 23f),
                initial.Simulation with { LocalAi = new NpcAiState(0f, 0f, 0f, 9f) });
            Assert.True(Npcs.TryUpdate(initial.Handle, in update, out NpcSnapshot seated));
            Initial = seated;
        }
        public WorldTileStore Tiles { get; }
        public RuntimeTownNpcStateStore Town { get; }
        public RuntimeNpcStore Npcs { get; }
        public NpcSnapshot Initial { get; }
    }

    private sealed class OrderedRandom(params int[] values) : IRuntimeTownNpcScheduleRandom1458
    {
        public int Calls { get; private set; }
        public int Next(int exclusiveMax)
        {
            if (exclusiveMax == 60) return values[Calls++];
            return exclusiveMax - 1;
        }
    }
    private sealed class Sink : INpcStateCommitSink
    {
        public List<NpcStateCommitKind> Commits { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Commits.Add(kind);
    }
}
