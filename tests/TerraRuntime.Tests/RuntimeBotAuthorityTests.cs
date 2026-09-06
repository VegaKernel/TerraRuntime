using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Players;
using TerraRuntime.HostContracts;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeBotAuthorityTests
{
    [Fact]
    public async Task Create_request_materializes_player_or_hostile_npc_actor_fail_closed()
    {
        using var fixture = new Fixture();

        RuntimeBotSnapshot player = Assert.IsType<RuntimeBotSnapshot>(
            await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.Equal(RuntimeBotBodyKind.Player, player.Configuration.Body);
        Assert.True(player.Player.IsAssigned);
        Assert.False(player.Npc.IsAssigned);
        Assert.True(fixture.ServerPlayers.TryGetItem(player.ServerPlayerId, 0, out ServerPlayerItemState weapon));
        Assert.Equal(VanillaItemIds.CopperBroadsword, weapon.ItemType);
        Assert.True(fixture.ServerPlayers.TryGetItem(player.ServerPlayerId, 1, out ServerPlayerItemState bow));
        Assert.Equal(VanillaItemIds.WoodenBow, bow.ItemType);
        Assert.True(fixture.ServerPlayers.TryGetItem(player.ServerPlayerId, 2, out ServerPlayerItemState gun));
        Assert.Equal(VanillaItemIds.Musket, gun.ItemType);
        Assert.True(fixture.ServerPlayers.TryGetItem(
            player.ServerPlayerId,
            VanillaPlayerItemSlotCatalog.AmmoSlotStart,
            out ServerPlayerItemState ammo));
        Assert.Equal(VanillaItemIds.WoodenArrow, ammo.ItemType);
        Assert.Equal(100, ammo.Stack);
        Assert.True(fixture.ServerPlayers.TryGetItem(
            player.ServerPlayerId,
            VanillaPlayerItemSlotCatalog.AmmoSlotStart + 1,
            out ServerPlayerItemState bulletAmmo));
        Assert.Equal(VanillaItemIds.MusketBall, bulletAmmo.ItemType);
        Assert.Equal(100, bulletAmmo.Stack);
        Assert.True(player.Configuration.FlightEnabled);
        Assert.True(fixture.ServerPlayers.TryGetItem(
            player.ServerPlayerId,
            checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 3)),
            out ServerPlayerItemState wings));
        Assert.Equal(VanillaItemIds.FishronWings, wings.ItemType);
        Assert.True(fixture.ServerPlayers.TryGetItem(
            player.ServerPlayerId,
            checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 4)),
            out ServerPlayerItemState booster));
        Assert.Equal(VanillaItemIds.EmpressFlightBooster, booster.ItemType);

        RuntimeBotSnapshot npc = Assert.IsType<RuntimeBotSnapshot>(
            await fixture.CreateBotAsync(new RuntimeBotCreateRequest(RuntimeBotBodyKind.Npc, VanillaNpcIds.Zombie)));
        Assert.Equal(RuntimeBotBodyKind.Npc, npc.Configuration.Body);
        Assert.False(npc.Player.IsAssigned);
        Assert.True(npc.Npc.IsAssigned);
        Assert.True(fixture.State.TryCaptureNpcSnapshot(npc.Npc, out NpcSnapshot npcState));
        Assert.Equal(VanillaNpcIds.Zombie, npcState.TypeIdentity);
        Assert.Equal(0, npcState.Simulation.DamageOverride);
        Assert.True(npcState.Simulation.DontTakeDamage);

        RuntimeBotSnapshot? rejected = await fixture.CreateBotAsync(
            new RuntimeBotCreateRequest(RuntimeBotBodyKind.Npc, VanillaNpcIds.Merchant));
        Assert.Null(rejected);
    }

    [Fact]
    public async Task Player_bot_auto_heal_consumes_closest_source_backed_quick_heal_item_and_obeys_delay()
    {
        using var fixture = new Fixture();
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));

        Assert.True(fixture.ServerPlayers.SetVitals(bot.ServerPlayerId, new ServerPlayerVitalsState(20, 100, 20, 20)));
        Assert.True(fixture.ServerPlayers.SetItem(
            bot.ServerPlayerId,
            new ServerPlayerItemState(1, VanillaWallOfFleshItemIds.HealingPotion, 2, VanillaPrefixIds.None, 0)));
        Assert.True(fixture.ServerPlayers.SetItem(
            bot.ServerPlayerId,
            new ServerPlayerItemState(2, VanillaItemIds.GreaterHealingPotion, 1, VanillaPrefixIds.None, 0)));

        fixture.State.Tick();

        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot healed));
        Assert.Equal(100, healed.Life); // Missing 80: vanilla QuickHeal chooses 100 over 150.
        Assert.True(fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, 1, out ServerPlayerItemState healing));
        Assert.Equal(1, healing.Stack);
        Assert.True(fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, 2, out ServerPlayerItemState greater));
        Assert.Equal(1, greater.Stack);

        Assert.True(fixture.ServerPlayers.SetVitals(bot.ServerPlayerId, new ServerPlayerVitalsState(20, 100, 20, 20)));
        fixture.State.Tick();
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot delayed));
        Assert.Equal(20, delayed.Life);
        Assert.True(fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, 1, out ServerPlayerItemState unchanged));
        Assert.Equal(1, unchanged.Stack);
    }

    [Fact]
    public async Task Player_bot_picks_only_required_intersecting_ammo_and_removes_exact_world_item()
    {
        using var fixture = new Fixture(botSpawnX: 64f, botSpawnY: 64f);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.True(fixture.WorldItems.TryAllocate(
            CreateWorldItem(VanillaItemIds.WoodenArrow, 64f, 64f, stack: 7),
            out WorldItemSnapshot arrow));
        Assert.True(fixture.WorldItems.TryAllocate(
            CreateWorldItem(VanillaItemIds.MusketBall, 64f, 64f, stack: 9),
            out WorldItemSnapshot bullet));

        fixture.State.Tick();

        Assert.False(fixture.WorldItems.TryGetActive(arrow.Handle.Slot, out _));
        Assert.True(fixture.WorldItems.TryGetActive(bullet.Handle.Slot, out _));
        Assert.True(fixture.ServerPlayers.TryGetItem(
            bot.ServerPlayerId,
            VanillaPlayerItemSlotCatalog.AmmoSlotStart,
            out ServerPlayerItemState ammo));
        Assert.Equal(VanillaItemIds.WoodenArrow, ammo.ItemType);
        Assert.Equal(107, ammo.Stack);
    }

    [Fact]
    public async Task Npc_bot_follow_uses_authoritative_actor_control_and_hard_distance_teleport()
    {
        using var fixture = new Fixture(botSpawnX: 0f, botSpawnY: 0f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(spawnTileX: 150, spawnTileY: 20);
        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out PlayerStateSnapshot targetState));
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(
            await fixture.CreateBotAsync(new RuntimeBotCreateRequest(RuntimeBotBodyKind.Npc, VanillaNpcIds.Zombie)));

        var configuration = bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow,
            Target = new RuntimeBotTarget(target.Player, "target")
        };
        RuntimeBotSnapshot configured = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(bot.Id, configuration));
        Assert.Equal(RuntimeBotBodyKind.Npc, configured.Configuration.Body);

        fixture.State.Tick();

        Assert.True(fixture.State.TryCaptureNpcSnapshot(bot.Npc, out NpcSnapshot moved));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Zombie, out VanillaNpcDefinition zombie));
        Assert.True(zombie.TryResolveHitbox(moved.Simulation.Scale, out VanillaNpcHitboxSize hitbox));
        Assert.InRange(targetState.PositionX - (moved.PositionX + hitbox.Width), 7.99f, 8.01f);
        Assert.InRange(MathF.Abs(moved.PositionY - targetState.PositionY), 0f, 2f); // NPC simulation may apply gravity after bot tick.
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTarget, moved.Target);
        fixture.Tick(6); // First runtime tick executes at update 0; reach update 6 before sampling telemetry.
        RuntimeBotSnapshot telemetry = Assert.Single(fixture.Telemetry.Capture());
        Assert.True(telemetry.TargetAvailable);
        Assert.Equal(1, telemetry.TeleportCount);
        Assert.False(telemetry.PvpEnabled);
    }

    [Fact]
    public async Task Npc_bot_verified_skeleton_preset_uses_the_same_controlled_follow_path()
    {
        using var fixture = new Fixture(botSpawnX: 0f, botSpawnY: 0f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(spawnTileX: 150, spawnTileY: 20);
        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out PlayerStateSnapshot targetState));
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(
            await fixture.CreateBotAsync(new RuntimeBotCreateRequest(RuntimeBotBodyKind.Npc, VanillaNpcIds.Skeleton)));
        RuntimeBotConfiguration configuration = bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow,
            Target = new RuntimeBotTarget(target.Player, "target")
        };
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(bot.Id, configuration));

        fixture.State.Tick();

        Assert.True(fixture.State.TryCaptureNpcSnapshot(bot.Npc, out NpcSnapshot moved));
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Skeleton, out VanillaNpcDefinition skeleton));
        Assert.True(skeleton.TryResolveHitbox(moved.Simulation.Scale, out VanillaNpcHitboxSize hitbox));
        Assert.InRange(targetState.PositionX - (moved.PositionX + hitbox.Width), 7.99f, 8.01f);
        Assert.InRange(MathF.Abs(moved.PositionY - targetState.PositionY), 0f, 2f);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTarget, moved.Target);
    }

    public static TheoryData<int> VerifiedFlyingNpcBotCases => new()
    {
        VanillaNpcIds.DemonEye.Value,
        VanillaNpcIds.EaterOfSouls.Value,
        VanillaNpcIds.CaveBat.Value
    };

    [Theory]
    [MemberData(nameof(VerifiedFlyingNpcBotCases))]
    public async Task Npc_bot_verified_flying_preset_uses_controlled_follow_and_stays_non_combat(int npcType)
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(spawnTileX: 16, spawnTileY: 10);
        Assert.True(NpcTypeId.TryCreate(npcType, out NpcTypeId type));
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(
            await fixture.CreateBotAsync(new RuntimeBotCreateRequest(
                RuntimeBotBodyKind.Npc,
                type)));
        RuntimeBotConfiguration configuration = bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow,
            Target = new RuntimeBotTarget(target.Player, "target")
        };
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(bot.Id, configuration));

        Assert.True(fixture.State.TryCaptureNpcSnapshot(bot.Npc, out NpcSnapshot before));
        // Bot intent stages after the NPC control snapshot is committed for the current tick. Bat movement also
        // crosses its separate world-motion boundary, so sample after both authoritative stages have advanced.
        fixture.Tick(7);
        Assert.True(fixture.State.TryCaptureNpcSnapshot(bot.Npc, out NpcSnapshot after));
        RuntimeBotSnapshot telemetry = Assert.Single(fixture.Telemetry.Capture());

        Assert.True(telemetry.TargetAvailable, $"NPC #{npcType} lost its configured live target.");
        Assert.True(
            after.PositionX > before.PositionX,
            $"NPC #{npcType} did not advance: x {before.PositionX}->{after.PositionX}, vx={after.VelocityX}, " +
            $"life={after.Simulation.Life}/{after.Simulation.LifeMax}, timeLeft={after.Simulation.TimeLeft}, target={after.Target}, " +
            $"direction=({after.Simulation.DirectionX},{after.Simulation.DirectionY}).");
        Assert.True(after.Simulation.NoGravity);
        Assert.Equal(0, after.Simulation.DamageOverride);
        Assert.True(after.Simulation.DontTakeDamage);
        Assert.Equal(VanillaNpcDefinitionCatalog.DefaultTarget, after.Target);
        Assert.Equal(0, fixture.Projectiles.ActiveCount);
    }

    [Fact]
    public async Task Player_bot_pvp_mirrors_target_toggle_only()
    {
        using var fixture = new Fixture();
        ConnectionHandle target = fixture.SpawnConnectionPlayer(spawnTileX: 10, spawnTileY: 10);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        var configuration = bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow,
            Target = new RuntimeBotTarget(target.Player, "target")
        };
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(bot.Id, configuration));

        fixture.State.Apply(new PlayerPvpToggleRuntimeCommand(target, Hostile: true));
        fixture.Tick(7);
        RuntimeBotSnapshot hostile = Assert.Single(fixture.Telemetry.Capture());
        Assert.True(hostile.PvpEnabled);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot botState));
        Assert.True(botState.Hostile);

        fixture.State.Apply(new PlayerPvpToggleRuntimeCommand(target, Hostile: false));
        fixture.Tick(6);
        RuntimeBotSnapshot peaceful = Assert.Single(fixture.Telemetry.Capture());
        Assert.False(peaceful.PvpEnabled);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out botState));
        Assert.False(botState.Hostile);
    }

    [Fact]
    public async Task Configure_can_switch_player_and_npc_body_without_leaking_previous_actor_generation()
    {
        using var fixture = new Fixture();
        RuntimeBotSnapshot player = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        PlayerHandle originalPlayer = player.Player;

        RuntimeBotConfiguration npcConfiguration = player.Configuration with
        {
            Body = RuntimeBotBodyKind.Npc,
            NpcType = VanillaNpcIds.Zombie,
            Mode = RuntimeBotMode.Idle,
            Target = default
        };
        RuntimeBotSnapshot npc = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(player.Id, npcConfiguration));
        Assert.False(npc.Player.IsAssigned);
        Assert.True(npc.Npc.IsAssigned);
        Assert.False(fixture.ServerPlayers.TryGet(originalPlayer, out _));
        Assert.True(fixture.State.TryCaptureNpcSnapshot(npc.Npc, out _));

        NpcHandle originalNpc = npc.Npc;
        RuntimeBotConfiguration playerConfiguration = npc.Configuration with
        {
            Body = RuntimeBotBodyKind.Player,
            NpcType = default,
            Mode = RuntimeBotMode.Idle,
            Target = default
        };
        RuntimeBotSnapshot replacement = Assert.IsType<RuntimeBotSnapshot>(
            await fixture.ConfigureAsync(player.Id, playerConfiguration));
        Assert.True(replacement.Player.IsAssigned);
        Assert.False(replacement.Npc.IsAssigned);
        Assert.NotEqual(originalPlayer.Generation, replacement.Player.Generation);
        Assert.False(fixture.State.TryCaptureNpcSnapshot(originalNpc, out _));
        Assert.True(fixture.ServerPlayers.TryGet(replacement.Player, out _));
    }

    [Fact]
    public async Task Guard_consumes_required_ammo_and_spawns_trusted_projectile_against_hostile_npc()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(spawnTileX: 10, spawnTileY: 10);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        RuntimeBotConfiguration configuration = bot.Configuration with
        {
            Mode = RuntimeBotMode.Guard,
            Target = new RuntimeBotTarget(target.Player, "target"),
            WeaponPolicy = RuntimeBotWeaponPolicy.Bow
        };
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(bot.Id, configuration));
        _ = fixture.SpawnNpc(VanillaNpcIds.BlueSlime, x: 520f, y: 160f);

        Assert.True(fixture.ServerPlayers.TryGetItem(
            bot.ServerPlayerId,
            VanillaPlayerItemSlotCatalog.AmmoSlotStart,
            out ServerPlayerItemState before));
        Assert.Equal(100, before.Stack);

        fixture.State.Tick();

        Assert.True(fixture.ServerPlayers.TryGetItem(
            bot.ServerPlayerId,
            VanillaPlayerItemSlotCatalog.AmmoSlotStart,
            out ServerPlayerItemState after));
        Assert.Equal(99, after.Stack);
        Assert.Equal(1, fixture.Projectiles.ActiveCount);
        var projectileBuffer = new ProjectileSnapshot[fixture.Projectiles.Capacity];
        Assert.Equal(1, fixture.Projectiles.CopyActive(projectileBuffer));
        Assert.Equal(bot.Player.Slot.Value, projectileBuffer[0].Spawner);
        Assert.Equal(VanillaProjectileIds.WoodenArrowFriendly, projectileBuffer[0].Type);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot attacking));
        Assert.Equal(0, attacking.SelectedItem);
        Assert.NotEqual(0, attacking.ControlFlags & (1 << 5));
    }

    [Fact]
    public async Task Automatic_guard_selects_visible_gun_for_distant_target_and_publishes_held_slot()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(spawnTileX: 10, spawnTileY: 10);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(
            bot.Id,
            bot.Configuration with
            {
                Mode = RuntimeBotMode.Guard,
                Target = new RuntimeBotTarget(target.Player, "target"),
                WeaponPolicy = RuntimeBotWeaponPolicy.Automatic
            }));
        _ = fixture.SpawnNpc(VanillaNpcIds.BlueSlime, x: 520f, y: 160f);

        fixture.State.Tick();

        var projectiles = new ProjectileSnapshot[fixture.Projectiles.Capacity];
        Assert.Equal(1, fixture.Projectiles.CopyActive(projectiles));
        Assert.Equal(VanillaProjectileIds.Bullet, projectiles[0].Type);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot attacking));
        Assert.Equal(2, attacking.SelectedItem);
        Assert.NotEqual(0, attacking.ControlFlags & (1 << 5));
    }

    [Fact]
    public async Task Bow_guard_leads_moving_target_with_exact_pick_ammo_launch_magnitude()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle targetPlayer = fixture.SpawnConnectionPlayer(spawnTileX: 10, spawnTileY: 10);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(
            bot.Id,
            bot.Configuration with
            {
                Mode = RuntimeBotMode.Guard,
                Target = new RuntimeBotTarget(targetPlayer.Player, "target"),
                WeaponPolicy = RuntimeBotWeaponPolicy.Bow
            }));
        NpcSnapshot moving = fixture.SetNpcVelocity(
            fixture.SpawnNpc(VanillaNpcIds.BlueSlime, x: 400f, y: 160f),
            velocityX: 0f,
            velocityY: 3f);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(moving.TypeIdentity, out VanillaNpcDefinition npcDefinition));
        Assert.True(npcDefinition.TryResolveHitbox(moving.Simulation.Scale, out VanillaNpcHitboxSize hitbox));
        float originX = 160f + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        float originY = 160f + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        float directX = moving.PositionX + hitbox.Width * 0.5f - (originX + 5f);
        float directY = moving.PositionY + hitbox.Height * 0.5f - (originY + 5f);
        float directLength = MathF.Sqrt(directX * directX + directY * directY);
        float directVelocityY = directY / directLength * 9.1f;

        fixture.State.Tick();

        var projectiles = new ProjectileSnapshot[fixture.Projectiles.Capacity];
        Assert.Equal(1, fixture.Projectiles.CopyActive(projectiles));
        ProjectileSnapshot arrow = projectiles[0];
        Assert.Equal(VanillaProjectileIds.WoodenArrowFriendly, arrow.Type);
        Assert.InRange(MathF.Sqrt(arrow.VelocityX * arrow.VelocityX + arrow.VelocityY * arrow.VelocityY), 9.099f, 9.101f);
        Assert.True(arrow.VelocityY > directVelocityY + 0.5f,
            $"Predictive vy {arrow.VelocityY} did not lead moving target beyond direct vy {directVelocityY}.");
    }

    [Fact]
    public async Task Guard_rejects_target_behind_solid_vanilla_line_of_sight_barrier()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(spawnTileX: 10, spawnTileY: 10);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(
            bot.Id,
            bot.Configuration with
            {
                Mode = RuntimeBotMode.Guard,
                Target = new RuntimeBotTarget(target.Player, "target"),
                WeaponPolicy = RuntimeBotWeaponPolicy.Bow
            }));
        _ = fixture.SpawnNpc(VanillaNpcIds.BlueSlime, x: 360f, y: 160f);
        for (int y = 7; y <= 15; y++)
        {
            fixture.Tiles.Set(15, y, new WorldTile
            {
                Type = checked((ushort)VanillaTileIds.Stone.Value),
                Flags = WorldTileFlags.Active
            });
        }

        fixture.State.Tick();

        Assert.Equal(0, fixture.Projectiles.ActiveCount);
        Assert.True(fixture.ServerPlayers.TryGetItem(
            bot.ServerPlayerId,
            VanillaPlayerItemSlotCatalog.AmmoSlotStart,
            out ServerPlayerItemState ammo));
        Assert.Equal(100, ammo.Stack);
    }

    [Fact]
    public async Task Automatic_guard_switches_to_broadsword_and_commits_authoritative_close_pve_hit()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(spawnTileX: 10, spawnTileY: 10);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(
            bot.Id,
            bot.Configuration with
            {
                Mode = RuntimeBotMode.Guard,
                Target = new RuntimeBotTarget(target.Player, "target"),
                WeaponPolicy = RuntimeBotWeaponPolicy.Automatic
            }));
        NpcSnapshot spawned = fixture.SpawnNpc(VanillaNpcIds.BlueSlime, x: 192f, y: 160f);

        fixture.State.Tick();

        Assert.Equal(0, fixture.Projectiles.ActiveCount);
        Assert.True(fixture.State.TryCaptureNpcSnapshot(spawned.Handle, out NpcSnapshot damaged));
        Assert.True(damaged.Simulation.Life < spawned.Simulation.Life);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot attacking));
        Assert.Equal(0, attacking.SelectedItem);
        Assert.NotEqual(0, attacking.ControlFlags & (1 << 5));
    }

    [Fact]
    public async Task Guard_pvp_requires_hostile_on_protected_target_and_opponent()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(spawnTileX: 10, spawnTileY: 10);
        ConnectionHandle opponent = fixture.SpawnConnectionPlayer(spawnTileX: 14, spawnTileY: 10);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        RuntimeBotConfiguration configuration = bot.Configuration with
        {
            Mode = RuntimeBotMode.Guard,
            Target = new RuntimeBotTarget(target.Player, "target"),
            WeaponPolicy = RuntimeBotWeaponPolicy.Bow
        };
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(bot.Id, configuration));

        fixture.State.Apply(new PlayerPvpToggleRuntimeCommand(target, Hostile: true));
        fixture.State.Tick();
        Assert.Equal(0, fixture.Projectiles.ActiveCount);
        Assert.True(fixture.ServerPlayers.TryGetItem(
            bot.ServerPlayerId,
            VanillaPlayerItemSlotCatalog.AmmoSlotStart,
            out ServerPlayerItemState peacefulAmmo));
        Assert.Equal(100, peacefulAmmo.Stack);

        fixture.State.Apply(new PlayerPvpToggleRuntimeCommand(opponent, Hostile: true));
        fixture.State.Tick();
        Assert.Equal(1, fixture.Projectiles.ActiveCount);
        Assert.True(fixture.ServerPlayers.TryGetItem(
            bot.ServerPlayerId,
            VanillaPlayerItemSlotCatalog.AmmoSlotStart,
            out ServerPlayerItemState hostileAmmo));
        Assert.Equal(99, hostileAmmo.Stack);
    }

    [Fact]
    public async Task Guard_auto_uses_supported_combat_buff_potion_but_leaves_unimplemented_buff_fail_closed()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(spawnTileX: 10, spawnTileY: 10);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        RuntimeBotConfiguration configuration = bot.Configuration with
        {
            Mode = RuntimeBotMode.Guard,
            Target = new RuntimeBotTarget(target.Player, "target"),
            WeaponPolicy = RuntimeBotWeaponPolicy.Bow,
            AutoPickup = true,
            AutoUseConsumables = true
        };
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(bot.Id, configuration));
        Assert.True(fixture.WorldItems.TryAllocate(
            CreateWorldItem(VanillaItemIds.ArcheryPotion, 160f, 160f, stack: 1),
            out WorldItemSnapshot archery));
        Assert.True(fixture.WorldItems.TryAllocate(
            CreateWorldItem(VanillaItemIds.RegenerationPotion, 160f, 160f, stack: 1),
            out WorldItemSnapshot regeneration));
        _ = fixture.SpawnNpc(VanillaNpcIds.BlueSlime, x: 520f, y: 160f);

        fixture.State.Tick();

        Assert.False(fixture.WorldItems.TryGetActive(archery.Handle.Slot, out _));
        Assert.True(fixture.WorldItems.TryGetActive(regeneration.Handle.Slot, out _));
        Assert.Equal(1, fixture.Projectiles.ActiveCount);
    }

    [Fact]
    public async Task Hard_distance_recovery_respects_teleport_cooldown_when_target_changes()
    {
        using var fixture = new Fixture(botSpawnX: 0f, botSpawnY: 0f);
        ConnectionHandle far = fixture.SpawnConnectionPlayer(spawnTileX: 150, spawnTileY: 20);
        ConnectionHandle origin = fixture.SpawnConnectionPlayer(spawnTileX: 1, spawnTileY: 1);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        RuntimeBotConfiguration configuration = bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow,
            Target = new RuntimeBotTarget(far.Player, "far")
        };
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(bot.Id, configuration));

        fixture.Tick(7);
        Assert.Equal(1, Assert.Single(fixture.Telemetry.Capture()).TeleportCount);

        configuration = configuration with { Target = new RuntimeBotTarget(origin.Player, "origin") };
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(bot.Id, configuration));
        fixture.Tick(6);
        Assert.Equal(1, Assert.Single(fixture.Telemetry.Capture()).TeleportCount);

        fixture.Tick(120);
        Assert.Equal(2, Assert.Single(fixture.Telemetry.Capture()).TeleportCount);
    }

    private static WorldItemStateUpdate CreateWorldItem(ItemTypeId type, float x, float y, short stack) => new(
        PositionX: x,
        PositionY: y,
        VelocityX: 0f,
        VelocityY: 0f,
        Stack: stack,
        Prefix: 0,
        Ownership: WorldItemOwnershipMode.None,
        ItemNetId: checked((short)type.Value),
        Shimmered: false,
        ShimmerTime: 0f,
        EnemyGrabDelayTime: 0,
        OwnerPlayerId: byte.MaxValue,
        TimeToKeepReservation: 0,
        GrabDelayPlayer: byte.MaxValue,
        GrabDelayTime: 0);

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(16);
        private readonly List<PlayerJoinSession> sessions = [];
        private long nextConnectionId = 1;

        public Fixture(float botSpawnX = 32f, float botSpawnY = 32f)
        {
            var identities = new ServerPlayerSlotRegistry(slots);
            var states = new ServerPlayerStateStore(identities, slots.Capacity);
            Tiles = new WorldTileStore(new WorldDimensions(300, 120));
            ServerPlayers = new ServerPlayerAuthority(states, identities, Tiles);
            WorldItems = new RuntimeWorldItemStore();
            Projectiles = new RuntimeProjectileStore();
            Telemetry = new RuntimeBotTelemetry();
            State = new ServerRuntimeState(
                worldTiles: Tiles,
                worldItems: WorldItems,
                projectiles: Projectiles,
                serverPlayers: ServerPlayers,
                botTelemetry: Telemetry,
                botSpawnX: botSpawnX,
                botSpawnY: botSpawnY);
        }

        public ServerRuntimeState State { get; }
        public ServerPlayerAuthority ServerPlayers { get; }
        public WorldTileStore Tiles { get; }
        public RuntimeWorldItemStore WorldItems { get; }
        public RuntimeProjectileStore Projectiles { get; }
        public RuntimeBotTelemetry Telemetry { get; }

        public async Task<RuntimeBotSnapshot?> CreateBotAsync(RuntimeBotCreateRequest request)
        {
            var completion = new TaskCompletionSource<RuntimeBotSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
            State.Apply(new RuntimeBotCreateCommand(request, completion));
            return await completion.Task;
        }

        public async Task<RuntimeBotSnapshot?> ConfigureAsync(int id, RuntimeBotConfiguration configuration)
        {
            var completion = new TaskCompletionSource<RuntimeBotSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
            State.Apply(new RuntimeBotConfigureCommand(id, configuration, completion));
            return await completion.Task;
        }

        public void Tick(int count)
        {
            for (int i = 0; i < count; i++)
                State.Tick();
        }

        public NpcSnapshot SpawnNpc(NpcTypeId type, float x, float y)
        {
            var completion = new TaskCompletionSource<NpcSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var state = new NpcStateUpdate(
                Type: type.Value,
                NetId: checked((short)type.Value),
                PositionX: x,
                PositionY: y,
                VelocityX: 0f,
                VelocityY: 0f,
                Target: VanillaNpcDefinitionCatalog.DefaultTarget,
                Ai: default,
                Simulation: NpcSimulationState.Initial);
            for (byte slot = 0; slot < 200; slot++)
            {
                State.Apply(new NpcSpawnRuntimeCommand(slot, state, completion));
                NpcSnapshot? spawned = completion.Task.GetAwaiter().GetResult();
                if (spawned is NpcSnapshot npc)
                    return npc;
                completion = new TaskCompletionSource<NpcSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            throw new InvalidOperationException("No NPC slot available in test fixture.");
        }

        public NpcSnapshot SetNpcVelocity(NpcSnapshot npc, float velocityX, float velocityY)
        {
            var update = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX,
                npc.PositionY,
                velocityX,
                velocityY,
                npc.Target,
                npc.Ai,
                npc.Simulation);
            State.Apply(new NpcUpdateRuntimeCommand(npc.Handle, update));
            Assert.True(State.TryCaptureNpcSnapshot(npc.Handle, out NpcSnapshot updated));
            return updated;
        }

        public ConnectionHandle SpawnConnectionPlayer(short spawnTileX, short spawnTileY)
        {
            Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
            var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            sessions.Add(session);
            Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
            Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(nextConnectionId++), session.Handle);
            var request = new PlayerSpawnCommitRequest(session.Slot, spawnTileX, spawnTileY, 0, 0, 0, 0, 0);
            State.Apply(new PlayerSpawnRuntimeCommand(connection, session, request));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
            State.Apply(new PlayerHealthRuntimeCommand(
                connection,
                new PlayerHealthCommitRequest(session.Slot, Life: 100, MaxLife: 100)));
            return connection;
        }

        public void Dispose()
        {
            foreach (PlayerJoinSession session in sessions)
                session.Dispose();
        }
    }
}
