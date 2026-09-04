using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Network;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeClientProjectileIngressTests
{
    [Fact]
    public void Unknown_packet27_allocates_first_physical_slot_and_exact_key_updates_same_handle()
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle source = fixture.SpawnPlayer(connectionId: 1);
        TerrariaProjectileUpdateState first = CreateUpdate(source.Player.Slot.Value, index: 777, generation: 9, type: 1, positionX: 100f);
        TerrariaProjectileKeyState firstKey = first.Key;

        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, first));

        Assert.Equal(1, fixture.State.AppliedProjectileSpawns);
        Assert.Equal(0, fixture.State.RejectedClientProjectileUpdates);
        Assert.True(fixture.Replication.WireIdentities.TryResolve(in firstKey, out ProjectileHandle handle));
        Assert.Equal((ushort)0, handle.Slot);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(handle, out ProjectileSnapshot created));
        Assert.Equal(100f, created.PositionX);
        Assert.Equal(new ProjectileRevision(1), created.Revision);

        TerrariaProjectileUpdateState moved = first with { PositionX = 150f };
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, moved));

        Assert.Equal(1, fixture.State.AppliedProjectileSpawns);
        Assert.Equal(1, fixture.State.AppliedProjectileUpdates);
        Assert.True(fixture.Replication.WireIdentities.TryResolve(in firstKey, out ProjectileHandle same));
        Assert.Equal(handle, same);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(handle, out ProjectileSnapshot updated));
        Assert.Equal(150f, updated.PositionX);
        Assert.Equal(new ProjectileRevision(2), updated.Revision);
    }

    [Fact]
    public void Existing_client_projectile_cannot_rewrite_combat_identity_or_damage()
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle source = fixture.SpawnPlayer(connectionId: 11);
        TerrariaProjectileUpdateState first = CreateUpdate(source.Player.Slot.Value, 91, 1, type: 1, positionX: 100f);
        TerrariaProjectileKeyState key = first.Key;
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, first));
        Assert.True(fixture.Replication.WireIdentities.TryResolve(in key, out ProjectileHandle handle));
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(handle, out ProjectileSnapshot before));

        TerrariaProjectileUpdateState forged = first with
        {
            ProjectileType = 3,
            Damage = 30_000,
            OriginalDamage = 30_000,
            KnockBack = 99f,
            PositionX = 150f
        };
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, forged));

        Assert.Equal(1, fixture.State.RejectedClientProjectileUpdates);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(handle, out ProjectileSnapshot after));
        Assert.Equal(before.Type, after.Type);
        Assert.Equal(before.Damage, after.Damage);
        Assert.Equal(before.OriginalDamage, after.OriginalDamage);
        Assert.Equal(before.KnockBack, after.KnockBack);
        Assert.Equal(before.PositionX, after.PositionX);
    }

    [Fact]
    public void New_generation_for_same_wire_index_shadows_lookup_but_preserves_old_reverse_identity()
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle source = fixture.SpawnPlayer(connectionId: 2);
        TerrariaProjectileUpdateState first = CreateUpdate(source.Player.Slot.Value, index: 88, generation: 1, type: 1, positionX: 10f);
        TerrariaProjectileKeyState firstKey = first.Key;
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, first));
        Assert.True(fixture.Replication.WireIdentities.TryResolve(in firstKey, out ProjectileHandle oldHandle));

        TerrariaProjectileUpdateState replacement = first with
        {
            Key = new TerrariaProjectileKeyState(source.Player.Slot.Value, 88, 2),
            PositionX = 20f
        };
        TerrariaProjectileKeyState replacementKey = replacement.Key;
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, replacement));

        Assert.True(fixture.Replication.WireIdentities.TryResolve(in replacementKey, out ProjectileHandle newHandle));
        Assert.Equal((ushort)1, newHandle.Slot);
        Assert.NotEqual(oldHandle, newHandle);
        Assert.False(fixture.Replication.WireIdentities.TryResolve(in firstKey, out _));
        Assert.True(fixture.Replication.WireIdentities.TryGetWireKey(oldHandle, out TerrariaProjectileKeyState retainedOld));
        Assert.Equal(firstKey, retainedOld);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(oldHandle, out _));
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(newHandle, out _));
    }

    [Fact]
    public void Authoritative_state_rejects_hostile_and_foreign_spawner_even_if_frame_sink_is_bypassed()
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle source = fixture.SpawnPlayer(connectionId: 3);
        TerrariaProjectileUpdateState hostile = CreateUpdate(source.Player.Slot.Value, 1, 1, type: 31, positionX: 10f);
        TerrariaProjectileUpdateState foreign = CreateUpdate(checked((byte)(source.Player.Slot.Value + 1)), 2, 1, type: 1, positionX: 20f);

        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, hostile));
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(source, foreign));

        Assert.Equal(0, fixture.State.AppliedProjectileSpawns);
        Assert.Equal(2, fixture.State.RejectedClientProjectileUpdates);
        Assert.Equal(0, fixture.Projectiles.ActiveCount);
    }

    [Fact]
    public async Task Trusted_server_projectile_rejects_owner_packet27_and_packet29_mutations()
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle owner = fixture.SpawnPlayer(connectionId: 12);
        var completion = new TaskCompletionSource<ProjectileSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var spawn = new ProjectileStateUpdate(
            VanillaProjectileIds.WoodenArrowFriendly,
            owner.Player.Slot.Value,
            100f,
            200f,
            6f,
            0f,
            default,
            0,
            25,
            2f,
            25);

        fixture.State.Apply(new ProjectileSpawnRuntimeCommand(0, spawn, completion));
        ProjectileSnapshot? trustedResult = await completion.Task;
        Assert.True(trustedResult.HasValue);
        ProjectileSnapshot trusted = trustedResult.Value;
        Assert.True(fixture.Projectiles.IsCombatTrusted(trusted.Handle));
        Assert.True(fixture.Replication.WireIdentities.TryGetWireKey(trusted.Handle, out TerrariaProjectileKeyState key));

        var forgedUpdate = new TerrariaProjectileUpdateState(
            key,
            trusted.Type.Value,
            900f,
            900f,
            120f,
            -80f,
            99f,
            98f,
            97f,
            trusted.BannerIdToRespondTo,
            trusted.Damage,
            trusted.KnockBack,
            trusted.OriginalDamage);
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, forgedUpdate));

        Assert.Equal(1, fixture.State.RejectedTrustedClientProjectileUpdates);
        Assert.Equal(1, fixture.State.RejectedClientProjectileUpdates);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(trusted.Handle, out ProjectileSnapshot afterUpdate));
        Assert.Equal(trusted.PositionX, afterUpdate.PositionX);
        Assert.Equal(trusted.PositionY, afterUpdate.PositionY);
        Assert.Equal(trusted.VelocityX, afterUpdate.VelocityX);
        Assert.Equal(trusted.VelocityY, afterUpdate.VelocityY);
        Assert.Equal(trusted.Ai, afterUpdate.Ai);

        var forgedDestroy = new TerrariaProjectileDestroyState(key, 777f, 888f);
        fixture.State.Apply(new ClientProjectileDestroyRuntimeCommand(owner, forgedDestroy));

        Assert.Equal(1, fixture.State.RejectedTrustedClientProjectileDestroys);
        Assert.Equal(1, fixture.State.RejectedClientProjectileDestroys);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(trusted.Handle, out ProjectileSnapshot afterDestroy));
        Assert.Equal(afterUpdate, afterDestroy);
    }

    [Fact]
    public void Source_backed_bow_projectile_is_authoritative_promoted_and_consumes_pickammo()
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle owner = fixture.SpawnPlayer(connectionId: 18);
        fixture.SetInventoryItem(owner, slot: 0, VanillaItemIds.WoodenBow, stack: 1);
        fixture.SetInventoryItem(owner, slot: VanillaPlayerItemSlotCatalog.AmmoSlotStart, VanillaItemIds.WoodenArrow, stack: 2);
        fixture.SetCombatPlayer(owner, positionX: 100f, positionY: 100f, life: 100, hostile: false);

        var packet = new TerrariaProjectileUpdateState(
            new TerrariaProjectileKeyState(owner.Player.Slot.Value, 600, 1),
            VanillaProjectileIds.WoodenArrowFriendly.Value,
            120f,
            100f,
            9.1f,
            0f,
            0f,
            0f,
            0f,
            99,
            9,
            2f,
            0);
        TerrariaProjectileKeyState key = packet.Key;

        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, packet));

        Assert.True(fixture.Replication.WireIdentities.TryResolve(in key, out ProjectileHandle handle));
        Assert.True(fixture.Projectiles.IsCombatTrusted(handle));
        Assert.True(fixture.Projectiles.TryGetCombatTrustedOwner(handle, out PlayerHandle trustedOwner));
        Assert.Equal(owner.Player, trustedOwner);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(handle, out ProjectileSnapshot projectile));
        Assert.Equal(VanillaProjectileIds.WoodenArrowFriendly, projectile.Type);
        Assert.Equal((short)9, projectile.Damage);
        Assert.Equal(2f, projectile.KnockBack);
        Assert.Equal(9.1f, projectile.VelocityX, 3);
        Assert.Equal(0f, projectile.VelocityY, 3);
        Assert.Equal(default, projectile.Ai);
        Assert.Equal((ushort)0, projectile.BannerIdToRespondTo);
        Assert.Equal((short)0, projectile.OriginalDamage);

        Assert.True(fixture.State.TryCapturePlayerInventoryItem(
            owner.Player, VanillaPlayerItemSlotCatalog.AmmoSlotStart, out RuntimePlayerInventoryItem ammo));
        Assert.Equal(VanillaItemIds.WoodenArrow, ammo.ItemType);
        Assert.Equal((short)1, ammo.Stack);
    }

    [Fact]
    public void Source_backed_gun_projectile_uses_server_pickammo_and_silver_bullet_transform()
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle owner = fixture.SpawnPlayer(connectionId: 20);
        fixture.SetInventoryItem(owner, slot: 0, VanillaItemIds.FlintlockPistol, stack: 1);
        fixture.SetInventoryItem(owner, slot: VanillaPlayerItemSlotCatalog.AmmoSlotStart, VanillaItemIds.SilverBullet, stack: 2);
        fixture.SetCombatPlayer(owner, positionX: 100f, positionY: 100f, life: 100, hostile: false);

        var packet = new TerrariaProjectileUpdateState(
            new TerrariaProjectileKeyState(owner.Player.Slot.Value, 620, 1),
            VanillaProjectileIds.SilverBullet.Value,
            120f,
            100f,
            10.5f,
            0f,
            0f,
            0f,
            0f,
            0,
            22,
            4f,
            0);

        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, packet));

        Assert.True(fixture.Replication.WireIdentities.TryResolve(packet.Key, out ProjectileHandle handle));
        Assert.True(fixture.Projectiles.IsCombatTrusted(handle));
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(handle, out ProjectileSnapshot projectile));
        Assert.Equal(VanillaProjectileIds.SilverBullet, projectile.Type);
        Assert.Equal((short)22, projectile.Damage);
        Assert.Equal(4f, projectile.KnockBack);
        Assert.Equal(10.5f, projectile.VelocityX, 3);
        Assert.True(fixture.State.TryCapturePlayerInventoryItem(
            owner.Player, VanillaPlayerItemSlotCatalog.AmmoSlotStart, out RuntimePlayerInventoryItem ammo));
        Assert.Equal(VanillaItemIds.SilverBullet, ammo.ItemType);
        Assert.Equal((short)1, ammo.Stack);
    }

    [Fact]
    public void Source_backed_bow_rejects_forged_damage_velocity_and_same_tick_cadence()
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle owner = fixture.SpawnPlayer(connectionId: 19);
        fixture.SetInventoryItem(owner, slot: 0, VanillaItemIds.WoodenBow, stack: 1);
        fixture.SetInventoryItem(owner, slot: VanillaPlayerItemSlotCatalog.AmmoSlotStart, VanillaItemIds.FlamingArrow, stack: 4);
        fixture.SetCombatPlayer(owner, positionX: 100f, positionY: 100f, life: 100, hostile: false);

        TerrariaProjectileUpdateState valid = new(
            new TerrariaProjectileKeyState(owner.Player.Slot.Value, 610, 1),
            VanillaProjectileIds.FireArrow.Value,
            120f,
            100f,
            9.6f,
            0f,
            0f,
            0f,
            0f,
            0,
            11,
            2f,
            0);
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, valid));
        Assert.Equal(1, fixture.Projectiles.ActiveCount);

        TerrariaProjectileUpdateState forgedDamage = valid with
        {
            Key = new TerrariaProjectileKeyState(owner.Player.Slot.Value, 611, 1),
            Damage = 111
        };
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, forgedDamage));

        TerrariaProjectileUpdateState forgedVelocity = valid with
        {
            Key = new TerrariaProjectileKeyState(owner.Player.Slot.Value, 612, 1),
            VelocityX = 96f
        };
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, forgedVelocity));

        TerrariaProjectileUpdateState sameTick = valid with
        {
            Key = new TerrariaProjectileKeyState(owner.Player.Slot.Value, 613, 1)
        };
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, sameTick));

        Assert.Equal(1, fixture.Projectiles.ActiveCount);
        Assert.False(fixture.Replication.WireIdentities.TryResolve(forgedDamage.Key, out _));
        Assert.False(fixture.Replication.WireIdentities.TryResolve(forgedVelocity.Key, out _));
        Assert.False(fixture.Replication.WireIdentities.TryResolve(sameTick.Key, out _));
        Assert.True(fixture.State.TryCapturePlayerInventoryItem(
            owner.Player, VanillaPlayerItemSlotCatalog.AmmoSlotStart, out RuntimePlayerInventoryItem ammo));
        Assert.Equal((short)3, ammo.Stack);
    }

    [Theory]
    [InlineData(42, 3, 10, 0f, 9f)]
    [InlineData(154, 21, 20, 2.3f, 8f)]
    [InlineData(279, 48, 12, 2f, 10f)]
    [InlineData(287, 54, 14, 2.4f, 12f)]
    [InlineData(1809, 318, 13, 6.5f, 9f)]
    [InlineData(1913, 330, 14, 0f, 12f)]
    [InlineData(3379, 599, 14, 1.5f, 10f)]
    public void Source_backed_standalone_projectile_is_promoted_and_consumes_selected_stack(
        int itemType,
        int projectileType,
        int damage,
        float knockBack,
        float speed)
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle owner = fixture.SpawnPlayer(connectionId: 30 + itemType);
        fixture.SetInventoryItem(owner, slot: 0, new ItemTypeId(itemType), stack: 2);
        fixture.SetCombatPlayer(owner, positionX: 100f, positionY: 100f, life: 100, hostile: false);

        var packet = new TerrariaProjectileUpdateState(
            new TerrariaProjectileKeyState(owner.Player.Slot.Value, checked((ushort)(700 + projectileType)), 1),
            projectileType,
            120f,
            100f,
            speed,
            0f,
            0f,
            0f,
            0f,
            0,
            checked((short)damage),
            knockBack,
            0);
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, packet));

        Assert.True(fixture.Replication.WireIdentities.TryResolve(packet.Key, out ProjectileHandle handle));
        Assert.True(fixture.Projectiles.IsCombatTrusted(handle));
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(handle, out ProjectileSnapshot projectile));
        Assert.Equal(new ProjectileTypeId(projectileType), projectile.Type);
        Assert.Equal(checked((short)damage), projectile.Damage);
        Assert.Equal(knockBack, projectile.KnockBack);
        Assert.Equal(speed, projectile.VelocityX, 3);
        Assert.Equal(0f, projectile.VelocityY, 3);
        Assert.True(fixture.State.TryCapturePlayerInventoryItem(owner.Player, 0, out RuntimePlayerInventoryItem remaining));
        Assert.Equal(new ItemTypeId(itemType), remaining.ItemType);
        Assert.Equal((short)1, remaining.Stack);
    }

    [Fact]
    public void Source_backed_standalone_projectile_rejects_forged_velocity_damage_ai_and_cadence()
    {
        using var fixture = new Fixture(playerCount: 1);
        ConnectionHandle owner = fixture.SpawnPlayer(connectionId: 32);
        fixture.SetInventoryItem(owner, slot: 0, VanillaItemIds.ThrowingKnife, stack: 5);
        fixture.SetCombatPlayer(owner, positionX: 100f, positionY: 100f, life: 100, hostile: false);

        TerrariaProjectileUpdateState valid = new(
            new TerrariaProjectileKeyState(owner.Player.Slot.Value, 760, 1),
            VanillaProjectileIds.ThrowingKnife.Value,
            120f,
            100f,
            10f,
            0f,
            0f,
            0f,
            0f,
            0,
            12,
            2f,
            0);
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, valid));

        TerrariaProjectileUpdateState forgedVelocity = valid with
        {
            Key = new TerrariaProjectileKeyState(owner.Player.Slot.Value, 761, 1),
            VelocityX = 100f
        };
        TerrariaProjectileUpdateState forgedDamage = valid with
        {
            Key = new TerrariaProjectileKeyState(owner.Player.Slot.Value, 762, 1),
            Damage = 120
        };
        TerrariaProjectileUpdateState forgedAi = valid with
        {
            Key = new TerrariaProjectileKeyState(owner.Player.Slot.Value, 763, 1),
            Ai1 = 1f
        };
        TerrariaProjectileUpdateState sameTick = valid with
        {
            Key = new TerrariaProjectileKeyState(owner.Player.Slot.Value, 764, 1)
        };

        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, forgedVelocity));
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, forgedDamage));
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, forgedAi));
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, sameTick));

        Assert.Equal(1, fixture.Projectiles.ActiveCount);
        Assert.False(fixture.Replication.WireIdentities.TryResolve(forgedVelocity.Key, out _));
        Assert.False(fixture.Replication.WireIdentities.TryResolve(forgedDamage.Key, out _));
        Assert.False(fixture.Replication.WireIdentities.TryResolve(forgedAi.Key, out _));
        Assert.False(fixture.Replication.WireIdentities.TryResolve(sameTick.Key, out _));
        Assert.True(fixture.State.TryCapturePlayerInventoryItem(owner.Player, 0, out RuntimePlayerInventoryItem remaining));
        Assert.Equal((short)4, remaining.Stack);
    }

    [Fact]
    public void Trusted_shuriken_pvp_damage_is_server_owned_and_projectile_local_immunity_is_40_ticks()
    {
        using var fixture = new Fixture(playerCount: 2, projectileStepper: new NoOpProjectileStepper());
        ConnectionHandle owner = fixture.SpawnPlayer(connectionId: 33);
        ConnectionHandle target = fixture.SpawnPlayer(connectionId: 34);
        fixture.SetInventoryItem(owner, slot: 0, VanillaItemIds.Shuriken, stack: 2);
        fixture.SetCombatPlayer(owner, positionX: 100f, positionY: 100f, life: 100, hostile: true);
        fixture.SetCombatPlayer(target, positionX: 120f, positionY: 100f, life: 100, hostile: true);

        TerrariaProjectileUpdateState spawn = new(
            new TerrariaProjectileKeyState(owner.Player.Slot.Value, 770, 1),
            VanillaProjectileIds.Shuriken.Value,
            120f,
            100f,
            9f,
            0f,
            0f,
            0f,
            0f,
            0,
            10,
            0f,
            0);
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, spawn));
        Assert.True(fixture.Replication.WireIdentities.TryResolve(spawn.Key, out ProjectileHandle handle));
        Assert.True(fixture.Projectiles.IsCombatTrusted(handle));

        fixture.State.Tick();
        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out PlayerStateSnapshot firstHit));
        Assert.InRange(firstHit.Life, (short)88, (short)92);
        short afterFirstHit = firstHit.Life;

        for (int i = 0; i < 20; i++)
            fixture.State.Tick();
        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out PlayerStateSnapshot duringProjectileImmunity));
        Assert.Equal(afterFirstHit, duringProjectileImmunity.Life);

        for (int i = 0; i < 20; i++)
            fixture.State.Tick();
        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out PlayerStateSnapshot afterSecondWindow));
        Assert.True(afterSecondWindow.Life < afterFirstHit);
    }

    [Fact]
    public void Untrusted_client_projectile_cannot_mutate_pvp_health()
    {
        using var fixture = new Fixture(playerCount: 2, projectileStepper: new NoOpProjectileStepper());
        ConnectionHandle owner = fixture.SpawnPlayer(connectionId: 20);
        ConnectionHandle target = fixture.SpawnPlayer(connectionId: 21);
        fixture.SetCombatPlayer(owner, positionX: 100f, positionY: 100f, life: 100, hostile: true);
        fixture.SetCombatPlayer(target, positionX: 120f, positionY: 100f, life: 100, hostile: true);

        TerrariaProjectileUpdateState spawn = CreateUpdate(
            owner.Player.Slot.Value,
            index: 700,
            generation: 1,
            type: VanillaProjectileIds.WoodenArrowFriendly.Value,
            positionX: 120f) with
        {
            PositionY = 100f,
            Damage = 40,
            OriginalDamage = 40,
            VelocityX = 1f,
            VelocityY = 0f
        };
        TerrariaProjectileKeyState key = spawn.Key;
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, spawn));

        Assert.True(fixture.Replication.WireIdentities.TryResolve(in key, out ProjectileHandle handle));
        Assert.False(fixture.Projectiles.IsCombatTrusted(handle));
        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out PlayerStateSnapshot before));
        Assert.Equal((short)100, before.Life);

        fixture.State.Tick();

        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out PlayerStateSnapshot after));
        Assert.Equal((short)100, after.Life);
        Assert.False(after.IsDead);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(handle, out _));
    }

    [Fact]
    public void Unknown_packet29_relays_to_playing_peer_without_local_mutation_or_sender_echo()
    {
        using var fixture = new Fixture(playerCount: 2);
        ConnectionHandle source = fixture.SpawnPlayer(connectionId: 4);
        ConnectionHandle peer = fixture.SpawnPlayer(connectionId: 5);
        TerrariaConnectionOutboundQueue sourceOutbound = fixture.Outbound(source.Source);
        TerrariaConnectionOutboundQueue peerOutbound = fixture.Outbound(peer.Source);
        var destroy = new TerrariaProjectileDestroyState(
            new TerrariaProjectileKeyState(source.Player.Slot.Value, 999, 7),
            300f,
            400f);

        fixture.State.Apply(new ClientProjectileDestroyRuntimeCommand(source, destroy));

        Assert.Equal(0, fixture.Projectiles.ActiveCount);
        Assert.Equal(1, fixture.State.RelayedUnknownProjectileDestroys);
        Assert.Equal(0, fixture.State.RejectedClientProjectileDestroys);
        Assert.Equal(0, sourceOutbound.QueuedFrames);
        Assert.Equal(1, peerOutbound.QueuedFrames);
    }

    [Fact]
    public void Resolved_packet29_requires_owner_and_applies_final_position_before_despawn()
    {
        using var fixture = new Fixture(playerCount: 2);
        ConnectionHandle owner = fixture.SpawnPlayer(connectionId: 6);
        ConnectionHandle foreign = fixture.SpawnPlayer(connectionId: 7);
        TerrariaProjectileUpdateState spawn = CreateUpdate(owner.Player.Slot.Value, 123, 1, type: 1, positionX: 10f);
        TerrariaProjectileKeyState spawnKey = spawn.Key;
        fixture.State.Apply(new ClientProjectileUpdateRuntimeCommand(owner, spawn));
        Assert.True(fixture.Replication.WireIdentities.TryResolve(in spawnKey, out ProjectileHandle handle));

        var destroy = new TerrariaProjectileDestroyState(spawnKey, 500f, 600f);
        fixture.State.Apply(new ClientProjectileDestroyRuntimeCommand(foreign, destroy));

        Assert.Equal(1, fixture.State.RejectedClientProjectileDestroys);
        Assert.True(fixture.State.TryCaptureProjectileSnapshot(handle, out ProjectileSnapshot alive));
        Assert.Equal(10f, alive.PositionX);

        fixture.State.Apply(new ClientProjectileDestroyRuntimeCommand(owner, destroy));

        Assert.Equal(1, fixture.State.AppliedProjectileDespawns);
        Assert.False(fixture.State.TryCaptureProjectileSnapshot(handle, out _));
        Assert.False(fixture.Replication.WireIdentities.TryResolve(in spawnKey, out _));
    }

    private static TerrariaProjectileUpdateState CreateUpdate(
        byte spawner,
        ushort index,
        ushort generation,
        int type,
        float positionX) =>
        new(
            new TerrariaProjectileKeyState(spawner, index, generation),
            type,
            positionX,
            200f,
            2f,
            -1f,
            0f,
            0f,
            0f,
            0,
            25,
            2f,
            25);

    private sealed class NoOpProjectileStepper : IProjectileStateStepper
    {
        public bool TryStepState(
            in ProjectileSimulationStepContext projectile,
            out ProjectileSimulationStepResult next)
        {
            next = default;
            return false;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots;
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly Dictionary<GameCommandSourceId, TerrariaConnectionOutboundQueue> outbound = [];

        public Fixture(int playerCount, IProjectileStateStepper? projectileStepper = null)
        {
            slots = new PlayerSlotPool(playerCount);
            Replication = new RuntimeProjectileReplicationRegistry();
            Projectiles = new RuntimeProjectileStore(capacity: 8, commitSink: Replication);
            State = new ServerRuntimeState(
                playerEvents: Replication,
                projectiles: Projectiles,
                projectileStepper: projectileStepper,
                projectileReplication: Replication);
        }

        public RuntimeProjectileReplicationRegistry Replication { get; }
        public RuntimeProjectileStore Projectiles { get; }
        public ServerRuntimeState State { get; }

        public ConnectionHandle SpawnPlayer(long connectionId)
        {
            Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
            var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            sessions.Add(session);
            Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
            Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());

            GameCommandSourceId source = GameCommandSourceId.FromConnection(connectionId);
            var queue = new TerrariaConnectionOutboundQueue(
                new OutboundQueueOptions(maxFrames: 32, maxQueuedBytes: 16_384, maxFrameBytes: 2_048));
            Assert.True(Replication.TryRegister(source, queue));
            outbound.Add(source, queue);

            var connection = new ConnectionHandle(source, session.Handle);
            var request = new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0);
            State.Apply(new PlayerSpawnRuntimeCommand(connection, session, request));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
            return connection;
        }


        public void SetInventoryItem(
            ConnectionHandle connection,
            short slot,
            ItemTypeId itemType,
            short stack)
        {
            var request = new PlayerEquipmentCommitRequest(
                connection.Player.Slot,
                slot,
                stack,
                Prefix: VanillaPrefixIds.NoneValue,
                ItemNetId: checked((short)itemType.Value),
                ItemFlags: 0);
            State.Apply(new PlayerEquipmentRuntimeCommand(connection, request));
        }

        public void SetCombatPlayer(
            ConnectionHandle connection,
            float positionX,
            float positionY,
            short life,
            bool hostile)
        {
            // Establish a known authoritative empty equipment projection for strict combat. Packet-5 empty functional
            // slot state creates the generation-owned transfer profile without inventing any combat modifiers.
            var emptyEquipment = new PlayerEquipmentCommitRequest(
                connection.Player.Slot,
                VanillaPlayerItemSlotCatalog.ArmorStart,
                Stack: 0,
                Prefix: 0,
                ItemNetId: 0,
                ItemFlags: 0);
            State.Apply(new PlayerEquipmentRuntimeCommand(connection, emptyEquipment));
            var health = new PlayerHealthCommitRequest(connection.Player.Slot, life, life);
            State.Apply(new PlayerHealthRuntimeCommand(connection, health));
            State.Apply(new PlayerPvpToggleRuntimeCommand(connection, hostile));
            var movement = new PlayerMovementCommitRequest(
                connection.Player.Slot,
                ControlFlags: 0,
                MovementFlags: 0,
                MiscFlags1: 0,
                MiscFlags2: 0,
                SelectedItem: 0,
                PositionX: positionX,
                PositionY: positionY,
                HasVelocity: false,
                VelocityX: 0f,
                VelocityY: 0f,
                HasMount: false,
                MountType: 0,
                HasPotionOfReturnPositions: false,
                PotionOfReturnOriginalPositionX: 0f,
                PotionOfReturnOriginalPositionY: 0f,
                PotionOfReturnHomePositionX: 0f,
                PotionOfReturnHomePositionY: 0f,
                HasCameraTarget: false,
                CameraTargetX: 0f,
                CameraTargetY: 0f);
            State.Apply(new PlayerMovementRuntimeCommand(connection, movement));
        }

        public TerrariaConnectionOutboundQueue Outbound(GameCommandSourceId source) => outbound[source];

        public void Dispose()
        {
            foreach (KeyValuePair<GameCommandSourceId, TerrariaConnectionOutboundQueue> pair in outbound)
                Replication.TryUnregister(pair.Key);
            foreach (PlayerJoinSession session in sessions)
                session.Dispose();
        }
    }
}
