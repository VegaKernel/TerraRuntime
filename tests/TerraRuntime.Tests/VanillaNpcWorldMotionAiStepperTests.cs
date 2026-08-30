using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaNpcWorldMotionAiStepperTests
{
    [Fact]
    public void Empty_world_applies_ai_and_position_motion_in_one_revision()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate state = CreateDemonEye();
        Assert.True(store.TrySpawn(0, in state, out NpcSnapshot spawned));
        var executor = new RuntimeNpcAiStateExecutor(store);
        var stepper = new VanillaNpcWorldMotionAiStepper(
            new VanillaDemonEyeAiStepper(),
            tiles);

        NpcAiStateTickSummary summary = executor.Tick(stepper);

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), summary);
        Assert.True(store.TryGet(spawned.Handle, out NpcSnapshot updated));
        Assert.Equal(new NpcRevision(2), updated.Revision);
        Assert.Equal(100.1f, updated.PositionX, 5);
        Assert.Equal(199.96f, updated.PositionY, 5);
        Assert.Equal(0.1f, updated.VelocityX, 5);
        Assert.Equal(-0.04f, updated.VelocityY, 5);
        Assert.Equal(0.1f, updated.Simulation.OldVelocityX, 5);
        Assert.Equal(-0.04f, updated.Simulation.OldVelocityY, 5);
        Assert.Equal(100f, updated.Simulation.OldPositionX, 5);
        Assert.Equal(200f, updated.Simulation.OldPositionY, 5);
        Assert.False(updated.Simulation.CollideX);
        Assert.False(updated.Simulation.CollideY);
    }

    [Fact]
    public void Live_post_ai_scale_controls_world_contact_geometry()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        WorldTile liquid = default;
        liquid.LiquidAmount = byte.MaxValue;
        liquid.LiquidKind = WorldLiquidKind.Water;
<<<<<<< Updated upstream
        tiles.Set(7, 7, liquid);
=======
        tiles.Set(7, 6, liquid);
>>>>>>> Stashed changes
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate state = CreateDemonEye() with
        {
            PositionX = 80f,
            PositionY = 80f
        };
        Assert.True(store.TrySpawn(0, in state, out NpcSnapshot spawned));
        var executor = new RuntimeNpcAiStateExecutor(store);
        var stepper = new VanillaNpcWorldMotionAiStepper(new FixedScaleStepper(scale: 2f), tiles);

        executor.Tick(stepper);

        Assert.True(store.TryGet(spawned.Handle, out NpcSnapshot updated));
        Assert.Equal(2f, updated.Simulation.Scale);
        Assert.True(updated.Simulation.Wet);
        Assert.Equal(NpcLiquidContactKind.Water, updated.Simulation.LiquidContact);
    }

    [Fact]
    public void Wall_collision_clamps_motion_and_becomes_next_tick_context()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(9, 10, SolidTile(1));
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate state = CreateDemonEye() with
        {
            PositionX = 100f,
            PositionY = 160f
        };
        Assert.True(store.TrySpawn(0, in state, out NpcSnapshot spawned));
        var executor = new RuntimeNpcAiStateExecutor(store);
        var stepper = new VanillaNpcWorldMotionAiStepper(new FixedVelocityStepper(20f, 0f), tiles);

        executor.Tick(stepper);

        Assert.True(store.TryGet(spawned.Handle, out NpcSnapshot updated));
        Assert.Equal(new NpcRevision(2), updated.Revision);
        Assert.Equal(114f, updated.PositionX, 5);
        Assert.Equal(14f, updated.VelocityX, 5);
        Assert.Equal(20f, updated.Simulation.OldVelocityX, 5);
        Assert.Equal(100f, updated.Simulation.OldPositionX, 5);
        Assert.Equal(160f, updated.Simulation.OldPositionY, 5);
        Assert.True(updated.Simulation.CollideX);
    }

    [Fact]
    public void Water_contact_slows_position_delta_but_preserves_collided_velocity()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        WorldTile liquid = default;
        liquid.LiquidAmount = byte.MaxValue;
        liquid.LiquidKind = WorldLiquidKind.Water;
        tiles.Set(6, 6, liquid);
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate state = CreateDemonEye() with
        {
            PositionX = 90f,
            PositionY = 80f
        };
        Assert.True(store.TrySpawn(0, in state, out NpcSnapshot spawned));
        var executor = new RuntimeNpcAiStateExecutor(store);
        var stepper = new VanillaNpcWorldMotionAiStepper(new FixedVelocityStepper(4f, 2f), tiles);

        executor.Tick(stepper);

        Assert.True(store.TryGet(spawned.Handle, out NpcSnapshot updated));
        Assert.Equal(92f, updated.PositionX, 5);
        Assert.Equal(81f, updated.PositionY, 5);
        Assert.Equal(4f, updated.VelocityX, 5);
        Assert.Equal(2f, updated.VelocityY, 5);
        Assert.True(updated.Simulation.Wet);
    }

    [Fact]
    public void Leaving_liquid_halves_horizontal_momentum_before_dry_motion()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate state = CreateDemonEye() with
        {
            Simulation = NpcSimulationState.Initial with
            {
                Wet = true,
                NoGravity = true
            }
        };
        Assert.True(store.TrySpawn(0, in state, out NpcSnapshot spawned));
        var executor = new RuntimeNpcAiStateExecutor(store);
        var stepper = new VanillaNpcWorldMotionAiStepper(new FixedVelocityStepper(4f, 0f), tiles);

        executor.Tick(stepper);

        Assert.True(store.TryGet(spawned.Handle, out NpcSnapshot updated));
        Assert.Equal(102f, updated.PositionX, 5);
        Assert.Equal(2f, updated.VelocityX, 5);
        Assert.False(updated.Simulation.Wet);
    }

    [Fact]
    public void No_tile_collide_path_still_captures_old_position_before_direct_motion()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate state = CreateDemonEye() with
        {
            Simulation = NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = -1,
                NoGravity = true,
                NoTileCollide = true,
                CollideX = true,
                Wet = true
            }
        };
        Assert.True(store.TrySpawn(0, in state, out NpcSnapshot spawned));
        var executor = new RuntimeNpcAiStateExecutor(store);
        var stepper = new VanillaNpcWorldMotionAiStepper(new FixedVelocityStepper(3f, 4f), tiles);

        executor.Tick(stepper);

        Assert.True(store.TryGet(spawned.Handle, out NpcSnapshot updated));
        Assert.Equal(103f, updated.PositionX, 5);
        Assert.Equal(204f, updated.PositionY, 5);
        Assert.Equal(100f, updated.Simulation.OldPositionX, 5);
        Assert.Equal(200f, updated.Simulation.OldPositionY, 5);
        Assert.True(updated.Simulation.CollideX);
        Assert.True(updated.Simulation.Wet);
    }

    [Fact]
    public void Walk_down_slope_adjustment_is_captured_before_tile_collision()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        tiles.Set(6, 6, SlopeTile(type: 1, slope: 1));
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate state = CreateBlueSlime() with
        {
            PositionX = 100f,
            PositionY = 82f
        };
        Assert.True(store.TrySpawn(0, in state, out NpcSnapshot spawned));
        var executor = new RuntimeNpcAiStateExecutor(store);
        var stepper = new VanillaNpcWorldMotionAiStepper(
            new GroundedVelocityStepper(2f),
            tiles);

        executor.Tick(stepper);

        Assert.True(store.TryGet(spawned.Handle, out NpcSnapshot updated));
        Assert.Equal(2f, updated.Simulation.OldVelocityX, 5);
        Assert.Equal(2.075f, updated.Simulation.OldVelocityY, 5);
        Assert.Equal(100f, updated.Simulation.OldPositionX, 5);
        Assert.Equal(82f, updated.Simulation.OldPositionY, 5);
    }

    private static NpcStateUpdate CreateDemonEye() =>
        new(
            Type: 2,
            NetId: 2,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = -1,
                NoGravity = true
            });

    private static NpcStateUpdate CreateBlueSlime() =>
        new(
            Type: 1,
            NetId: 1,
            PositionX: 100f,
            PositionY: 82f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = 1
            });

    private static WorldTile SolidTile(ushort type) =>
        new()
        {
            Type = type,
            Flags = WorldTileFlags.Active
        };

    private static WorldTile SlopeTile(ushort type, int slope) =>
        new()
        {
            Type = type,
            Flags = WorldTileFlags.Active,
            Shape = checked((byte)(slope + 1))
        };

    private sealed class FixedVelocityStepper(float velocityX, float velocityY) : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                velocityX,
                velocityY,
                npc.Target,
                npc.Ai,
                npc.Simulation with { NoGravity = true });
            return true;
        }
    }

    private sealed class FixedScaleStepper(float scale) : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                0f,
                0f,
                npc.Target,
                npc.Ai,
                npc.Simulation with
                {
                    Scale = scale,
                    NoGravity = true
                });
            return true;
        }
    }

    private sealed class GroundedVelocityStepper(float velocityX) : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                velocityX,
                0f,
                npc.Target,
                npc.Ai,
                npc.Simulation with { NoGravity = false });
            return true;
        }
    }
}
