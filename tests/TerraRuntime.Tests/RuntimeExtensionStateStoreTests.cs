using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeExtensionStateStoreTests
{
    [Fact]
    public void Npc_state_is_generation_bound_and_stale_handle_cannot_resurrect_after_retire()
    {
        var store = new RuntimeNpcExtensionStateStore<string>(capacity: 4);
        NpcHandle first = new(2, new NpcGeneration(1));
        NpcHandle second = new(2, new NpcGeneration(2));

        Assert.True(store.TryActivate(first));
        Assert.True(store.TrySet(first, "phase-one"));
        Assert.True(store.TryGet(first, out string? value));
        Assert.Equal("phase-one", value);

        Assert.True(store.TryRetire(first));
        Assert.False(store.TryGet(first, out _));
        Assert.False(store.TrySet(first, "stale"));
        Assert.False(store.TryActivate(first));

        Assert.True(store.TryActivate(second));
        Assert.True(store.TryGet(second, out string? reset));
        Assert.Null(reset);
        Assert.True(store.TrySet(second, "phase-two"));
        Assert.False(store.TryGet(first, out _));
    }

    [Fact]
    public void Projectile_state_rejects_older_generation_even_while_newer_generation_is_active()
    {
        var store = new RuntimeProjectileExtensionStateStore<int>(capacity: 8);
        ProjectileHandle newer = new(5, new ProjectileGeneration(9));
        ProjectileHandle older = new(5, new ProjectileGeneration(8));

        Assert.True(store.TryActivate(newer));
        Assert.True(store.TrySet(newer, 42));

        Assert.False(store.TryActivate(older));
        Assert.False(store.TrySet(older, 11));
        Assert.False(store.TryRetire(older));
        Assert.True(store.TryGet(newer, out int value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void Repeated_activate_of_same_live_handle_preserves_state()
    {
        var store = new RuntimeNpcExtensionStateStore<int>(capacity: 2);
        NpcHandle handle = new(1, new NpcGeneration(3));

        Assert.True(store.TryActivate(handle));
        Assert.True(store.TrySet(handle, 17));
        Assert.True(store.TryActivate(handle));
        Assert.True(store.TryGet(handle, out int value));
        Assert.Equal(17, value);
    }

    [Fact]
    public void Clear_all_releases_state_and_resets_generation_tombstones()
    {
        var store = new RuntimeProjectileExtensionStateStore<object>(capacity: 2);
        ProjectileHandle handle = new(1, new ProjectileGeneration(4));
        var state = new object();

        Assert.True(store.TryActivate(handle));
        Assert.True(store.TrySet(handle, state));
        store.ClearAll();

        Assert.False(store.TryGet(handle, out _));
        Assert.True(store.TryActivate(handle));
        Assert.True(store.TryGet(handle, out object? reset));
        Assert.Null(reset);
    }

    [Fact]
    public void Out_of_capacity_handles_fail_closed()
    {
        var npc = new RuntimeNpcExtensionStateStore<int>(capacity: 1);
        var projectile = new RuntimeProjectileExtensionStateStore<int>(capacity: 1);

        Assert.False(npc.TryActivate(new NpcHandle(1, new NpcGeneration(1))));
        Assert.False(projectile.TryActivate(new ProjectileHandle(1, new ProjectileGeneration(1))));
    }
}
