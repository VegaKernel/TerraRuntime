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
    [InlineData(true, 11, new[] { 1, 1, 0 }, 352)]
    [InlineData(true, 12, new[] { 1, 1, 1, 1, 0 }, 342)]
    [InlineData(true, 13, new[] { 1, 1, 1, 0 }, 352)]
    [InlineData(true, 15, new[] { 1, 1, 1, 1, 1 }, 343)]
    [InlineData(true, 16, new[] { 1, 1, 1, 1, 0 }, 352)]
    [InlineData(true, 17, new[] { 1, 1, 1, 1, 1, 0 }, 351)]
    [InlineData(true, 18, new[] { 1, 1, 1, 1, 0 }, 348)]
    [InlineData(true, 19, new[] { 1, 1, 1, 1 }, 343)]
    [InlineData(true, 20, new[] { 1, 2 }, 344)]
    [InlineData(false, 1, new[] { 309 }, 309)]
    [InlineData(false, 2, new[] { 0 }, 326)]
    [InlineData(false, 4, new[] { 0 }, 330)]
    [InlineData(false, 5, new[] { 0 }, 315)]
    [InlineData(false, 6, new[] { 1, 0 }, 326)]
    [InlineData(false, 7, new[] { 1, 0 }, 330)]
    [InlineData(false, 8, new[] { 1, 0 }, 330)]
    [InlineData(false, 9, new[] { 1, 1, 1, 0 }, 326)]
    [InlineData(false, 10, new[] { 1, 0 }, 329)]
    [InlineData(false, 11, new[] { 1, 0 }, 330)]
    [InlineData(false, 12, new[] { 0 }, 327)]
    [InlineData(false, 13, new[] { 1, 1, 0 }, 330)]
    public void Early_wave_selection_preserves_source_random_order(bool snow, int wave, int[] rolls, short expected)
    {
        var random = new SequenceRandom(rolls);
        Assert.Equal(expected, VanillaMoonEventEarlySpawnSelector1458.Select(snow, wave, random, static _ => 0).GetValueOrDefault().Value);
    }

    [Fact]
    public void Snow_special_spawn_falls_through_when_the_source_cap_is_reached()
    {
        var random = new SequenceRandom(1, 0, 0);
        Assert.Equal(350, VanillaMoonEventEarlySpawnSelector1458.Select(true, 4, random, static type => type == 344 ? 1 : 0).GetValueOrDefault().Value);
    }

    [Fact]
    public void Snow_later_wave_cap_falls_through_in_source_order()
    {
        var random = new SequenceRandom(1, 0, 0);
        Assert.Equal(352, VanillaMoonEventEarlySpawnSelector1458.Select(true, 11, random, static type => type == 345 ? 1 : 0).GetValueOrDefault().Value);
    }

    [Fact]
    public void Snow_wave_fourteen_preserves_the_source_no_spawn_branch()
    {
        var random = new SequenceRandom(1, 1, 1, 1, 1);
        Assert.Null(VanillaMoonEventEarlySpawnSelector1458.Select(true, 14, random, static _ => 0));
    }

    [Fact]
    public void Snow_wave_twenty_consumes_its_choice_when_the_boss_cap_is_reached()
    {
        var random = new SequenceRandom(1, 2);
        Assert.Null(VanillaMoonEventEarlySpawnSelector1458.Select(true, 20, random, static _ => 0, reachedInvasionBossCap: true));
    }

    [Fact]
    public void Pumpkin_wave_fourteen_retains_both_source_spawn_intents()
    {
        var random = new SequenceRandom(0, 1, 1, 1, 1, 1, 305);
        var selection = VanillaMoonEventEarlySpawnSelector1458.SelectPlan(false, 14, random, static _ => 0);
        Assert.Equal(327, selection.First.GetValueOrDefault().Value);
        Assert.Equal(305, selection.Second.GetValueOrDefault().Value);
    }

    [Fact]
    public void Pumpkin_wave_eighteen_can_select_two_boss_intents()
    {
        var random = new SequenceRandom(0, 0);
        var selection = VanillaMoonEventEarlySpawnSelector1458.SelectPlan(false, 18, random, static _ => 0);
        Assert.Equal(327, selection.First.GetValueOrDefault().Value);
        Assert.Equal(325, selection.Second.GetValueOrDefault().Value);
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
