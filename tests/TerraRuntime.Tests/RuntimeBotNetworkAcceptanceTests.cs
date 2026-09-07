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

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(16);
        private readonly List<PlayerJoinSession> sessions = [];
        private readonly RuntimeConnectionRegistry connections = new();
        private readonly RuntimeProjectileReplicationRegistry projectileReplication = new();
        private readonly RuntimeWorldItemReplicationRegistry worldItemReplication = new();
        private readonly RuntimeNpcReplicationRegistry npcReplication = new();
        private long nextConnectionId = 1;

        public Fixture(float botSpawnX = 32f, float botSpawnY = 32f)
        {
            Outbound = new TerrariaConnectionOutboundQueue(
                new OutboundQueueOptions(maxFrames: 512, maxQueuedBytes: 512 * 1024, maxFrameBytes: 64 * 1024));
            var identities = new ServerPlayerSlotRegistry(slots);
            var serverPlayerStates = new ServerPlayerStateStore(identities, slots.Capacity);
            var tiles = new WorldTileStore(new WorldDimensions(300, 120));
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
                botSpawnY: botSpawnY);
        }

        public ServerRuntimeState State { get; }
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
