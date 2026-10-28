using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GroundCritterFighterPhysics1458Tests
{
    [Fact]
    public void Collision_immediately_uses_the_turned_style_for_platforms_and_stair_fall()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        for (int x = 0; x < 100; x++) tiles.Set(x, 11, new WorldTile { Type = 19, Flags = WorldTileFlags.Active });
        var home = new VanillaNpcTownCollisionContext1458(11, true, false, false);
        foreach (var sample in new (int Type, int? Style, bool Fighter, int Height)[] {
                     (299, null, false, 20), (299, 3, true, 20), (46, null, false, 20), (3, null, true, 40) })
        {
            var actor = Actor(sample.Type, sample.Style, 176f - sample.Height, height: sample.Height);
            var state = State(actor) with { VelocityY = .3f };
            Assert.True(VanillaNpcWorldMotionAiStepper.TryFinishCollision(tiles, in state, .3f,
                out var result, townContext: home));
            // NPC.Collision_DecideFallThroughPlatforms and its subsequent slope
            // stage observe aiStyle after AI, including the first turning update.
            Assert.Equal(sample.Fighter ? state.PositionY + .3f : state.PositionY, result.PositionY);
            Assert.Equal(sample.Fighter ? .3f : 0f, result.VelocityY);
            Assert.Equal(sample.Fighter, result.Simulation.StairFall);
        }
    }

    [Fact]
    public void Hostile_squirrel_retains_real_ground_support_for_stuck_hop_without_door_clock_resets()
    {
        var tiles = Floor();
        var motion = new VanillaNpcWorldMotionAiStepper(new RefuseInner(), tiles);
        var actor = Actor(299, 3, 140f, vx: 0f);
        var planned = State(actor) with { VelocityX = .1f, Ai = new(0, 6, 12, 1) };
        Assert.True(motion.TryApplyTerrainMotion(in actor, in planned, out var result));
        Assert.Equal(-5f, result.VelocityY);
        Assert.Equal(planned.Ai, result.Ai);

        var ordinary = Actor(3, null, 120f, vx: 0f, height: 40);
        var ordinaryPlan = State(ordinary) with { VelocityX = .1f, Ai = planned.Ai };
        Assert.True(motion.TryApplyTerrainMotion(in ordinary, in ordinaryPlan, out var ordinaryResult));
        Assert.Equal(-5f, ordinaryResult.VelocityY);
        Assert.Equal(0f, ordinaryResult.Ai.Ai1);
        Assert.Equal(0f, ordinaryResult.Ai.Ai2);

        var neutral = Actor(46, null, 140f, vx: 0f);
        var neutralPlan = State(neutral) with { VelocityX = .1f, Ai = planned.Ai };
        Assert.True(motion.TryApplyTerrainMotion(in neutral, in neutralPlan, out var neutralResult));
        Assert.Equal(0f, neutralResult.VelocityY);
        Assert.Equal(planned.Ai, neutralResult.Ai);
    }

    [Fact]
    public void Hostile_squirrel_uses_the_existing_source_obstacle_jump_and_force_sync_offer()
    {
        var tiles = Floor();
        tiles.Set(11, 8, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var motion = new VanillaNpcWorldMotionAiStepper(new RefuseInner(), tiles);
        foreach (var sample in new (int Type, int? Style, int Height)[] { (299, 3, 20), (3, null, 40) })
        {
            var actor = Actor(sample.Type, sample.Style, 160f - sample.Height, height: sample.Height);
            var planned = State(actor) with { VelocityX = 1f };
            Assert.True(motion.TryApplyTerrainMotion(in actor, in planned, out var result));
            Assert.Equal(-6f, result.VelocityY);
            Assert.True(result.RequiresImmediateSync);
        }
        var neutral = Actor(299, null, 140f);
        var neutralPlan = State(neutral) with { VelocityX = 1f };
        Assert.True(motion.TryApplyTerrainMotion(in neutral, in neutralPlan, out var neutralResult));
        Assert.Equal(0f, neutralResult.VelocityY);
        Assert.False(neutralResult.RequiresImmediateSync);
    }

    [Fact]
    public void Expert_elevated_target_jump_keeps_support_when_squirrels_do_not_process_doors()
    {
        var tiles = Floor();
        var targeting = new VanillaNpcTargetingAiStepper(new RefuseInner());
        targeting.SetWorldConditions(dayTime: true, slimeRainActive: false, goodWorld: false, expertMode: true);
        var motion = new VanillaNpcWorldMotionAiStepper(targeting, tiles);
        var rawTarget = new VanillaNpcRawPlayer1458(0, true, false, false, 160f, 64f, 20, 42, 0, false, 0);
        foreach (var sample in new (int Type, int? Style, int Height)[] { (299, 3, 20), (3, null, 40) })
        {
            var actor = Actor(sample.Type, sample.Style, 160f - sample.Height, height: sample.Height);
            var planned = State(actor) with { VelocityX = 1f, Target = 0 };
            Assert.True(motion.TryApplyTerrainMotion(in actor, in planned, out var result, rawTarget));
            Assert.Equal(-7.9f, result.VelocityY);
            Assert.Equal(1f, result.VelocityX);
        }
    }

    private static WorldTileStore Floor()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        for (int x = 0; x < 100; x++) tiles.Set(x, 10, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        return tiles;
    }

    private static NpcSnapshot Actor(int type, int? style, float y, float vx = .9f, int height = 20) =>
        new(new(0, new(1)), new(1), type, (short)type, 160f, y, vx, 0, 255, default,
            NpcSimulationState.Initial with { Life = style == 3 ? 25 : 5, LifeMax = style == 3 ? 25 : 5,
                AiStyleOverride = style is { } value ? new NpcAiStyleId(value) : null,
                HitboxOverride = new(18, height), DirectionX = 1, DirectionY = 1, SpriteDirection = 1,
                OldPositionX = 159f, OldPositionY = y, OldVelocityX = vx, TownNpc = false });

    private static NpcStateUpdate State(in NpcSnapshot npc) => new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY,
        npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);

    private sealed class RefuseInner : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next) =>
            throw new InvalidOperationException("Direct motion capability must not execute another AI step.");
    }
}
