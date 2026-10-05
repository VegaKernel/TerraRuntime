using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class NpcDamagePrelude1458Tests
{
    [Fact]
    public void Changed_phase_owner_during_lethal_admission_retains_original_life_and_counter()
    {
        var (store, before) = Create();
        bool current = true;
        bool Admit(in NpcSnapshot pending)
        {
            Assert.Equal(0, pending.Simulation.Life);
            Assert.Equal(-7, pending.Simulation.LifeRegenCounter);
            current = false;
            return true;
        }
        var executor = new RuntimeNpcDamageExecutor(store, lethalAdmission: Admit);
        var prelude = new NpcDamagePrelude1458(before,
            before.Simulation with { Life = 1, LifeRegenCounter = -7 }, () => current, _ => { });
        var request = new NpcDamageRequest(before.Handle, DamageSource.Environment, 9999);
        Assert.False(executor.TryApplyUnpublished(in request, out _, out _, out _, out _, prelude: prelude));
        Assert.True(store.TryGet(before.Handle, out var retained));
        Assert.Equal(before, retained);
    }

    [Fact]
    public void Accepted_phase_uses_source_life_one_and_commits_counter_with_the_single_damage_revision()
    {
        var (store, before) = Create();
        int admissions = 0;
        bool Admit(in NpcSnapshot pending)
        {
            admissions++;
            Assert.Equal(0, pending.Simulation.Life);
            Assert.Equal(-7, pending.Simulation.LifeRegenCounter);
            return true;
        }
        var executor = new RuntimeNpcDamageExecutor(store, lethalAdmission: Admit);
        var prelude = new NpcDamagePrelude1458(before,
            before.Simulation with { Life = 1, LifeRegenCounter = -7 }, () => true, _ => { });
        var request = new NpcDamageRequest(before.Handle, DamageSource.Environment, 9999);
        Assert.True(executor.TryApplyUnpublished(in request, out var result, out var committed,
            out _, out _, prelude: prelude));
        Assert.True(result.Lethal);
        Assert.Equal(1, result.LifeBefore);
        Assert.Equal(0, committed.Simulation.Life);
        Assert.Equal(-7, committed.Simulation.LifeRegenCounter);
        Assert.Equal(before.Revision.Value + 1, committed.Revision.Value);
        Assert.Equal(1, admissions);
    }

    private static (RuntimeNpcStore Store, NpcSnapshot Before) Create()
    {
        var store = new RuntimeNpcStore();
        var simulation = NpcSimulationState.Initial with { Life = 8, LifeMax = 25, LifeRegenCounter = -119 };
        Assert.True(store.TrySpawn(0, new(1, 1, 800, 800, 0, 0, 255, default, simulation), out var before));
        return (store, before);
    }
}
