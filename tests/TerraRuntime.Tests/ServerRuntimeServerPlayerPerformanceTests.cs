using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeServerPlayerPerformanceTests
{
    [Fact]
    public async Task Controlled_physics_ticks_are_deterministic_and_allocation_free_after_warmup()
    {
        RuntimeFixture first = await CreateFixtureAsync("test:perf-first");
        RuntimeFixture second = await CreateFixtureAsync("test:perf-second");
        var baseline = new ServerRuntimeState(
            worldTiles: new WorldTileStore(new WorldDimensions(100, 100)));

        for (int index = 0; index < 16; index++)
        {
            first.Runtime.Tick();
            second.Runtime.Tick();
            baseline.Tick();
        }

        long baselineBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 256; index++)
            baseline.Tick();
        long baselineAllocated = GC.GetAllocatedBytesForCurrentThread() - baselineBefore;

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 256; index++)
            first.Runtime.Tick();
        long controlledAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        for (int index = 0; index < 256; index++)
            second.Runtime.Tick();

        Assert.True(
            controlledAllocated <= baselineAllocated + 4_096,
            $"Controlled ticks allocated {controlledAllocated} bytes versus {baselineAllocated} baseline bytes.");
        Assert.True(first.States.TryGet(first.Player, out PlayerStateSnapshot firstResult));
        Assert.True(second.States.TryGet(second.Player, out PlayerStateSnapshot secondResult));
        Assert.Equal(firstResult.PositionX, secondResult.PositionX);
        Assert.Equal(firstResult.PositionY, secondResult.PositionY);
        Assert.Equal(firstResult.VelocityX, secondResult.VelocityX);
        Assert.Equal(firstResult.VelocityY, secondResult.VelocityY);
        Assert.Equal(firstResult.Revision, secondResult.Revision);
    }

    private static async Task<RuntimeFixture> CreateFixtureAsync(string idValue)
    {
        var tiles = new WorldTileStore(new WorldDimensions(100, 100));
        for (int x = 0; x < 100; x++)
        {
            tiles.Set(x, 8, new WorldTile
            {
                Type = 1,
                Flags = WorldTileFlags.Active
            });
        }

        var slots = new PlayerSlotPool(1);
        var identities = new RuntimeServerPlayerSlotRegistry(slots);
        var states = new RuntimeServerPlayerStateStore(identities, slots.Capacity);
        var runtime = new ServerRuntimeState(
            worldTiles: tiles,
            serverPlayerStates: states,
            serverPlayerIdentities: identities);
        var id = new ServerPlayerId(idValue);
        var create = new TaskCompletionSource<ServerPlayerCreateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerCreateRuntimeCommand(id, 96f, 86f, create));
        ServerPlayerCreateResult created = await create.Task;
        Assert.True(created.IsCreated);
        var setIntent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new ServerPlayerMovementIntentRuntimeCommand(
            id,
            ServerPlayerMovementIntent.MoveTo(1_200f, 107f),
            setIntent));
        Assert.True(await setIntent.Task);
        return new RuntimeFixture(runtime, states, created.Player);
    }

    private sealed record RuntimeFixture(
        ServerRuntimeState Runtime,
        RuntimeServerPlayerStateStore States,
        PlayerHandle Player);
}
