using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileWireIdentityRegistryTests
{
    [Fact]
    public void Exact_wire_key_round_trips_to_generation_safe_handle()
    {
        var identities = new RuntimeProjectileWireIdentityRegistry();
        TerrariaProjectileKeyState key = Key(spawner: 4, index: 12, generation: 7);
        ProjectileHandle handle = Handle(slot: 3, generation: 9);

        Assert.True(identities.TryBind(in key, handle));

        Assert.True(identities.TryResolve(in key, out ProjectileHandle resolved));
        Assert.Equal(handle, resolved);
        Assert.True(identities.TryGetWireKey(handle, out TerrariaProjectileKeyState reverse));
        Assert.Equal(key, reverse);
    }

    [Fact]
    public void Same_wire_index_from_different_spawners_does_not_alias()
    {
        var identities = new RuntimeProjectileWireIdentityRegistry();
        TerrariaProjectileKeyState firstKey = Key(spawner: 4, index: 12, generation: 1);
        TerrariaProjectileKeyState secondKey = Key(spawner: 5, index: 12, generation: 1);
        ProjectileHandle first = Handle(slot: 2, generation: 1);
        ProjectileHandle second = Handle(slot: 3, generation: 1);

        Assert.True(identities.TryBind(in firstKey, first));
        Assert.True(identities.TryBind(in secondKey, second));

        Assert.True(identities.TryResolve(in firstKey, out ProjectileHandle resolvedFirst));
        Assert.Equal(first, resolvedFirst);
        Assert.True(identities.TryResolve(in secondKey, out ProjectileHandle resolvedSecond));
        Assert.Equal(second, resolvedSecond);
    }

    [Fact]
    public void New_wire_generation_shadows_forward_lookup_but_old_live_handle_keeps_reverse_key()
    {
        var identities = new RuntimeProjectileWireIdentityRegistry();
        TerrariaProjectileKeyState oldKey = Key(spawner: 4, index: 12, generation: 1);
        TerrariaProjectileKeyState newKey = Key(spawner: 4, index: 12, generation: 2);
        ProjectileHandle oldHandle = Handle(slot: 2, generation: 1);
        ProjectileHandle newHandle = Handle(slot: 3, generation: 1);

        Assert.True(identities.TryBind(in oldKey, oldHandle));
        Assert.True(identities.TryBind(in newKey, newHandle));

        Assert.False(identities.TryResolve(in oldKey, out _));
        Assert.True(identities.TryResolve(in newKey, out ProjectileHandle resolved));
        Assert.Equal(newHandle, resolved);
        Assert.True(identities.TryGetWireKey(oldHandle, out TerrariaProjectileKeyState retainedOldKey));
        Assert.Equal(oldKey, retainedOldKey);
        Assert.True(identities.TryGetWireKey(newHandle, out TerrariaProjectileKeyState retainedNewKey));
        Assert.Equal(newKey, retainedNewKey);
    }

    [Fact]
    public void Unbinding_shadowed_old_handle_does_not_clear_newer_forward_mapping()
    {
        var identities = new RuntimeProjectileWireIdentityRegistry();
        TerrariaProjectileKeyState oldKey = Key(spawner: 4, index: 12, generation: 1);
        TerrariaProjectileKeyState newKey = Key(spawner: 4, index: 12, generation: 2);
        ProjectileHandle oldHandle = Handle(slot: 2, generation: 1);
        ProjectileHandle newHandle = Handle(slot: 3, generation: 1);
        Assert.True(identities.TryBind(in oldKey, oldHandle));
        Assert.True(identities.TryBind(in newKey, newHandle));

        Assert.True(identities.TryUnbind(oldHandle, out TerrariaProjectileKeyState removed));

        Assert.Equal(oldKey, removed);
        Assert.True(identities.TryResolve(in newKey, out ProjectileHandle resolved));
        Assert.Equal(newHandle, resolved);
        Assert.False(identities.TryGetWireKey(oldHandle, out _));
    }

    [Fact]
    public void Reusing_runtime_slot_removes_stale_forward_binding_for_previous_generation()
    {
        var identities = new RuntimeProjectileWireIdentityRegistry();
        TerrariaProjectileKeyState oldKey = Key(spawner: 4, index: 12, generation: 1);
        TerrariaProjectileKeyState replacementKey = Key(spawner: 7, index: 33, generation: 5);
        ProjectileHandle oldHandle = Handle(slot: 2, generation: 1);
        ProjectileHandle replacement = Handle(slot: 2, generation: 2);
        Assert.True(identities.TryBind(in oldKey, oldHandle));

        Assert.True(identities.TryBind(in replacementKey, replacement));

        Assert.False(identities.TryResolve(in oldKey, out _));
        Assert.False(identities.TryGetWireKey(oldHandle, out _));
        Assert.True(identities.TryResolve(in replacementKey, out ProjectileHandle resolved));
        Assert.Equal(replacement, resolved);
    }

    [Fact]
    public void Reusing_shadowed_runtime_slot_does_not_clear_newer_forward_mapping_for_old_wire_index()
    {
        var identities = new RuntimeProjectileWireIdentityRegistry();
        TerrariaProjectileKeyState oldKey = Key(spawner: 4, index: 12, generation: 1);
        TerrariaProjectileKeyState newerKey = Key(spawner: 4, index: 12, generation: 2);
        TerrariaProjectileKeyState replacementKey = Key(spawner: 9, index: 40, generation: 3);
        ProjectileHandle oldHandle = Handle(slot: 2, generation: 1);
        ProjectileHandle newerHandle = Handle(slot: 3, generation: 1);
        ProjectileHandle replacement = Handle(slot: 2, generation: 2);
        Assert.True(identities.TryBind(in oldKey, oldHandle));
        Assert.True(identities.TryBind(in newerKey, newerHandle));

        Assert.True(identities.TryBind(in replacementKey, replacement));

        Assert.True(identities.TryResolve(in newerKey, out ProjectileHandle resolvedNewer));
        Assert.Equal(newerHandle, resolvedNewer);
        Assert.True(identities.TryResolve(in replacementKey, out ProjectileHandle resolvedReplacement));
        Assert.Equal(replacement, resolvedReplacement);
    }

    [Fact]
    public void Invalid_wire_key_and_unassigned_or_out_of_capacity_handle_are_rejected()
    {
        var identities = new RuntimeProjectileWireIdentityRegistry(runtimeCapacity: 4);
        TerrariaProjectileKeyState invalidGeneration = Key(spawner: 1, index: 1, generation: 0);
        TerrariaProjectileKeyState valid = Key(spawner: 1, index: 1, generation: 1);
        ProjectileHandle unassigned = default;
        ProjectileHandle outsideCapacity = Handle(slot: 4, generation: 1);

        Assert.False(identities.TryBind(in invalidGeneration, Handle(slot: 0, generation: 1)));
        Assert.False(identities.TryBind(in valid, unassigned));
        Assert.False(identities.TryBind(in valid, outsideCapacity));
        Assert.False(identities.TryResolve(in invalidGeneration, out _));
        Assert.False(identities.TryGetWireKey(outsideCapacity, out _));
        Assert.False(identities.TryUnbind(outsideCapacity, out _));
    }

    private static TerrariaProjectileKeyState Key(byte spawner, ushort index, ushort generation) =>
        new(spawner, index, generation);

    private static ProjectileHandle Handle(ushort slot, ulong generation) =>
        new(slot, new ProjectileGeneration(generation));
}
