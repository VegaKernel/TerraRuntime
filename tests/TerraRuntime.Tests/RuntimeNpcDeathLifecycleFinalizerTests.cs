using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcDeathLifecycleFinalizerTests
{
    [Fact]
    public void Dead_eye_without_imported_loot_completes_boss_lifecycle()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcSnapshot eye = Spawn(store, 0, VanillaNpcIds.EyeOfCthulhu);
        Kill(store, eye.Handle);
        Assert.True(store.TryGet(eye.Handle, out NpcSnapshot dead));
        Assert.Equal(0, dead.Simulation.Life);
        var finalizer = new RuntimeNpcDeathLifecycleFinalizer(store);

        Assert.True(finalizer.TryFinalizeWhenLootUnsupported(
            eye.Handle,
            out NpcDeathLifecycleResult result));

        Assert.True(result.IsValid);
        Assert.True(result.WasBoss);
        Assert.Equal(eye.Handle, result.Target);
        Assert.Equal(new NpcRevision(2), result.FinalRevision);
        Assert.Equal(VanillaNpcIds.EyeOfCthulhu, result.Type);
        Assert.Equal(NpcArchetypeRole.Boss, result.Role);
        Assert.False(store.TryGet(eye.Handle, out _));
    }

    [Fact]
    public void Imported_loot_table_cannot_be_bypassed_by_lifecycle_fallback()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot slime = Spawn(store, 0, VanillaNpcIds.BlueSlime);
        Kill(store, slime.Handle);
        var finalizer = new RuntimeNpcDeathLifecycleFinalizer(store);

        Assert.False(finalizer.TryFinalizeWhenLootUnsupported(slime.Handle, out _));

        Assert.True(store.TryGet(slime.Handle, out NpcSnapshot current));
        Assert.Equal(0, current.Simulation.Life);
    }

    [Fact]
    public void Live_eye_is_not_finalized()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot eye = Spawn(store, 0, VanillaNpcIds.EyeOfCthulhu);
        var finalizer = new RuntimeNpcDeathLifecycleFinalizer(store);

        Assert.False(finalizer.TryFinalizeWhenLootUnsupported(eye.Handle, out _));
        Assert.True(store.TryGet(eye.Handle, out NpcSnapshot current));
        Assert.True(current.Simulation.Life > 0);
    }

    [Fact]
    public void Stale_generation_cannot_finalize_dead_replacement()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot stale = Spawn(store, 0, VanillaNpcIds.EyeOfCthulhu);
        Assert.True(store.TryDespawn(stale.Handle));
        NpcSnapshot replacement = Spawn(store, 0, VanillaNpcIds.EyeOfCthulhu);
        Kill(store, replacement.Handle);
        var finalizer = new RuntimeNpcDeathLifecycleFinalizer(store);

        Assert.False(finalizer.TryFinalizeWhenLootUnsupported(stale.Handle, out _));

        Assert.True(store.TryGet(replacement.Handle, out NpcSnapshot current));
        Assert.Equal(0, current.Simulation.Life);
        Assert.NotEqual(stale.Handle.Generation, replacement.Handle.Generation);
    }

    [Fact]
    public void Successful_fallback_is_exactly_once_for_one_generation()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot eye = Spawn(store, 0, VanillaNpcIds.EyeOfCthulhu);
        Kill(store, eye.Handle);
        var finalizer = new RuntimeNpcDeathLifecycleFinalizer(store);

        Assert.True(finalizer.TryFinalizeWhenLootUnsupported(eye.Handle, out _));
        Assert.False(finalizer.TryFinalizeWhenLootUnsupported(eye.Handle, out _));
    }

    private static NpcSnapshot Spawn(RuntimeNpcStore store, byte slot, NpcTypeId type)
    {
        var update = new NpcStateUpdate(
            Type: type.Value,
            NetId: checked((short)type.Value),
            PositionX: 48f,
            PositionY: 64f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(slot, in update, out NpcSnapshot npc));
        return npc;
    }

    private static void Kill(RuntimeNpcStore store, NpcHandle target)
    {
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(
            target,
            DamageSource.Environment,
            BaseDamage: int.MaxValue);
        Assert.True(executor.TryApply(in request, out NpcDamageResult result));
        Assert.True(result.Lethal);
    }
}
