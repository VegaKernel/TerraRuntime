using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class NpcSourceHomeOwnership1458Tests
{
    private static readonly byte[] AirborneTaxHome = Convert.FromHexString("0a003c0000ffffffff01");

    [Fact]
    public void Silent_join_baseline_does_not_acknowledge_the_source_old_home()
    {
        var (registry, _, before) = CreateActor();
        var home = Home();
        registry.ConfigureTownHomeBaselines([home]);
        AssertHistory(registry, in before, -1, -1, false);
        var final = State(in before);
        Assert.True(registry.TryPrepareSourceTownHome(in before, in final, in home, false, out var plan));
        Assert.Equal(AirborneTaxHome, plan.LateFrame);
        AssertHistory(registry, in before, -1, -1, false);
    }

    [Fact]
    public void An_actual_home_call_invalidates_an_earlier_prepared_comparison()
    {
        var (registry, store, before) = CreateActor();
        var home = Home(); var final = State(in before);
        Assert.True(registry.TryPrepareSourceTownHome(in before, in final, in home, false, out var plan));
        Assert.True(registry.TryAdoptSourceHomeUpdate(in before, AirborneTaxHome));
        Assert.False(registry.IsSourceTownHomeCurrent(in plan));
        Assert.True(store.TryUpdateUnpublished(before.Handle, in final, out var accepted));
        Assert.False(registry.TryAdoptSourceTownHome(in plan, in accepted));
        AssertHistory(registry, in accepted, -1, -1, true);
    }

    [Fact]
    public void A_home_API_call_acknowledges_even_equal_arguments_and_the_next_phase_is_quiet()
    {
        var (registry, store, before) = CreateActor();
        var home = Home(); var final = State(in before);
        Assert.True(registry.TryPrepareSourceTownHome(in before, in final, in home, true, out var plan));
        Assert.Null(plan.LateFrame);
        Assert.True(store.TryUpdateUnpublished(before.Handle, in final, out var accepted));
        Assert.True(registry.TryAdoptSourceTownHome(in plan, in accepted));
        registry.NpcStateCommitted(NpcStateCommitKind.Update, in accepted);
        AssertHistory(registry, in accepted, -1, -1, true);
        var next = State(in accepted);
        Assert.True(registry.TryPrepareSourceTownHome(in accepted, in next, in home, false, out var nextPlan));
        Assert.Null(nextPlan.LateFrame);
    }

    [Fact]
    public void Reusing_the_slot_retires_the_old_plan_and_resets_only_the_new_constructor_history()
    {
        var (registry, store, before) = CreateActor();
        var home = Home(); var final = State(in before);
        Assert.True(registry.TryPrepareSourceTownHome(in before, in final, in home, false, out var plan));
        Assert.True(store.TryDespawn(before.Handle));
        Assert.True(store.TrySpawn(0, in final, out var replacement));
        Assert.NotEqual(before.Handle, replacement.Handle);
        Assert.False(registry.IsSourceTownHomeCurrent(in plan));
        Assert.False(registry.TryAdoptSourceTownHome(in plan, in replacement));
        Assert.False(registry.TryAdoptSourceHomeUpdate(in before, AirborneTaxHome));
        AssertHistory(registry, in replacement, -1, -1, false);
    }

    private static (RuntimeNpcReplicationRegistry Registry, RuntimeNpcStore Store, NpcSnapshot Actor) CreateActor()
    {
        var registry = new RuntimeNpcReplicationRegistry();
        var store = new RuntimeNpcStore(commitSink: registry);
        var input = new NpcStateUpdate(441, 441, 1023f, 1560f, .75f, -.4f, 0, default,
            NpcSimulationState.Initial with { TownNpc = true, Homeless = true, HomeTileX = -1, HomeTileY = -1 });
        Assert.True(store.TrySpawn(0, in input, out var actor));
        return (registry, store, actor);
    }

    private static RuntimeTownNpcHomeCommit Home() => new(0, VanillaNpcIds.TaxCollector, -1, -1, TerrariaNpcHomeStatus.Homeless);

    private static NpcStateUpdate State(in NpcSnapshot npc) => new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY,
        npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);

    private static void AssertHistory(RuntimeNpcReplicationRegistry registry, in NpcSnapshot npc, int x, int y, bool homeless)
    {
        Assert.True(registry.TryCaptureSourceTownHomeHistory(in npc, out int oldX, out int oldY, out bool oldHomeless));
        Assert.Equal(x, oldX); Assert.Equal(y, oldY); Assert.Equal(homeless, oldHomeless);
    }
}
