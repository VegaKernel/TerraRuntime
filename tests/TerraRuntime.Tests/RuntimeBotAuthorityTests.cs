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
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class RuntimeBotAuthorityTests
{
    [Fact]
    public async Task Autonomous_mining_rejects_changed_section_and_reconfiguration_selects_a_new_ore()
    {
        using var fixture = new Fixture(480, 806);
        Assert.True(fixture.Tiles.TryAttachWorldSurface(30));
        for (int x = 20; x < 70; x++) fixture.Tiles.Set(x, 53, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        fixture.Tiles.Set(58, 51, new WorldTile { Type = 7, Flags = WorldTileFlags.Active });
        var bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.NotNull(await fixture.ConfigureAsync(bot.Id, bot.Configuration with { Mode = RuntimeBotMode.Mining }));
        fixture.Tick(19);
        Assert.NotNull(Assert.Single(fixture.Telemetry.Capture()).Observation!.Value.MiningTarget);
        // ABA at the same coordinate is still a new section revision, not permission from the old observation.
        fixture.Tiles.Set(58, 51, default);
        fixture.Tiles.Set(58, 51, new WorldTile { Type = 7, Flags = WorldTileFlags.Active });
        fixture.Tick(7);
        Assert.Equal(RuntimeBotActionFailureCode.TargetChanged, Assert.Single(fixture.Telemetry.Capture()).RecentActionResult!.Value.FailureCode);
        Assert.True(fixture.Tiles.Get(58, 51).IsActive);
        Assert.True(fixture.ServerPlayers.TryTeleport(bot.ServerPlayerId, 480, 806));
        fixture.Tiles.Set(32, 51, new WorldTile { Type = 8, Flags = WorldTileFlags.Active });
        Assert.NotNull(await fixture.ConfigureAsync(bot.Id, bot.Configuration with { Mode = RuntimeBotMode.Mining, MiningOre = RuntimeBotOre.Gold }));
        fixture.Tick(90);
        Assert.False(fixture.Tiles.Get(32, 51).IsActive);
        Assert.True(fixture.Tiles.Get(58, 51).IsActive);
    }

    [Fact]
    public async Task Autonomous_mining_excavates_a_body_sized_stone_corridor_and_picks_up_the_ore()
    {
        using var fixture = new Fixture(480, 806);
        Assert.True(fixture.Tiles.TryAttachWorldSurface(30));
        for (int x = 20; x < 50; x++)
        {
            fixture.Tiles.Set(x, 49, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            fixture.Tiles.Set(x, 53, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        }
        for (int x = 32; x < 39; x++)
        for (int y = 50; y <= 52; y++)
            fixture.Tiles.Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        fixture.Tiles.Set(38, 51, new WorldTile { Type = 7, Flags = WorldTileFlags.Active });
        var bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.NotNull(await fixture.ConfigureAsync(bot.Id, bot.Configuration with { Mode = RuntimeBotMode.Mining }));
        fixture.Tick(900);
        Assert.False(fixture.Tiles.Get(38, 51).IsActive);
        int ore = 0;
        for (short slot = 0; slot < 50; slot++)
            if (fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, slot, out var item) && item.ItemType.Value == 12) ore += item.Stack;
        Assert.True(ore == 1, $"Stored={ore}; status={Assert.Single(fixture.Telemetry.Capture())}");
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out var self));
        Assert.True(self.PositionX > 520);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Autonomous_mining_cancel_or_despawn_releases_the_ore_for_another_bot(bool despawn)
    {
        using var fixture = new Fixture(480, 806);
        Assert.True(fixture.Tiles.TryAttachWorldSurface(30));
        for (int x = 20; x < 70; x++) fixture.Tiles.Set(x, 53, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        fixture.Tiles.Set(58, 51, new WorldTile { Type = 7, Flags = WorldTileFlags.Active });
        var first = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        var second = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.NotNull(await fixture.ConfigureAsync(first.Id, first.Configuration with { Mode = RuntimeBotMode.Mining }));
        Assert.NotNull(await fixture.ConfigureAsync(second.Id, second.Configuration with { Mode = RuntimeBotMode.Mining }));
        fixture.Tick(19);
        var observed = fixture.Telemetry.Capture();
        Assert.NotNull(observed.Single(b => b.Id == first.Id).Observation!.Value.MiningTarget);
        Assert.Null(observed.Single(b => b.Id == second.Id).Observation!.Value.MiningTarget);
        if (despawn)
        {
            var completion = new TaskCompletionSource<bool>();
            fixture.State.Apply(new RuntimeBotDespawnCommand(first.Id, completion));
            Assert.True(await completion.Task);
        }
        else Assert.NotNull(await fixture.ConfigureAsync(first.Id, first.Configuration with { Mode = RuntimeBotMode.Idle }));
        fixture.Tick(900);
        Assert.False(fixture.Tiles.Get(58, 51).IsActive);
        Assert.Equal(1, fixture.State.AppliedWorldItemAllocations);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void Falling_material_hits_npc_only_with_server_trust(bool trusted)
    {
        using var fixture=new Fixture(simulateProjectiles:true);
        fixture.SpawnConnectionPlayer(30, 40);
        var npc=fixture.SpawnNpc(VanillaNpcIds.Zombie,480,600);
        Assert.True(fixture.Projectiles.TrySpawnVanilla(new(new ProjectileTypeId(31),255,485,605,0,.5f,default,0,10,0,10),out var p));
        if(trusted)Assert.True(fixture.Projectiles.TryMarkCombatTrusted(p.Handle));
        fixture.Tick(1);
        Assert.True(fixture.State.TryCaptureNpcSnapshot(npc.Handle,out var after));
        Assert.Equal(trusted,after.Simulation.Life<npc.Simulation.Life);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Falling_sand_uses_normal_server_player_damage_and_respects_godmode(bool godMode)
    {
        using var fixture=new Fixture(480,600,simulateProjectiles:true);
        var bot=Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.NotNull(await fixture.ConfigureAsync(bot.Id,bot.Configuration with {GodMode=godMode}));
        fixture.Tiles.EnableFallingBlockUpdates();
        fixture.Tiles.Set(30,20,new WorldTile{Type=53,Flags=WorldTileFlags.Active});
        for(int n=0;n<65;n++)
        {
            Assert.True(fixture.ServerPlayers.TryTeleport(bot.ServerPlayerId,480,600));
            fixture.Tick(1);
        }
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player,out var self));
        if(godMode)Assert.Equal(500,self.Life);else Assert.True(self.Life<500);
    }
    [Theory]
    [InlineData(7, 12)] [InlineData(166, 699)] [InlineData(6, 11)] [InlineData(167, 700)]
    [InlineData(9, 14)] [InlineData(168, 701)] [InlineData(8, 13)] [InlineData(169, 702)]
    public async Task Autonomous_mining_selects_ore_without_human_digging_and_materializes_one_drop(int tile, int item)
    {
        using var fixture = new Fixture(480, 800);
        Assert.True(fixture.Tiles.TryAttachWorldSurface(30));
        var bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        fixture.Tiles.Set(32, 51, new WorldTile { Type = (ushort)tile, Flags = WorldTileFlags.Active });
        fixture.Tiles.Set(34, 51, new WorldTile { Type = 22, Flags = WorldTileFlags.Active });
        for (int x = 20; x < 40; x++) fixture.Tiles.Set(x, 53, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        Assert.NotNull(await fixture.ConfigureAsync(bot.Id, bot.Configuration with { Mode = RuntimeBotMode.Mining, MiningOre = (RuntimeBotOre)tile }));
        for (int tick = 0; tick < 30 && fixture.Tiles.Get(32, 51).IsActive; tick++) fixture.Tick(1);
        Assert.False(fixture.Tiles.Get(32, 51).IsActive);
        Assert.True(fixture.Tiles.Get(34, 51).IsActive);
        Assert.Equal(1, fixture.State.AppliedWorldItemAllocations);
        var drops = new WorldItemSnapshot[400];
        int count = fixture.WorldItems.CopyActive(drops);
        Assert.Equal(1, count);
        Assert.True(drops[0].TryGetItemType(out var actual));
        Assert.Equal(item, actual.Value);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out var self));
        Assert.Equal(6, self.SelectedItem);
        // Normal intersection pickup, not a direct mining-to-inventory shortcut.
        Assert.True(fixture.ServerPlayers.TryTeleport(bot.ServerPlayerId, drops[0].PositionX, drops[0].PositionY));
        fixture.Tick(1);
        Assert.Equal(0, fixture.WorldItems.CopyActive(drops));
        int stored = 0;
        for (short slot = 0; slot < 50; slot++)
            if (fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, slot, out var state) && state.ItemType.Value == item) stored += state.Stack;
        Assert.Equal(1, stored);
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, true)]
    public async Task Autonomous_mining_refuses_surface_and_full_inventory(bool surface, bool full)
    {
        using var fixture = new Fixture(480, 800);
        Assert.True(fixture.Tiles.TryAttachWorldSurface(surface ? 60 : 30));
        var bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        fixture.Tiles.Set(32, 51, new WorldTile { Type = 7, Flags = WorldTileFlags.Active });
        if (full)
            for (short slot = 1; slot < 50; slot++)
                if (slot != 6) Assert.True(fixture.ServerPlayers.SetItem(bot.ServerPlayerId,
                    new(slot, VanillaItemIds.StoneBlock, 9999, VanillaPrefixIds.None, 0)));
        Assert.NotNull(await fixture.ConfigureAsync(bot.Id, bot.Configuration with { Mode = RuntimeBotMode.Mining }));
        fixture.Tick(40);
        Assert.True(fixture.Tiles.Get(32, 51).IsActive);
        Assert.Equal(0, fixture.State.AppliedWorldItemAllocations);
    }

    [Fact]
    public async Task Addressed_pickup_action_takes_only_observed_item_and_rejects_reused_generation()
    {
        using var fixture = new Fixture(160f, 160f);
        var created = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        var bot = new BotState(created.Id, created.ServerPlayerId, created.Name, created.Configuration,
            RuntimePlayerBotLoadoutCatalog1458.Pick(new Random(42)), default, default, 0)
        {
            Player = created.Player, ObservationRevision = 1
        };
        var items = new WorldItemAuthority(new PlayerAuthority(null, fixture.Tiles), fixture.WorldItems,
            new TerraRuntime.Core.Worlds.SystemWorldItemSpawnRandom(42), null);
        var inventory = new RuntimeBotInventory(bot, fixture.ServerPlayers, items, new(), fixture.State.WorldIdentity);
        Assert.True(fixture.ServerPlayers.TryGet(created.Player, out var self));
        Assert.True(fixture.WorldItems.TryAllocate(CreateWorldItem(VanillaItemIds.SuperHealingPotion, 160f, 160f, 1), out var first));
        Assert.True(fixture.WorldItems.TryAllocate(CreateWorldItem(VanillaItemIds.SuperHealingPotion, 161f, 160f, 1), out var requested));
        var observation = new RuntimeBotObservationSnapshot(bot.Id, fixture.State.WorldIdentity, 1, 1, 0,
            self, bot.Configuration, null, null, requested, false, null, null);
        Assert.Equal(RuntimeBotActionStatus.Failure, inventory.Pickup(observation,
            new(requested.Handle.Slot, new WorldItemGeneration(requested.Handle.Generation.Value + 1))).Status);
        var context = new RuntimeBotActionContext(observation, null!, null!, inventory, null!);
        Assert.Equal(RuntimeBotActionStatus.Success, new RuntimeBotPickupUsefulItemAction().Tick(context).Status);
        Assert.True(fixture.WorldItems.TryGetActive(first.Handle.Slot, out _));
        Assert.False(fixture.WorldItems.TryGetActive(requested.Handle.Slot, out _));
        Assert.True(fixture.ServerPlayers.TryGetItem(created.ServerPlayerId, 3, out var stack));
        Assert.Equal(31, stack.Stack);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Old_bot_lifecycle_cannot_modify_a_replacement_server_player_generation(bool recovering)
    {
        using var fixture = new Fixture(160f, 160f);
        var target = fixture.SpawnConnectionPlayer(recovering ? (short)150 : (short)20, 10);
        var bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.NotNull(await fixture.ConfigureAsync(bot.Id, bot.Configuration with { Mode = RuntimeBotMode.Follow, Target = new(target.Player, "target") }));
        fixture.Tick(1);
        Assert.True(fixture.ServerPlayers.Despawn(bot.ServerPlayerId));
        var replacement = fixture.ServerPlayers.Create(bot.ServerPlayerId, 400f, 160f);
        Assert.Equal(ServerPlayerCreateStatus.Created, replacement.Status);
        Assert.NotEqual(bot.Player, replacement.Player);
        Assert.True(fixture.ServerPlayers.SetHeldItem(bot.ServerPlayerId, 2, useItem: false));
        Assert.True(fixture.ServerPlayers.SetMovementIntent(bot.ServerPlayerId, ServerPlayerMovementIntent.MoveTo(800f, 160f)));
        fixture.Tick(1);
        Assert.Equal(ServerPlayerMovementIntentKind.MoveTo, fixture.ServerPlayers.GetMovementIntent(replacement.Player).Kind);
        Assert.True(fixture.ServerPlayers.TryGet(replacement.Player, out var after));
        Assert.Equal(2, after.SelectedItem);
        Assert.Null(await fixture.ConfigureAsync(bot.Id, bot.Configuration with { GodMode = true }));
        var completion = new TaskCompletionSource<bool>();
        fixture.State.Apply(new RuntimeBotDespawnCommand(bot.Id, completion));
        Assert.False(await completion.Task);
        Assert.True(fixture.ServerPlayers.TryGet(replacement.Player, out _));
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(60, false)]
    public async Task Follow_assistance_uses_existing_tile_authority_and_never_mines_surface(int surface, bool allowed)
    {
        using var fixture = new Fixture(botSpawnX: 480f, botSpawnY: 800f);
        Assert.True(fixture.Tiles.TryAttachWorldSurface(surface));
        ConnectionHandle target = fixture.SpawnConnectionPlayer(34, 53);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        fixture.Tiles.Set(33, 51, new WorldTile { Type = 0, Flags = WorldTileFlags.Active });
        for (int y = 50; y <= 52; y++) fixture.Tiles.Set(32, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        fixture.State.Apply(new PlayerEquipmentRuntimeCommand(target, new PlayerEquipmentCommitRequest(
            target.Player.Slot, SlotId: 0, Stack: 1, Prefix: 0, ItemNetId: checked((short)VanillaItemIds.CopperPickaxe.Value), ItemFlags: 0)));
        fixture.State.Apply(new ClientTileManipulationRuntimeCommand(target,
            new TerrariaTileManipulationState((byte)TerrariaTileManipulationAction.KillTile, 33, 51, Data: 0, Style: 0)));
        Assert.NotNull(await fixture.ConfigureAsync(bot.Id, bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow, Target = new(target.Player, "miner")
        }));
        fixture.State.Tick();
        Assert.Equal(!allowed, fixture.Tiles.Get(32, 50).IsActive);
        Assert.Equal(allowed ? 2 : 1, fixture.State.AppliedWorldItemAllocations);
        if (allowed)
        {
            Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out var actor));
            Assert.Equal(6, actor.SelectedItem);
            Assert.NotEqual(0, actor.ControlFlags & (1 << 5));
            // Exercise action cadence at the same obstruction. Random loadouts/formation can otherwise
            // move the actor around it between swings; navigation is covered by separate traversal tests.
            for (int tick = 0; tick < 11; tick++)
            {
                Assert.True(fixture.ServerPlayers.TryTeleport(bot.ServerPlayerId, 480f, 800f));
                fixture.Tick(1);
            }
            Assert.Equal(2, fixture.State.AppliedWorldItemAllocations); // No accelerated excavation between swings.
            Assert.True(fixture.ServerPlayers.TryTeleport(bot.ServerPlayerId, 480f, 800f));
            fixture.Tick(1);
            Assert.False(fixture.Tiles.Get(32, 51).IsActive);
            Assert.Equal(3, fixture.State.AppliedWorldItemAllocations); // The next action actually executes.
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Targeted_modes_reject_missing_target(int mode)
    {
        using var fixture = new Fixture();
        var bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.Null(await fixture.ConfigureAsync(bot.Id, bot.Configuration with { Mode = (RuntimeBotMode)mode }));
    }

    [Fact]
    public async Task Collect_mode_moves_then_uses_the_existing_exact_item_pickup()
    {
        using var fixture = new Fixture(160f, 160f);
        for (int x = 0; x < 80; x++) fixture.Tiles.Set(x, 13, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        var bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.NotNull(await fixture.ConfigureAsync(bot.Id, bot.Configuration with { Mode = RuntimeBotMode.Collect }));
        Assert.True(fixture.WorldItems.TryAllocate(CreateWorldItem(VanillaItemIds.SuperHealingPotion, 300f, 184f, stack: 2), out var item));
        fixture.State.Tick();
        Assert.Equal(ServerPlayerMovementIntentKind.MoveTo, fixture.ServerPlayers.GetMovementIntent(bot.Player).Kind);
        fixture.Tick(100);
        Assert.False(fixture.WorldItems.TryGetActive(item.Handle.Slot, out _));
        var status = Assert.Single(fixture.Telemetry.Capture());
        Assert.Equal(RuntimeBotMode.Collect, status.Configuration.Mode);
        Assert.NotNull(status.Observation);
        Assert.Equal(fixture.State.WorldIdentity, status.Observation.Value.World);
        Assert.Equal(32, status.Observation.Value.Inventory.HealingPotions);
    }

    [Fact]
    public async Task Observations_are_detached_and_reconfigure_invalidates_previous_decision()
    {
        using var fixture = new Fixture();
        var target = fixture.SpawnConnectionPlayer(10, 10);
        var bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.NotNull(await fixture.ConfigureAsync(bot.Id, bot.Configuration with { Mode = RuntimeBotMode.Follow, Target = new(target.Player, "target") }));
        fixture.Tick(7);
        var before = Assert.Single(fixture.Telemetry.Capture()).Observation!.Value;
        Assert.Equal(RuntimeBotMode.Follow, before.Configuration.Mode);
        Assert.NotNull(await fixture.ConfigureAsync(bot.Id, bot.Configuration with { Mode = RuntimeBotMode.Idle }));
        fixture.Tick(7);
        var after = Assert.Single(fixture.Telemetry.Capture()).Observation!.Value;
        Assert.Equal(RuntimeBotMode.Follow, before.Configuration.Mode);
        Assert.Equal(RuntimeBotMode.Idle, after.Configuration.Mode);
        Assert.True(after.GoalGeneration > before.GoalGeneration);
        Assert.True(after.ObservationRevision > before.ObservationRevision);
        var envelope = new RuntimeBotDecisionEnvelope(before.BotId, before.Self.Player, before.World,
            before.ObservationRevision, before.GoalGeneration, new(new(RuntimeBotActionKind.Follow, target.Player)));
        Assert.Equal(RuntimeBotActionFailureCode.StaleDecision, envelope.Validate(after));
    }

    [Fact]
    public async Task Return_mode_stops_with_space_around_target_instead_of_overlapping()
    {
        using var fixture = new Fixture(150f, 120f);
        var target = fixture.SpawnConnectionPlayer(10, 10);
        var bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.NotNull(await fixture.ConfigureAsync(bot.Id, bot.Configuration with { Mode = RuntimeBotMode.ReturnToPlayer, Target = new(target.Player, "target") }));
        fixture.Tick(7);
        Assert.Equal(ServerPlayerMovementIntentKind.Stop, fixture.ServerPlayers.GetMovementIntent(bot.Player).Kind);
        Assert.Equal(RuntimeBotActionStatus.Success, Assert.Single(fixture.Telemetry.Capture()).RecentActionResult!.Value.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Collect_lease_is_released_on_real_despawn_or_reconfigure(bool despawn)
    {
        using var fixture = new Fixture(160f, 160f);
        var first = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        var second = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        Assert.True(fixture.WorldItems.TryAllocate(CreateWorldItem(VanillaItemIds.SuperHealingPotion, 400f, 184f, stack: 1), out _));
        Assert.NotNull(await fixture.ConfigureAsync(first.Id, first.Configuration with { Mode = RuntimeBotMode.Collect }));
        Assert.NotNull(await fixture.ConfigureAsync(second.Id, second.Configuration with { Mode = RuntimeBotMode.Collect }));
        fixture.State.Tick();
        Assert.Equal(ServerPlayerMovementIntentKind.MoveTo, fixture.ServerPlayers.GetMovementIntent(first.Player).Kind);
        Assert.Equal(ServerPlayerMovementIntentKind.Stop, fixture.ServerPlayers.GetMovementIntent(second.Player).Kind);
        if (despawn)
        {
            var completion = new TaskCompletionSource<bool>();
            fixture.State.Apply(new RuntimeBotDespawnCommand(first.Id, completion));
            Assert.True(await completion.Task);
        }
        else Assert.NotNull(await fixture.ConfigureAsync(first.Id, first.Configuration with { Mode = RuntimeBotMode.Idle }));
        fixture.State.Tick();
        Assert.Equal(ServerPlayerMovementIntentKind.MoveTo, fixture.ServerPlayers.GetMovementIntent(second.Player).Kind);
    }

    [Theory]
    [InlineData(30, 0, true)]
    [InlineData(60, 0, false)]
    [InlineData(0, 0, false)]
    [InlineData(30, 1, false)]
    [InlineData(30, -1, false)]
    [InlineData(30, 2, false)]
    [InlineData(30, 3, false)]
    [InlineData(30, 4, false)]
    [InlineData(30, 5, false)]
    public async Task Mining_assistance_requires_recent_completed_underground_mining(int surface, int observation, bool shouldMine)
    {
        using var fixture = new Fixture(botSpawnX: 480f, botSpawnY: 800f);
        if (surface > 0) Assert.True(fixture.Tiles.TryAttachWorldSurface(surface));
        ConnectionHandle target = fixture.SpawnConnectionPlayer(34, 53);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        fixture.Tiles.Set(33, 51, new WorldTile { Type = 0, Flags = WorldTileFlags.Active });
        for (int y = 50; y <= 52; y++) fixture.Tiles.Set(32, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        fixture.State.Apply(new PlayerEquipmentRuntimeCommand(target, new PlayerEquipmentCommitRequest(
            target.Player.Slot, SlotId: 0, Stack: 1, Prefix: 0, ItemNetId: checked((short)VanillaItemIds.CopperPickaxe.Value), ItemFlags: 0)));
        if (observation >= 0)
            fixture.State.Apply(new ClientTileManipulationRuntimeCommand(target,
                new TerrariaTileManipulationState((byte)TerrariaTileManipulationAction.KillTile, 33, 51,
                    Data: observation == 1 ? (short)1 : (short)0, Style: 0)));
        if (observation == 2)
        {
            fixture.Tick(121);
            Assert.True(fixture.ServerPlayers.TryTeleport(bot.ServerPlayerId, 480f, 800f));
        }
        if (observation == 3) target = fixture.SpawnConnectionPlayer(34, 53); // another player did the mining
        if (observation == 4) fixture.Tiles.Set(32, 50, new WorldTile { Type = 1, Wall = 1, Flags = WorldTileFlags.Active });
        if (observation == 5) fixture.Tiles.Set(32, 50, new WorldTile { Type = 226, Flags = WorldTileFlags.Active });
        _ = await fixture.ConfigureAsync(bot.Id, bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow, Target = new RuntimeBotTarget(target.Player, "miner")
        });
        fixture.State.Tick();
        Assert.Equal(!shouldMine, fixture.Tiles.Get(32, 50).IsActive);
        Assert.True(fixture.ServerPlayers.TryGetItem(bot.ServerPlayerId, 6, out var pick));
        Assert.Equal(VanillaItemIds.VortexPickaxe, pick.ItemType);
        if (shouldMine)
        {
            Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out var self));
            Assert.Equal(6, self.SelectedItem);
            Assert.NotEqual(0, self.ControlFlags & (1 << 5));
            Assert.Equal(2, fixture.State.AppliedWorldItemAllocations); // human dirt + bot stone, exactly once each
            fixture.State.Tick();
            Assert.True(fixture.Tiles.Get(32, 51).IsActive); // full animation cadence, no burst excavation
        }
    }

    [Fact]
    public async Task Mirror_does_not_commit_when_the_entire_landing_neighbourhood_is_solid()
    {
        using var fixture = new Fixture(botSpawnX: 32f, botSpawnY: 32f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(150, 50);
        for (int x = 110; x <= 190; x++)
        for (int y = 15; y <= 80; y++)
            fixture.Tiles.Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = await fixture.ConfigureAsync(bot.Id, bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow, Target = new RuntimeBotTarget(target.Player, "solid destination")
        });
        fixture.Tick(60);
        Assert.Equal(0, Assert.Single(fixture.Telemetry.Capture()).TeleportCount);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out var self));
        Assert.False(VanillaWorldSolidCollision.Intersects(fixture.Tiles, self.PositionX, self.PositionY, 20, 42));
    }

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
        ItemTypeId[] mobility = [VanillaItemIds.FishronWings, VanillaItemIds.SoaringInsignia,
            VanillaItemIds.TerrasparkBoots, VanillaItemIds.Magiluminescence];
        for (int index = 0; index < mobility.Length; index++)
        {
            Assert.True(fixture.ServerPlayers.TryGetItem(player.ServerPlayerId,
                checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 3 + index)), out var accessory));
            Assert.Equal(mobility[index], accessory.ItemType);
        }
    }

    [Fact]
    public async Task Follow_climbs_hundreds_of_pixels_with_wings_without_mirror_recovery()
    {
        using var fixture = new Fixture(botSpawnX: 470f, botSpawnY: 950f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(30, 30);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = await fixture.ConfigureAsync(bot.Id, bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow, Target = new RuntimeBotTarget(target.Player, "high target")
        });
        fixture.Tick(100);
        Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out var climbed));
        Assert.True(climbed.PositionY < 500f, $"Bot only reached Y={climbed.PositionY} from 950.");
        Assert.True(climbed.VelocityY < 0f || climbed.PositionY < 450f);
    }

    [Fact]
    public async Task Follow_exits_from_under_a_roof_before_climbing_to_the_player()
    {
        using var fixture = new Fixture(botSpawnX: 470f, botSpawnY: 800f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(30, 30);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        for (int x = 10; x <= 50; x++)
            fixture.Tiles.Set(x, 44, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        _ = await fixture.ConfigureAsync(bot.Id, bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow, Target = new RuntimeBotTarget(target.Player, "above roof")
        });
        bool reachedAbove = false;
        for (int tick = 0; tick < 160; tick++)
        {
            fixture.State.Tick();
            Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out var self));
            Assert.False(VanillaWorldSolidCollision.Intersects(fixture.Tiles, self.PositionX, self.PositionY, 20, 42));
            if (self.PositionY < 620f) { reachedAbove = true; break; }
        }
        Assert.True(reachedAbove, "Bot kept pushing against the underside of the roof.");
        Assert.Equal(0, Assert.Single(fixture.Telemetry.Capture()).TeleportCount);
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
    public async Task Player_bot_navigates_past_solid_terrain_blocking_the_escort_route()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        // All allowed random escort offsets must lie BEYOND the wall. At tile30 some left-side formations
        // ended before it, so a grounded destination was correct and this assertion failed intermittently.
        ConnectionHandle target = fixture.SpawnConnectionPlayer(spawnTileX: 40, spawnTileY: 13);
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

        bool crossed = false;
        // A valid detour may first move sideways; verify traversal, not one particular first intent.
        for (int tick = 0; tick < 100; tick++)
        {
            fixture.State.Tick();
            Assert.True(fixture.ServerPlayers.TryGet(bot.Player, out PlayerStateSnapshot self));
            Assert.False(VanillaWorldSolidCollision.Intersects(fixture.Tiles, self.PositionX, self.PositionY, 20, 42));
            if (self.PositionX > 304f) { crossed = true; break; }
        }
        Assert.True(crossed, "Bot failed to traverse the obstructed escort route.");
        Assert.Equal(0, Assert.Single(fixture.Telemetry.Capture()).TeleportCount);
    }

    [Fact]
    public async Task Follow_formation_does_not_keep_a_destination_inside_a_wall_beside_the_player()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle escort = fixture.SpawnConnectionPlayer(30, 13);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = await fixture.ConfigureAsync(bot.Id, bot.Configuration with
        {
            Mode = RuntimeBotMode.Follow, Target = new RuntimeBotTarget(escort.Player, "escort"), FlightEnabled = false
        });
        fixture.State.Tick();
        ServerPlayerMovementIntent original = fixture.ServerPlayers.GetMovementIntent(bot.Player);
        int tileX = (int)(original.TargetX / 16f);
        for (int y = 9; y <= 12; y++)
            fixture.Tiles.Set(tileX, y, new WorldTile { Type = checked((ushort)VanillaTileIds.Stone.Value), Flags = WorldTileFlags.Active });
        Assert.True(VanillaWorldSolidCollision.Intersects(fixture.Tiles, original.TargetX - 10f, original.TargetY - 21f, 20, 42));
        fixture.State.Tick();
        ServerPlayerMovementIntent updated = fixture.ServerPlayers.GetMovementIntent(bot.Player);
        Assert.False(VanillaWorldSolidCollision.Intersects(fixture.Tiles, updated.TargetX - 10f, updated.TargetY - 21f, 20, 42));
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
    public async Task Gun_guard_does_not_lob_silver_bullets_above_stationary_distant_target()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle escort = fixture.SpawnConnectionPlayer(10, 10);
        RuntimeBotSnapshot bot = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
        _ = await fixture.ConfigureAsync(bot.Id, bot.Configuration with
        {
            Mode = RuntimeBotMode.Guard, Target = new RuntimeBotTarget(escort.Player, "escort"), WeaponPolicy = RuntimeBotWeaponPolicy.Gun
        });
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.BlueSlime, out VanillaNpcDefinition definition));
        Assert.True(definition.TryResolveHitbox(1f, out VanillaNpcHitboxSize hitbox));
        // Projectile center is bot center + half the four-pixel bullet body, as in the current spawn contract.
        fixture.SpawnNpc(VanillaNpcIds.BlueSlime, 740f, 183f - hitbox.Height * 0.5f);
        fixture.State.Tick();
        var projectiles = new ProjectileSnapshot[fixture.Projectiles.Capacity];
        Assert.Equal(1, fixture.Projectiles.CopyActive(projectiles));
        Assert.Equal(VanillaProjectileIds.SilverBullet, projectiles[0].Type);
        Assert.InRange(projectiles[0].VelocityY, -0.05f, 0.05f);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Guards_coordinate_only_when_protecting_the_same_player(bool sameProtectedPlayer)
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(10, 10);
        ConnectionHandle secondTarget = sameProtectedPlayer ? target : fixture.SpawnConnectionPlayer(10, 10);
        var squad = new RuntimeBotSnapshot[2];
        for (int i = 0; i < squad.Length; i++)
        {
            squad[i] = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
            _ = await fixture.ConfigureAsync(squad[i].Id, squad[i].Configuration with
            {
                Mode = RuntimeBotMode.Guard, Target = new RuntimeBotTarget(i == 0 ? target.Player : secondTarget.Player, "target"),
                WeaponPolicy = RuntimeBotWeaponPolicy.Bow
            });
        }
        _ = fixture.SpawnNpc(VanillaNpcIds.Zombie, 40f, 160f);
        _ = fixture.SpawnNpc(VanillaNpcIds.Zombie, 400f, 160f);
        fixture.State.Tick();
        var shots = new ProjectileSnapshot[fixture.Projectiles.Capacity];
        Assert.Equal(2, fixture.Projectiles.CopyActive(shots));
        Assert.Equal(sameProtectedPlayer, shots[0].VelocityX * shots[1].VelocityX < 0f);
    }

    [Fact]
    public async Task Melee_squad_approaches_a_shared_enemy_from_distinct_positions()
    {
        using var fixture = new Fixture(botSpawnX: 160f, botSpawnY: 160f);
        ConnectionHandle target = fixture.SpawnConnectionPlayer(10, 10);
        var squad = new RuntimeBotSnapshot[2];
        for (int i = 0; i < squad.Length; i++)
        {
            squad[i] = Assert.IsType<RuntimeBotSnapshot>(await fixture.CreateBotAsync(RuntimeBotCreateRequest.Player));
            _ = await fixture.ConfigureAsync(squad[i].Id, squad[i].Configuration with
            {
                Mode = RuntimeBotMode.Guard, Target = new RuntimeBotTarget(target.Player, "shared target"),
                WeaponPolicy = RuntimeBotWeaponPolicy.Melee
            });
        }
        _ = fixture.SpawnNpc(VanillaNpcIds.Zombie, 500f, 160f);
        fixture.State.Tick();
        var first = fixture.ServerPlayers.GetMovementIntent(squad[0].Player);
        var second = fixture.ServerPlayers.GetMovementIntent(squad[1].Player);
        Assert.Equal(48f, second.TargetX - first.TargetX);
        Assert.Equal(first.TargetY, second.TargetY);
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

        public Fixture(float botSpawnX = 32f, float botSpawnY = 32f, bool simulateProjectiles = false)
        {
            var identities = new ServerPlayerSlotRegistry(slots);
            var states = new ServerPlayerStateStore(identities, slots.Capacity);
            Tiles = new WorldTileStore(new WorldDimensions(300, 120));
            ServerPlayers = new ServerPlayerAuthority(states, identities, Tiles);
            WorldItems = new RuntimeWorldItemStore();
            Projectiles = new RuntimeProjectileStore();
            Telemetry = new RuntimeBotTelemetry();
            State = new ServerRuntimeState(
                projectileStepper: simulateProjectiles ? new VanillaProjectileWorldStateStepper(Tiles) : null,
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
