using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class GroundCritterHome1458Tests
{
    [Fact]
    public void Squirrel_style_change_retires_late_join_home_and_stale_neutral_commit_cannot_restore_it()
    {
        var registry = new RuntimeNpcReplicationRegistry();
        var npcs = new RuntimeNpcStore(commitSink: registry);
        var neutral = Create(299);
        Assert.True(npcs.TrySpawn(0, in neutral, out var birth));
        Assert.Single(GemBunnyWire1458Tests.Drain(GemBunnyWire1458Tests.Endpoint(registry, 9410, 0)), frame => frame[2] == 60);

        var turn = neutral with { Ai = new(1, 0, 0, 2), Simulation = birth.Simulation with {
            AiStyleOverride = new(3), Life = 25, LifeMax = 25, DamageOverride = 20, DefenseOverride = 6 } };
        Assert.True(npcs.TryUpdate(birth.Handle, in turn, out var hostile));
        Assert.Equal(birth.Handle, hostile.Handle);
        var joined = GemBunnyWire1458Tests.Drain(GemBunnyWire1458Tests.Endpoint(registry, 9411, 1));
        Assert.Single(joined, frame => frame[2] == 23);
        Assert.DoesNotContain(joined, frame => frame[2] == 60);

        registry.NpcStateCommitted(NpcStateCommitKind.Update, in birth);
        Assert.DoesNotContain(GemBunnyWire1458Tests.Drain(GemBunnyWire1458Tests.Endpoint(registry, 9412, 2)), frame => frame[2] == 60);
        Assert.True(npcs.TryGet(hostile.Handle, out var retained));
        Assert.Equal(hostile, retained);
    }

    [Fact]
    public void Turning_phase_may_offer_ai7_home_but_later_ai3_phase_does_not_offer_home_changes()
    {
        var npcs = new RuntimeNpcStore();
        Assert.True(npcs.TrySpawn(0, Create(299), out var before));
        var turning = Create(299) with { Simulation = before.Simulation with {
            AiStyleOverride = new(3), Life = 25, LifeMax = 25, HomeTileX = 41 } };
        Assert.True(RuntimeNpcReplicationRegistry.TryPrepareGemBunnyHome(in before, in turning, out var firstHome));
        Assert.NotNull(firstHome);
        Assert.Equal(60, firstHome[2]);
        Assert.True(npcs.TryUpdate(before.Handle, in turning, out var hostile));
        var later = turning with { Simulation = turning.Simulation with { HomeTileX = 42 } };
        Assert.True(RuntimeNpcReplicationRegistry.TryPrepareGemBunnyHome(in hostile, in later, out var laterHome));
        Assert.Null(laterHome);
    }

    [Fact]
    public void Existing_bunny_keeps_home_baseline_across_same_identity_updates()
    {
        var registry = new RuntimeNpcReplicationRegistry();
        var npcs = new RuntimeNpcStore(commitSink: registry);
        Assert.True(npcs.TrySpawn(0, Create(46), out var birth));
        var update = Create(46) with { PositionX = 655, Simulation = birth.Simulation };
        Assert.True(npcs.TryUpdate(birth.Handle, in update, out _));
        var joined = GemBunnyWire1458Tests.Drain(GemBunnyWire1458Tests.Endpoint(registry, 9413, 3));
        Assert.Single(joined, frame => frame[2] == 23);
        Assert.Single(joined, frame => frame[2] == 60);
    }

    private static NpcStateUpdate Create(int type) => new(type, checked((short)type), 639, 1580, 0, 0,
        255, default, NpcSimulationState.Initial with {
            HomeTileX = 40, HomeTileY = -1, Homeless = false, TownNpc = false });
}
