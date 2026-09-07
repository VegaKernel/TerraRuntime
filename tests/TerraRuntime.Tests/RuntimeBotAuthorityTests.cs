using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Players;
using TerraRuntime.HostContracts;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeBotAuthorityTests
{
    [Fact]
    public async Task Create_request_materializes_powerful_player_bot_with_vanilla_caps_and_consumables()
    {
        using var fixture = new Fixture();
        RuntimeBotSnapshot player = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));

        Assert.True(player.Player.IsAssigned);
        Assert.False(player.IsDead);
        Assert.False(player.Configuration.GodMode);
        Assert.DoesNotMatch(@"^Bot\s+\d+$", player.Name);
        Assert.InRange(player.Name.Length, 1, 20);
        Assert.True(fixture.ServerPlayers.TryGet(player.Player, out PlayerStateSnapshot state));
        Assert.Equal(500, state.Life);
        Assert.Equal(500, state.MaxLife);
        Assert.Equal(200, state.Mana);
        Assert.Equal(200, state.MaxMana);

        Assert.True(fixture.ServerPlayers.TryGetItem(player.ServerPlayerId, 0, out ServerPlayerItemState melee));
        Assert.Equal(VanillaItemIds.Muramasa, melee.ItemType);
        Assert.True(fixture.ServerPlayers.TryGetItem(player.ServerPlayerId, 1, out ServerPlayerItemState bow));
        Assert.Equal(VanillaItemIds.PlatinumBow, bow.ItemType);
        Assert.True(fixture.ServerPlayers.TryGetItem(player.ServerPlayerId, 2, out ServerPlayerItemState gun));
        Assert.Contains(gun.ItemType, new[] { VanillaItemIds.Handgun, VanillaItemIds.Minishark, VanillaItemIds.Revolver, VanillaItemIds.Musket });

        Assert.True(fixture.ServerPlayers.TryGetItem(player.ServerPlayerId, 3, out ServerPlayerItemState healing));
        Assert.Equal(VanillaItemIds.SuperHealingPotion, healing.ItemType);
        Assert.Equal(30, healing.Stack);
        Assert.True(fixture.ServerPlayers.TryGetItem(player.ServerPlayerId, 4, out ServerPlayerItemState mana));
        Assert.Equal(VanillaItemIds.GreaterManaPotion, mana.ItemType);
        Assert.Equal(30, mana.Stack);

        Assert.True(fixture.ServerPlayers.TryGetItem(player.ServerPlayerId, VanillaPlayerItemSlotCatalog.AmmoSlotStart, out ServerPlayerItemState arrowAmmo));
        Assert.Equal(VanillaItemIds.UnholyArrow, arrowAmmo.ItemType);
        Assert.Equal(999, arrowAmmo.Stack);
        Assert.True(fixture.ServerPlayers.TryGetItem(player.ServerPlayerId, VanillaPlayerItemSlotCatalog.AmmoSlotStart + 1, out ServerPlayerItemState bulletAmmo));
        Assert.Equal(VanillaItemIds.SilverBullet, bulletAmmo.ItemType);
        Assert.Equal(999, bulletAmmo.Stack);
    }

    [Fact]
    public async Task Player_bots_receive_distinct_automatic_visual_identity_and_escort_destinations()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(spawnTileX: 30, spawnTileY: 13);
        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out PlayerStateSnapshot targetState));
        var bots = new RuntimeBotSnapshot[3];
        for (int index = 0; index < bots.Length; index++)
        {
            RuntimeBotSnapshot created = Assert.IsType<RuntimeBotSnapshot>(
                await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
            bots[index] = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(
                created.Id,
                created.Configuration with
                {
                    Mode = RuntimeBotMode.Follow,
                    Target = new RuntimeBotTarget(target.Player, "target")
                }));
        }

        fixture.State.Tick();

        var destinations = new HashSet<float>();
        var visuals = new HashSet<(byte Skin, byte Hair, PlayerRgbColor Shirt, PlayerRgbColor HairColor)>();
        float targetCenterX = targetState.PositionX + PlayerAuthority.VanillaBasePlayerWidth * 0.5f;
        foreach (RuntimeBotSnapshot bot in bots)
        {
            ServerPlayerMovementIntent intent = fixture.ServerPlayers.GetMovementIntent(bot.Player);
            Assert.Equal(ServerPlayerMovementIntentKind.MoveTo, intent.Kind);
            Assert.False(intent.TargetPlayer.IsAssigned);
            Assert.True(MathF.Abs(intent.TargetX - targetCenterX) >= 40f);
            destinations.Add(intent.TargetX);

            Assert.True(fixture.ServerPlayers.TryGetAppearance(bot.Player, out ServerPlayerAppearanceState appearance));
            visuals.Add((appearance.SkinVariant, appearance.Hair, appearance.ShirtColor, appearance.HairColor));
        }
        Assert.Equal(bots.Length, destinations.Count);
        Assert.True(visuals.Count > 1);
    }

    [Fact]
    public async Task Player_bot_walks_at_target_level_when_the_escort_route_is_clear()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(spawnTileX: 30, spawnTileY: 13);
        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out PlayerStateSnapshot targetState));
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(
            await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(
            bot.Id,
            bot.Configuration with
            {
                Mode = RuntimeBotMode.Follow,
                Target = new RuntimeBotTarget(target.Player, "target")
            }));
        for (int x = 0; x < 80; x++)
        {
            fixture.Tiles.Set(x, 13, new WorldTile
            {
                Type = checked((ushort)VanillaTileIds.Stone.Value),
                Flags = WorldTileFlags.Active
            });
        }

        fixture.State.Tick();

        ServerPlayerMovementIntent intent = fixture.ServerPlayers.GetMovementIntent(bot.Player);
        float targetCenterY = targetState.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        Assert.Equal(targetCenterY, intent.TargetY, 3);
        Assert.True(intent.Options.FlightEnabled); // Wings remain available without forcing a permanent hover target.
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot self));
        float selfCenterY = self.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        Assert.True(intent.TargetY >= selfCenterY - intent.Options.JumpVerticalThreshold);

        fixture.Tick(30);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot walking));
        Assert.True(walking.PositionX > 160f);
        Assert.Equal(0, walking.ControlFlags & (1 << 4));
        Assert.InRange(walking.PositionY, 165f, 167f);
    }

    [Fact]
    public async Task Player_bot_raises_its_flight_target_when_solid_terrain_blocks_the_escort_route()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(spawnTileX: 30, spawnTileY: 13);
        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out PlayerStateSnapshot targetState));
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(
            await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(
            bot.Id,
            bot.Configuration with
            {
                Mode = RuntimeBotMode.Follow,
                Target = new RuntimeBotTarget(target.Player, "target")
            }));
        for (int y = 6; y <= 16; y++)
        {
            fixture.Tiles.Set(18, y, new WorldTile
            {
                Type = checked((ushort)VanillaTileIds.Stone.Value),
                Flags = WorldTileFlags.Active
            });
        }

        fixture.State.Tick();

        ServerPlayerMovementIntent intent = fixture.ServerPlayers.GetMovementIntent(bot.Player);
        float targetCenterY = targetState.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        Assert.True(intent.TargetY <= targetCenterY - 64f,
            $"Obstacle-aware flight target {intent.TargetY} did not rise above target center {targetCenterY}.");
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot self));
        float selfCenterY = self.PositionY + PlayerAuthority.VanillaBasePlayerHeight * 0.5f;
        Assert.True(intent.TargetY < selfCenterY - intent.Options.JumpVerticalThreshold);

        fixture.Tick(30);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot ascending));
        Assert.NotEqual(0, ascending.ControlFlags & (1 << 4));
        Assert.True(ascending.PositionY < 120f,
            $"Obstacle-aware bot remained at low height {ascending.PositionY} instead of ascending above the route.");
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
            CreateWorldItem(VanillaItemIds.UnholyArrow, 64f, 64f, stack: 7),
            out WorldItemSnapshot arrow));
        Assert.True(fixture.WorldItems.TryAllocate(
            CreateWorldItem(VanillaItemIds.SilverBullet, 64f, 64f, stack: 9),
            out WorldItemSnapshot bullet));

        fixture.State.Tick();

        Assert.False(fixture.WorldItems.TryGetActive(arrow.Handle.Slot, out _));
        Assert.True(fixture.WorldItems.TryGetActive(bullet.Handle.Slot, out _));
        Assert.True(fixture.ServerPlayers.TryGetItem(
            bot.ServerPlayerId,
            VanillaPlayerItemSlotCatalog.AmmoSlotStart,
            out ServerPlayerItemState ammo));
        Assert.Equal(VanillaItemIds.UnholyArrow, ammo.ItemType);
        Assert.Equal(1006, ammo.Stack);
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
        Assert.Equal(999, before.Stack);

        fixture.State.Tick();

        Assert.True(fixture.ServerPlayers.TryGetItem(
            bot.ServerPlayerId,
            VanillaPlayerItemSlotCatalog.AmmoSlotStart,
            out ServerPlayerItemState after));
        Assert.Equal(998, after.Stack);
        Assert.Equal(1, fixture.Projectiles.ActiveCount);
        var projectileBuffer = new ProjectileSnapshot[fixture.Projectiles.Capacity];
        Assert.Equal(1, fixture.Projectiles.CopyActive(projectileBuffer));
        Assert.Equal(bot.Player.Slot.Value, projectileBuffer[0].Spawner);
        Assert.Equal(VanillaProjectileIds.UnholyArrow, projectileBuffer[0].Type);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot attacking));
        Assert.Equal(0, attacking.SelectedItem); // Explicit Bow policy owns hotbar slot 0.
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
        Assert.Equal(VanillaProjectileIds.SilverBullet, projectiles[0].Type);
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
        // Platinum Bow 6.6 + Unholy Arrow 3.4 = exact PickAmmo magnitude 10.0 in 1.4.5.8.
        float expectedLaunchMagnitude = 10f;
        for (short slot = VanillaPlayerItemSlotCatalog.ArmorStart;
             slot < VanillaPlayerItemSlotCatalog.BaselineFunctionalArmorEndExclusive; slot++)
        {
            Assert.True(fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, slot, out ServerPlayerItemState equipped));
            if (equipped.ItemType == VanillaItemIds.MagicQuiver)
                expectedLaunchMagnitude *= 1.1f; // Player.PickAmmo, independent of the runtime launch resolver.
        }
        float directVelocityY = directY / directLength * expectedLaunchMagnitude;

        fixture.State.Tick();

        var projectiles = new ProjectileSnapshot[fixture.Projectiles.Capacity];
        Assert.Equal(1, fixture.Projectiles.CopyActive(projectiles));
        ProjectileSnapshot arrow = projectiles[0];
        Assert.Equal(VanillaProjectileIds.UnholyArrow, arrow.Type);
        Assert.InRange(MathF.Sqrt(arrow.VelocityX * arrow.VelocityX + arrow.VelocityY * arrow.VelocityY),
            expectedLaunchMagnitude - .001f, expectedLaunchMagnitude + .001f);
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
        Assert.Equal(999, ammo.Stack);
        ServerPlayerMovementIntent reposition = fixture.ServerPlayers.GetMovementIntent(bot.Player);
        Assert.True(reposition.TargetY < 160f); // Flight climbs to recover the blocked line, not endless low strafing.
        fixture.State.Tick();
        ServerPlayerMovementIntent heldReposition = fixture.ServerPlayers.GetMovementIntent(bot.Player);
        Assert.Equal(reposition.TargetX, heldReposition.TargetX);
        Assert.Equal(reposition.TargetY, heldReposition.TargetY);
    }

    [Fact]
    public async Task Guard_holds_clear_firing_position_and_lock_when_another_npc_moves_closer()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(10, 10);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = await fixture.ConfigureAsync(bot.Id, bot.Configuration with
        {
            Mode = RuntimeBotMode.Guard, Target = new RuntimeBotTarget(target.Player, "target"),
            WeaponPolicy = RuntimeBotWeaponPolicy.Bow
        });
        _ = fixture.SpawnNpc(VanillaNpcIds.Zombie, 360f, 160f);
        fixture.State.Tick();
        ServerPlayerMovementIntent aim = fixture.ServerPlayers.GetMovementIntent(bot.Player);
        Assert.Equal(160f + PlayerAuthority.VanillaBasePlayerWidth / 2, aim.TargetX);
        Assert.Equal(160f + PlayerAuthority.VanillaBasePlayerHeight / 2, aim.TargetY);
        _ = fixture.SpawnNpc(VanillaNpcIds.Zombie, 210f, 160f);
        fixture.State.Tick();
        ServerPlayerMovementIntent locked = fixture.ServerPlayers.GetMovementIntent(bot.Player);
        // A reacquisition would retreat from the new close NPC. The current target stays in a clear firing band.
        Assert.Equal(aim.TargetX, locked.TargetX);
        Assert.InRange(locked.TargetY, aim.TargetY, aim.TargetY + 1f);
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
        bool remainsActive = fixture.State.TryCaptureNpcSnapshot(spawned.Handle, out NpcSnapshot damaged);
        Assert.True(!remainsActive || damaged.Simulation.Life < spawned.Simulation.Life);
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
        Assert.Equal(999, peacefulAmmo.Stack);

        fixture.State.Apply(new PlayerPvpToggleRuntimeCommand(opponent, Hostile: true));
        fixture.State.Tick();
        Assert.Equal(1, fixture.Projectiles.ActiveCount);
        Assert.True(fixture.ServerPlayers.TryGetItem(
            bot.ServerPlayerId,
            VanillaPlayerItemSlotCatalog.AmmoSlotStart,
            out ServerPlayerItemState hostileAmmo));
        Assert.Equal(998, hostileAmmo.Stack);
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
            WeaponPolicy = RuntimeBotWeaponPolicy.Bow
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
    public async Task Player_bot_auto_mana_consumes_real_inventory_stack_when_mana_is_missing()
    {
        using var fixture = new Fixture();
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.True(fixture.ServerPlayers.SetVitals(bot.ServerPlayerId, new ServerPlayerVitalsState(500, 500, 10, 200)));
        fixture.State.Tick();
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot state));
        Assert.Equal(200, state.Mana);
        Assert.True(fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, 4, out ServerPlayerItemState mana));
        Assert.Equal(VanillaItemIds.GreaterManaPotion, mana.ItemType);
        Assert.Equal(29, mana.Stack);
    }

    [Fact]
    public async Task Healing_and_mana_in_same_tick_preserve_both_vitals_and_consume_once()
    {
        using var fixture = new Fixture();
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.True(fixture.ServerPlayers.SetVitals(bot.ServerPlayerId, new ServerPlayerVitalsState(250, 500, 10, 200)));
        fixture.State.Tick();
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot state));
        Assert.Equal(450, state.Life);
        Assert.Equal(200, state.Mana);
        Assert.True(fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, 3, out ServerPlayerItemState heal));
        Assert.True(fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, 4, out ServerPlayerItemState mana));
        Assert.Equal(29, heal.Stack);
        Assert.Equal(29, mana.Stack);
        fixture.Tick(3);
        Assert.True(fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, 3, out heal));
        Assert.True(fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, 4, out mana));
        Assert.Equal(29, heal.Stack);
        Assert.Equal(29, mana.Stack);
    }

    [Fact]
    public async Task Minor_vital_loss_does_not_waste_large_potions()
    {
        using var fixture = new Fixture();
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.True(fixture.ServerPlayers.SetVitals(bot.ServerPlayerId, new ServerPlayerVitalsState(499, 500, 199, 200)));
        fixture.Tick(3);
        Assert.True(fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, 3, out ServerPlayerItemState heal));
        Assert.True(fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, 4, out ServerPlayerItemState mana));
        Assert.Equal(30, heal.Stack);
        Assert.Equal(30, mana.Stack);
    }

    [Fact]
    public async Task Player_bot_configuration_updates_server_owned_godmode()
    {
        using var fixture = new Fixture();
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        RuntimeBotSnapshot configured = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(bot.Id, bot.Configuration with { GodMode = true }));
        Assert.True(configured.Configuration.GodMode);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot state));
        Assert.True(state.GodMode);
    }

    [Fact]
    public async Task Player_bot_godmode_blocks_authoritative_pve_damage_and_disabled_godmode_allows_real_death()
    {
        using var fixture = new Fixture();
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        NpcSnapshot source = fixture.SpawnNpc(VanillaNpcIds.Zombie, 64f, 32f);
        Assert.True(fixture.ServerPlayers.SetVitals(bot.ServerPlayerId, new ServerPlayerVitalsState(1, 500, 200, 200)));

        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(
            bot.Id, bot.Configuration with { GodMode = true }));
        PlayerDamageCommitResult avoided = fixture.ServerPlayers.TryCommitAuthoritativeNpcContactDamage(
            tick: 10, source.Handle, bot.Player, damage: 2000, hitDirection: 1,
            VanillaPlayerImmunityChannel1458.General, expertMode: false, masterMode: false, out _);
        Assert.Equal(PlayerDamageCommitResult.AvoidedByGodMode, avoided);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot protectedState));
        Assert.Equal(1, protectedState.Life);
        Assert.False(protectedState.IsDead);

        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(
            bot.Id, bot.Configuration with { GodMode = false }));
        PlayerDamageCommitResult committed = fixture.ServerPlayers.TryCommitAuthoritativeNpcContactDamage(
            tick: 11, source.Handle, bot.Player, damage: 2000, hitDirection: 1,
            VanillaPlayerImmunityChannel1458.General, expertMode: false, masterMode: false, out PlayerStateSnapshot dead);
        Assert.Equal(PlayerDamageCommitResult.Committed, committed);
        Assert.Equal(0, dead.Life);
        Assert.True(dead.IsDead);
        Assert.Equal(ServerPlayerMovementIntentKind.Stop, fixture.ServerPlayers.GetMovementIntent(bot.Player).Kind);

        fixture.Tick(7); // Create published at tick 0; the next bounded telemetry snapshot is tick 6.
        Assert.True(Assert.Single(fixture.Telemetry.Capture()).IsDead);
    }

    [Fact]
    public async Task Generated_player_bot_names_are_unique_non_placeholder_and_within_vanilla_limit()
    {
        using var fixture = new Fixture();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 12; i++)
        {
            RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
            Assert.InRange(bot.Name.Length, 1, 20);
            Assert.DoesNotMatch(@"^Bot\s+\d+$", bot.Name);
            Assert.True(names.Add(bot.Name), $"Duplicate bot name: {bot.Name}");
        }
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
        Assert.Equal(0, Assert.Single(fixture.Telemetry.Capture()).TeleportCount);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot windingUp));
        Assert.Equal(5, windingUp.SelectedItem);
        Assert.NotEqual(0, windingUp.ControlFlags & (1 << 5));
        fixture.Tick(45);
        Assert.Equal(1, Assert.Single(fixture.Telemetry.Capture()).TeleportCount);

        configuration = configuration with { Target = new RuntimeBotTarget(origin.Player, "origin") };
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(bot.Id, configuration));
        fixture.Tick(6);
        Assert.Equal(1, Assert.Single(fixture.Telemetry.Capture()).TeleportCount);

        fixture.Tick(180);
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
