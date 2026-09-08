using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeUnderworldSpawn1458Tests
{
    [Theory]
    [InlineData(59, new[] { 1, 1, 1, 1, 0 })]
    [InlineData(60, new[] { 1, 1, 1, 1, 1 })]
    [InlineData(0, new[] { 1, 1, 0 })] // Fire Imp has defaults, but no admitted coverage entry yet.
    [InlineData(0, new[] { 0 })] // Unadmitted multi-actor bait: no substitution.
    [InlineData(0, new[] { 1, 1, 1, 0, 1 })] // Demon AI is not admitted: no substitution.
    public void Real_world_tick_uses_hell_branch_and_preserves_ai_admission(int expected, int[] selection)
        => AssertSpawn(expected, selection);

    [Fact]
    public void Live_hardmode_and_mechanical_progression_unlock_lava_bat_without_reloading_metadata()
        => AssertSpawn(151, [1, 1, 1, 1, 1, 1, 1], liveHardmode: true);

    [Fact]
    public void Missing_world_facts_do_not_fall_back_to_cave_slime_in_hell()
        => AssertSpawn(0, [], knownFacts: false);

    private static void AssertSpawn(int expected, int[] selection, bool liveHardmode = false, bool knownFacts = true)
    {
        var npcs = new RuntimeNpcStore();
        var tiles = new WorldTileStore(new WorldDimensions(500, 1200));
        for (int x = 0; x < 500; x++)
            tiles.Tiles[tiles.GetUncheckedIndex(x, 1050)] = new WorldTile { Type = 57, Flags = WorldTileFlags.Active };
        var random = new SpawnRandom(selection, liveHardmode ? 540 : 600);
        var progression = new RuntimeWorldProgressionMutations();
        RuntimeTownCommerceWorldFacts1458 world = default;
        world = world with { WorldSurface = 350, RockLayer = 500 };
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles,
            worldClock: new RuntimeWorldClock(1000, false, default, 0, 0),
            townCommerceWorldFacts: knownFacts ? world : null, townSpawnWorldFacts: default(VanillaTownSpawnWorldFacts1458),
            naturalSpawnRandom: random, worldProgression: progression);
        if (liveHardmode)
        {
            progression.MarkCompleted(VanillaWorldProgressionId.Hardmode);
            progression.MarkCompleted(VanillaWorldProgressionId.AnyMechanicalBoss);
        }
        var slots = new PlayerSlotPool(1);
        Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(811), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session,
            new PlayerSpawnCommitRequest(session.Handle.Slot, 200, 1048, 0, 0, 0, 0, 0)));

        state.Tick();

        random.AssertConsumed();
        var snapshots = new NpcSnapshot[npcs.Capacity];
        int count = npcs.CopyActive(snapshots);
        Assert.Equal(expected == 0 ? 0 : 1, count);
        if (count != 0) Assert.Equal(expected, snapshots[0].Type);
    }

    private sealed class SpawnRandom(int[] selection, int rate) : IVanillaNpcRandom
    {
        private int call;
        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            int index = call++;
            if (index == 0) { Assert.Equal(rate, exclusiveMax); return 0; }
            if (index == 1) { Assert.Equal(-84, inclusiveMin); return 70; }
            if (index == 2) { Assert.Equal(-52, inclusiveMin); return 0; }
            Assert.True(index - 3 < selection.Length, "Unexpected reroll/substitute after underworld selection.");
            return selection[index - 3];
        }
        public void AssertConsumed() => Assert.Equal(selection.Length + 3, call);
    }
}
