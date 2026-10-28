using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.HostContracts;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWiring1458Tests
{
    [Fact]
    public void Real_packet59_decode_queue_apply_mutates_only_on_writer_and_publishes_authoritative_light_before_relay()
    {
        using var scene = new Scene();
        scene.Put(20, 20, 136); scene.Put(21, 20, 4);
        var section = new WorldSectionId(0, 0);
        long beforeVersion = scene.Tiles.GetSectionVersion(section);
        scene.Tiles.DirtySections.Clear(); scene.Tiles.PersistenceDirtySections.Clear();
        byte[] bytes = [7, 0, 59, 20, 0, 20, 0]; // Independent MessageBuffer59/NetMessage59 layout.
        var frame = Decode(bytes);
        Assert.Equal(TerrariaFrameSinkResult.Continue, scene.Sink.OnFrame(in frame));
        Assert.Equal((short)0, scene.Tiles.Get(21, 20).FrameX);
        Assert.Single(scene.Commands.Commands);
        scene.Drain();
        Assert.Equal((short)66, scene.Tiles.Get(21, 20).FrameX);
        Assert.Equal((short)18, scene.Tiles.Get(20, 20).FrameY);
        Assert.Equal(1, scene.State.AppliedClientTileManipulations);
        Assert.Equal(new byte[] { 20 }, scene.DrainFrames(scene.OwnerOutbound));
        Assert.Equal(new byte[] { 20, 59 }, scene.DrainFrames(scene.PeerOutbound));
        Assert.Equal(1, scene.Tiles.PersistenceDirtySections.DirtyCount);
        Assert.Equal(1, scene.Tiles.DirtySections.DirtyCount);
        Assert.True(scene.Tiles.GetSectionVersion(section) > beforeVersion);

        // Valid repeats are new presses, not idempotent requested output state.
        Assert.Equal(TerrariaFrameSinkResult.Continue, scene.Sink.OnFrame(in frame)); scene.Drain();
        Assert.Equal((short)0, scene.Tiles.Get(21, 20).FrameX);
        Assert.Equal((short)0, scene.Tiles.Get(20, 20).FrameY);
    }

    [Fact]
    public void Shipping_native_wiring_smoke_executes_authoritative_command_clocks_and_original_save_bytes() =>
        Assert.True(WiringNativeSmoke1458.Run());

    [Fact]
    public void Repeated_valid_presses_are_bounded_by_the_existing_per_tick_budget_and_recover_next_tick()
    {
        using var scene = new Scene();
        scene.Put(20, 20, 136); scene.Put(21, 20, 4);
        for (int i = 0; i < 9; i++) scene.Press(20, 20);
        Assert.Equal(8, scene.State.AppliedClientTileManipulations);
        Assert.Equal(1, scene.State.RejectedClientTileManipulations);
        Assert.Equal((short)0, scene.Tiles.Get(21, 20).FrameX);
        scene.State.Tick(); scene.State.Tick(); // AdvanceTo observes the writer's new tick.
        scene.Press(20, 20);
        Assert.Equal(9, scene.State.AppliedClientTileManipulations);
        Assert.Equal((short)66, scene.Tiles.Get(21, 20).FrameX);
    }

    [Fact]
    public void Wired_multicell_door_emits_source_action_before_final_tiles_and_outer_switch_relay()
    {
        using var scene = new Scene();
        scene.Put(20, 20, 136); scene.Put(21, 19, 1); scene.Put(21, 23, 1);
        for (int row = 0; row < 3; row++)
            scene.Tiles.Set(21, 20 + row, new WorldTile { Type = 10, FrameY = (short)(row * 18),
                Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        scene.Press(20, 20);
        Assert.Equal((ushort)11, scene.Tiles.Get(21, 20).Type);
        Assert.Equal(new byte[] { 19, 20 }, scene.DrainFrames(scene.OwnerOutbound));
        Assert.Equal(new byte[] { 19, 20, 59 }, scene.DrainFrames(scene.PeerOutbound));
        scene.Press(20, 20);
        Assert.Equal((ushort)10, scene.Tiles.Get(21, 20).Type);
        Assert.Equal(new byte[] { 19, 20 }, scene.DrainFrames(scene.OwnerOutbound));
        Assert.Equal(new byte[] { 19, 20, 59 }, scene.DrainFrames(scene.PeerOutbound));
    }

    [Fact]
    public void Wired_tall_gate_respects_a_server_owned_player_then_opens_after_its_generation_is_despawned()
    {
        var slots = new PlayerSlotPool(2);
        var identities = new ServerPlayerSlotRegistry(slots);
        var states = new ServerPlayerStateStore(identities, 2);
        var players = new ServerPlayerAuthority(states, identities);
        using var scene = new Scene(serverPlayers: players, playerSlots: slots);
        Assert.Equal(new PlayerSlotId(0), scene.Owner.Player.Slot);
        scene.Put(20, 20, 136);
        short[] rowFrames = [0, 20, 38, 56, 74];
        for (int row = 0; row < rowFrames.Length; row++)
            scene.Tiles.Set(21, 20 + row, new WorldTile { Type = 388, FrameY = rowFrames[row],
                Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        var id = new ServerPlayerId("test:wired-gate-blocker");
        var created = players.Create(id, 21 * 16, 21 * 16);
        Assert.True(created.IsCreated);
        Assert.Equal(new PlayerSlotId(1), created.Player.Slot);

        // Enter through the production packet59 sink, queue and writer. This server player has
        // no connection membership; the world-owned occupancy probe must include its live body.
        scene.Press(20, 20);
        for (int row = 0; row < rowFrames.Length; row++)
            Assert.Equal((ushort)388, scene.Tiles.Get(21, 20 + row).Type);
        Assert.True(players.Despawn(id));
        scene.Press(20, 20);
        for (int row = 0; row < rowFrames.Length; row++)
            Assert.Equal((ushort)389, scene.Tiles.Get(21, 20 + row).Type);
    }

    [Fact]
    public void Timer_runs_through_real_state_ticks_turns_off_and_does_not_cross_world_lifetimes()
    {
        using var scene = new Scene();
        scene.Put(20, 20, 144); scene.Put(21, 20, 4);
        scene.Press(20, 20);
        for (int i = 0; i < 59; i++) scene.State.Tick();
        Assert.Equal((short)0, scene.Tiles.Get(21, 20).FrameX);
        scene.State.Tick();
        Assert.Equal((short)66, scene.Tiles.Get(21, 20).FrameX);
        scene.Press(20, 20);
        for (int i = 0; i < 120; i++) scene.State.Tick();
        Assert.Equal((short)66, scene.Tiles.Get(21, 20).FrameX);
        using var replacement = new Scene();
        replacement.Put(20, 20, 144); replacement.Put(21, 20, 4);
        for (int i = 0; i < 60; i++) replacement.State.Tick();
        Assert.Equal((short)0, replacement.Tiles.Get(21, 20).FrameX);
    }

    [Fact]
    public void Stale_session_source_destroyed_device_invalid_coordinates_and_other_world_are_rejected()
    {
        using var scene = new Scene();
        scene.Put(20, 20, 136); scene.Put(21, 20, 4);
        var owner = scene.Owner;
        var stale = owner with { Player = new PlayerHandle(owner.Player.Slot, new PlayerSessionGeneration(owner.Player.Generation.Value + 1)) };
        scene.State.Apply(new ClientHitSwitchRuntimeCommand1458(stale, new(20, 20)));
        var foreign = owner with { Source = GameCommandSourceId.FromConnection(8009) };
        scene.State.Apply(new ClientHitSwitchRuntimeCommand1458(foreign, new(20, 20)));
        scene.Press(-1, 20); scene.Press(100, 20);
        WorldTile removed = default; scene.Tiles.Set(20, 20, in removed); scene.Press(20, 20);
        using var otherWorld = new Scene(8100);
        otherWorld.Put(20, 20, 136); otherWorld.Put(21, 20, 4);
        otherWorld.State.Apply(new ClientHitSwitchRuntimeCommand1458(owner, new(20, 20)));
        Assert.Equal((short)0, scene.Tiles.Get(21, 20).FrameX);
        Assert.Equal((short)0, otherWorld.Tiles.Get(21, 20).FrameX);
        Assert.Equal(5, scene.State.RejectedClientTileManipulations);
        Assert.Equal(1, otherWorld.State.RejectedClientTileManipulations);
        Assert.Empty(scene.DrainFrames(scene.PeerOutbound));
    }

    [Fact]
    public void Malformed_packet_stops_before_queue_and_codec_owns_the_payload()
    {
        using var scene = new Scene();
        byte[] bytes = [7, 0, 59, 0x85, 0xFF, 0xC8, 1];
        var frame = Decode(bytes);
        Assert.True(TerrariaHitSwitchCodec1458.TryDecode(in frame, out var state));
        Assert.Equal(new TerrariaHitSwitchState1458(-123, 456), state);
        Assert.Equal(bytes, TerrariaHitSwitchCodec1458.Encode(in state));
        Assert.Equal(TerrariaFrameSinkResult.Continue, scene.Sink.OnFrame(in frame));
        Array.Fill(bytes, (byte)0);
        var command = Assert.IsType<ClientHitSwitchRuntimeCommand1458>(Assert.Single(scene.Commands.Commands));
        Assert.Equal(state, command.State);
        var malformed = new TerrariaFrame(6, 59, ReadOnlySequence<byte>.Empty, new ReadOnlySequence<byte>(new byte[3]));
        Assert.Equal(TerrariaFrameSinkResult.Stop, scene.Sink.OnFrame(in malformed));
        Assert.Equal(TerrariaFrameRejectionCategory.MalformedProtocol, scene.Sink.RejectionCategory);
        Assert.Single(scene.Commands.Commands);
        var destroy = new TerrariaProjectileDestroyState(new TerrariaProjectileKeyState(0, 1, 1), 320, 320);
        Assert.True(TerrariaProjectileEncoder.TryEncodeDestroy(in destroy, out var destroyBytes));
        var validDestroy = Decode(destroyBytes);
        Assert.Equal(TerrariaFrameSinkResult.Stop, scene.Sink.OnFrame(in validDestroy));
        Assert.Single(scene.Commands.Commands);
    }

    [Fact]
    public void Server_owned_player_physics_enters_player_only_plate_once_and_obeys_actor_filters()
    {
        foreach (int style in new[] { 0, 2, 5, 7 })
        {
            var tiles = new WorldTileStore(new WorldDimensions(100, 100));
            Put(tiles, 20, 20, 135, frameY: style * 18); Put(tiles, 21, 20, 4);
            var slots = new PlayerSlotPool(1);
            var identities = new ServerPlayerSlotRegistry(slots);
            var states = new ServerPlayerStateStore(identities, 1);
            var players = new ServerPlayerAuthority(states, identities, tiles);
            Assert.True(players.Create(new ServerPlayerId("test:plate"), 320, 289).IsCreated);
            var state = new ServerRuntimeState(worldTiles: tiles, serverPlayers: players);
            // Feet start331; gravity enters the source >332 overlap during real dry physics.
            for (int i = 0; i < 3; i++) state.Tick();
            Assert.Equal(style is 5 or 7 ? (short)0 : (short)66, tiles.Get(21, 20).FrameX);
        }
    }

    [Fact]
    public void Npc_plate_contact_uses_actor_rules_and_mutates_tiles_before_the_next_live_slot_steps()
    {
        foreach (int style in new[] { 0, 2, 5 })
        {
            var tiles = new WorldTileStore(new WorldDimensions(100, 100));
            Put(tiles, 20, 20, 135, frameY: style * 18); Put(tiles, 21, 20, 4);
            var npcs = new RuntimeNpcStore(2);
            var simulation = NpcSimulationState.Initial with { LifeMax = 50, Life = 50,
                HitboxOverride = new NpcHitboxDimensions(16, 32), NoTileCollide = false };
            var initial = new NpcStateUpdate(1, 1, 320, 300, 0, 1, 0, default, simulation);
            Assert.True(npcs.TrySpawn(0, in initial, out _));
            Assert.True(npcs.TrySpawn(1, initial with { PositionX = 400 }, out _));
            var stepper = new PlateStepper(tiles, style != 2);
            var state = new ServerRuntimeState(npcs: npcs, npcAiStepper: stepper, worldTiles: tiles);
            state.Tick();
            Assert.True(stepper.SawSecondSlot);
            Assert.Equal(style == 2 ? (short)0 : (short)66, tiles.Get(21, 20).FrameX);
        }
    }

    private sealed class PlateStepper(WorldTileStore tiles, bool expectPulse) : INpcAiStateStepper
    {
        public bool SawSecondSlot { get; private set; }
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (npc.Handle.Slot == 1)
            {
                SawSecondSlot = true;
                Assert.Equal(expectPulse ? (short)66 : (short)0, tiles.Get(21, 20).FrameX);
            }
            next = new(npc.Type, npc.NetId, npc.PositionX, npc.PositionY + 1, npc.VelocityX,
                npc.VelocityY, npc.Target, npc.Ai, npc.Simulation);
            return true;
        }
    }

    [Fact]
    public void Physical_critter_plate_entry_commits_source_escape_state_and_honors_collision_and_life_gates()
    {
        foreach (var rules in new[] { (NoTile: false, Life: 5), (NoTile: true, Life: 5), (NoTile: false, Life: 1) })
        {
            var tiles = new WorldTileStore(new WorldDimensions(100, 100));
            Put(tiles, 20, 20, 135); Put(tiles, 21, 20, 4);
            var npcs = new RuntimeNpcStore(1);
            var simulation = NpcSimulationState.Initial with { LifeMax = rules.Life, Life = rules.Life,
                HitboxOverride = new NpcHitboxDimensions(16, 32), NoTileCollide = rules.NoTile };
            var initial = new NpcStateUpdate(46, 46, 320, 300, 0, 1, 0, new(0, 0, 7, 9), simulation);
            Assert.True(npcs.TrySpawn(0, in initial, out var before));
            var state = new ServerRuntimeState(npcs: npcs, npcAiStepper: new PlateStepper(tiles, false), worldTiles: tiles);
            state.Tick();
            Assert.True(npcs.TryGet(before.Handle, out var after));
            bool eligible = !rules.NoTile && rules.Life > 1;
            Assert.Equal(eligible ? new NpcAiState(1, 400, 0, 9) : initial.Ai, after.Ai);
            Assert.Equal(eligible ? (short)66 : (short)0, tiles.Get(21, 20).FrameX);
        }
    }

    private static void Put(WorldTileStore tiles, int x, int y, ushort type, int frameY = 0) =>
        tiles.Set(x, y, new WorldTile { Type = type, FrameY = (short)frameY, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });

    private static TerrariaFrame Decode(byte[] bytes)
    {
        var sequence = new ReadOnlySequence<byte>(bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out var frame));
        return frame;
    }

    private sealed class Scene : IDisposable, IPlayerSpawnCommitIngress
    {
        public WorldTileStore Tiles { get; } = new(new WorldDimensions(100, 100));
        public RuntimeTileManipulationReplicationRegistry Replication { get; } = new();
        public ServerRuntimeState State { get; }
        public TerrariaConnectionOutboundQueue OwnerOutbound { get; } = Outbound();
        public TerrariaConnectionOutboundQueue PeerOutbound { get; } = Outbound();
        public QueuedIngress Commands { get; } = new();
        public PlayerBootstrapFrameSink Bootstrap { get; }
        public ProjectileLifecycleFrameSink Sink { get; }
        public ConnectionHandle Owner { get; private set; }

        public Scene(int sourceId = 8000, ServerPlayerAuthority? serverPlayers = null, PlayerSlotPool? playerSlots = null)
        {
            State = new(worldTiles: Tiles, tileManipulationReplication: Replication, serverPlayers: serverPlayers);
            var source = GameCommandSourceId.FromConnection(sourceId);
            Bootstrap = new(playerSlots ?? new PlayerSlotPool(1), Outbound(), PlayerBootstrapPacketSet.CreateForTesting(
                new byte[] { 3, 0, 7 }, Array.Empty<ReadOnlyMemory<byte>>(), new byte[] { 3, 0, 49 }), source, this);
            foreach (byte[] bytes in new byte[][] { [15, 0, 1, 11, 84, 101, 114, 114, 97, 114, 105, 97, 51, 50, 54],
                [3, 0, 6], [12, 0, 8, 0, 0, 0, 0, 0, 0, 0, 0, 0] })
                Assert.Equal(TerrariaFrameSinkResult.Continue, Bootstrap.OnFrame(Decode(bytes)));
            byte[] spawn = new byte[3 + TerrariaJoinRequestDecoder.PlayerSpawnPayloadLength];
            BinaryPrimitives.WriteUInt16LittleEndian(spawn, (ushort)spawn.Length); spawn[2] = 12;
            BinaryPrimitives.WriteInt16LittleEndian(spawn.AsSpan(4), 20); BinaryPrimitives.WriteInt16LittleEndian(spawn.AsSpan(6), 20);
            Assert.Equal(TerrariaFrameSinkResult.Continue, Bootstrap.OnFrame(Decode(spawn)));
            Assert.Equal(PlayerJoinState.Playing, Bootstrap.JoinState);
            Assert.True(Replication.TryRegister(source, OwnerOutbound));
            var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(sourceId + 1), new(new PlayerSlotId(1), new(1)));
            Assert.True(Replication.TryRegister(peer.Source, PeerOutbound));
            var request = new PlayerSpawnCommitRequest(Owner.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
            Replication.PlayerSpawned(Owner, in request);
            var peerRequest = request with { ClaimedSlot = peer.Player.Slot };
            Replication.PlayerSpawned(peer, in peerRequest);
            Sink = new(source, Bootstrap, new PassingSink(), new RuntimeProjectileNetworkIngress(Commands));
        }

        public bool TryPost(GameCommandSourceId source, PlayerJoinSession session, in PlayerSpawnCommitRequest request)
        {
            Owner = new(source, session.Handle); State.Apply(new PlayerSpawnRuntimeCommand(Owner, session, request));
            return State.LastSpawnCommitResult == PlayerSpawnCommitResult.Committed;
        }
        public void Put(int x, int y, ushort type) => RuntimeWiring1458Tests.Put(Tiles, x, y, type);
        public void Press(short x, short y) { var frame = Decode(TerrariaHitSwitchCodec1458.Encode(new(x, y))); Assert.Equal(TerrariaFrameSinkResult.Continue, Sink.OnFrame(in frame)); Drain(); }
        public void Drain() { foreach (var command in Commands.Commands) State.Apply(command); Commands.Commands.Clear(); }
        public byte[] DrainFrames(TerrariaConnectionOutboundQueue outbound)
        {
            var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(outbound)!;
            var ids = new List<byte>(); while (queue.TryRead(out var frame)) ids.Add(Decode(frame.Bytes.ToArray()).MessageId); return ids.ToArray();
        }
        public void Dispose() => Bootstrap.Dispose();
        private static TerrariaConnectionOutboundQueue Outbound() => new(new OutboundQueueOptions(256, 262144, 8192));
    }
    private sealed class QueuedIngress : IGameCommandIngress<RuntimeCommand>
    {
        public List<RuntimeCommand> Commands { get; } = [];
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command) { Commands.Add(command); return true; }
    }
    private sealed class PassingSink : ITerrariaFrameSink
    { public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue; }
}
