using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownNpcDoorsFurniture1458Tests
{
    // Complete independent official AI007/UpdateNPC fixtures: seed14 opens; seed1458 misses.
    [Theory]
    [InlineData(14, false, false, true, 99f, .57f, 1, 1173303932)]
    [InlineData(14, true, false, true, 99f, .57f, 1, 1173303932)]
    [InlineData(14, true, true, false, 19f, .57f, -1, 1173303932)]
    [InlineData(1458, false, false, false, 19f, -.57f, -1, 1627693788)]
    public void Opening_matches_original_failure_velocity_and_complete_walking_rng(int seed, bool blockedRight,
        bool blockedLeft, bool opens, float timer, float vx, int direction, int next)
    {
        var f = new Fixture(seed);
        f.Door();
        if (blockedRight) for (int y = 27; y < 30; y++) f.Tile(42, y, 1);
        if (blockedLeft) for (int y = 27; y < 30; y++) f.Tile(40, y, 1);
        f.Tick();
        Assert.Equal(timer, f.Current.Ai.Ai1);
        Assert.Equal(vx, f.Current.VelocityX);
        Assert.Equal(direction, f.Current.Simulation.DirectionX);
        Assert.Equal(opens, f.Schedule.HasRememberedDoor(f.Current.Handle));
        Assert.Equal(next, f.Random.Stream.Next());
        Assert.Equal(NpcStateCommitKind.ForcedUpdate, Assert.Single(f.Sink.Commits));
        if (opens) Assert.Equal((ushort)11, f.Tiles.Get(41, 27).Type);
    }

    [Fact]
    public void Night_away_from_home_draws_chance_before_unconditional_shelter_open()
    {
        var f = new Fixture(1458, homeX: 20);
        f.Door();
        var c = new RuntimeTownNpcScheduleConditions1458(false, false, false, false, false);
        f.Phase.Tick(in c, [new RuntimeTownPlayerBounds1458(600f, 440f, 20f, 42f)]);
        Assert.Equal(99f, f.Current.Ai.Ai1);
        Assert.True(f.Schedule.HasRememberedDoor(f.Current.Handle));
        Assert.Equal(new[] { 10, 300, 600 }, f.Random.Bounds);
        Assert.Equal(1627693788, f.Random.Stream.Next());
    }

    [Theory]
    [InlineData(679f, false, true, 11, 1916656655)]
    [InlineData(703f, false, false, 10, 1507290721)]
    [InlineData(703f, true, true, 11, 1916656655)]
    [InlineData(727f, true, false, 11, 1916656655)]
    public void Remembered_close_has_strict_distances_unforced_occupancy_and_source_frame_rng(
        float x, bool occupied, bool remembered, int type, int next)
    {
        var f = new Fixture(14);
        f.Door(); f.Tick();
        f.Seed(x: x); f.Random.Reset(1458); f.Occupancy.Free = !occupied;
        f.Tick();
        Assert.Equal((ushort)type, f.Tiles.Get(41, 27).Type);
        Assert.Equal(remembered, f.Schedule.HasRememberedDoor(f.Current.Handle));
        Assert.Equal(19f, f.Current.Ai.Ai1);
        Assert.Equal(next, f.Random.Stream.Next());
        Assert.Equal(NpcStateCommitKind.Update, Assert.Single(f.Sink.Commits)); // Close alone does not force NPC sync.
        if (type == 10) Assert.Equal(new short[] { 18, 18, 36 }, Enumerable.Range(27, 3).Select(y => f.Tiles.Get(41, y).FrameX));
    }

    [Theory]
    [InlineData(711f, 440f, true)]
    [InlineData(615f, 440f, true)]
    [InlineData(583f, 440f, true)]
    [InlineData(703f, 476f, true)]
    [InlineData(703f, 476.01f, false)]
    public void Blocked_memory_retains_exact_four_tile_edges_and_abandons_only_beyond(float x, float y, bool remembered)
    {
        var f = new Fixture(14); f.Door(); f.Tick(); f.Occupancy.Free = false;
        f.Seed(x: x, y: y); f.Random.Reset(1458); f.Tick();
        Assert.Equal(remembered, f.Schedule.HasRememberedDoor(f.Current.Handle));
        Assert.Equal((ushort)11, f.Tiles.Get(41, 27).Type);
        Assert.DoesNotContain(3, f.Random.Bounds);
    }

    [Fact]
    public void Remembered_state_survives_same_generation_transform_but_not_slot_reuse()
    {
        var f = new Fixture(14); f.Door(); f.Tick();
        NpcHandle old = f.Current.Handle;
        f.Seed(type: 22);
        Assert.True(f.Schedule.HasRememberedDoor(old));
        f.Tick(); // Home type no longer matches, but this is still the same source NPC object.
        Assert.True(f.Schedule.HasRememberedDoor(old));
        Assert.True(f.Npcs.TryDespawn(old));
        var update = f.Update(type: 17, x: 703f);
        Assert.True(f.Npcs.TrySpawn(0, in update, out var replacement));
        f.Random.Reset(1458); f.Tick();
        Assert.False(f.Schedule.HasRememberedDoor(old));
        Assert.False(f.Schedule.HasRememberedDoor(replacement.Handle));
        Assert.Equal((ushort)11, f.Tiles.Get(41, 27).Type);
    }

    [Fact]
    public void Replaced_remembered_tile_discards_memory_without_close_rng()
    {
        var f = new Fixture(14); f.Door(); f.Tick(); f.Seed(x: 703f);
        f.Tile(41, 27, 1); f.Random.Reset(1458); f.Tick();
        Assert.False(f.Schedule.HasRememberedDoor(f.Current.Handle));
        Assert.Equal(new[] { 300, 600 }, f.Random.Bounds);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Actual_runtime_tick_opens_with_same_tick_physics_and_remembers_for_later_close(bool gate)
    {
        var f = new Fixture(14); if (gate) f.Gate(); else f.Door();
        if (gate) f.Seed(x: 638.9f); // Original EmptyTile integer rectangle ends exactly at gate edge.
        var rng = new NpcRandom(14);
        var runtime = new ServerRuntimeState(npcs: f.Npcs, worldTiles: f.Tiles, townNpcs: f.Town, naturalSpawnRandom: rng);
        f.Sink.Commits.Clear(); runtime.Tick();
        Assert.Equal(gate ? 638.9f + .57f : 639.57f, f.Current.PositionX);
        Assert.Equal(99f, f.Current.Ai.Ai1);
        Assert.Equal((ushort)(gate ? 389 : 11), f.Tiles.Get(41, 27).Type);
        Assert.Single(f.Sink.Commits);
        f.Seed(x: 703f); rng.Reset(1458); runtime.Tick();
        Assert.Equal((ushort)(gate ? 388 : 10), f.Tiles.Get(41, 27).Type);
        Assert.Equal(703.57f, f.Current.PositionX);
        Assert.Equal(gate ? 1916656655 : 1507290721, rng.Stream.Next());
    }

    [Theory]
    [InlineData(false, false, false, true, 637f, -1, 1761514906)]
    [InlineData(true, false, false, true, 641f, 1, 1761514906)]
    [InlineData(false, true, false, true, 637f, -1, 1761514906)]
    [InlineData(false, false, true, false, 639.57f, 1, 1409701741)]
    public void Walking_chair_offer_matches_original_inactive_identity_and_reserved_frames(bool right,
        bool inactive, bool reserved, bool sits, float x, int direction, int next)
    {
        var f = new Fixture(146);
        f.Chair(right, inactive, reserved); f.Tick();
        Assert.Equal(sits ? 5f : 1f, f.Current.Ai.Ai0);
        Assert.Equal(sits ? 7989f : 19f, f.Current.Ai.Ai1);
        Assert.Equal(x, f.Current.PositionX); Assert.Equal(direction, f.Current.Simulation.DirectionX);
        Assert.Equal(sits ? 0f : .57f, f.Current.VelocityX);
        Assert.Equal(next, f.Random.Stream.Next());
        Assert.Equal(sits ? new[] { 300, 10800 } : new[] { 300 }, f.Random.Bounds);
        Assert.Single(f.Sink.Commits);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(255, true)]
    public void Walking_chair_excludes_sitting_players_except_source_slot255(byte slot, bool sits)
    {
        var f = new Fixture(146); f.Chair();
        f.Tick([new RuntimeTownPlayerSeat1458(slot, new RuntimeTownPlayerBounds1458(638f, 443f, 20f, 42f))]);
        Assert.Equal(sits ? 5f : 1f, f.Current.Ai.Ai0);
        Assert.Equal(sits ? 1761514906 : 1409701741, f.Random.Stream.Next());
    }

    [Fact]
    public void Walking_chair_excludes_active_seated_npc_and_missed_offer_does_not_draw_furniture()
    {
        var f = new Fixture(146); f.Chair();
        var peer = f.Update(type: 22) with { Ai = new NpcAiState(5f, 20f, 0f, 0f) };
        Assert.True(f.Npcs.TrySpawn(1, in peer, out _)); f.Sink.Commits.Clear(); f.Tick();
        Assert.Equal(1f, f.Current.Ai.Ai0);
        Assert.Equal(new[] { 300 }, f.Random.Bounds);
        Assert.Equal(1409701741, f.Random.Stream.Next());
    }

    [Theory]
    [InlineData(false, false, true, 56f, 762487543)]
    [InlineData(true, false, false, 90f, 388817304)]
    [InlineData(false, true, false, 19f, 388817304)]
    public void Walking_furniture_offer_matches_original_avoidance_and_actuation(bool avoided, bool actuated,
        bool inspecting, float timer, int next)
    {
        var f = new Fixture(580); f.Tile(41, 28, 17);
        if (avoided) f.Tile(40, 28, 21);
        if (actuated) { WorldTile t = f.Tiles.Get(41, 28); t.Flags |= WorldTileFlags.Inactive; f.Tiles.Set(41, 28, in t); }
        f.Tick();
        Assert.Equal(inspecting ? 9f : 1f, f.Current.Ai.Ai0); Assert.Equal(timer, f.Current.Ai.Ai1);
        Assert.Equal(inspecting ? 0f : .57f, f.Current.VelocityX);
        Assert.Equal(next, f.Random.Stream.Next());
        Assert.Equal(inspecting ? new[] { 300, 600, 90 } : new[] { 300, 600 }, f.Random.Bounds);
        Assert.Single(f.Sink.Commits);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void State9_has_source_damping_timer_expiry_and_single_physics_commit(bool expires)
    {
        var f = new Fixture(580); f.Seed(state: 9f, timer: expires ? 1f : 20f); f.Tick();
        Assert.Equal(expires ? 0f : 9f, f.Current.Ai.Ai0);
        Assert.Equal(expires ? 103f : 19f, f.Current.Ai.Ai1);
        Assert.Equal(expires ? 30f : 9f, f.Current.Simulation.LocalAi.Ai3);
        Assert.Equal(.4f, f.Current.VelocityX); Assert.Equal(639.4f, f.Current.PositionX);
        if (expires) Assert.Equal(new[] { 60, 60, 300, 1800, 1200, 600, 1800, 600, 1200 }, f.Random.Bounds); else Assert.Empty(f.Random.Bounds);
        // Expired state9 proceeds into the real source idle offer chain.
        Assert.Equal(expires ? 320707549 : 1573945525, f.Random.Stream.Next());
        Assert.Equal(expires ? NpcStateCommitKind.ForcedUpdate : NpcStateCommitKind.Update, Assert.Single(f.Sink.Commits));
    }

    [Theory]
    [InlineData(146, true, 637f, 5f, 7989f, 1761514906)]
    [InlineData(580, false, 639f, 9f, 56f, 762487543)]
    public void Actual_world_tick_matches_complete_original_furniture_UpdateNPC(int seed, bool chair,
        float x, float state, float timer, int next)
    {
        var f = new Fixture(seed); if (chair) f.Chair(); else f.Tile(41, 28, 17);
        var rng = new NpcRandom(seed);
        var runtime = new ServerRuntimeState(npcs: f.Npcs, worldTiles: f.Tiles, townNpcs: f.Town, naturalSpawnRandom: rng);
        f.Sink.Commits.Clear(); runtime.Tick();
        Assert.Equal(x, f.Current.PositionX); Assert.Equal(state, f.Current.Ai.Ai0); Assert.Equal(timer, f.Current.Ai.Ai1);
        Assert.Equal(0f, f.Current.VelocityX); Assert.Equal(next, rng.Stream.Next()); Assert.Single(f.Sink.Commits);
    }

    [Fact]
    public void Successful_chair_roll_without_chair_skips_furniture_offer_and_delay_draws()
    {
        var f = new Fixture(146); f.Tile(41, 28, 17); f.Tick();
        Assert.Equal(1f, f.Current.Ai.Ai0); Assert.Equal(new[] { 300 }, f.Random.Bounds);
        Assert.Equal(1409701741, f.Random.Stream.Next());
    }

    [Fact]
    public void Furniture_admission_uses_source_nearby_danger_instead_of_hostiles_anywhere_in_world()
    {
        var f = new Fixture(580); f.Tile(41, 28, 17);
        var hostile = new NpcStateUpdate(3, 3, 1400f, 440f, 0f, 0f, 255, default, NpcSimulationState.Initial);
        Assert.True(f.Npcs.TrySpawn(1, in hostile, out _)); f.Tick();
        Assert.Equal(9f, f.Current.Ai.Ai0); Assert.Equal(56f, f.Current.Ai.Ai1);
        Assert.Equal(new[] { 300, 600, 90 }, f.Random.Bounds);
        Assert.Equal(762487543, f.Random.Stream.Next());
    }

    private sealed class Fixture
    {
        public Fixture(int seed, int homeX = 40)
        {
            Random = new(seed); Tiles = new(new WorldDimensions(100, 80));
            for (int x = 0; x < 100; x++) Tile(x, 30, 1);
            Town = new(new WorldNpcPersistence([], [new WorldTownNpc(17, "Merchant", 639f, 440f, false, homeX, 30, null, false)], []),
                [new WorldTownRoom(17, homeX, 30)], Tiles.Dimensions);
            Npcs = new(commitSink: Sink); Assert.True(Town.TryReserveRuntimeSlots(Npcs));
            Seed(); Schedule = new(Town, Npcs, Tiles, Random, Occupancy);
            Phase = new(Town, Npcs, Tiles, Schedule); Sink.Commits.Clear();
        }
        public WorldTileStore Tiles { get; }
        public RuntimeTownNpcStateStore Town { get; }
        public RuntimeNpcStore Npcs { get; }
        public RuntimeTownNpcSchedule1458 Schedule { get; }
        public TownNpcTestPhase1458 Phase { get; }
        public RandomSource Random { get; }
        public Occupancy Occupancy { get; } = new();
        public Sink Sink { get; } = new();
        public NpcSnapshot Current { get { Assert.True(Npcs.TryGetActive(0, out var n)); return n; } }
        public NpcStateUpdate Update(int type = 17, float x = 639f, float state = 1f, float timer = 20f, float y = 440f) =>
            new(type, (short)type, x, y, .5f, 0f, 255, new(state, timer, 17f, 23f),
                NpcSimulationState.Initial with { DirectionX = 1, LocalAi = new(0f, 0f, 0f, 9f) });
        public void Seed(float x = 639f, float state = 1f, float timer = 20f, int type = 17, float y = 440f)
        { var update = Update(type, x, state, timer, y); Assert.True(Npcs.TryUpdate(Current.Handle, in update, out _)); Sink.Commits.Clear(); }
        public void Tile(int x, int y, ushort type, short fx = 0, short fy = 0, bool active = true) =>
            Tiles.Set(x, y, new WorldTile { Type = type, FrameX = fx, FrameY = fy, Flags = active ? WorldTileFlags.Active : 0 });
        public void Door() { Tile(41, 26, 1); for (int row = 0; row < 3; row++) Tile(41, 27 + row, 10, fy: (short)(row * 18)); }
        public void Gate() { Tile(41, 24, 1); for (int row = 0; row < 5; row++) Tile(41, 25 + row, 388, fy: (short)new[] { 0, 20, 38, 56, 74 }[row]); }
        public void Chair(bool right = false, bool inactive = false, bool reserved = false)
        { Tile(40, 28, 15, right ? (short)18 : (short)0, reserved ? (short)1080 : (short)0); Tile(40, 29, 15, right ? (short)18 : (short)0, reserved ? (short)1098 : (short)20, !inactive); }
        public void Tick(ReadOnlySpan<RuntimeTownPlayerSeat1458> seats = default)
        { var c = new RuntimeTownNpcScheduleConditions1458(true, false, false, false, false); Phase.Tick(in c, [], seatedPlayers: seats); }
    }
    private sealed class RandomSource(int seed) : IRuntimeTownNpcScheduleRandom1458
    {
        public VanillaUnifiedRandom1458 Stream { get; private set; } = new(seed);
        public List<int> Bounds { get; } = [];
        public int Next(int max) { Bounds.Add(max); return Stream.Next(max); }
        public void Reset(int seed) { Stream = new(seed); Bounds.Clear(); }
    }
    private sealed class NpcRandom(int seed) : IVanillaNpcRandom
    {
        public VanillaUnifiedRandom1458 Stream { get; private set; } = new(seed);
        public int NextInt32(int min, int max) => Stream.Next(min, max);
        public void Reset(int seed) => Stream = new(seed);
    }
    private sealed class Occupancy : IVanillaTallGateOccupancyProbe
    { public bool Free { get; set; } = true; public bool IsActorFree(int x, int y) => Free; }
    private sealed class Sink : INpcStateCommitSink
    { public List<NpcStateCommitKind> Commits { get; } = []; public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot npc) => Commits.Add(kind); }
}
