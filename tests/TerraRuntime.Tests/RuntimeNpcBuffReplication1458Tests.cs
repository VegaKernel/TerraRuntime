using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
namespace TerraRuntime.Tests;
public sealed class RuntimeNpcBuffReplication1458Tests
{
    [Fact]
    public void Packet53_resolves_authenticated_current_membership_and_rejects_forged_or_recycled_sender()
    {
        var npcs = new RuntimeNpcStore(); var initial = State(17);
        Assert.True(npcs.TrySpawn(0, in initial, out NpcSnapshot npc));
        var state = new ServerRuntimeState(npcs: npcs);
        var runtime = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        using PlayerJoinSession session = Session(new PlayerSlotPool(1));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(8111), session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0)));
        var add = new TerrariaNpcBuffState(0, 120, 180);
        state.Apply(new ClientNpcBuffRuntimeCommand(new ConnectionHandle(GameCommandSourceId.FromConnection(8112), connection.Player), add));
        state.Apply(new ClientNpcBuffRuntimeCommand(connection with { Player = new PlayerHandle(connection.Player.Slot, new PlayerSessionGeneration(connection.Player.Generation.Value + 1)) }, add));
        Assert.Equal(2, runtime.Npcs.RejectedNpcBuffs);
        Assert.True(runtime.Npcs.NpcBuffStatus.TryGetWireDuration(npc.Handle, out int duration)); Assert.Equal(-1, duration);
        state.Apply(new ClientNpcBuffRuntimeCommand(connection, add));
        Assert.Equal(1, runtime.Npcs.AppliedNpcBuffs);
        Assert.True(runtime.Npcs.NpcBuffStatus.TryGetWireDuration(npc.Handle, out duration)); Assert.Equal(180, duration);
        Assert.True(npcs.TryDespawn(npc.Handle)); Assert.True(npcs.TrySpawn(0, in initial, out var replacement));
        Assert.False(runtime.Npcs.NpcBuffStatus.TryApply(npc.Handle, 180));
        Assert.True(runtime.Npcs.NpcBuffStatus.TryGetWireDuration(replacement.Handle, out duration)); Assert.Equal(-1, duration);
        state.Apply(new PlayerDisconnectRuntimeCommand(connection)); state.Apply(new ClientNpcBuffRuntimeCommand(connection, add));
        Assert.Equal(3, runtime.Npcs.RejectedNpcBuffs);
    }

    [Fact]
    public void Source54_reaches_requester_and_other_playing_peer_and_join_replays23_then_current54()
    {
        var registry = new RuntimeNpcReplicationRegistry(); var npcs = new RuntimeNpcStore(commitSink: registry);
        var owner = new RuntimeNpcBuffStatus1458(npcs); registry.BindNpcBuffStatus(owner);
        var initial = State(17); Assert.True(npcs.TrySpawn(0, in initial, out NpcSnapshot npc));
        var requester = Endpoint(registry, 8113, 0); var peer = Endpoint(registry, 8114, 1);
        Assert.Equal(new byte[] { 23, 54 }, Drain(requester).Select(x => x[2]).ToArray());
        Assert.Equal(new byte[] { 23, 54 }, Drain(peer).Select(x => x[2]).ToArray());
        Assert.True(owner.TryApply(npc.Handle, 180)); registry.PublishNpcBuffs(npc.Handle);
        Assert.True(TerrariaNpcBuffCodec.TryEncodeCurrent(0, 180, out byte[] expected));
        Assert.Equal(expected, Assert.Single(Drain(requester))); Assert.Equal(expected, Assert.Single(Drain(peer)));
        owner.BeginWorldTick(); Assert.True(owner.TryPlan(in npc, out var plan)); Assert.True(owner.Commit(in plan, in npc)); var joining = Endpoint(registry, 8115, 2); byte[][] replay = Drain(joining);
        Assert.Equal(new byte[] { 23, 54 }, replay.Select(x => x[2]).ToArray());
        Assert.True(TerrariaNpcBuffCodec.TryEncodeCurrent(0, 179, out byte[] current)); Assert.Equal(current, replay[1]);
        Assert.True(npcs.TryDespawn(npc.Handle)); Assert.True(npcs.TrySpawn(0, in initial, out _));
        var replacementJoin = Endpoint(registry, 8116, 3); replay = Drain(replacementJoin);
        Assert.True(TerrariaNpcBuffCodec.TryEncodeCurrent(0, -1, out byte[] clear)); Assert.Equal(clear, replay[1]);
        registry.PublishNpcBuffs(npc.Handle); Assert.Empty(Drain(replacementJoin));
    }
    private static TerrariaConnectionOutboundQueue Endpoint(RuntimeNpcReplicationRegistry registry, long id, byte slot)
    {
        var source = GameCommandSourceId.FromConnection(id); var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 16384, 1024));
        Assert.True(registry.TryRegister(source, queue)); var connection = new ConnectionHandle(source, new PlayerHandle(new PlayerSlotId(slot), new PlayerSessionGeneration(1)));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0); registry.PlayerSpawned(connection, in spawn); return queue;
    }
    private static byte[][] Drain(TerrariaConnectionOutboundQueue queue)
    { var frames = new List<byte[]>(); var owned = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!; while (owned.TryRead(out OutboundFrame frame)) frames.Add(frame.Bytes.ToArray()); return frames.ToArray(); }
    private static PlayerJoinSession Session(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease)); var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest()); Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest()); return session;
    }
    private static NpcStateUpdate State(int type) => new(type, (short)type, 639, 440, 0, 0, 255, default, NpcSimulationState.Initial with { Life = 250, LifeMax = 250 });
}
