using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeVanillaNpcRoleBoundaryTests
{
    [Fact]
    public void Eye_of_cthulhu_live_generation_selects_boss_lifecycle()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcSnapshot eye = Spawn(store, 0, VanillaNpcIds.EyeOfCthulhu);
        var boundary = new RuntimeVanillaNpcRoleBoundary(store);

        Assert.True(boundary.TryClassify(eye.Handle, out VanillaNpcRoleClassification classification));

        Assert.True(classification.IsValid);
        Assert.Equal(eye.Handle, classification.Npc);
        Assert.Equal(VanillaNpcIds.EyeOfCthulhu, classification.Type);
        Assert.Equal(NpcArchetypeRole.Boss, classification.Role);
        Assert.True(classification.RequiresBossLifecycle);
        Assert.False(classification.AllowsTownInteraction);
        Assert.False(classification.UsesOrdinaryLifecycle);
    }

    [Fact]
    public void Blue_slime_live_generation_selects_ordinary_lifecycle()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot slime = Spawn(store, 0, VanillaNpcIds.BlueSlime);
        var boundary = new RuntimeVanillaNpcRoleBoundary(store);

        Assert.True(boundary.TryClassify(slime.Handle, out VanillaNpcRoleClassification classification));

        Assert.Equal(NpcArchetypeRole.Ordinary, classification.Role);
        Assert.True(classification.UsesOrdinaryLifecycle);
        Assert.False(classification.RequiresBossLifecycle);
    }

    [Fact]
    public void Stale_generation_cannot_inherit_replacement_vanilla_role()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot eye = Spawn(store, 0, VanillaNpcIds.EyeOfCthulhu);
        var boundary = new RuntimeVanillaNpcRoleBoundary(store);
        Assert.True(store.TryDespawn(eye.Handle));
        NpcSnapshot replacement = Spawn(store, 0, VanillaNpcIds.BlueSlime);

        Assert.False(boundary.TryClassify(eye.Handle, out _));
        Assert.True(boundary.TryClassify(replacement.Handle, out VanillaNpcRoleClassification current));
        Assert.Equal(NpcArchetypeRole.Ordinary, current.Role);
        Assert.NotEqual(eye.Handle.Generation, replacement.Handle.Generation);
    }

    [Fact]
    public void Unsupported_vanilla_type_fails_closed()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        var update = new NpcStateUpdate(
            Type: 999,
            NetId: 999,
            PositionX: 0f,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot npc));
        var boundary = new RuntimeVanillaNpcRoleBoundary(store);

        Assert.False(boundary.TryClassify(npc.Handle, out _));
    }

    private static NpcSnapshot Spawn(RuntimeNpcStore store, byte slot, NpcTypeId type)
    {
        var update = new NpcStateUpdate(
            Type: type.Value,
            NetId: checked((short)type.Value),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(slot, in update, out NpcSnapshot npc));
        return npc;
    }
}
