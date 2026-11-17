using System.Buffers;
using System.Buffers.Binary;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringTeleporter1458Tests
{
    [Fact]
    public void Authenticated_switch_moves_both_actors_retains_pending_birth_and_rejects_queued_old13()
    {
        using var f = new TeleportFixture();
        var press = new ReadOnlySequence<byte>(new byte[] { 7, 0, 59, 10, 0, 40, 0 });
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref press, out var frame));
        Assert.True(TerraRuntime.Protocol.Multiplicity.TerrariaHitSwitchCodec1458.TryDecode(in frame, out var input));
        f.State.Apply(new ClientHitSwitchRuntimeCommand1458(f.Connection, input));
        Assert.True(f.State.TryCapturePlayerSnapshot(f.Connection.Player, out var player));
        Assert.Equal(878f, player.PositionX); Assert.Equal(598f, player.PositionY);
        Assert.Equal(2.25f, player.VelocityX); Assert.Equal(-1.5f, player.VelocityY);
        Assert.True(f.Npcs.TryGetActive(0, out var npc)); Assert.Equal(884f, npc.PositionX);
        Assert.Equal(1f, npc.Simulation.TeleportTime); Assert.True(f.Npcs.HasPendingBirth(npc.Handle));
        Assert.Equal(new[] { "0F004100000000805B440080154400", "0F004101000000005D440080154400" }, Drain(f.Peer));
        f.State.Apply(new PlayerMovementRuntimeCommand(f.Connection, Motion(f.Connection.Player.Slot, 318f, 598f)));
        Assert.True(f.State.TryCapturePlayerSnapshot(f.Connection.Player, out player)); Assert.Equal(878f, player.PositionX);
        f.State.Apply(new PlayerMovementRuntimeCommand(f.Connection, Motion(f.Connection.Player.Slot, 879f, 598f)));
        Assert.True(f.State.TryCapturePlayerSnapshot(f.Connection.Player, out player)); Assert.Equal(879f, player.PositionX);
        Assert.False(f.Graph.Players.CaptureWiringTeleport(player.Player).AwaitingLanding);
    }

    [Theory]
    [InlineData("frame")] [InlineData("missing")] [InlineData("wet")] [InlineData("actuator")] [InlineData("inactive")]
    public void Unsupported_pad_refuses_without_motion_entropy_or65(string kind)
    {
        using var f = new TeleportFixture(); var tile = f.Tiles.Get(56, 40);
        switch (kind)
        {
            case "frame": tile.FrameX++; break;
            case "missing": tile.Flags &= ~WorldTileFlags.Active; break;
            case "wet": tile.LiquidAmount = 1; break;
            case "actuator": tile.Flags |= WorldTileFlags.Actuator; break;
            case "inactive": tile.Flags |= WorldTileFlags.Inactive; break;
        }
        f.Tiles.Set(56, 40, in tile); var before = f.Random.Clone(); f.Press();
        Assert.True(f.State.TryCapturePlayerSnapshot(f.Connection.Player, out var player)); Assert.Equal(318f, player.PositionX);
        Assert.True(f.Npcs.TryGetActive(0, out var npc)); Assert.Equal(324f, npc.PositionX);
        Assert.True(before.HasSameState(f.Random)); Assert.Empty(Drain(f.Peer));
    }

    [Theory]
    [InlineData("mounted")] [InlineData("wet-source")] [InlineData("wet-destination")] [InlineData("dead")]
    public void Unsupported_player_context_does_not_block_eligible_npc(string kind)
    {
        using var f = new TeleportFixture(); Assert.True(f.Graph.Players.TryGet(f.Connection, out var member));
        if (kind == "mounted") member.HasMount = true;
        if (kind == "dead") member.IsDead = true;
        if (kind.StartsWith("wet")) { int x = kind == "wet-source" ? 19 : 54; var tile = f.Tiles.Get(x, 38); tile.LiquidAmount = 1; f.Tiles.Set(x, 38, in tile); }
        f.Press();
        Assert.Equal(318f, member.PositionX); Assert.True(f.Npcs.TryGetActive(0, out var npc)); Assert.Equal(884f, npc.PositionX);
        Assert.Equal(new[] { "0F004101000000005D440080154400" }, Drain(f.Peer));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Section_preparation_reentry_cannot_adopt_a_stale_player_or_random(bool mutateRandom)
    {
        using var f = new TeleportFixture(); var before = f.Random.Clone(); int calls = 0;
        f.OnPrepare = () => { calls++; if (mutateRandom) f.Random.Next(); else { var t = f.Tiles.Get(56, 40); t.FrameX++; f.Tiles.Set(56, 40, in t); } };
        f.Press(); Assert.True(calls > 0);
        Assert.True(f.State.TryCapturePlayerSnapshot(f.Connection.Player, out var player)); Assert.Equal(318f, player.PositionX);
        if (mutateRandom) Assert.False(before.HasSameState(f.Random)); else Assert.True(before.HasSameState(f.Random));
        Assert.DoesNotContain(Drain(f.Peer), x => x.StartsWith("0F004100"));
    }

    [Fact]
    public void Concurrent_section_completion_counts_each_section_once()
    {
        var state = new PlayerSectionStreamingState(new WorldDimensions(4200, 1200));
        Parallel.For(0, 10000, i => state.MarkSent(new WorldSectionId(i % 21, i % 8)));
        Assert.Equal(168, state.SentSectionCount);
        for (int x = 0; x < 21; x++) for (int y = 0; y < 8; y++) Assert.True(state.OwnsSectionAtTile(x * 200, y * 150));
    }

    [Fact]
    public void Receive_landing_handoff_rejects_old13_until_near_target_ack()
    {
        var slots = new PlayerSlotPool(1); Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest(); Assert.Equal(PlayerSpawnCommitResult.Committed, session.TryCommitSpawn(session.Slot));
        var output = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(64, 65536, 16384));
        var ingress = new LandingIngress();
        using var sink = new PlayerBootstrapFrameSink(slots, output, PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 }, [], new byte[] { 3, 0, 49 }),
            GameCommandSourceId.FromConnection(893), ingress, ingress);
        sink.AdoptPlayingSession(session, null); sink.CommitServerTeleport(878f, 598f);
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(MovementFrame(318f, 598f))); Assert.Equal(0, ingress.Count);
        Assert.Single(Drain(output));
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(MovementFrame(878f, 598f))); Assert.Equal(1, ingress.Count);
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(MovementFrame(318f, 598f))); Assert.Equal(2, ingress.Count);
    }

    [Fact]
    public void All_colors_finish_material_updates_before_the_first_teleport()
    {
        var tiles = new WorldTileStore(new(80, 80));
        for (int x = 10; x <= 60; x++) tiles.Set(x, 40, new WorldTile { Flags = WorldTileFlagMasks.Wires });
        tiles.Set(10, 40, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlagMasks.Wires });
        tiles.Set(60, 40, new WorldTile { Type = 4, Flags = WorldTileFlags.Active | WorldTileFlagMasks.Wires });
        foreach (int left in new[] { 20, 55 }) for (int dx = 0; dx < 3; dx++) tiles.Set(left + dx, 40, new WorldTile { Type = 235, FrameX = (short)(dx * 18), Flags = WorldTileFlags.Active | WorldTileFlagMasks.Wires });
        int changes = 0, teleports = 0;
        var devices = new WorldWiringDeviceExecutor1458(tiles, publishTileSquare: (x, y, _, _) => { if (x == 60 && y == 40) changes++; });
        var circuit = new WorldWiringCircuit1458(tiles, devices, teleport: (a, b, blocked) =>
        { Assert.Equal(4, changes); Assert.Equal((short)0, tiles.Get(60, 40).FrameX); Assert.False(blocked); Assert.Equal(20, a.Left); Assert.Equal(55, b.Left); teleports++; });
        Assert.True(circuit.HitSwitch(10, 40)); Assert.Equal(4, teleports);
    }

    [Fact]
    public void Resident_checkpoint_position_updates_before_outer_npc_tick()
    {
        var tiles = new WorldTileStore(new(80, 80)); var random = new VanillaUnifiedRandom1458(1458); var npcs = new RuntimeNpcStore();
        var town = new RuntimeTownNpcStateStore(new WorldNpcPersistence([], [new WorldTownNpc(22, "Oracle", 324f, 598f, false, 10, 20, 2, false)], []), [], tiles.Dimensions);
        Assert.True(town.TryReserveRuntimeSlots(npcs)); town.BindRuntimeNames(npcs);
        var saved = Assert.Single(town.CaptureNpcPersistence().TownNpcs);
        var state = new ServerRuntimeState(npcs: npcs, worldTiles: tiles, townNpcs: town,
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458),
            naturalSpawnRandom: new SystemVanillaNpcRandom(random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(random));
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Hidden)!.GetValue(state)!;
        for (int x = 10; x <= 57; x++) tiles.Set(x, 40, new WorldTile { Flags = WorldTileFlags.WireRed });
        tiles.Set(10, 40, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        foreach (int left in new[] { 20, 55 }) for (int dx = 0; dx < 3; dx++) tiles.Set(left + dx, 40, new WorldTile { Type = 235, FrameX = (short)(dx * 18), Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        var circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", Hidden)!.GetValue(graph.WorldTileAuthority)!;
        Assert.True(circuit.HitSwitch(10, 40)); Assert.True(npcs.TryGetActive(0, out var moved)); Assert.Equal(884f, moved.PositionX);
        Assert.Equal(saved with { X = 884f }, Assert.Single(town.CaptureNpcPersistence().TownNpcs));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Section_warmup_marks_only_frames_that_entered_the_queue_and_retries(bool available)
    {
        var worldBytes = (byte[])typeof(WorldFileLoaderTests).GetMethod("CreateCompleteCurrentWorld", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null)!;
        var limits = (WorldFileLoadLimits)typeof(WorldFileLoaderTests).GetMethod("CreateLimits", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null)!;
        Assert.True(WorldFileLoader.TryLoad(worldBytes, limits, out var world).IsLoaded);
        var packets = available ? PlayerBootstrapPacketSet.Create(world!) : PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 }, [], new byte[] { 3, 0, 49 });
        var output = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(1, 65536, 16384));
        using var sink = new PlayerBootstrapFrameSink(new PlayerSlotPool(1), output, packets);
        var streaming = new PlayerSectionStreamingState(world!.Header.Dimensions);
        typeof(PlayerBootstrapFrameSink).GetField("_sectionStreaming", Hidden)!.SetValue(sink, streaming);
        Assert.Equal(OutboundEnqueueResult.Enqueued, output.TryEnqueue(new OutboundFrame(new byte[] { 3, 0, 7 })));
        sink.PrepareServerTeleportSections(0, 0); Assert.Equal(0, streaming.SentSectionCount); Assert.Single(Drain(output));
        sink.PrepareServerTeleportSections(0, 0);
        Assert.Equal(available ? 1 : 0, streaming.SentSectionCount);
        var frames = Drain(output); Assert.Equal(available ? 1 : 0, frames.Length);
        if (available) Assert.Equal((byte)10, Convert.FromHexString(frames[0])[2]);
        sink.PrepareServerTeleportSections(0, 0); Assert.Empty(Drain(output));
    }

    [Theory]
    [InlineData(100f, 100f, 640f, 640f)]
    [InlineData(67100f, 19100f, 66540f, 18518f)]
    [InlineData(100f, 8000f, 640f, 8000f)]
    [InlineData(8000f, 100f, 8000f, 640f)]
    public void Border_clamped_ack_is_admitted_without_accepting_distant_old_motion(float x, float y, float ackX, float ackY)
    {
        var dimensions = new WorldTileDimensions(4200, 1200);
        Assert.True(VanillaPlayerWorldBounds1458.IsNearLanding(in dimensions, x, y, ackX, ackY));
        Assert.False(VanillaPlayerWorldBounds1458.IsNearLanding(in dimensions, x, y, 30000f, 8000f));
        Assert.False(VanillaPlayerWorldBounds1458.IsNearLanding(in dimensions, x, y, x, y));
    }

    [Fact]
    public void Writer_warmup_does_not_request_rebuild_wait_or_publish_stale_cached_sections()
    {
        var worldBytes = (byte[])typeof(WorldFileLoaderTests).GetMethod("CreateCompleteCurrentWorld", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null)!;
        var limits = (WorldFileLoadLimits)typeof(WorldFileLoaderTests).GetMethod("CreateLimits", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null)!;
        Assert.True(WorldFileLoader.TryLoad(worldBytes, limits, out var world).IsLoaded);
        var packets = PlayerBootstrapPacketSet.Create(world!);
        var output = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(64, 65536, 16384));
        using var sink = new PlayerBootstrapFrameSink(new PlayerSlotPool(1), output, packets);
        var tile = world!.Tiles.Get(0, 0); tile.FrameX++;
        world.Tiles.Set(0, 0, in tile);
        packets.AttachSectionRebuildRequester(_ => throw new InvalidOperationException("Writer entered blocking section resolution."));
        sink.PrepareServerTeleportSections(0, 0);
        Assert.Empty(Drain(output)); Assert.False(((PlayerSectionStreamingState)typeof(PlayerBootstrapFrameSink).GetField("_sectionStreaming", Hidden)!.GetValue(sink)!).OwnsSectionAtTile(0, 0));
    }

    [Theory]
    [InlineData("adopt")] [InlineData("transfer")] [InlineData("respawn")]
    public void New_session_transfer_and_respawn_forget_an_older_pending_teleport_landing(string transition)
    {
        var slots = new PlayerSlotPool(1); Assert.True(slots.TryAcquireConnection(out var lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest(); session.ObserveSectionRequest(); Assert.Equal(PlayerSpawnCommitResult.Committed, session.TryCommitSpawn(session.Slot));
        var output = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(64, 65536, 16384)); var ingress = new LandingIngress();
        using var sink = new PlayerBootstrapFrameSink(slots, output, PlayerBootstrapPacketSet.CreateForTesting(new byte[] { 3, 0, 7 }, [], new byte[] { 3, 0, 49 }),
            GameCommandSourceId.FromConnection(894), ingress, ingress);
        if (transition == "adopt") { sink.CommitServerTeleport(878f, 598f); sink.AdoptPlayingSession(session, null); }
        else
        {
            sink.AdoptPlayingSession(session, null); sink.CommitServerTeleport(878f, 598f);
            if (transition == "transfer") sink.BeginWorldTransferLanding(new PlayerSpawnCommitRequest(session.Slot, 20, 20, 0, 0, 0, 0, 0));
            else
            {
                byte[] bytes = new byte[18]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, 18); bytes[2] = 12;
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), 20); BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(6), 20);
                var sequence = new ReadOnlySequence<byte>(bytes); Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out var frame));
                Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(in frame));
            }
        }
        Assert.Equal(TerrariaFrameSinkResult.Continue, sink.OnFrame(MovementFrame(318f, 278f))); Assert.Equal(1, ingress.Count);
    }

    private static TerrariaFrame MovementFrame(float x, float y)
    {
        byte[] bytes = new byte[17]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, 17); bytes[2] = 13;
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(9), x); BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(13), y);
        var sequence = new ReadOnlySequence<byte>(bytes); Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out var frame)); return frame;
    }
    private sealed class LandingIngress : IPlayerMovementIngress, IPlayerSpawnCommitIngress
    {
        internal int Count;
        public bool TryPost(ConnectionHandle connection, in PlayerMovementCommitRequest request) { Count++; return true; }
        public bool TryPost(GameCommandSourceId source, PlayerJoinSession session, in PlayerSpawnCommitRequest request) => false;
        public bool TryPostRespawn(GameCommandSourceId source, PlayerHandle player, in PlayerSpawnCommitRequest request) => true;
    }
    private static PlayerMovementCommitRequest Motion(PlayerSlotId slot, float x, float y) => new(slot, 0, 4, 0, 0, 0, x, y, true, 2.25f, -1.5f, false, 0, false, 0, 0, 0, 0, false, 0, 0);

    private sealed class TeleportFixture : IDisposable
    {
        internal readonly WorldTileStore Tiles = new(new(80, 80));
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly RuntimeNpcStore Npcs;
        internal readonly ServerRuntimeState State;
        internal readonly ServerRuntimeComposition Graph;
        internal readonly ConnectionHandle Connection;
        internal readonly TerrariaConnectionOutboundQueue Peer = new(new OutboundQueueOptions(8192, 4194304, 8192));
        internal readonly PlayerJoinSession Session;
        internal Action? OnPrepare;
        internal TeleportFixture()
        {
            var registry = new RuntimeConnectionRegistry(); var npcReplication = new RuntimeNpcReplicationRegistry(); Npcs = new(commitSink: npcReplication);
            var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(891), new(new PlayerSlotId(254), new(1)));
            Assert.True(registry.TryRegister(peer.Source, Peer)); Assert.True(npcReplication.TryRegister(peer.Source, Peer));
            var peerSpawn = new PlayerSpawnCommitRequest(peer.Player.Slot, 20, 20, 0, 0, 0, 0, 0); registry.PlayerSpawned(peer, in peerSpawn); npcReplication.PlayerSpawned(peer, in peerSpawn);
            State = new(playerEvents: registry, npcs: Npcs, npcReplication: npcReplication, worldTiles: Tiles,
                naturalSpawnRandom: new SystemVanillaNpcRandom(Random), worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random));
            Graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Hidden)!.GetValue(State)!;
            var slots = new PlayerSlotPool(1); Assert.True(slots.TryAcquireConnection(out var lease)); Session = new(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            Session.ObserveWorldRequest(); Session.ObserveSectionRequest(); Connection = new(GameCommandSourceId.FromConnection(890), Session.Handle);
            Assert.True(registry.TryRegister(Connection.Source, new(new OutboundQueueOptions(8192, 4194304, 8192)), (_, _) => OnPrepare?.Invoke()));
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, Session, new(Connection.Player.Slot, 20, 40, 0, 0, 0, 0, 0)));
            State.Apply(new PlayerMovementRuntimeCommand(Connection, Motion(Connection.Player.Slot, 318f, 598f)));
            var birth = new NpcStateUpdate(3, 3, 324f, 598f, 2.25f, -1.5f, 255, default, NpcSimulationState.Initial);
            Assert.True(Npcs.TrySpawn(0, in birth, out var npc)); Assert.True(Npcs.TryRetainPendingBirth(in npc));
            for (int x = 10; x <= 57; x++) Tiles.Set(x, 40, new WorldTile { Flags = WorldTileFlags.WireRed });
            Tiles.Set(10, 40, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
            foreach (int left in new[] { 20, 55 }) for (int dx = 0; dx < 3; dx++) Tiles.Set(left + dx, 40, new WorldTile { Type = 235, FrameX = (short)(dx * 18), Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
            Drain(Peer);
        }
        internal void Press() => State.Apply(new ClientHitSwitchRuntimeCommand1458(Connection, new(10, 40)));
        public void Dispose() => Session.Dispose();
    }
}
