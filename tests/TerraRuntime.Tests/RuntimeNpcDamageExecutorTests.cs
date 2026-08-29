using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcDamageExecutorTests
{
    [Fact]
    public void Player_item_source_requires_generation_safe_player_only()
    {
        PlayerHandle player = Player(7, 1);
        ProjectileHandle projectile = Projectile(3, 1);

        Assert.True(DamageSource.FromPlayerItem(player).IsValid);
        Assert.True(DamageSource.FromPlayerProjectile(player, projectile).IsValid);
        Assert.False(new DamageSource(
            DamageSourceKind.PlayerItem,
            player,
            default,
            projectile).IsValid);
        Assert.False(default(DamageSource).IsValid);
    }

    [Fact]
    public void Blue_slime_damage_applies_verified_defense_and_commits_hp()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSnapshot target = SpawnBlueSlime(store);
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(
            target.Handle,
            DamageSource.FromPlayerItem(Player(7, 1)),
            BaseDamage: 10);

        Assert.True(executor.TryApply(in request, out NpcDamageResult result));

        Assert.Equal(2, result.Defense);
        Assert.Equal(2, result.EffectiveDefense);
        Assert.Equal(9, result.ResolvedDamage);
        Assert.Equal(25, result.LifeBefore);
        Assert.Equal(16, result.LifeAfter);
        Assert.Equal(9, result.LifeLost);
        Assert.False(result.Lethal);
        Assert.True(store.TryGet(target.Handle, out NpcSnapshot committed));
        Assert.Equal(16, committed.Simulation.Life);
        Assert.Equal(new NpcRevision(2), committed.Revision);
    }

    [Fact]
    public void Armor_penetration_and_critical_modifier_are_applied_in_order()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSnapshot target = SpawnBlueSlime(store);
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(
            target.Handle,
            DamageSource.FromPlayerItem(Player(7, 1)),
            BaseDamage: 10,
            ArmorPenetration: 2,
            Critical: true);

        Assert.True(executor.TryApply(in request, out NpcDamageResult result));

        Assert.Equal(0, result.EffectiveDefense);
        Assert.Equal(20, result.ResolvedDamage);
        Assert.Equal(5, result.LifeAfter);
        Assert.True(result.Critical);
    }

    [Fact]
    public void Defense_never_reduces_a_valid_hit_below_one_damage()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSnapshot target = SpawnZombie(store);
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(
            target.Handle,
            DamageSource.Server,
            BaseDamage: 1);

        Assert.True(executor.TryApply(in request, out NpcDamageResult result));

        Assert.Equal(6, result.EffectiveDefense);
        Assert.Equal(1, result.ResolvedDamage);
        Assert.Equal(44, result.LifeAfter);
    }

    [Fact]
    public void Lethal_hit_commits_zero_life_without_guessing_death_side_effects()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSnapshot target = SpawnBlueSlime(store);
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(
            target.Handle,
            DamageSource.Environment,
            BaseDamage: 100);

        Assert.True(executor.TryApply(in request, out NpcDamageResult result));

        Assert.True(result.Lethal);
        Assert.Equal(99, result.ResolvedDamage);
        Assert.Equal(0, result.LifeAfter);
        Assert.Equal(25, result.LifeLost);
        Assert.True(store.TryGet(target.Handle, out NpcSnapshot committed));
        Assert.True(committed.IsActive);
        Assert.Equal(0, committed.Simulation.Life);
    }

    [Fact]
    public void Stale_target_generation_cannot_damage_reused_npc_slot()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcSnapshot stale = SpawnBlueSlime(store);
        Assert.True(store.TryDespawn(stale.Handle));
        NpcSnapshot replacement = SpawnBlueSlime(store);
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(
            stale.Handle,
            DamageSource.Server,
            BaseDamage: 10);

        Assert.False(executor.TryApply(in request, out _));

        Assert.True(store.TryGet(replacement.Handle, out NpcSnapshot unchanged));
        Assert.Equal(25, unchanged.Simulation.Life);
        Assert.NotEqual(stale.Handle.Generation, replacement.Handle.Generation);
    }

    [Fact]
    public void Resolver_clamps_extreme_critical_damage_without_integer_overflow()
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(
            VanillaNpcIds.BlueSlime,
            out VanillaNpcDefinition definition));
        var request = new NpcDamageRequest(
            new NpcHandle(0, new NpcGeneration(1)),
            DamageSource.Server,
            BaseDamage: int.MaxValue,
            Critical: true);

        Assert.True(VanillaNpcDamageResolver.TryResolve(
            in definition,
            in request,
            out _,
            out int damage));
        Assert.Equal(int.MaxValue, damage);
    }

    private static NpcSnapshot SpawnBlueSlime(RuntimeNpcStore store) =>
        Spawn(store, type: 1);

    private static NpcSnapshot SpawnZombie(RuntimeNpcStore store) =>
        Spawn(store, type: 3);

    private static NpcSnapshot Spawn(RuntimeNpcStore store, int type)
    {
        var update = new NpcStateUpdate(
            Type: type,
            NetId: checked((short)type),
            PositionX: 0f,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);

        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot snapshot));
        return snapshot;
    }

    private static PlayerHandle Player(byte slot, ulong generation) =>
        new(new PlayerSlotId(slot), new PlayerSessionGeneration(generation));

    private static ProjectileHandle Projectile(ushort slot, ulong generation) =>
        new(slot, new ProjectileGeneration(generation));
}
