using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class NpcStairFall1458Tests
{
    // Original unmodified Linux TerrariaServer 1.4.5.8 assembly, executed on Windows CoreCLR.
    // NPC.Collision_DecideFallThroughPlatforms then Collision_MoveSlopesAndStairFall: 2560 rows.
    // Log SHA256: 6012335735108ad703dab70ffca283253a9c53ff85b148b8f0f7f62ad84c3c1e.
    // Each nibble captures fall, retained stairFall, Y==128 and velocityY==0.
    private const string Captured = """
            03c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c3
            03c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c3
            03c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c3
            03c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c3
            03c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c303c3
            03230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323
            03230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323
            03230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323
            03230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323
            03230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323032303230323
            00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc
            00220022002200220022002200220022002200220022002200220022002200220022002200220022002200220022002233333333333333333333333333333333
            333333333333333333333333333333333333333333333333333333333333333300cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc
            00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc0022002200cc00cc0022002200cc00cc002200220022002200220022
            00cc00cc0022002200cc00cc0022002200cc33333333333300223333333333333333333333333333333333333333333333333333333333333333333333333333
            00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc00cc
            00220022002200220022002200220022002200220022002200220022002200220022002200220022002200220022002233333333333333333333333333333333
            33333333333333333333333333333333333333333333333333333333333333330022002200cc00cc0022002200cc00cc0022002200cc00cc00cc00cc00cc00cc
            0022002200cc00cc0022002200cc00cc0022002200cc00cc00cc00cc00cc00cc0022002200220022002200220022002200220022002200220022002200220022
            00220022002200220022002200220022002233333333333300223333333333333333333333333333333333333333333333333333333333333333333333333333
        """;

    [Fact]
    public void Source_platform_and_stair_policies_match_complete_independent_matrix()
    {
        string expected = string.Concat(Captured.Where(c => !char.IsWhiteSpace(c)));
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        int index = 0;
        foreach (int style in new[] { 3, 7 })
        foreach (bool old in new[] { false, true })
        foreach (bool day in new[] { false, true })
        foreach (int home in new[] { 9, 10, 11, 26, 27 })
        foreach (bool town in new[] { false, true })
        foreach (bool head in new[] { false, true })
        foreach (bool eclipse in new[] { false, true })
        foreach (bool invasion in new[] { false, true })
        foreach (bool stairs in new[] { false, true })
        foreach (int direction in new[] { -1, 1 })
        {
            tiles.Set(6, 10, stairs ? new WorldTile { Type = 19, Shape = 2, Flags = WorldTileFlags.Active } : default);
            tiles.Set(7, 8, head ? new WorldTile { Type = 1, Flags = WorldTileFlags.Active } : default);
            var state = State(style == 3 ? 3 : 17, 130f, 1f) with
            { Simulation = NpcSimulationState.Initial with { TownNpc = town, StairFall = old, DirectionY = direction } };
            var context = new VanillaNpcTownCollisionContext1458(home, day, eclipse, invasion);
            bool fall = style == 3 ? direction == 1 :
                VanillaNpcWorldMotionAiStepper.DecideTownPlatformFall(in state, 40, in context);
            bool retained = old || fall;
            if (style == 7)
                retained = VanillaNpcWorldMotionAiStepper.DecideTownStairFall(tiles, 104f, 130f, 18, 40,
                    town, in context, retained);
            var slope = VanillaWorldSlopeCollision.Resolve(tiles, 104f, 130f, 0f, 1f, 18, 40, retained);
            retained = slope.StairFall || fall && retained;
            int actual = (fall ? 1 : 0) | (retained ? 2 : 0) | (slope.PositionY == 128f ? 4 : 0) |
                (slope.VelocityY == 0f ? 8 : 0);
            Assert.Equal(104f, slope.PositionX);
            Assert.True(slope.PositionY is 128f or 130f);
            Assert.Equal(0f, slope.VelocityX);
            Assert.True(slope.VelocityY is 0f or 1f);
            Assert.Equal(Convert.ToInt32(expected[index++].ToString(), 16), actual);
        }
        Assert.Equal(2560, index);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(17)]
    public void Shared_collision_uses_retained_descent_then_clears_it_after_leaving_stairs(int type)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 10, new WorldTile { Type = 19, Shape = 2, Flags = WorldTileFlags.Active });
        var state = State(type, 129f, 1f) with
        { Simulation = NpcSimulationState.Initial with { DirectionY = -1, StairFall = true, TownNpc = type == 17 } };
        Assert.True(VanillaNpcWorldMotionAiStepper.TryFinishCollision(tiles, in state, .3f, out var descending,
            townContext: type == 17 ? new(10, true, false, false) : null));
        Assert.Equal(130f, descending.PositionY);
        Assert.Equal(1f, descending.VelocityY);
        Assert.True(descending.Simulation.StairFall);
        tiles.Set(6, 10, default);
        Assert.True(VanillaNpcWorldMotionAiStepper.TryFinishCollision(tiles, in descending, .3f, out var clear,
            townContext: type == 17 ? new(10, true, false, false) : null));
        Assert.False(clear.Simulation.StairFall);
    }

    [Fact]
    public void Slope_fall_output_records_actual_contact_instead_of_echoing_input_intent()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var empty = VanillaWorldSlopeCollision.Resolve(tiles, 104f, 130f, 0f, 1f, 18, 40, true);
        Assert.False(empty.StairFall);
    }

    [Fact]
    public void Store_owns_stair_history_across_omitted_updates_transform_and_slot_reuse()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        var state = State(3, 129f, 1f);
        Assert.True(store.TrySpawn(0, in state, out var born));
        Assert.False(born.Simulation.StairFall);
        var descending = state with { Simulation = born.Simulation with { StairFall = true } };
        Assert.True(store.TryUpdate(born.Handle, in descending, out var committed));
        var omitted = state with { Type = 21, NetId = 21 };
        Assert.True(store.TryUpdate(committed.Handle, in omitted, out var changed));
        Assert.True(changed.Simulation.StairFall);
        Assert.True(store.TryDespawn(changed.Handle));
        Assert.True(store.TrySpawn(0, in state, out var replacement));
        Assert.False(replacement.Simulation.StairFall);
    }

    private static NpcStateUpdate State(int type, float y, float vy) =>
        new(type, checked((short)type), 104f, y, 0f, vy, 255, default, NpcSimulationState.Initial);
}
