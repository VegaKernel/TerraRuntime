using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaMoonEventEarlySpawnSelector1458Tests
{
    [Theory]
    [InlineData(true, 1, new[] { 1, 2, 340 }, 340)]
    [InlineData(true, 2, new[] { 1, 0 }, 350)]
    [InlineData(true, 4, new[] { 1, 0 }, 344)]
    [InlineData(true, 6, new[] { 1, 1, 1, 0 }, 348)]
    [InlineData(true, 6, new[] { 1, 1, 0 }, 347)]
    [InlineData(true, 7, new[] { 1, 1, 0 }, 342)]
    [InlineData(true, 8, new[] { 1, 1, 1, 0 }, 348)]
    [InlineData(true, 9, new[] { 1, 1, 1, 0 }, 348)]
    [InlineData(true, 10, new[] { 1, 1, 1, 0 }, 351)]
    [InlineData(false, 1, new[] { 309 }, 309)]
    [InlineData(false, 2, new[] { 0 }, 326)]
    [InlineData(false, 4, new[] { 0 }, 330)]
    [InlineData(false, 5, new[] { 0 }, 315)]
    public void Early_wave_selection_preserves_source_random_order(bool snow, int wave, int[] rolls, short expected)
    {
        var random = new SequenceRandom(rolls);
        Assert.Equal(expected, VanillaMoonEventEarlySpawnSelector1458.Select(snow, wave, random, static _ => 0).Value);
    }

    [Fact]
    public void Snow_special_spawn_falls_through_when_the_source_cap_is_reached()
    {
        var random = new SequenceRandom(1, 0, 0);
        Assert.Equal(350, VanillaMoonEventEarlySpawnSelector1458.Select(true, 4, random, static type => type == 344 ? 1 : 0).Value);
    }

    [Theory]
    [InlineData(false, new[] { 305 }, 305, 1)]
    [InlineData(true, new[] { 0 }, 341, 1)]
    public void Live_authority_uses_the_early_moon_selection_without_an_ordinary_fallback(
        bool snowMoon,
        int[] eventRolls,
        short expectedType,
        int expectedCount)
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        for (int x = 0; x < 500; x++)
            tiles.Tiles[tiles.GetUncheckedIndex(x, 303)] = new WorldTile { Type = 57, Flags = WorldTileFlags.Active };

        var random = new MoonSpawnRandom(eventRolls);
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 500 };
        var clock = new RuntimeWorldClock(1_000, false, default, 0, 0);
        Assert.True(snowMoon ? clock.TryStartSnowMoon() : clock.TryStartPumpkinMoon());
        var state = new ServerRuntimeState(npcs: npcs, npcAiStepper: new IdleNpcStepper(), worldTiles: tiles,
            worldClock: clock, townCommerceWorldFacts: world,
            townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458), naturalSpawnRandom: random,
            worldProgression: new RuntimeWorldProgressionMutations());
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1_458), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 300, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        var snapshots = new NpcSnapshot[npcs.Capacity];
        Assert.Equal(expectedCount, npcs.CopyActive(snapshots));
        if (expectedCount != 0)
            Assert.Equal(expectedType, snapshots[0].Type);
    }

    private sealed class SequenceRandom(params int[] values) : IVanillaNpcRandom
    {
        private int index;
        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            int value = values[index++];
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }
    }

    private sealed class MoonSpawnRandom(int[] eventRolls) : IVanillaNpcRandom
    {
        private int call;

        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            int index = call++;
            if (index == 0) { Assert.Equal((0, 20), (inclusiveMin, exclusiveMax)); return 0; }
            if (index == 1) { Assert.Equal(-84, inclusiveMin); return 70; }
            if (index == 2) { Assert.Equal(-52, inclusiveMin); return 0; }
            int eventIndex = index - 3;
            Assert.True(eventIndex < eventRolls.Length, $"Unexpected Moon-event selector roll [{inclusiveMin}, {exclusiveMax}).");
            int value = eventRolls[eventIndex];
            Assert.InRange(value, inclusiveMin, exclusiveMax - 1);
            return value;
        }

        public void AssertConsumed() => Assert.Equal(eventRolls.Length + 3, call);
    }

    private sealed class IdleNpcStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
