using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class VanillaTownNpcRescue1458Tests
{
    [Fact]
    public void Talk_rescue_catalog_matches_pinned_1458_transform_pairs()
    {
        int[] expectedBound = [589, 105, 106, 123, 354, 376, 579];
        int[] expectedTown = [588, 107, 108, 124, 353, 369, 550];
        VanillaTownNpcRescueRule1458[] talk = VanillaTownNpcRescue1458.All
            .ToArray()
            .Where(static r => r.Trigger == VanillaTownNpcRescueTrigger1458.Talk)
            .ToArray();
        Assert.Equal(expectedBound.Order(), talk.Select(static r => r.BoundType.Value).Order());
        Assert.Equal(expectedTown.Order(), talk.Select(static r => r.ResidentType.Value).Order());
        Assert.All(talk, static rule => Assert.True(rule.IsValid));
    }

    [Fact]
    public void Runtime_rescue_preserves_bottom_repositions_and_journals_saved_fact()
    {
        var npcs = new RuntimeNpcStore();
        var town = new RuntimeTownNpcStateStore(
            new WorldNpcPersistence([], [], []),
            [],
            new WorldDimensions(200, 200));
        var progression = new RuntimeWorldProgressionMutations();
        var service = new RuntimeTownNpcRescueService1458(npcs, town, progression);
        NpcSimulationState sim = NpcSimulationState.Initial with { Life = 125, LifeMax = 250 };
        Assert.True(npcs.TrySpawn(3, new NpcStateUpdate(105, 105, 100f, 200f, 0f, 0f, 255, default, sim), out NpcSnapshot before));

        Assert.True(service.TryRescueTalk(3, out NpcSnapshot after));
        Assert.Equal(VanillaNpcIds.GoblinTinkerer.Value, after.Type);
        Assert.Equal(before.PositionY + 34f - 40f, after.PositionY);
        Assert.Equal(125, after.Simulation.Life);
        Assert.True(town.ContainsNpcType(VanillaNpcIds.GoblinTinkerer));
        Assert.Equal(RuntimeTownRescueFacts1458.Goblin, progression.CaptureSnapshot().RescuedTownNpcs);
    }
}
