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
        Assert.True(committed.Simulation.JustHit);
        Assert.Equal(new NpcRevision(2), committed.Revision);
    }

    [Fact]
    public void Strong_hit_uses_source_direction_and_vanilla_resistance_multiplier()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSnapshot target = SpawnZombie(store);
        target = UpdateMotion(store, target, velocityX: 2f, velocityY: 1f);
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(
            target.Handle,
            DamageSource.FromPlayerItem(Player(7, 1)),
            BaseDamage: 10,
            KnockBack: 10f,
            HitDirection: -1);

        Assert.True(executor.TryApply(in request, out NpcDamageResult result));
        Assert.Equal(7, result.ResolvedDamage);
        Assert.True(store.TryGet(target.Handle, out NpcSnapshot committed));
        Assert.Equal(-5f, committed.VelocityX);
        Assert.Equal(-2.75f, committed.VelocityY);
        Assert.True(committed.Simulation.JustHit);
    }

    [Fact]
    public void Weak_hit_replaces_velocity_and_applies_resistance_twice_like_vanilla()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSnapshot target = SpawnZombie(store);
        target = UpdateMotion(store, target, velocityX: -7f, velocityY: 4f);
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(
            target.Handle,
            DamageSource.Server,
            BaseDamage: 1,
            KnockBack: 10f,
            HitDirection: 1);

        Assert.True(executor.TryApply(in request, out NpcDamageResult result));
        Assert.Equal(1, result.ResolvedDamage);
        Assert.True(store.TryGet(target.Handle, out NpcSnapshot committed));
        Assert.Equal(2.5f, committed.VelocityX);
        Assert.Equal(-1.875f, committed.VelocityY);
    }

    [Fact]
    public void Expert_threshold_can_select_strong_knockback_branch()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSnapshot target = SpawnZombie(store);
        var executor = new RuntimeNpcDamageExecutor(store, expertMode: true);
        var request = new NpcDamageRequest(
            target.Handle,
            DamageSource.Server,
            BaseDamage: 7,
            KnockBack: 4f,
            HitDirection: 1);

        Assert.True(executor.TryApply(in request, out NpcDamageResult result));
        Assert.Equal(4, result.ResolvedDamage);
        Assert.True(store.TryGet(target.Handle, out NpcSnapshot committed));
        Assert.Equal(2f, committed.VelocityX);
        Assert.Equal(-1.5f, committed.VelocityY);
    }

    [Fact]
    public void Critical_knockback_applies_soft_caps_before_critical_multiplier()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSnapshot target = SpawnBlueSlime(store);
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(
            target.Handle,
            DamageSource.Server,
            BaseDamage: 100,
            Critical: true,
            KnockBack: 30f,
            HitDirection: 1);

        Assert.True(executor.TryApply(in request, out _));
        Assert.True(store.TryGet(target.Handle, out NpcSnapshot committed));
        Assert.Equal(22.4f, committed.VelocityX, precision: 4);
        Assert.Equal(-16.8f, committed.VelocityY, precision: 4);
    }

    [Fact]
    public void Zero_resistance_boss_is_just_hit_without_velocity_change()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSnapshot target = Spawn(store, VanillaNpcIds.EyeOfCthulhu.Value);
        target = UpdateMotion(store, target, velocityX: 3f, velocityY: -2f);
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(
            target.Handle,
            DamageSource.Server,
            BaseDamage: 10,
            KnockBack: 20f,
            HitDirection: -1);

        Assert.True(executor.TryApply(in request, out _));
        Assert.True(store.TryGet(target.Handle, out NpcSnapshot committed));
        Assert.Equal(3f, committed.VelocityX);
        Assert.Equal(-2f, committed.VelocityY);
        Assert.True(committed.Simulation.JustHit);
    }

    [Fact]
    public void Net_variant_knockback_uses_effective_definition_instead_of_positive_type_defaults()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSnapshot target = Spawn(
            store,
            VanillaNpcIds.BlueSlime.Value,
            checked((short)VanillaNpcNetVariantCatalog.GreenSlime.Value));
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(
            target.Handle,
            DamageSource.Server,
            BaseDamage: 1,
            KnockBack: 5f,
            HitDirection: 1);

        Assert.True(executor.TryApply(in request, out NpcDamageResult result));
        Assert.Equal(0, result.Defense);
        Assert.True(store.TryGet(target.Handle, out NpcSnapshot committed));
        Assert.Equal(7.2f, committed.VelocityX, precision: 4);
        Assert.Equal(-5.4f, committed.VelocityY, precision: 4);
    }

    [Fact]
    public void Runtime_invulnerability_rejects_damage_without_advancing_revision()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSnapshot target = SpawnBlueSlime(store);
        var invulnerableUpdate = new NpcStateUpdate(
            target.Type,
            target.NetId,
            target.PositionX,
            target.PositionY,
            target.VelocityX,
            target.VelocityY,
            target.Target,
            target.Ai,
            target.Simulation with { DontTakeDamage = true });
        Assert.True(store.TryUpdate(target.Handle, in invulnerableUpdate, out NpcSnapshot invulnerable));
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(
            invulnerable.Handle,
            DamageSource.Server,
            BaseDamage: 100);

        Assert.False(executor.TryApply(in request, out _));

        Assert.True(store.TryGet(invulnerable.Handle, out NpcSnapshot unchanged));
        Assert.Equal(invulnerable.Revision, unchanged.Revision);
        Assert.Equal(25, unchanged.Simulation.Life);
        Assert.True(unchanged.Simulation.DontTakeDamage);
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
    public void Zero_source_damage_still_resolves_to_vanilla_minimum_one()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcSnapshot target = SpawnZombie(store);
        var executor = new RuntimeNpcDamageExecutor(store);
        var request = new NpcDamageRequest(target.Handle, DamageSource.Server, BaseDamage: 0);

        Assert.True(executor.TryApply(in request, out NpcDamageResult result));
        Assert.Equal(1, result.ResolvedDamage);
        Assert.Equal(44, result.LifeAfter);
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

    private static NpcSnapshot Spawn(RuntimeNpcStore store, int type, short? netId = null)
    {
        var update = new NpcStateUpdate(
            Type: type,
            NetId: netId ?? checked((short)type),
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

    private static NpcSnapshot UpdateMotion(
        RuntimeNpcStore store,
        NpcSnapshot target,
        float velocityX,
        float velocityY)
    {
        var update = new NpcStateUpdate(
            target.Type,
            target.NetId,
            target.PositionX,
            target.PositionY,
            velocityX,
            velocityY,
            target.Target,
            target.Ai,
            target.Simulation);

        Assert.True(store.TryUpdate(target.Handle, in update, out NpcSnapshot committed));
        return committed;
    }

    private static PlayerHandle Player(byte slot, ulong generation) =>
        new(new PlayerSlotId(slot), new PlayerSessionGeneration(generation));

    private static ProjectileHandle Projectile(ushort slot, ulong generation) =>
        new(slot, new ProjectileGeneration(generation));
}
