using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcLocomotion1458Tests
{
    // Independent TerrariaServer 1.4.5.8 full AI007 quiet fixtures, seed1458, floor row30.
    // Walking now includes actual chair/furniture offers; idle social selection remains separate.
    [Theory]
    [InlineData(0f, 3f, .5f, 0f, 1, 0f, 2f, .4f, 8f, false, 1)]
    [InlineData(0f, 1f, .5f, 0f, 1, 1f, 326f, .4f, 0f, true, 2)]
    [InlineData(1f, 3f, .5f, 0f, 1, 1f, 2f, .57f, -1f, false, 0)]
    [InlineData(1f, 3f, -.5f, 0f, -1, 1f, 2f, -.57f, -1f, false, 0)]
    [InlineData(1f, 1f, .5f, 0f, 1, 0f, 985f, .57f, -1f, true, 2)]
    [InlineData(1f, 3f, 2f, 0f, 1, 1f, 2f, 1.6f, -1f, false, 0)]
    [InlineData(1f, 3f, 2f, 1f, 1, 1f, 2f, 2f, 9f, false, 0)]
    [InlineData(1f, 3f, .98f, 0f, 1, 1f, 2f, 1f, -1f, false, 0)]
    [InlineData(1f, 3f, -.98f, 0f, -1, 1f, 2f, -1.0500001f, -1f, false, 0)]
    public void Quiet_body_matches_original_timer_speed_local_state_and_one_commit(float state, float timer,
        float vx, float vy, int direction, float expectedState, float expectedTimer, float expectedVx,
        float expectedLocal, bool forced, int draws)
    {
        var f = new Fixture(state, timer, vx, vy, direction);
        f.Tick();
        NpcSnapshot after = f.Current;
        Assert.Equal(expectedState, after.Ai.Ai0);
        Assert.Equal(expectedTimer, after.Ai.Ai1);
        Assert.Equal(expectedVx, after.VelocityX);
        Assert.Equal(expectedLocal, after.Simulation.LocalAi.Ai3);
        Assert.Equal(23f, after.Ai.Ai3);
        Assert.Equal(state != expectedState ? 0f : 17f, after.Ai.Ai2);
        Assert.Equal(draws + (expectedState == 1f && vy == 0f ? 2 : 0) + (expectedState == 0f && vy == 0f ? 7 : 0), f.Random.Bounds.Count);
        Assert.Equal(forced ? NpcStateCommitKind.ForcedUpdate : NpcStateCommitKind.Update,
            Assert.Single(f.Sink.Commits));
        Assert.Equal(f.Initial.PositionX + expectedVx, after.PositionX);
        Assert.False(after.Simulation.CollideX);
        if (vy == 0f) Assert.True(after.Simulation.CollideY);
    }

    [Theory]
    [InlineData(1, 639.57f)]
    [InlineData(-1, 638.43f)]
    public void Real_world_tick_matches_original_UpdateNPC_same_tick_acceleration_and_integration(int direction, float x)
    {
        var f = new Fixture(1f, 3f, .5f * direction, 0f, direction);
        var runtime = new ServerRuntimeState(npcs: f.Npcs, worldTiles: f.Tiles, townNpcs: f.Town,
            naturalSpawnRandom: new NpcRandom());
        f.Sink.Commits.Clear();
        runtime.Tick();
        Assert.Equal(x, f.Current.PositionX);
        Assert.Equal(440f, f.Current.PositionY);
        Assert.Equal(.57f * direction, f.Current.VelocityX);
        Assert.Equal(2f, f.Current.Ai.Ai1);
        Assert.True(f.Current.Simulation.CollideY);
        Assert.Single(f.Sink.Commits);
    }

    [Theory]
    [InlineData(1, 14f)]
    [InlineData(-1, 19f)]
    public void Walking_away_from_home_consumes_five_extra_ticks_only_in_outward_direction(int direction, float timer)
    {
        var f = new Fixture(1f, 20f, .5f * direction, 0f, direction, x: 1239f);
        f.Tick();
        Assert.Equal(timer, f.Current.Ai.Ai1);
        Assert.Equal(new[] { 300, 600 }, f.Random.Bounds);
    }

    [Fact]
    public void Expiry_keeps_running_walking_navigation_and_preserves_rng_operand_order()
    {
        var f = new Fixture(1f, 1f, .5f, 0f, 1);
        f.Tick();
        Assert.Equal(new[] { 300, 900, 300, 1800, 1200, 600, 1800, 600, 1200 }, f.Random.Bounds);
        Assert.Equal(-1f, f.Current.Simulation.LocalAi.Ai3); // Source writes this after setting60 on expiry.
        Assert.Equal(.57f, f.Current.VelocityX);
        Assert.Equal(1960233424, f.Random.Stream.Next()); // Original fullAI includes the real idle offers.
    }

    [Fact]
    public void Idle_transition_does_not_reenter_walking_acceleration_and_still_rolls_home_turn()
    {
        var f = new Fixture(0f, 1f, .5f, 0f, 1);
        f.Tick();
        Assert.Equal(new[] { 300, 80, 300, 600 }, f.Random.Bounds);
        Assert.Equal(.4f, f.Current.VelocityX);
        Assert.Equal(1014131397, f.Random.Stream.Next());
    }

    [Fact]
    public void Cliff_stops_walk_but_keeps_same_branch_acceleration_and_final_local_reset()
    {
        var f = new Fixture(1f, 20f, .5f, 0f, 1);
        f.Tiles.Set(41, 30, default);
        f.Tick();
        Assert.Equal(0f, f.Current.Ai.Ai0);
        Assert.Equal(71f, f.Current.Ai.Ai1);
        Assert.Equal(.57f, f.Current.VelocityX);
        Assert.Equal(-1f, f.Current.Simulation.LocalAi.Ai3);
        Assert.Equal(new[] { 50, 300, 1800, 1200, 600, 1800, 600, 1200 }, f.Random.Bounds);
        Assert.Equal(NpcStateCommitKind.ForcedUpdate, Assert.Single(f.Sink.Commits));
    }

    [Fact]
    public void One_block_stepup_precedes_shared_physics_and_is_not_a_fighter_jump()
    {
        var f = new Fixture(1f, 20f, .5f, 0f, 1);
        f.Tiles.Set(41, 29, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        f.Tick();
        Assert.Equal(424f, f.Current.PositionY);
        Assert.Equal(639.57f, f.Current.PositionX);
        Assert.Equal(0f, f.Current.VelocityY);
        Assert.Equal(19f, f.Current.Ai.Ai1);
        Assert.Equal(new[] { 300, 600 }, f.Random.Bounds);
        Assert.Equal(NpcStateCommitKind.Update, Assert.Single(f.Sink.Commits));
    }

    [Fact]
    public void At_home_night_walking_ends_with_source_timer_and_preserves_ai_target()
    {
        var f = new Fixture(1f, 3f, .5f, 0f, 1);
        f.Tick(night: true);
        Assert.Equal(0f, f.Current.Ai.Ai0);
        Assert.Equal(284f, f.Current.Ai.Ai1);
        Assert.Equal(17f, f.Current.Ai.Ai2);
        Assert.Equal(60f, f.Current.Simulation.LocalAi.Ai3);
        Assert.Equal(.5f, f.Current.VelocityX);
        Assert.Equal(new[] { 200, 300, 1800, 1200, 600, 1800, 600, 1200 }, f.Random.Bounds);
        Assert.Single(f.Sink.Commits);
    }

    [Theory]
    [InlineData(10f)] [InlineData(12f)] [InlineData(14f)] [InlineData(15f)] [InlineData(24f)]
    public void Attack_owners_do_not_receive_a_second_schedule_or_physics_pass(float state)
    {
        var f = new Fixture(state, 20f, .5f, 0f, 1);
        f.Tick();
        if (state == 10f)
        {
            Assert.Equal(10f, f.Current.Ai.Ai0); Assert.Equal(19f, f.Current.Ai.Ai1);
            Assert.Equal(10f, f.Current.Simulation.LocalAi.Ai3);
            Assert.Equal(639.4f, f.Current.PositionX); Assert.Single(f.Sink.Commits);
        }
        else { Assert.Equal(f.Initial, f.Current); Assert.Empty(f.Sink.Commits); }
        Assert.Empty(f.Random.Bounds);
    }

    [Theory]
    [InlineData(1)] [InlineData(-1)]
    public void Two_block_obstacle_uses_original_jump_before_gravity_and_mirrors_both_directions(int direction)
    {
        var f = new Fixture(1f, 20f, .5f * direction, 0f, direction);
        int ahead = direction == 1 ? 41 : 39;
        for (int y = 28; y <= 29; y++) f.Tiles.Set(ahead, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        f.Tick();
        Assert.Equal(19f, f.Current.Ai.Ai1);
        Assert.Equal(639f, f.Current.Simulation.LocalAi.Ai3);
        Assert.Equal(-4.925f, f.Current.VelocityY);
        Assert.Equal(435.075f, f.Current.PositionY);
        Assert.Equal(.57f * direction, f.Current.VelocityX);
        Assert.Empty(f.Random.Bounds);
        Assert.Equal(NpcStateCommitKind.ForcedUpdate, Assert.Single(f.Sink.Commits));
    }

    [Fact]
    public void Blocked_jump_clearance_turns_and_reverses_velocity_before_motion()
    {
        var f = new Fixture(1f, 20f, .5f, 0f, 1);
        for (int y = 28; y <= 29; y++) f.Tiles.Set(41, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        f.Tiles.Set(40, 25, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        f.Tick();
        Assert.Equal(19f, f.Current.Ai.Ai1);
        Assert.Equal(-1, f.Current.Simulation.DirectionX);
        Assert.Equal(-.57f, f.Current.VelocityX);
        Assert.Equal(638.43f, f.Current.PositionX);
        Assert.Equal(-1f, f.Current.Simulation.LocalAi.Ai3);
        Assert.Equal(0f, f.Current.VelocityY);
        Assert.Single(f.Sink.Commits);
    }

    [Fact]
    public void Source_stuck_threshold_is_postincrement_and_resets_with_one_ordered_delay_draw()
    {
        var f = new Fixture(1f, 20f, .5f, 0f, 1);
        var before = f.Initial;
        var update = new NpcStateUpdate(before.Type, before.NetId, before.PositionX, before.PositionY,
            before.VelocityX, before.VelocityY, before.Target, before.Ai, before.Simulation with {
                OldPositionX = 639f, OldPositionY = 440f, OldVelocityX = .57f, OldVelocityY = .075f,
                LocalAi = before.Simulation.LocalAi with { Ai2 = 30f } });
        Assert.True(f.Npcs.TryUpdate(before.Handle, in update, out _));
        f.Sink.Commits.Clear();
        f.Tick();
        Assert.Equal(326f, f.Current.Ai.Ai1);
        Assert.Equal(0f, f.Current.Ai.Ai2);
        Assert.Equal(0f, f.Current.Simulation.LocalAi.Ai2);
        Assert.Equal(-.57f, f.Current.VelocityX);
        Assert.Equal(-1, f.Current.Simulation.DirectionX);
        Assert.Equal(new[] { 300, 300, 600 }, f.Random.Bounds);
        Assert.Equal(NpcStateCommitKind.ForcedUpdate, Assert.Single(f.Sink.Commits));
    }

    private sealed class Fixture
    {
        public Fixture(float state, float timer, float vx, float vy, int direction, float x = 639f)
        {
            Tiles = new WorldTileStore(new WorldDimensions(100, 80));
            for (int column = 0; column < 100; column++) Tiles.Set(column, 30, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            Town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [
                new WorldTownNpc(17, "Merchant", x, 440f, false, 40, 30, null, false) ], []),
                [new WorldTownRoom(17, 40, 30)], Tiles.Dimensions);
            Npcs = new RuntimeNpcStore(commitSink: Sink);
            Assert.True(Town.TryReserveRuntimeSlots(Npcs));
            Assert.True(Npcs.TryGetActive(0, out NpcSnapshot initial));
            var update = new NpcStateUpdate(initial.Type, initial.NetId, x, 440f, vx, vy, initial.Target,
                new NpcAiState(state, timer, 17f, 23f), initial.Simulation with {
                    DirectionX = direction, LocalAi = new NpcAiState(0f, 0f, 0f, 9f) });
            Assert.True(Npcs.TryUpdate(initial.Handle, in update, out NpcSnapshot seeded));
            Initial = seeded;
            Schedule = new RuntimeTownNpcSchedule1458(Town, Npcs, Tiles, Random);
            Phase = new(Town, Npcs, Tiles, Schedule);
            Sink.Commits.Clear();
        }
        public WorldTileStore Tiles { get; }
        public RuntimeTownNpcStateStore Town { get; }
        public RuntimeNpcStore Npcs { get; }
        public NpcSnapshot Initial { get; }
        public NpcSnapshot Current { get { Assert.True(Npcs.TryGetActive(0, out NpcSnapshot value)); return value; } }
        public RuntimeTownNpcSchedule1458 Schedule { get; }
        public TownNpcTestPhase1458 Phase { get; }
        public RandomSource Random { get; } = new();
        public Sink Sink { get; } = new();
        public void Tick(bool night = false) { var conditions = new RuntimeTownNpcScheduleConditions1458(!night, false, false, false, false); Phase.Tick(in conditions, []); }
    }
    private sealed class RandomSource : IRuntimeTownNpcScheduleRandom1458
    {
        public VanillaUnifiedRandom1458 Stream { get; } = new(1458);
        public List<int> Bounds { get; } = [];
        public int Next(int maximum) { Bounds.Add(maximum); return Stream.Next(maximum); }
    }
    private sealed class NpcRandom : IVanillaNpcRandom
    {
        private readonly VanillaUnifiedRandom1458 stream = new(1458);
        public int NextInt32(int min, int max) => stream.Next(min, max);
    }
    private sealed class Sink : INpcStateCommitSink
    {
        public List<NpcStateCommitKind> Commits { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Commits.Add(kind);
    }
}
