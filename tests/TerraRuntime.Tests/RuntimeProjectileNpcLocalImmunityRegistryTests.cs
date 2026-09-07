using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileNpcLocalImmunityRegistryTests
{
    [Fact]
    public void Positive_cooldown_is_generation_safe_and_expires_on_source_tick_boundary()
    {
        var registry = new RuntimeProjectileNpcLocalImmunityRegistry(projectileCapacity: 2, npcCapacity: 2);
        var projectile = new ProjectileHandle(0, new ProjectileGeneration(1));
        var npc = new NpcHandle(0, new NpcGeneration(1));

        Assert.False(registry.IsImmune(projectile, npc, tick: 100, cooldownTicks: 12));
        registry.MarkHit(projectile, npc, tick: 100);

        Assert.True(registry.IsImmune(projectile, npc, tick: 100, cooldownTicks: 12));
        Assert.True(registry.IsImmune(projectile, npc, tick: 111, cooldownTicks: 12));
        Assert.False(registry.IsImmune(projectile, npc, tick: 112, cooldownTicks: 12));
        Assert.False(registry.IsImmune(
            projectile,
            new NpcHandle(0, new NpcGeneration(2)),
            tick: 101,
            cooldownTicks: 12));
        Assert.False(registry.IsImmune(
            new ProjectileHandle(0, new ProjectileGeneration(2)),
            npc,
            tick: 101,
            cooldownTicks: 12));
    }

    [Fact]
    public void Negative_cooldown_is_permanent_only_for_the_exact_projectile_and_npc_generations()
    {
        var registry = new RuntimeProjectileNpcLocalImmunityRegistry(projectileCapacity: 2, npcCapacity: 2);
        var projectile = new ProjectileHandle(1, new ProjectileGeneration(7));
        var npc = new NpcHandle(1, new NpcGeneration(9));
        registry.MarkHit(projectile, npc, tick: 5);

        Assert.True(registry.IsImmune(projectile, npc, tick: long.MaxValue, cooldownTicks: -1));
        Assert.False(registry.IsImmune(
            new ProjectileHandle(1, new ProjectileGeneration(8)),
            npc,
            tick: 6,
            cooldownTicks: -1));
        Assert.False(registry.IsImmune(
            projectile,
            new NpcHandle(1, new NpcGeneration(10)),
            tick: 6,
            cooldownTicks: -1));
    }

    [Fact]
    public void Invalid_lookup_fails_closed_and_invalid_mark_is_rejected()
    {
        var registry = new RuntimeProjectileNpcLocalImmunityRegistry(projectileCapacity: 1, npcCapacity: 1);
        var projectile = new ProjectileHandle(0, new ProjectileGeneration(1));
        var npc = new NpcHandle(0, new NpcGeneration(1));

        Assert.True(registry.IsImmune(default, npc, tick: 0, cooldownTicks: 12));
        Assert.True(registry.IsImmune(projectile, default, tick: 0, cooldownTicks: 12));
        Assert.True(registry.IsImmune(projectile, npc, tick: -1, cooldownTicks: 12));
        Assert.Throws<ArgumentException>(() => registry.MarkHit(default, npc, tick: 0));
        Assert.Throws<ArgumentException>(() => registry.MarkHit(projectile, default, tick: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.MarkHit(projectile, npc, tick: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.MarkHit(
            new ProjectileHandle(1, new ProjectileGeneration(1)), npc, tick: 0));
    }
}
