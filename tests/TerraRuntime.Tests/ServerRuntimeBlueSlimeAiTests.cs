using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeBlueSlimeAiTests
{
    [Fact]
    public void World_backed_tick_runs_blue_slime_jump_gravity_and_motion_in_one_revision()
    {
        WorldTileStore tiles = CreateWorld();
        tiles.Set(6, 6, SolidTile());
        tiles.Set(7, 6, SolidTile());
        var state = new ServerRuntimeState(worldTiles: tiles);
        NpcSnapshot slime = Spawn(
            state,
            slot: 5,
            new NpcStateUpdate(
                Type: 1,
                NetId: 1,
                PositionX: 96f,
                PositionY: 78f,
                VelocityX: 0f,
                VelocityY: 0f,
                Target: VanillaNpcDefinitionCatalog.DefaultTarget,
                Ai: new NpcAiState(0f, 0f, 1f, 0f),
                Simulation: NpcSimulationState.Initial with
                {
                    DirectionX = 1,
                    DirectionY = 1
                }));

        state.Tick();

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), state.LastNpcAiTick);
        Assert.True(state.TryCaptureNpcSnapshot(slime.Handle, out NpcSnapshot updated));
        Assert.Equal(new NpcRevision(2), updated.Revision);
        Assert.Equal(2f, updated.VelocityX, 5);
        Assert.Equal(-5.925f, updated.VelocityY, 5);
        Assert.Equal(98f, updated.PositionX, 5);
        Assert.Equal(72.075f, updated.PositionY, 5);
        Assert.Equal(-1120f, updated.Ai.Ai0);
        Assert.False(updated.Simulation.NoGravity);
        Assert.Equal(2f, updated.Simulation.OldVelocityX, 5);
        Assert.Equal(-5.925f, updated.Simulation.OldVelocityY, 5);
    }

    [Fact]
    public void Blue_slime_remains_disabled_without_world_collision_context()
    {
        var state = new ServerRuntimeState();
        NpcSnapshot slime = Spawn(
            state,
            slot: 2,
            new NpcStateUpdate(
                Type: 1,
                NetId: 1,
                PositionX: 96f,
                PositionY: 78f,
                VelocityX: 0f,
                VelocityY: 0f,
                Target: VanillaNpcDefinitionCatalog.DefaultTarget,
                Ai: new NpcAiState(0f, 0f, 1f, 0f),
                Simulation: NpcSimulationState.Initial));

        state.Tick();

        Assert.Equal(new NpcAiStateTickSummary(1, 0, 0, 0), state.LastNpcAiTick);
        Assert.True(state.TryCaptureNpcSnapshot(slime.Handle, out NpcSnapshot unchanged));
        Assert.Equal(new NpcRevision(1), unchanged.Revision);
    }

    private static NpcSnapshot Spawn(ServerRuntimeState state, byte slot, NpcStateUpdate update)
    {
        var completion = new TaskCompletionSource<NpcSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        state.Apply(new NpcSpawnRuntimeCommand(slot, update, completion));
        NpcSnapshot? snapshot = completion.Task.GetAwaiter().GetResult();
        Assert.True(snapshot.HasValue);
        return snapshot.Value;
    }

    private static WorldTileStore CreateWorld() =>
        new(new WorldDimensions(4200, 1200));

    private static WorldTile SolidTile() =>
        new()
        {
            Type = 1,
            Flags = WorldTileFlags.Active
        };
}
