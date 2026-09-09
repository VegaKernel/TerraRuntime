using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.HostContracts;
using TerraRuntime.World;
using TerraRuntime.Core.Players;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace TerraRuntime.Tests;

[Collection(SteadyStateAllocationCollection.Name)]
public sealed class ServerRuntimeServerPlayerPerformanceTests
{
    [Fact]
    public void Empty_actor_and_shop_tick_path_stays_below_four_bytes_per_tick_after_warmup()
    {
        var runtime = new ServerRuntimeState();
        // This is a steady-state gate, not a JIT-startup gate. A single short batch can finish before
        // CoreCLR's delayed tier promotion. Exercise the same loop throughout a bounded warmup window;
        // never retry/filter measured allocation samples or disable tiering for the suite.
        TimeSpan warmupWindow = TimeSpan.FromMilliseconds(500);
        long warmupStart = Stopwatch.GetTimestamp();
        do
        {
            RunTicks(runtime, 4_096);
        }
        while (Stopwatch.GetElapsedTime(warmupStart) < warmupWindow);

        int gen0Before = GC.CollectionCount(0), gen1Before = GC.CollectionCount(1), gen2Before = GC.CollectionCount(2);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        RunTicks(runtime, 1_024);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.True(
            allocated <= 1_024L * 4L,
            $"Empty actor/shop ticks allocated {allocated} bytes; the gate is 4 bytes per tick. " +
            $"Collections during measurement: {GC.CollectionCount(0) - gen0Before}/" +
            $"{GC.CollectionCount(1) - gen1Before}/{GC.CollectionCount(2) - gen2Before}.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunTicks(ServerRuntimeState runtime, int count)
    {
        for (int index = 0; index < count; index++)
            runtime.Tick();
    }

    [Fact]
    public async Task Controlled_physics_ticks_are_deterministic_and_stay_within_allocation_gate()
    {
        RuntimeFixture first = await CreateFixtureAsync("test:perf-first");
        RuntimeFixture second = await CreateFixtureAsync("test:perf-second");
        for (int index = 0; index < 16; index++)
        {
            first.Runtime.Tick();
            second.Runtime.Tick();
        }

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 256; index++)
            first.Runtime.Tick();
        long controlledAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        for (int index = 0; index < 256; index++)
            second.Runtime.Tick();

        // The first ArrayPool rent for the 2,500-cell liquid scratch may allocate one bucket array. Its object
        // header is 24 bytes on the test runtime; keep 64 bytes of one-time headroom while retaining the 3 KiB
        // per-tick payload gate.
        const long oneTimePooledScratchHeaderAllowance = 64;
        Assert.True(
            controlledAllocated <= 256L * 3_072L + oneTimePooledScratchHeaderAllowance,
            $"Controlled ticks allocated {controlledAllocated} bytes; the gate is 3 KiB per tick plus one pooled-array header.");
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
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, slots.Capacity);
        var runtime = new ServerRuntimeState(
            worldTiles: tiles,
            serverPlayers: new ServerPlayerAuthority(states, identities, tiles));
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
        ServerPlayerStateStore States,
        PlayerHandle Player);
}
