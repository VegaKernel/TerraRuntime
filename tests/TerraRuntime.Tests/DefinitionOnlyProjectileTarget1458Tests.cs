using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests;

public sealed class DefinitionOnlyProjectileTarget1458Tests
{
    private static NpcStateUpdate Create(int type, float x) => new(type, (short)type,
        x, 320, 0, 0, 255, default, NpcSimulationState.Initial with
        { Life = 70, LifeMax = 70, Friendly = false, Chaseable = true, Immortal = false });

    [Fact]
    public void Retained_definition_only_actor_is_not_a_controlled_magic_or_rocket_target()
    {
        var store = new RuntimeNpcStore(2);
        Assert.True(store.TrySpawn(0, Create(185, 320), out var actor));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(actor.TypeIdentity, actor.NetIdentity, out var definition));
        Assert.True(definition.DefinitionOnly);
        Assert.True(VanillaNpcChaseability1458.CanBeChasedBy(in actor));
        var resolver = new VanillaProjectileNpcTargetResolver(store, new(new WorldDimensions(100, 100)));
        Assert.False(resolver.TryGetChaseableTargetCenter(0, out _, out _));
        Assert.False(resolver.TryGetCelebrationRocketTargetCenter(0, 320, 320, 800, out _, out _));
        Assert.True(store.TryGet(actor.Handle, out var retained));
        Assert.Equal(actor, retained);
    }

    [Fact]
    public void Verified_actor_target_remains_available_without_NPC_or_source_random_mutation()
    {
        var random = new VanillaUnifiedRandom1458(1458);
        var store = new RuntimeNpcStore(2);
        store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
        Assert.True(store.TrySpawn(0, Create(3, 320), out var actor));
        var before = random.Clone();
        var resolver = new VanillaProjectileNpcTargetResolver(store, new(new WorldDimensions(100, 100)));
        Assert.True(resolver.TryGetChaseableTargetCenter(0, out float x, out float y));
        Assert.Equal(329, x);
        Assert.Equal(340, y);
        Assert.True(resolver.TryGetCelebrationRocketTargetCenter(0, 320, 320, 800, out _, out _));
        Assert.True(random.HasSameState(before));
        Assert.True(store.TryGet(actor.Handle, out var retained));
        Assert.Equal(actor, retained);
    }

    [Fact]
    public void Closest_scan_skips_unadmitted_nearer_actor_and_selects_verified_actor()
    {
        var store = new RuntimeNpcStore(2);
        Assert.True(store.TrySpawn(0, Create(185, 320), out _));
        Assert.True(store.TrySpawn(1, Create(3, 400), out _));
        var resolver = new VanillaProjectileNpcTargetResolver(store, new(new WorldDimensions(100, 100)));
        Assert.True(TerraRuntime.Gameplay.Projectiles.VanillaDefinitionCatalog.TryGet(new(16), out var definition));
        var projectile = new ProjectileSnapshot(new(0, new(1)), new(1), new(16), 0,
            300, 320, 1, 0, default, 0, 10, 0, 0);
        Assert.True(resolver.TryFindClosestTargetWithLineOfSight(in projectile, in definition, 800,
            out int slot, out _, out _));
        Assert.Equal(1, slot);
        Assert.True(resolver.TryFindClosestCelebrationRocketTarget(in projectile, in definition, 800,
            out slot, out _, out _));
        Assert.Equal(1, slot);
    }
}
