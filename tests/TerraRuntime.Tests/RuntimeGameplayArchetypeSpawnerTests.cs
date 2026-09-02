using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeGameplayArchetypeSpawnerTests
{
    [Fact]
    public void Npc_custom_identity_is_separate_from_vanilla_presentation_and_retires_with_generation()
    {
        var identities = new RuntimeNpcArchetypeIdentityStore(capacity: 4);
        var store = new RuntimeNpcStore(capacity: 4, commitSink: identities);
        var archetypes = new RuntimeNpcArchetypeRegistry();
        GameplayArchetypeId id = new("worldslicer:guard");
        Assert.Equal(
            GameplayArchetypeRegistrationResult.Registered,
            archetypes.TryRegister(new NpcArchetypeDescriptor(id, VanillaNpcIds.Zombie), out _));
        archetypes.CommitPending();
        var spawner = new RuntimeNpcArchetypeSpawner(store, archetypes, identities);
        var request = new NpcArchetypeSpawnRequest(id, Slot: 2, PositionX: 100f, PositionY: 200f);

        Assert.True(spawner.TrySpawn(in request, out NpcSnapshot spawned));
        Assert.Equal(VanillaNpcIds.Zombie, spawned.TypeIdentity);
        Assert.Equal((short)VanillaNpcIds.Zombie.Value, spawned.NetId);
        Assert.True(identities.TryGet(spawned.Handle, out GameplayArchetypeId resolved));
        Assert.Equal(id, resolved);

        Assert.True(store.TryDespawn(spawned.Handle));
        Assert.False(identities.TryGet(spawned.Handle, out _));
    }

    [Fact]
    public void Projectile_custom_identity_uses_vanilla_allocator_and_clears_on_in_place_replacement()
    {
        var identities = new RuntimeProjectileArchetypeIdentityStore(RuntimeProjectileStore.VanillaPhysicalSlotCount);
        var store = new RuntimeProjectileStore(
            RuntimeProjectileStore.VanillaPhysicalSlotCount,
            commitSink: identities);
        var archetypes = new RuntimeProjectileArchetypeRegistry();
        GameplayArchetypeId id = new("worldslicer:knife");
        Assert.Equal(
            GameplayArchetypeRegistrationResult.Registered,
            archetypes.TryRegister(
                new ProjectileArchetypeDescriptor(id, VanillaProjectileIds.ThrowingKnife),
                out _));
        archetypes.CommitPending();
        var spawner = new RuntimeProjectileArchetypeSpawner(store, archetypes, identities);
        var request = new ProjectileArchetypeSpawnRequest(
            id,
            Spawner: VanillaProjectileOwnership.ServerOwner,
            PositionX: 10f,
            PositionY: 20f,
            VelocityX: 3f,
            VelocityY: -1f,
            Damage: 12,
            KnockBack: 2f,
            OriginalDamage: 12);

        Assert.True(spawner.TrySpawn(in request, out ProjectileSnapshot custom));
        Assert.Equal((ushort)0, custom.Handle.Slot);
        Assert.Equal(VanillaProjectileIds.ThrowingKnife, custom.Type);
        Assert.True(identities.TryGet(custom.Handle, out GameplayArchetypeId resolved));
        Assert.Equal(id, resolved);

        var filler = new ProjectileStateUpdate(
            VanillaProjectileIds.Shuriken,
            VanillaProjectileOwnership.ServerOwner,
            PositionX: 30f,
            PositionY: 40f,
            VelocityX: 0f,
            VelocityY: 0f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 5,
            KnockBack: 1f,
            OriginalDamage: 5);
        for (int slot = 1; slot < RuntimeProjectileStore.VanillaPhysicalSlotCount; slot++)
            Assert.True(store.TrySpawnVanilla(in filler, out _));

        Assert.True(store.TrySpawnVanilla(in filler, out ProjectileSnapshot replacement));

        Assert.Equal(custom.Handle.Slot, replacement.Handle.Slot);
        Assert.NotEqual(custom.Handle.Generation, replacement.Handle.Generation);
        Assert.False(identities.TryGet(custom.Handle, out _));
        Assert.False(identities.TryGet(replacement.Handle, out _));
    }

    [Fact]
    public void Spawn_fails_closed_when_identity_store_is_not_in_authoritative_commit_chain()
    {
        var identities = new RuntimeNpcArchetypeIdentityStore(capacity: 2);
        var store = new RuntimeNpcStore(capacity: 2);
        var archetypes = new RuntimeNpcArchetypeRegistry();
        GameplayArchetypeId id = new("test:isolated");
        Assert.Equal(
            GameplayArchetypeRegistrationResult.Registered,
            archetypes.TryRegister(new NpcArchetypeDescriptor(id, VanillaNpcIds.BlueSlime), out _));
        archetypes.CommitPending();
        var spawner = new RuntimeNpcArchetypeSpawner(store, archetypes, identities);
        var request = new NpcArchetypeSpawnRequest(id, Slot: 0, PositionX: 0f, PositionY: 0f);

        Assert.False(spawner.TrySpawn(in request, out _));
        Assert.Equal(0, store.ActiveCount);
    }
}
