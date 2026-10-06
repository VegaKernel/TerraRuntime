using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class GuardOnlyPreludeTests
{
    [Fact]
    public void Null_override_keeps_normal_life_counter_and_single_revision_without_publication()
    {
        var (store, before) = Create();
        int publications = 0;
        var prelude = new NpcDamagePrelude1458(before, null, () => true, _ => publications++);
        var request = new NpcDamageRequest(before.Handle, DamageSource.Environment, 3);
        var executor = new RuntimeNpcDamageExecutor(store);
        Assert.True(executor.TryApplyUnpublished(in request, out var result, out var after,
            out _, out _, prelude: prelude));
        Assert.Equal(8, result.LifeBefore);
        Assert.Equal(6, after.Simulation.Life);
        Assert.Equal(-119, after.Simulation.LifeRegenCounter);
        Assert.Equal(before.Revision.Value + 1, after.Revision.Value);
        Assert.Equal(0, publications);
    }

    [Fact]
    public void Null_override_rechecks_dependencies_after_lethal_admission()
    {
        var (store, before) = Create();
        bool current = true;
        bool Admit(in NpcSnapshot pending)
        {
            Assert.Equal(0, pending.Simulation.Life);
            Assert.Equal(-119, pending.Simulation.LifeRegenCounter);
            current = false;
            return true;
        }
        var executor = new RuntimeNpcDamageExecutor(store, lethalAdmission: Admit);
        var prelude = new NpcDamagePrelude1458(before, null, () => current, _ => { });
        var request = new NpcDamageRequest(before.Handle, DamageSource.Environment, 9999);
        Assert.False(executor.TryApplyUnpublished(in request, out _, out _, out _, out _, prelude: prelude));
        Assert.True(store.TryGet(before.Handle, out var retained));
        Assert.Equal(before, retained);
    }

    [Fact]
    public void Callback_reentry_preserves_independent_NPC_change_and_refuses_stale_override()
    {
        var (store, before) = Create();
        int captures = 0;
        NpcSnapshot changed = default;
        bool Current()
        {
            if (++captures == 2)
            {
                var update = new NpcStateUpdate(before.Type, before.NetId, before.PositionX,
                    before.PositionY, before.VelocityX, before.VelocityY, before.Target, before.Ai,
                    before.Simulation with { Life = 6 });
                Assert.True(store.TryUpdate(before.Handle, in update, out changed));
            }
            return true;
        }
        var prelude = new NpcDamagePrelude1458(before, null, Current, _ => { });
        var request = new NpcDamageRequest(before.Handle, DamageSource.Environment, 3);
        var executor = new RuntimeNpcDamageExecutor(store);
        Assert.False(executor.TryApplyUnpublished(in request, out _, out _, out _, out _, prelude: prelude));
        Assert.True(store.TryGet(before.Handle, out var retained));
        Assert.Equal(changed, retained);
        Assert.Equal(6, retained.Simulation.Life);
    }

    [Fact]
    public void DOT_override_keeps_Life_one_LifeMax_and_state_validity_fences()
    {
        foreach (var simulation in new[]
        {
            NpcSimulationState.Initial with { Life = 2, LifeMax = 25 },
            NpcSimulationState.Initial with { Life = 1, LifeMax = 26 },
            NpcSimulationState.Initial with { Life = 1, LifeMax = 25, Rotation = float.NaN },
        })
        {
            var (store, before) = Create();
            var executor = new RuntimeNpcDamageExecutor(store);
            var prelude = new NpcDamagePrelude1458(before, simulation, () => true, _ => { });
            var request = new NpcDamageRequest(before.Handle, DamageSource.Environment, 9999);
            Assert.False(executor.TryApplyUnpublished(in request, out _, out _, out _, out _, prelude: prelude));
            Assert.True(store.TryGet(before.Handle, out var retained));
            Assert.Equal(before, retained);
        }
    }

    [Fact]
    public void Guard_only_does_not_admit_stale_Before_or_shared_life_owner()
    {
        foreach (bool stale in new[] { false, true })
        {
            var (store, before) = Create();
            var prelude = new NpcDamagePrelude1458(stale ? before with { PositionX = 900 } : before,
                null, () => true, _ => { });
            var request = new NpcDamageRequest(before.Handle, DamageSource.Environment, 3);
            var executor = new RuntimeNpcDamageExecutor(store);
            Assert.False(executor.TryApplyUnpublished(in request, out _, out _, out _, out _,
                sharedLifeOwner: stale ? null : before, prelude: prelude));
            Assert.True(store.TryGet(before.Handle, out var retained));
            Assert.Equal(before, retained);
        }
    }

    private static (RuntimeNpcStore Store, NpcSnapshot Before) Create()
    {
        var store = new RuntimeNpcStore();
        var simulation = NpcSimulationState.Initial with { Life = 8, LifeMax = 25, LifeRegenCounter = -119 };
        Assert.True(store.TrySpawn(0, new(1, 1, 800, 800, 0, 0, 255, default, simulation), out var before));
        return (store, before);
    }
}
