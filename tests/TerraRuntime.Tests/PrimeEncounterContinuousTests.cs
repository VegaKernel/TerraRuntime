using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

/// <summary>Continuous source trace for the first authoritative Skeletron Prime encounter tick.</summary>
public sealed class PrimeEncounterContinuousTests
{
    [Fact]
    public void First_encounter_tick_updates_head_and_all_spawned_arms_in_slot_order()
    {
        var npcs = new RuntimeNpcStore();
        npcs.SetVanillaSpawnContextSource(() => new VanillaNpcSpawnContext(1, 1, false));
        Assert.True(npcs.TrySpawnIntent(new NpcAiSpawnIntent(
            VanillaNpcIds.SkeletronPrime, 1000, 1000, 0f, 0f, 0)
        {
            StartSlot = 0,
            InitialAi = new NpcAiState(0f, 0f, 599f, 0f)
        }, out _));

        var targeting = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper());
        targeting.SetWorldConditions(dayTime: false, slimeRainActive: false);
        targeting.SetCandidates([new VanillaNpcTargetCandidate(0, 1510f, 1021f, 0, true, false, false, false)]);
        var motion = new VanillaNpcWorldMotionAiStepper(
            targeting, new WorldTileStore(new WorldDimensions(400, 400)));

        Assert.Equal(5, new RuntimeNpcAiStateExecutor(npcs).Tick(motion).Applied);

        AssertNpc(npcs, 0, VanillaNpcIds.SkeletronPrime, 960.1f, 897.9f, 0.1f, -0.1f,
            1, 0f, new(1f, 1f, 0f, 0f), new(0f, 0f, 0f, 0f));
        AssertNpc(npcs, 1, VanillaNpcIds.PrimeCannon, 974.1f, 896.93f, 0.1f, -0.07f,
            0, 2.3672495f, new(-1f, 0f, 0f, 0f), new(3f, 0f, 0f, 0f));
        AssertNpc(npcs, 2, VanillaNpcIds.PrimeSaw, 974.05f, 897.05f, 0.05f, 0.05f,
            1, 3.9138436f, new(1f, 0f, 0f, 1f), new(0f, 0f, 0f, 0f));
        AssertNpc(npcs, 3, VanillaNpcIds.PrimeVice, 985.7844f, 899.26447f, 11.784408f, 2.2644548f,
            1, 2.3672495f, new(-1f, 0f, 0f, 151f), new(0f, 0f, 0f, 0f));
        AssertNpc(npcs, 4, VanillaNpcIds.PrimeLaser, 973.9f, 896.93f, -0.1f, -0.07f,
            1, -1.3801572f, new(1f, 0f, 0f, 150f), new(4f, 0f, 0f, 0f));
    }

    private static void AssertNpc(RuntimeNpcStore npcs, byte slot, NpcTypeId type, float x, float y, float vx, float vy,
        int direction, float rotation, NpcAiState ai, NpcAiState localAi)
    {
        Assert.True(npcs.TryGetActive(slot, out var actual));
        Assert.Equal(type.Value, actual.Type);
        Assert.Equal(x, actual.PositionX); Assert.Equal(y, actual.PositionY);
        Assert.Equal(vx, actual.VelocityX); Assert.Equal(vy, actual.VelocityY);
        Assert.Equal(direction, actual.Simulation.DirectionX); Assert.Equal(rotation, actual.Simulation.Rotation);
        Assert.Equal(ai, actual.Ai); Assert.Equal(localAi, actual.Simulation.LocalAi);
    }
}
