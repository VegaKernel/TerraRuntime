using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeGameplayArchetypeRegistryTests
{
    [Fact]
    public void Npc_archetype_is_invisible_until_safe_boundary_and_preserves_server_identity()
    {
        var registry = new RuntimeNpcArchetypeRegistry();
        var descriptor = new NpcArchetypeDescriptor(
            new GameplayArchetypeId("worldslicer:guard"),
            VanillaNpcIds.Zombie,
            new GameplayExtensionId("worldslicer:guard-ai"));

        Assert.Equal(
            GameplayArchetypeRegistrationResult.Registered,
            registry.TryRegister(descriptor, out IGameplayArchetypeRegistrationLease? lease));
        Assert.NotNull(lease);
        Assert.False(registry.Snapshot.TryGet(descriptor.Id, out _));

        RuntimeGameplayArchetypeSnapshot<NpcArchetypeDescriptor> snapshot = registry.CommitPending();

        Assert.True(snapshot.TryGet(descriptor.Id, out NpcArchetypeDescriptor resolved));
        Assert.Equal(descriptor, resolved);
        Assert.Equal(VanillaNpcIds.Zombie, resolved.VanillaPresentationType);
        Assert.Equal(new GameplayExtensionId("worldslicer:guard-ai"), resolved.BehaviorId);
    }

    [Fact]
    public void Registries_reject_presentations_not_supported_by_the_active_vanilla_catalogs()
    {
        var npcs = new RuntimeNpcArchetypeRegistry();
        var projectiles = new RuntimeProjectileArchetypeRegistry();

        GameplayArchetypeRegistrationResult npcResult = npcs.TryRegister(
            new NpcArchetypeDescriptor(new GameplayArchetypeId("test:unknown-npc"), new NpcTypeId(9999)),
            out _);
        GameplayArchetypeRegistrationResult projectileResult = projectiles.TryRegister(
            new ProjectileArchetypeDescriptor(
                new GameplayArchetypeId("test:unknown-projectile"),
                new ProjectileTypeId(VanillaProjectileIds.Count)),
            out _);

        Assert.Equal(GameplayArchetypeRegistrationResult.InvalidDescriptor, npcResult);
        Assert.Equal(GameplayArchetypeRegistrationResult.InvalidDescriptor, projectileResult);
    }

    [Fact]
    public void Duplicate_archetype_id_is_rejected_and_cannot_be_reused_until_retirement_is_published()
    {
        var registry = new RuntimeProjectileArchetypeRegistry();
        GameplayArchetypeId id = new("worldslicer:rocket");
        var first = new ProjectileArchetypeDescriptor(id, VanillaProjectileIds.Shuriken);
        var second = new ProjectileArchetypeDescriptor(id, VanillaProjectileIds.ThrowingKnife);

        Assert.Equal(
            GameplayArchetypeRegistrationResult.Registered,
            registry.TryRegister(first, out IGameplayArchetypeRegistrationLease? lease));
        Assert.NotNull(lease);
        registry.CommitPending();

        Assert.Equal(GameplayArchetypeRegistrationResult.DuplicateId, registry.TryRegister(second, out _));
        lease.Dispose();
        Assert.True(lease.IsRetirementPending);
        Assert.Equal(GameplayArchetypeRegistrationResult.DuplicateId, registry.TryRegister(second, out _));

        RuntimeGameplayArchetypeSnapshot<ProjectileArchetypeDescriptor> retired = registry.CommitPending();
        Assert.True(lease.IsRetired);
        Assert.False(retired.TryGet(id, out _));

        Assert.Equal(
            GameplayArchetypeRegistrationResult.Registered,
            registry.TryRegister(second, out IGameplayArchetypeRegistrationLease? replacement));
        Assert.NotNull(replacement);
    }

    [Fact]
    public void Unassigned_behavior_id_is_valid_for_vanilla_backed_archetype()
    {
        var registry = new RuntimeNpcArchetypeRegistry();
        var descriptor = new NpcArchetypeDescriptor(
            new GameplayArchetypeId("test:vanilla-zombie"),
            VanillaNpcIds.Zombie);

        Assert.Equal(GameplayArchetypeRegistrationResult.Registered, registry.TryRegister(descriptor, out _));
        RuntimeGameplayArchetypeSnapshot<NpcArchetypeDescriptor> snapshot = registry.CommitPending();
        Assert.True(snapshot.TryGet(descriptor.Id, out NpcArchetypeDescriptor resolved));
        Assert.False(resolved.BehaviorId.IsAssigned);
    }
}
