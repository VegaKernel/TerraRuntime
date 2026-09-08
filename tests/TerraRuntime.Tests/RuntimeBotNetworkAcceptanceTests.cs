using System.Buffers;
using System.Reflection;
using TerraRuntime.Application.Bots;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.HostContracts;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeBotNetworkAcceptanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_bot_survives_boot_protection_then_lava_death_obeys_godmode(bool godMode)
    {
        using var f = new Fixture(botSpawnX: 160, botSpawnY: 160, environmentKnown: true);
        f.SpawnPlayingConnection(2, 2);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await f.CreateBotAsync(RuntimeBotCreateRequest.Player));
        // Random loadouts do not promise lava protection or a fixed defense. Pin only this fixture's
        // equipment, so the independent packet118 expectation is source80-45=35 for every random kit.
        for (short slot = VanillaPlayerItemSlotCatalog.ArmorStart; slot < VanillaPlayerItemSlotCatalog.BaselineFunctionalArmorEndExclusive; slot++)
            Assert.True(f.ServerPlayers.SetItem(bot.ServerPlayerId, new ServerPlayerItemState(slot, default, 0, default, 0)));
        Assert.True(f.ServerPlayers.SetItem(bot.ServerPlayerId, new ServerPlayerItemState(
            (short)(VanillaPlayerItemSlotCatalog.ArmorStart + 7), new ItemTypeId(5000), 1, default, 0)));
        FillLavaPool(f.Tiles);
        Assert.True(f.ServerPlayers.SetGodMode(bot.ServerPlayerId, godMode));
        for (int i = 0; i < 420; i++) { f.State.Tick(); f.DrainFrames(); }
        Assert.True(f.ServerPlayers.TryGet(bot.Player, out var protectedBot));
        Assert.Equal(500, protectedBot.Life);
        // Remove healing supplies only in this fixture, so auto-heal cannot mask a lethal environmental hit.
        for (short slot = 0; slot < 50; slot++)
            Assert.True(f.ServerPlayers.SetItem(bot.ServerPlayerId, new ServerPlayerItemState(slot, default, 0, default, 0)));
        Assert.True(f.ServerPlayers.SetVitals(bot.ServerPlayerId, new ServerPlayerVitalsState(1, 500, 200, 200)));
        f.DrainFrames();
        f.State.Tick();
        Assert.True(f.ServerPlayers.TryGet(bot.Player, out var after));
        Assert.Equal(!godMode, after.IsDead);
        var frames = f.DrainFrames();
        if (godMode) Assert.DoesNotContain(frames, frame => frame.MessageId == 118);
        else
        {
            byte[] death = Assert.Single(frames, frame => frame.MessageId == 118).Payload.ToArray();
            Assert.Equal(new byte[] { bot.Player.Slot.Value, 8, 2, 35, 0, 1, 0 }, death);
        }
    }

    [Fact]
    public async Task Real_random_bot_without_lava_boots_takes_damage_on_first_contact()
    {
        using var f = new Fixture(botSpawnX: 160, botSpawnY: 160, environmentKnown: true);
        f.SpawnPlayingConnection(2, 2);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await f.CreateBotAsync(RuntimeBotCreateRequest.Player));
        FillLavaPool(f.Tiles);
        f.DrainFrames();
        f.State.Tick();
        Assert.True(f.ServerPlayers.TryGet(bot.Player, out var after));
        Assert.InRange(after.Life, 1, 499);
        Assert.Contains(f.DrainFrames(), frame => frame.MessageId == 50);
    }

    [Fact]
    public async Task Server_owned_burning_has_packet50_late_join_baseline_and_packet118_dot_death()
    {
        using var f = new Fixture(botSpawnX: 160, botSpawnY: 160, environmentKnown: true);
        f.SpawnPlayingConnection(2, 2);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await f.CreateBotAsync(RuntimeBotCreateRequest.Player));
        for (short slot = 0; slot < 50; slot++)
            Assert.True(f.ServerPlayers.SetItem(bot.ServerPlayerId, new ServerPlayerItemState(slot, default, 0, default, 0)));
        for (short slot = VanillaPlayerItemSlotCatalog.ArmorStart; slot < VanillaPlayerItemSlotCatalog.BaselineFunctionalArmorEndExclusive; slot++)
            Assert.True(f.ServerPlayers.SetItem(bot.ServerPlayerId, new ServerPlayerItemState(slot, default, 0, default, 0)));
        Assert.True(f.ServerPlayers.SetVitals(bot.ServerPlayerId, new ServerPlayerVitalsState(81, 500, 200, 200)));
        FillLavaPool(f.Tiles);
        f.DrainFrames();
        f.State.Tick();
        var first = f.DrainFrames();
        byte[] expectedBuff = [bot.Player.Slot.Value, 24, 0, 0, 0];
        Assert.Equal(expectedBuff, Assert.Single(first, frame => frame.MessageId == 50).Payload.ToArray());
        Assert.DoesNotContain(first, frame => frame.MessageId is 55 or 118);
        var late = f.CreateOutboundQueue();
        f.SpawnPlayingConnection(3, 3, late);
        Assert.Contains(f.DrainFrames(late), frame => frame.MessageId == 50 && frame.Payload.ToArray().SequenceEqual(expectedBuff));
        Assert.True(f.ServerPlayers.TryTeleport(bot.ServerPlayerId, 800, 160));
        f.DrainFrames();
        for (int i = 0; i < 14; i++) { f.State.Tick(); f.DrainFrames(); }
        Assert.True(f.ServerPlayers.TryGet(bot.Player, out var alive));
        Assert.Equal(1, alive.Life);
        f.State.Tick();
        var final = f.DrainFrames();
        Assert.Equal(new byte[] { bot.Player.Slot.Value, 8, 8, 10, 0, 1, 0 },
            Assert.Single(final, frame => frame.MessageId == 118).Payload.ToArray());
        Assert.Equal(new byte[] { bot.Player.Slot.Value, 0, 0 },
            Assert.Single(final, frame => frame.MessageId == 50).Payload.ToArray());
        Assert.DoesNotContain(final, frame => frame.MessageId == 55);
    }

    private static void FillLavaPool(WorldTileStore tiles)
    {
        for (int x = 8; x <= 20; x++)
        for (int y = 8; y <= 26; y++)
            tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = x is 8 or 20 || y is 8 or 26
                ? new WorldTile { Type = 1, Flags = WorldTileFlags.Active }
                : new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Lava };
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Client_melee_claim_reaches_bot_only_with_pvp_and_respects_godmode(bool godMode, bool hostile)
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 120f);
        ConnectionHandle attacker = fixture.SpawnPlayingConnection(10, 10);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.True(fixture.ServerPlayers.SetHostile(bot.ServerPlayerId, hostile));
        Assert.True(fixture.ServerPlayers.SetGodMode(bot.ServerPlayerId, godMode));
        Assert.True(fixture.ServerPlayers.SetVitals(bot.ServerPlayerId, new ServerPlayerVitalsState(1, 500, 200, 200)));
        fixture.State.Apply(new PlayerPvpToggleRuntimeCommand(attacker, Hostile: true));
        fixture.State.Apply(new PlayerEquipmentRuntimeCommand(attacker,
            new PlayerEquipmentCommitRequest(attacker.Player.Slot, VanillaPlayerItemSlotCatalog.ArmorStart, 0, 0, 0, 0)));
        fixture.State.Apply(new PlayerEquipmentRuntimeCommand(attacker,
            new PlayerEquipmentCommitRequest(attacker.Player.Slot, 0, 1, 0, checked((short)VanillaItemIds.CopperBroadsword.Value), 0)));
        fixture.DrainFrames();
        var reason = new TerrariaPlayerDeathReasonState(attacker.Player.Slot.Value, -1, -1, -1, 0, 0, 0, null);
        // The claim's damage is deliberately 1; authoritative weapon/equipment math remains the input.
        var hurt = new TerrariaPlayerHurtState(bot.Player.Slot.Value, reason, 1, 1, 2, -1);
        fixture.State.Apply(new ClientPlayerPvpHitRuntimeCommand(attacker, hurt));
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot victim));
        bool killed = hostile && !godMode;
        Assert.Equal(killed, victim.IsDead);
        TerrariaFrame[] frames = fixture.DrainFrames();
        if (killed)
        {
            TerrariaFrame death = Assert.Single(frames, frame => frame.MessageId == 118);
            byte[] bytes = death.Payload.ToArray();
            Assert.Equal(bot.Player.Slot.Value, bytes[0]);
            Assert.Equal(1, bytes[1]); // PlayerDeathReason: only source player present.
            Assert.Equal(attacker.Player.Slot.Value, System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(2)));
            Assert.Equal(1, bytes[^1]); // PvP death bit.
        }
        else
            Assert.DoesNotContain(frames, frame => frame.MessageId == 118);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Trusted_player_projectile_can_kill_bot_but_untrusted_or_godmode_cannot(bool godMode, bool trusted)
    {
        using var fixture = new Fixture(botSpawnX: 1000f, botSpawnY: 800f);
        ConnectionHandle attacker = fixture.SpawnPlayingConnection(75, 50);
        ConnectionHandle escort = fixture.SpawnPlayingConnection(65, 50);
        fixture.State.Apply(new PlayerEquipmentRuntimeCommand(attacker,
            new PlayerEquipmentCommitRequest(attacker.Player.Slot, VanillaPlayerItemSlotCatalog.ArmorStart, 0, 0, 0, 0)));
        fixture.State.Apply(new PlayerPvpToggleRuntimeCommand(attacker, Hostile: true));
        fixture.State.Apply(new PlayerPvpToggleRuntimeCommand(escort, Hostile: true));
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = await fixture.ConfigureAsync(bot.Id, bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow, Target = new RuntimeBotTarget(escort.Player, "escort"), GodMode = godMode
        });
        var update = new ProjectileStateUpdate(VanillaProjectileIds.Bullet, attacker.Player.Slot.Value,
            1000f, 800f, 1f, 0f, default, 0, 2000, 0f, 0);
        Assert.True(fixture.Projectiles.TrySpawnVanilla(in update, out ProjectileSnapshot projectile));
        if (trusted)
            Assert.True(fixture.Projectiles.TryMarkCombatTrusted(projectile.Handle, attacker.Player));
        fixture.DrainFrames();
        fixture.State.Tick();
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot victim));
        Assert.Equal(trusted && !godMode, victim.IsDead);
        Assert.Equal(trusted && !godMode, fixture.DrainFrames().Any(frame => frame.MessageId == 118));
    }

    [Fact]
    public async Task Player_bot_materializes_complete_observer_visible_protocol_baseline_and_pvp_toggle()
    {
        using var fixture = new Fixture();
        ConnectionHandle target = fixture.SpawnPlayingConnection(spawnTileX: 10, spawnTileY: 10);
        fixture.DrainFrames();

        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(
            await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        TerrariaFrame[] baseline = fixture.DrainFrames();

        Assert.Contains(baseline, frame => frame.MessageId == (byte)TerrariaMessageId.PlayerActive &&
            TryReadPlayerActive(in frame, bot.Player.Slot.Value, active: true));
        Assert.Contains(baseline, frame =>
            TerrariaPlayerAppearanceCodec.TryDecode(frame, out TerrariaPlayerAppearanceState appearance) ==
                TerrariaPlayerAppearanceDecodeResult.Decoded &&
            appearance.PlayerId == bot.Player.Slot.Value && appearance.Name == bot.Name);
        Assert.Contains(baseline, frame =>
            TerrariaPlayerVitalsCodec.TryDecodeHealth(frame, out TerrariaPlayerHealthState health) ==
                TerrariaPlayerHealthDecodeResult.Decoded &&
            health.PlayerId == bot.Player.Slot.Value && health.Life == 500 && health.MaxLife == 500);
        Assert.Contains(baseline, frame =>
            TerrariaPlayerEquipmentCodec.TryDecode(frame, out TerrariaPlayerEquipmentState equipment) ==
                TerrariaPlayerEquipmentDecodeResult.Decoded &&
            equipment.PlayerId == bot.Player.Slot.Value && equipment.SlotId == 0 &&
            equipment.ItemNetId == VanillaItemIds.Muramasa.Value);
        Assert.Contains(baseline, frame =>
            TerrariaPlayerEquipmentCodec.TryDecode(frame, out TerrariaPlayerEquipmentState equipment) ==
                TerrariaPlayerEquipmentDecodeResult.Decoded &&
            equipment.PlayerId == bot.Player.Slot.Value && equipment.SlotId == 1 &&
            equipment.ItemNetId == VanillaItemIds.PlatinumBow.Value);
        Assert.Contains(baseline, frame =>
            TerrariaPlayerEquipmentCodec.TryDecode(frame, out TerrariaPlayerEquipmentState equipment) ==
                TerrariaPlayerEquipmentDecodeResult.Decoded &&
            equipment.PlayerId == bot.Player.Slot.Value && equipment.SlotId == 2 &&
            IsSupportedBotGun(equipment.ItemNetId));
        Assert.Contains(baseline, frame =>
            TerrariaPlayerEquipmentCodec.TryDecode(frame, out TerrariaPlayerEquipmentState equipment) ==
                TerrariaPlayerEquipmentDecodeResult.Decoded &&
            equipment.PlayerId == bot.Player.Slot.Value &&
            equipment.SlotId == VanillaPlayerItemSlotCatalog.ArmorStart + 3 &&
            equipment.ItemNetId == VanillaItemIds.FishronWings.Value);
        Assert.Contains(baseline, frame =>
            TerrariaPlayerCombatCodec.TryDecodePvpToggle(frame, out byte player, out bool hostile) &&
            player == bot.Player.Slot.Value && !hostile);
        Assert.Contains(baseline, frame =>
            TerrariaPlayerMovementDecoder.TryDecode(frame, out TerrariaPlayerMovementRequest movement) ==
                TerrariaPlayerMovementDecodeResult.Decoded && movement.ClaimedPlayerId == bot.Player.Slot.Value);
        Assert.All(baseline.Where(frame => frame.MessageId == 13 && frame.Payload.ToArray()[0] == bot.Player.Slot.Value),
            frame => Assert.NotEqual(0, frame.Payload.ToArray()[2] & 0x10)); // MessageBuffer: normal gravDir.

        RuntimeBotConfiguration configuration = bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow,
            Target = new RuntimeBotTarget(target.Player, "target")
        };
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(bot.Id, configuration));
        fixture.DrainFrames();

        fixture.State.Apply(new PlayerPvpToggleRuntimeCommand(target, Hostile: true));
        fixture.DrainFrames(); // target's own packet-30 relay is not the bot transition under test.
        fixture.State.Tick();
        TerrariaFrame[] hostileFrames = fixture.DrainFrames();
        Assert.Contains(hostileFrames, frame =>
            TerrariaPlayerMovementDecoder.TryDecode(frame, out TerrariaPlayerMovementRequest movement) ==
                TerrariaPlayerMovementDecodeResult.Decoded &&
            movement.ClaimedPlayerId == bot.Player.Slot.Value);
        Assert.Contains(hostileFrames, frame =>
            TerrariaPlayerCombatCodec.TryDecodePvpToggle(frame, out byte player, out bool hostile) &&
            player == bot.Player.Slot.Value && hostile);

        fixture.State.Apply(new PlayerPvpToggleRuntimeCommand(target, Hostile: false));
        fixture.DrainFrames();
        fixture.State.Tick();
        TerrariaFrame[] peacefulFrames = fixture.DrainFrames();
        Assert.Contains(peacefulFrames, frame =>
            TerrariaPlayerCombatCodec.TryDecodePvpToggle(frame, out byte player, out bool hostile) &&
            player == bot.Player.Slot.Value && !hostile);
    }

    [Fact]
    public async Task Player_bot_guard_projectile_and_ammo_commit_cross_the_production_replication_graph()
    {
        using var fixture = new Fixture();
        ConnectionHandle target = fixture.SpawnPlayingConnection(spawnTileX: 10, spawnTileY: 10);
        fixture.DrainFrames();
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        RuntimeBotConfiguration configuration = bot.Configuration with
        {
            Mode = RuntimeBotMode.Guard,
            Target = new RuntimeBotTarget(target.Player, "target"),
            WeaponPolicy = RuntimeBotWeaponPolicy.Bow
        };
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(bot.Id, configuration));
        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out PlayerStateSnapshot targetState));
        fixture.SpawnNpc(VanillaNpcIds.Zombie, targetState.PositionX + 48f, targetState.PositionY);
        fixture.DrainFrames();

        fixture.State.Tick();
        TerrariaFrame[] frames = fixture.DrainFrames();

        Assert.Contains(frames, frame =>
            TerrariaProjectileDecoder.TryDecodeUpdate(frame, out TerrariaProjectileUpdateState projectile) ==
                TerrariaProjectileDecodeResult.Decoded &&
            projectile.Key.Spawner == bot.Player.Slot.Value &&
            projectile.ProjectileType == VanillaProjectileIds.UnholyArrow.Value);
        Assert.Contains(frames, frame =>
            TerrariaPlayerEquipmentCodec.TryDecode(frame, out TerrariaPlayerEquipmentState equipment) ==
                TerrariaPlayerEquipmentDecodeResult.Decoded &&
            equipment.PlayerId == bot.Player.Slot.Value &&
            equipment.SlotId >= VanillaPlayerItemSlotCatalog.AmmoSlotStart &&
            equipment.SlotId < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive &&
            equipment.ItemNetId == VanillaItemIds.UnholyArrow.Value && equipment.Stack == 998);
        Assert.Contains(frames, frame =>
            TerrariaPlayerMovementDecoder.TryDecode(frame, out TerrariaPlayerMovementRequest movement) ==
                TerrariaPlayerMovementDecodeResult.Decoded &&
            movement.ClaimedPlayerId == bot.Player.Slot.Value && movement.SelectedItem == 0);
        Assert.Contains(frames, frame => frame.MessageId == 13 &&
            frame.Payload.ToArray()[0] == bot.Player.Slot.Value &&
            (frame.Payload.ToArray()[1] & 0x20) != 0 && (frame.Payload.ToArray()[4] & 0x40) != 0);
        Assert.Contains(frames, frame => frame.MessageId == 41 && frame.Payload.Length == 7 &&
            frame.Payload.ToArray()[0] == bot.Player.Slot.Value);
    }

    [Fact]
    public async Task Catchup_uses_owned_mirror_then_replicates_recall_floor_at_source_half_time()
    {
        using var fixture = new Fixture(botSpawnX: 32, botSpawnY: 32);
        ConnectionHandle target = fixture.SpawnPlayingConnection(150, 20);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.True(fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, 5, out ServerPlayerItemState mirror));
        Assert.Equal(VanillaItemIds.MagicMirror, mirror.ItemType);
        _ = await fixture.ConfigureAsync(bot.Id, bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow, Target = new RuntimeBotTarget(target.Player, "target")
        });
        fixture.DrainFrames();
        fixture.State.Tick();
        TerrariaFrame[] windup = fixture.DrainFrames();
        Assert.Contains(windup, frame =>
            TerrariaPlayerMovementDecoder.TryDecode(frame, out TerrariaPlayerMovementRequest movement) ==
                TerrariaPlayerMovementDecodeResult.Decoded && movement.ClaimedPlayerId == bot.Player.Slot.Value &&
            movement.SelectedItem == 5 && (movement.ControlFlags & (1 << 5)) != 0);
        Assert.DoesNotContain(windup, frame => frame.MessageId == (byte)TerrariaMessageId.PlayerSpawn);
        for (int tick = 0; tick < 44; tick++)
            fixture.State.Tick();
        Assert.DoesNotContain(fixture.DrainFrames(), frame => frame.MessageId == (byte)TerrariaMessageId.PlayerSpawn);
        fixture.State.Tick();
        TerrariaFrame recall = Assert.Single(fixture.DrainFrames(), frame => frame.MessageId == (byte)TerrariaMessageId.PlayerSpawn);
        byte[] payload = recall.Payload.ToArray();
        Assert.Equal(bot.Player.Slot.Value, payload[0]);
        Assert.Equal(2, payload[14]); // PlayerSpawnContext.RecallFromItem.
        short floorX = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(1));
        short floorY = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(3));
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot landed));
        Assert.Equal(floorX * 16 + 8 - PlayerAuthority.VanillaBasePlayerWidth / 2, landed.PositionX);
        Assert.InRange(landed.PositionY, floorY * 16 - PlayerAuthority.VanillaBasePlayerHeight,
            floorY * 16 - PlayerAuthority.VanillaBasePlayerHeight + 1); // Same-tick ordinary gravity.
        Assert.True(fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, 5, out mirror));
        Assert.Equal(1, mirror.Stack);
        for (int tick = 0; tick < 50; tick++)
            fixture.State.Tick();
        Assert.DoesNotContain(fixture.DrainFrames(), frame => frame.MessageId == (byte)TerrariaMessageId.PlayerSpawn);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot resumed));
        Assert.NotEqual(5, resumed.SelectedItem);
    }

    [Fact]
    public async Task Losing_follow_target_cancels_mirror_without_recall_or_stale_held_slot()
    {
        using var fixture = new Fixture(botSpawnX: 32, botSpawnY: 32);
        _ = fixture.SpawnPlayingConnection(8, 10);
        var targetOutbound = fixture.CreateOutboundQueue();
        ConnectionHandle target = fixture.SpawnPlayingConnection(150, 20, targetOutbound);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = await fixture.ConfigureAsync(bot.Id, bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow, Target = new RuntimeBotTarget(target.Player, "target")
        });
        fixture.State.Tick();
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot windingUp));
        Assert.Equal(5, windingUp.SelectedItem);
        fixture.State.Apply(new PlayerDisconnectRuntimeCommand(target));
        fixture.DrainFrames();

        fixture.State.Tick();
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot cancelled));
        Assert.Equal(0, cancelled.SelectedItem);
        Assert.Equal(0, cancelled.ControlFlags & (1 << 5));
        for (int tick = 0; tick < 100; tick++)
            fixture.State.Tick();
        Assert.DoesNotContain(fixture.DrainFrames(), frame => frame.MessageId == (byte)TerrariaMessageId.PlayerSpawn);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    public async Task Npc_projectile_and_termination_explosion_reach_bot_vitals_only_with_provenance(
        bool explosion, bool godMode, bool trusted)
    {
        using var fixture = new Fixture(botSpawnX: 100, botSpawnY: 100);
        _ = fixture.SpawnPlayingConnection(100, 80);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = await fixture.ConfigureAsync(bot.Id, bot.Configuration with { GodMode = godMode });
        NpcSnapshot cultist = fixture.SpawnNpc(VanillaNpcIds.LunaticCultist, 1200, 500);
        var intent = new NpcAiProjectileIntent(VanillaProjectileIds.CultistBossFireBall,
            explosion ? 160 : 100, 100, 0, 0, 1000, 0)
        {
            TimeLeftOverride = explosion ? 1 : 120
        };
        Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(fixture.Projectiles,
            trusted ? cultist.Handle : default, intent, out _));
        fixture.DrainFrames();
        fixture.State.Tick();
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot victim));
        bool killed = trusted && !godMode;
        Assert.Equal(killed ? 0 : 500, victim.Life);
        Assert.Equal(killed, victim.IsDead);
        TerrariaFrame[] frames = fixture.DrainFrames();
        if (killed)
            Assert.Single(frames, frame => frame.MessageId == (byte)TerrariaMessageId.PlayerDeathV2);
        else
            Assert.DoesNotContain(frames, frame => frame.MessageId == (byte)TerrariaMessageId.PlayerDeathV2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Npc_contact_pass_respects_live_friendly_flag_for_bot(bool friendly)
    {
        using var fixture = new Fixture(botSpawnX: 100, botSpawnY: 100);
        _ = fixture.SpawnPlayingConnection(100, 80);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        NpcSnapshot npc = fixture.SpawnNpc(VanillaNpcIds.Zombie, 100, 100);
        var update = new NpcStateUpdate(npc.Type, npc.NetId, npc.PositionX, npc.PositionY,
            npc.VelocityX, npc.VelocityY, npc.Target, npc.Ai,
            npc.Simulation with { Friendly = friendly, DamageOverride = 2000 });
        Assert.True(fixture.Npcs.TryUpdate(npc.Handle, update, out _));
        fixture.DrainFrames();
        fixture.State.Tick();
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot victim));
        Assert.Equal(friendly ? 500 : 0, victim.Life);
        Assert.Equal(!friendly, victim.IsDead);
    }

    [Theory]
    [InlineData(36, false)]
    [InlineData(100, true)]
    public async Task Shared_contact_pass_uses_committed_physical_body_for_bot_damage(int width, bool hit)
    {
        using var fixture = new Fixture(botSpawnX: 100, botSpawnY: 100);
        _ = fixture.SpawnPlayingConnection(100, 80);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        NpcSnapshot npc = fixture.SpawnNpc(VanillaNpcIds.Zombie, 40, 100);
        var update = new NpcStateUpdate(npc.Type, npc.NetId, npc.PositionX, npc.PositionY,
            0, 0, npc.Target, npc.Ai, npc.Simulation with
            { HitboxOverride = new NpcHitboxDimensions(width, 100), DamageOverride = 2000 });
        Assert.True(fixture.Npcs.TryUpdate(npc.Handle, in update, out _));
        fixture.DrainFrames();
        fixture.State.Tick();
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot victim));
        Assert.Equal(hit ? 0 : 500, victim.Life);
        Assert.Equal(hit, victim.IsDead);
    }

    [Fact]
    public async Task Player_bot_pickup_removes_exact_world_item_on_wire_and_updates_inventory_on_wire()
    {
        using var fixture = new Fixture(botSpawnX: 64f, botSpawnY: 64f);
        _ = fixture.SpawnPlayingConnection(spawnTileX: 10, spawnTileY: 10);
        fixture.DrainFrames();
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        fixture.DrainFrames();

        Assert.True(fixture.WorldItems.TryAllocate(
            CreateWorldItem(VanillaItemIds.UnholyArrow, 64f, 64f, stack: 7),
            out WorldItemSnapshot item));
        fixture.DrainFrames(); // initial packet-21 drop.

        fixture.State.Tick();
        TerrariaFrame[] frames = fixture.DrainFrames();

        Assert.Contains(frames, frame =>
            TerrariaWorldItemDropDecoder.TryDecode(in frame, out TerrariaWorldItemDropState drop) ==
                TerrariaWorldItemDropDecodeResult.Decoded &&
            drop.ItemIndex == item.Handle.Slot && drop.Stack == 0 && drop.ItemNetId == 0);
        Assert.Contains(frames, frame =>
            TerrariaPlayerEquipmentCodec.TryDecode(frame, out TerrariaPlayerEquipmentState equipment) ==
                TerrariaPlayerEquipmentDecodeResult.Decoded &&
            equipment.PlayerId == bot.Player.Slot.Value &&
            equipment.SlotId >= VanillaPlayerItemSlotCatalog.AmmoSlotStart &&
            equipment.SlotId < VanillaPlayerItemSlotCatalog.AmmoSlotEndExclusive &&
            equipment.ItemNetId == VanillaItemIds.UnholyArrow.Value && equipment.Stack == 1006);
    }

    [Fact]
    public async Task Late_join_receives_existing_player_bot_authoritative_baseline_with_current_pvp_state()
    {
        using var fixture = new Fixture();
        ConnectionHandle target = fixture.SpawnPlayingConnection(spawnTileX: 10, spawnTileY: 10);
        fixture.DrainFrames();
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(
            bot.Id,
            bot.Configuration with
            {
                Mode = RuntimeBotMode.Follow,
                Target = new RuntimeBotTarget(target.Player, "target")
            }));
        fixture.DrainFrames();

        fixture.State.Apply(new PlayerPvpToggleRuntimeCommand(target, Hostile: true));
        fixture.DrainFrames();
        fixture.State.Tick();
        fixture.DrainFrames();

        var lateOutbound = fixture.CreateOutboundQueue();
        _ = fixture.SpawnPlayingConnection(spawnTileX: 12, spawnTileY: 10, lateOutbound);
        TerrariaFrame[] lateFrames = fixture.DrainFrames(lateOutbound);

        Assert.Contains(lateFrames, frame => frame.MessageId == (byte)TerrariaMessageId.PlayerActive &&
            TryReadPlayerActive(in frame, bot.Player.Slot.Value, active: true));
        Assert.Contains(lateFrames, frame =>
            TerrariaPlayerAppearanceCodec.TryDecode(frame, out TerrariaPlayerAppearanceState appearance) ==
                TerrariaPlayerAppearanceDecodeResult.Decoded &&
            appearance.PlayerId == bot.Player.Slot.Value && appearance.Name == bot.Name);
        Assert.Contains(lateFrames, frame =>
            TerrariaPlayerEquipmentCodec.TryDecode(frame, out TerrariaPlayerEquipmentState equipment) ==
                TerrariaPlayerEquipmentDecodeResult.Decoded &&
            equipment.PlayerId == bot.Player.Slot.Value && equipment.SlotId == 0 &&
            equipment.ItemNetId == VanillaItemIds.Muramasa.Value);
        Assert.Contains(lateFrames, frame =>
            TerrariaPlayerCombatCodec.TryDecodePvpToggle(frame, out byte player, out bool hostile) &&
            player == bot.Player.Slot.Value && hostile);
    }

    [Fact]
    public async Task Target_disconnect_clears_player_bot_mirrored_pvp_for_remaining_observers()
    {
        using var fixture = new Fixture();
        _ = fixture.SpawnPlayingConnection(spawnTileX: 8, spawnTileY: 10); // persistent observer on primary queue.
        var targetOutbound = fixture.CreateOutboundQueue();
        ConnectionHandle target = fixture.SpawnPlayingConnection(spawnTileX: 10, spawnTileY: 10, targetOutbound);
        fixture.DrainFrames();
        fixture.DrainFrames(targetOutbound);

        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(
            bot.Id,
            bot.Configuration with
            {
                Mode = RuntimeBotMode.Follow,
                Target = new RuntimeBotTarget(target.Player, "target")
            }));
        fixture.DrainFrames();
        fixture.DrainFrames(targetOutbound);

        fixture.State.Apply(new PlayerPvpToggleRuntimeCommand(target, Hostile: true));
        fixture.DrainFrames();
        fixture.DrainFrames(targetOutbound);
        fixture.State.Tick();
        Assert.Contains(fixture.DrainFrames(), frame =>
            TerrariaPlayerCombatCodec.TryDecodePvpToggle(frame, out byte player, out bool hostile) &&
            player == bot.Player.Slot.Value && hostile);
        fixture.DrainFrames(targetOutbound);

        fixture.State.Apply(new PlayerDisconnectRuntimeCommand(target));
        fixture.DrainFrames();
        fixture.DrainFrames(targetOutbound);
        fixture.State.Tick();
        TerrariaFrame[] afterDisconnect = fixture.DrainFrames();
        Assert.Contains(afterDisconnect, frame =>
            TerrariaPlayerCombatCodec.TryDecodePvpToggle(frame, out byte player, out bool hostile) &&
            player == bot.Player.Slot.Value && !hostile);
        for (int tick = 0; tick < 6; tick++) fixture.State.Tick();
        RuntimeBotSnapshot disconnected = Assert.Single(fixture.Telemetry.Capture());
        Assert.False(disconnected.Configuration.Target.IsAssigned);
        Assert.Equal(RuntimeBotMode.Idle, disconnected.Configuration.Mode);
    }

    [Fact]
    public async Task Player_bot_authoritative_death_emits_packet118_with_vanilla_npc_reason()
    {
        using var fixture = new Fixture();
        _ = fixture.SpawnPlayingConnection(spawnTileX: 10, spawnTileY: 10);
        fixture.DrainFrames();
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        NpcSnapshot npc = fixture.SpawnNpc(VanillaNpcIds.Zombie, 64f, 32f);
        fixture.DrainFrames();
        Assert.True(fixture.ServerPlayers.SetVitals(bot.ServerPlayerId, new ServerPlayerVitalsState(1, 500, 200, 200)));
        fixture.DrainFrames();

        PlayerDamageCommitResult result = fixture.ServerPlayers.TryCommitAuthoritativeNpcContactDamage(
            tick: 10, npc.Handle, bot.Player, damage: 2000, hitDirection: 1,
            TerraRuntime.Gameplay.Players.VanillaPlayerImmunityChannel1458.General,
            expertMode: false, masterMode: false, out PlayerStateSnapshot dead);
        Assert.Equal(PlayerDamageCommitResult.Committed, result);
        Assert.True(dead.IsDead);

        TerrariaFrame death = Assert.Single(fixture.DrainFrames(), frame => frame.MessageId == (byte)TerrariaMessageId.PlayerDeathV2);
        Assert.Equal(8, death.Payload.Length);
        Span<byte> payload = stackalloc byte[8];
        death.Payload.CopyTo(payload);
        Assert.Equal(bot.Player.Slot.Value, payload[0]);
        Assert.Equal(0x02, payload[1]); // PlayerDeathReason.ByNPC: only the NPC bit is present.
        Assert.Equal(npc.Handle.Slot, System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(2, 2)));
        Assert.True(System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(payload.Slice(4, 2)) > 0);
        Assert.Equal(2, payload[6]); // hitDirection + 1 for direction +1.
        Assert.Equal(0, payload[7]); // PvE death.
    }

    [Fact]
    public async Task Supported_internal_bot_buff_does_not_emit_player55_to_unrelated_observers()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle target = fixture.SpawnPlayingConnection(spawnTileX: 10, spawnTileY: 10);
        fixture.DrainFrames();
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(
            bot.Id,
            bot.Configuration with
            {
                Mode = RuntimeBotMode.Guard,
                Target = new RuntimeBotTarget(target.Player, "target"),
                WeaponPolicy = RuntimeBotWeaponPolicy.Bow
            }));
        Assert.True(fixture.WorldItems.TryAllocate(
            CreateWorldItem(VanillaItemIds.ArcheryPotion, 160f, 160f, stack: 1),
            out WorldItemSnapshot archery));
        fixture.DrainFrames();

        fixture.State.Tick();
        TerrariaFrame[] frames = fixture.DrainFrames();

        Assert.False(fixture.WorldItems.TryGetActive(archery.Handle.Slot, out _));
        Assert.DoesNotContain(frames, frame => frame.MessageId == (byte)TerrariaMessageId.AddPlayerBuffPvp);
    }


    private static bool IsSupportedBotGun(short itemNetId) =>
        itemNetId == VanillaItemIds.Handgun.Value ||
        itemNetId == VanillaItemIds.Minishark.Value ||
        itemNetId == VanillaItemIds.Revolver.Value ||
        itemNetId == VanillaItemIds.Musket.Value;

    private static bool TryReadPlayerActive(in TerrariaFrame frame, byte expectedPlayer, bool active)
    {
        if (frame.MessageId != (byte)TerrariaMessageId.PlayerActive || frame.Payload.Length != 2)
            return false;
        Span<byte> payload = stackalloc byte[2];
        frame.Payload.CopyTo(payload);
        return payload[0] == expectedPlayer && payload[1] == (active ? (byte)1 : (byte)0);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clientless_guard_bot_can_kill_expert_boss_without_aborting_loop_or_giving_observer_its_bag(bool master)
    {
        using var fixture = new Fixture(expertMode: true, masterMode: master);
        ConnectionHandle target = fixture.SpawnPlayingConnection(10, 10);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = Assert.IsType<RuntimeBotSnapshot>(await fixture.ConfigureAsync(bot.Id, bot.Configuration with
        {
            Mode = RuntimeBotMode.Guard,
            Target = new RuntimeBotTarget(target.Player, "observer"),
            WeaponPolicy = RuntimeBotWeaponPolicy.Bow,
            GodMode = true
        }));
        Assert.True(fixture.State.TryCapturePlayerSnapshot(target.Player, out PlayerStateSnapshot player));
        NpcSnapshot boss = fixture.SpawnNpc(VanillaNpcIds.QueenSlime, player.PositionX + 48, player.PositionY);
        var weak = new NpcStateUpdate(boss.Type, boss.NetId, boss.PositionX, boss.PositionY,
            boss.VelocityX, boss.VelocityY, boss.Target, boss.Ai,
            boss.Simulation with { Life = 1, DamageOverride = 0 });
        Assert.True(fixture.Npcs.TryUpdate(boss.Handle, weak, out _));
        fixture.DrainFrames();
        for (int tick = 0; tick < 180 && fixture.Npcs.TryGet(boss.Handle, out _); tick++)
        {
            fixture.State.Tick();
            Assert.DoesNotContain(fixture.DrainFrames(), static frame => frame.MessageId == 90);
        }
        Assert.False(fixture.Npcs.TryGet(boss.Handle, out _));
        Assert.True(fixture.State.WorldProgression.IsCompleted(TerraRuntime.World.VanillaWorldProgressionId.QueenSlime));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(16);
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly RuntimeConnectionRegistry connections = new();
        private readonly RuntimeProjectileReplicationRegistry projectileReplication = new();
        private readonly RuntimeWorldItemReplicationRegistry worldItemReplication = new();
        private readonly RuntimeNpcReplicationRegistry npcReplication = new();
        private long nextConnectionId = 1;

        public Fixture(float botSpawnX = 32f, float botSpawnY = 32f, bool expertMode = false, bool masterMode = false, bool environmentKnown = false)
        {
            Outbound = new TerrariaConnectionOutboundQueue(
                new OutboundQueueOptions(maxFrames: 512, maxQueuedBytes: 512 * 1024, maxFrameBytes: 64 * 1024));
            var identities = new ServerPlayerSlotRegistry(slots);
            var serverPlayerStates = new ServerPlayerStateStore(identities, slots.Capacity);
            var tiles = new WorldTileStore(new WorldDimensions(300, 120));
            Tiles = tiles;
            ServerPlayers = new ServerPlayerAuthority(serverPlayerStates, identities, tiles, connections);
            WorldItems = new RuntimeWorldItemStore(worldItemReplication);
            Projectiles = new RuntimeProjectileStore(commitSink: projectileReplication);
            Npcs = new RuntimeNpcStore(commitSink: npcReplication);
            Telemetry = new RuntimeBotTelemetry();

            IRuntimePlayerEventSink entityEvents = new RuntimePlayerEventFanout(
                projectileReplication,
                new RuntimePlayerEventFanout(worldItemReplication, npcReplication));
            IRuntimePlayerEventSink playerEvents = new RuntimePlayerEventFanout(connections, entityEvents);
            State = new ServerRuntimeState(
                playerEvents,
                npcs: Npcs,
                worldTiles: tiles,
                projectiles: Projectiles,
                worldItems: WorldItems,
                projectileReplication: projectileReplication,
                npcReplication: npcReplication,
                worldItemReplication: worldItemReplication,
                serverPlayers: ServerPlayers,
                botTelemetry: Telemetry,
                botSpawnX: botSpawnX,
                botSpawnY: botSpawnY,
                expertMode: expertMode,
                masterMode: masterMode,
                townCommerceWorldFacts: environmentKnown ? default(RuntimeTownCommerceWorldFacts1458) : null);
        }

        public ServerRuntimeState State { get; }
        public WorldTileStore Tiles { get; }
        public ServerPlayerAuthority ServerPlayers { get; }
        public RuntimeWorldItemStore WorldItems { get; }
        public RuntimeProjectileStore Projectiles { get; }
        public RuntimeNpcStore Npcs { get; }
        public RuntimeBotTelemetry Telemetry { get; }
        public TerrariaConnectionOutboundQueue Outbound { get; }

        public TerrariaConnectionOutboundQueue CreateOutboundQueue() => new(
            new OutboundQueueOptions(maxFrames: 512, maxQueuedBytes: 512 * 1024, maxFrameBytes: 64 * 1024));

        public ConnectionHandle SpawnPlayingConnection(
            short spawnTileX,
            short spawnTileY,
            TerrariaConnectionOutboundQueue? outbound = null)
        {
            outbound ??= Outbound;
            Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
            var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            sessions.Add(session);
            Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
            Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
            GameCommandSourceId source = GameCommandSourceId.FromConnection(nextConnectionId++);
            Assert.True(connections.TryRegister(source, outbound));
            Assert.True(projectileReplication.TryRegister(source, outbound));
            Assert.True(worldItemReplication.TryRegister(source, outbound));
            Assert.True(npcReplication.TryRegister(source, outbound));
            var connection = new ConnectionHandle(source, session.Handle);
            var request = new PlayerSpawnCommitRequest(session.Slot, spawnTileX, spawnTileY, 0, 0, 0, 0, 0);
            State.Apply(new PlayerSpawnRuntimeCommand(connection, session, request));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
            State.Apply(new PlayerHealthRuntimeCommand(
                connection,
                new PlayerHealthCommitRequest(session.Slot, Life: 100, MaxLife: 100)));
            return connection;
        }

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

        public async Task<bool> DespawnAsync(int id)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            State.Apply(new RuntimeBotDespawnCommand(id, completion));
            return await completion.Task;
        }

        public NpcSnapshot SpawnNpc(NpcTypeId type, float x, float y)
        {
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
                var completion = new TaskCompletionSource<NpcSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
                State.Apply(new NpcSpawnRuntimeCommand(slot, state, completion));
                if (completion.Task.GetAwaiter().GetResult() is NpcSnapshot npc)
                    return npc;
            }
            throw new InvalidOperationException("No NPC slot available in network acceptance fixture.");
        }

        public TerrariaFrame[] DrainFrames() => DrainFrames(Outbound);

        public TerrariaFrame[] DrainFrames(TerrariaConnectionOutboundQueue outbound)
        {
            var frames = new List<TerrariaFrame>();
            BoundedOutboundQueue queue = GetInnerQueue(outbound);
            while (queue.TryRead(out OutboundFrame queuedFrame))
            {
                var sequence = new ReadOnlySequence<byte>(queuedFrame.Bytes);
                Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out TerrariaFrame frame));
                Assert.True(sequence.IsEmpty);
                frames.Add(frame);
            }
            return [.. frames];
        }


        private static BoundedOutboundQueue GetInnerQueue(TerrariaConnectionOutboundQueue outbound)
        {
            PropertyInfo property = typeof(TerrariaConnectionOutboundQueue).GetProperty(
                "InnerQueue",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Terraria outbound queue internal reader is unavailable.");
            return Assert.IsType<BoundedOutboundQueue>(property.GetValue(outbound));
        }

        public void Dispose()
        {
            foreach (PlayerJoinSession session in sessions)
                session.Dispose();
        }
    }
}
