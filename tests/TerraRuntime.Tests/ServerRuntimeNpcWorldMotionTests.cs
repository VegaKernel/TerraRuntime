using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeNpcWorldMotionTests
{
    [Fact]
    public async Task Authoritative_tick_applies_targeting_ai_and_world_motion_in_one_revision()
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        var state = new ServerRuntimeState(worldTiles: tiles);
        var completion = new TaskCompletionSource<NpcSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var spawn = new NpcStateUpdate(
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
                DirectionY = -1
            });

        state.Apply(new NpcSpawnRuntimeCommand(5, spawn, completion));
        NpcSnapshot? createdValue = await completion.Task;
        Assert.True(createdValue.HasValue);
        NpcSnapshot created = createdValue.Value;

        state.Tick();

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), state.LastNpcAiTick);
        Assert.True(state.TryCaptureNpcSnapshot(created.Handle, out NpcSnapshot updated));
        Assert.Equal(new NpcRevision(2), updated.Revision);
        Assert.Equal(100.1f, updated.PositionX, 5);
        Assert.Equal(199.96f, updated.PositionY, 5);
        Assert.Equal(0.1f, updated.VelocityX, 5);
        Assert.Equal(-0.04f, updated.VelocityY, 5);
        Assert.True(updated.Simulation.NoGravity);
        Assert.False(updated.Simulation.CollideX);
        Assert.False(updated.Simulation.CollideY);
    }
}
