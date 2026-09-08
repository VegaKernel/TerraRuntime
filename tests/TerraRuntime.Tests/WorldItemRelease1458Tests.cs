using System.Buffers;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldItemRelease1458Tests
{
    [Fact]
    public void Packet39_owner_release_reaches_real_writer_and_doll_burns()
    {
        using var f = new Fixture();
        var doll = f.Allocate(WorldItemOwnershipMode.ReserveForLocalPlayer);
        Assert.Equal(f.Connection.Player.Slot.Value, doll.OwnerPlayerId);
        f.State.Tick();
        Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out _));
        Assert.Equal(TerrariaFrameSinkResult.Continue, f.Sink.OnFrame(Release(doll.Handle.Slot)));
        Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var released));
        Assert.Equal(255, released.OwnerPlayerId);
        Assert.Equal(0, released.TimeToKeepReservation);
        f.State.Tick();
        Assert.False(f.Items.TryGetActive(doll.Handle.Slot, out _));
        Assert.False(f.Npcs.TryGet(f.Guide.Handle, out _));
        Assert.Equal(1, f.Npcs.ActiveCount);
    }

    [Fact]
    public void Packet39_on_dry_item_does_not_gain_summoning_authority()
    {
        using var f = new Fixture();
        var doll = f.Allocate(WorldItemOwnershipMode.ReserveForLocalPlayer, y: 3840);
        f.Sink.OnFrame(Release(doll.Handle.Slot));
        f.State.Tick();
        Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out _));
        Assert.True(f.Npcs.TryGet(f.Guide.Handle, out _));
    }

    [Fact]
    public void New_packet21_drop_keeps_source_grab_delay_and_falls_into_lava()
    {
        using var f = new Fixture();
        // Independently pinned packet21 bytes: slot400, (2560,3840), zero velocity, stack1, prefix0,
        // ownership2, type267. Goes through real decoder/ingress, not a direct environmental call.
        byte[] payload = [144,1, 0,0,32,69, 0,0,112,69, 0,0,0,0, 0,0,0,0, 1,0, 0,2, 11,1];
        Assert.Equal(TerrariaFrameSinkResult.Continue, f.Sink.OnFrame(Frame(21, payload)));
        var buffer = new WorldItemSnapshot[400];
        Assert.Equal(1, f.Items.CopyActive(buffer));
        var doll = buffer[0];
        Assert.Equal(f.Connection.Player.Slot.Value, doll.GrabDelayPlayer);
        Assert.Equal(100, doll.GrabDelayTime);
        for (int i = 0; i < 100 && f.Items.TryGetActive(doll.Handle.Slot, out _); i++) f.State.Tick();
        Assert.False(f.Items.TryGetActive(doll.Handle.Slot, out _));
        Assert.False(f.Npcs.TryGet(f.Guide.Handle, out _));
    }

    [Fact]
    public void Nonowner_stale_connection_and_reused_item_commands_cannot_release()
    {
        using var f = new Fixture();
        var doll = f.Allocate(WorldItemOwnershipMode.ReserveForLocalPlayer);
        var stale = new ConnectionHandle(f.Connection.Source, new PlayerHandle(f.Connection.Player.Slot,
            new PlayerSessionGeneration(f.Connection.Player.Generation.Value + 1)));
        f.State.Apply(new WorldItemReleaseRuntimeCommand(stale, doll.Handle, true));
        Assert.True(f.Items.TryGetActive(doll.Handle.Slot, out var unchanged));
        Assert.Equal(doll.Revision, unchanged.Revision);
        Assert.True(f.Items.TryRemove(doll.Handle.Slot, out _));
        var replacement = f.Allocate(WorldItemOwnershipMode.ReserveForLocalPlayer);
        f.State.Apply(new WorldItemReleaseRuntimeCommand(f.Connection, doll.Handle, true));
        Assert.True(f.Items.TryGetActive(replacement.Handle.Slot, out unchanged));
        Assert.Equal(replacement.Revision, unchanged.Revision);
        Assert.True(f.Items.TryApplyOwner(replacement.Handle.Slot, new WorldItemOwnerStateUpdate(1, 0, 255, 0, 2560, 4000), out var foreign));
        f.Sink.OnFrame(Release(replacement.Handle.Slot));
        Assert.True(f.Items.TryGetActive(replacement.Handle.Slot, out unchanged));
        Assert.Equal(foreign.Revision, unchanged.Revision);
    }

    [Theory]
    [InlineData(WorldItemOwnershipMode.GrabDelayForLocalPlayer)]
    [InlineData(WorldItemOwnershipMode.GrabDelayForAllPlayers)]
    public void Spawn_grab_delay_expires_without_per_tick_owner_broadcast(WorldItemOwnershipMode mode)
    {
        using var f = new Fixture();
        var item = f.Allocate(mode, y: 3840, type: 2);
        for (int i = 0; i < 100; i++) f.State.Tick();
        Assert.True(f.Items.TryGetActive(item.Handle.Slot, out var expired));
        Assert.Equal(0, expired.GrabDelayTime);
        Assert.Equal(255, expired.OwnerPlayerId);
        f.State.Tick(); f.State.Tick();
        Assert.True(f.Items.TryGetActive(item.Handle.Slot, out var reserved));
        Assert.Equal(f.Connection.Player.Slot.Value, reserved.OwnerPlayerId);
    }

    private static TerrariaFrame Release(short slot) => Frame(39, [(byte)slot, (byte)(slot >> 8), 1]);

    private static TerrariaFrame Frame(byte message, byte[] payload)
    {
        byte[] bytes = new byte[payload.Length + 3];
        bytes[0] = (byte)bytes.Length; bytes[1] = (byte)(bytes.Length >> 8); bytes[2] = message;
        payload.CopyTo(bytes, 3);
        var sequence = new ReadOnlySequence<byte>(bytes);
        Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref sequence, out var frame));
        return frame;
    }

    private sealed class Fixture : IDisposable
    {
        public RuntimeWorldItemStore Items { get; } = new();
        public RuntimeNpcStore Npcs { get; } = new();
        public NpcSnapshot Guide { get; }
        public ServerRuntimeState State { get; }
        public ConnectionHandle Connection { get; }
        public WorldItemFrameSink Sink { get; }
        private readonly PlayerBootstrapFrameSink bootstrap;
        public Fixture()
        {
            var tiles = new WorldTileStore(new WorldDimensions(400, 400));
            for (int x = 150; x <= 170; x++)
            for (int y = 247; y <= 258; y++)
                tiles.Tiles[tiles.GetUncheckedIndex(x, y)] = x is 150 or 170 || y == 258
                    ? new WorldTile { Type = 1, Flags = WorldTileFlags.Active }
                    : new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Lava };
            State = new ServerRuntimeState(npcs: Npcs, worldItems: Items, worldTiles: tiles,
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458));
            Assert.True(Npcs.TrySpawnVanilla(new NpcStateUpdate(22, 22, 800, 800, 0, 0, 255, default, NpcSimulationState.Initial), out var guide));
            Guide = guide;
            var slots = new PlayerSlotPool(2);
            Assert.True(slots.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var source = GameCommandSourceId.FromConnection(991);
            Connection = new ConnectionHandle(source, session.Handle);
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new PlayerSpawnCommitRequest(session.Slot, 160, 240, 0, 0, 0, 0, 0)));
            Assert.Equal(PlayerSpawnCommitResult.Committed, State.LastSpawnCommitResult);
            bootstrap = new PlayerBootstrapFrameSink(slots,
                new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 16384, 1024)),
                PlayerBootstrapPacketSet.CreateForTesting(new byte[] {3,0,7}, Array.Empty<ReadOnlyMemory<byte>>(), new byte[] {3,0,49}));
            bootstrap.AdoptPlayingSession(session, playerName: null);
            Sink = new WorldItemFrameSink(source, bootstrap, new Pass(), new RuntimeWorldItemIngress(new Immediate(State), Items));
        }
        public WorldItemSnapshot Allocate(WorldItemOwnershipMode mode, float y = 4000, short type = 267)
        {
            var result = new TaskCompletionSource<WorldItemSnapshot?>();
            State.Apply(new WorldItemAllocateRuntimeCommand(Connection,
                new WorldItemDropStateUpdate(2560, y, 0, 0, 1, 0, mode, type, false, 0, 0), result));
            return Assert.IsType<WorldItemSnapshot>(result.Task.Result);
        }
        public void Dispose() => bootstrap.Dispose();
    }
    private sealed class Immediate(ServerRuntimeState state) : IGameCommandIngress<RuntimeCommand>
    {
        public bool TryPost(GameCommandSourceId source, RuntimeCommand command) { state.Apply(command); return true; }
    }
    private sealed class Pass : ITerrariaFrameSink
    {
        public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame) => TerrariaFrameSinkResult.Continue;
    }
}
