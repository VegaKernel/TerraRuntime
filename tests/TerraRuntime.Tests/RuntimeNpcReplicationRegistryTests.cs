using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Application.Operations;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcReplicationRegistryTests
{
    [Fact]
    public void Existing_npc_is_sent_as_spawn_baseline_only_after_player_enters_playing_state()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));
        NpcSnapshot npc = CreateNpc(revision: 3, positionX: 120f);

        replication.NpcStateCommitted(NpcStateCommitKind.Update, in npc);
        Assert.Equal(0, outbound.QueuedFrames);

        ConnectionHandle player = Connection(source, slot: 4, generation: 1);
        PlayerSpawnCommitRequest spawn = CreatePlayerSpawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);

        Assert.Equal(1, outbound.QueuedFrames);
        Assert.Equal(1, replication.BaselineFrames);
        Assert.Equal(0, replication.RelayedFrames);
        Assert.Equal(0, replication.RejectedFrames);
    }

    [Fact]
    public void Playing_client_receives_spawn_update_and_despawn_and_despawn_clears_future_baseline()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(1);
        TerrariaConnectionOutboundQueue firstOutbound = CreateOutbound();
        Assert.True(replication.TryRegister(firstSource, firstOutbound));
        ConnectionHandle first = Connection(firstSource, slot: 1, generation: 1);
        PlayerSpawnCommitRequest firstSpawn = CreatePlayerSpawn(first.Player.Slot);
        replication.PlayerSpawned(first, in firstSpawn);

        NpcSnapshot npc = CreateNpc(revision: 1, positionX: 100f);
        replication.NpcStateCommitted(NpcStateCommitKind.Spawn, in npc);
        Assert.Equal(1, firstOutbound.QueuedFrames);

        NpcSnapshot moved = npc with
        {
            Revision = new NpcRevision(2),
            PositionX = 130f
        };
        replication.NpcStateCommitted(NpcStateCommitKind.Update, in moved);
        Assert.Equal(2, firstOutbound.QueuedFrames);

        replication.NpcStateCommitted(NpcStateCommitKind.Despawn, in moved);
        Assert.Equal(3, firstOutbound.QueuedFrames);

        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(2);
        TerrariaConnectionOutboundQueue secondOutbound = CreateOutbound();
        Assert.True(replication.TryRegister(secondSource, secondOutbound));
        ConnectionHandle second = Connection(secondSource, slot: 2, generation: 1);
        PlayerSpawnCommitRequest secondSpawn = CreatePlayerSpawn(second.Player.Slot);
        replication.PlayerSpawned(second, in secondSpawn);

        Assert.Equal(0, secondOutbound.QueuedFrames);
        Assert.Equal(3, replication.RelayedFrames);
        Assert.Equal(0, replication.RejectedFrames);
    }

    [Fact]
    public void Unsupported_npc_type_is_not_put_on_the_wire()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));
        ConnectionHandle player = Connection(source, slot: 1, generation: 1);
        PlayerSpawnCommitRequest spawn = CreatePlayerSpawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);
        // 99 is now SeekerBody (worm) and is supported; 900 is truly unsupported.
        NpcSnapshot unsupported = CreateNpc(revision: 1, positionX: 100f) with
        {
            Type = 900,
            NetId = 900
        };

        replication.NpcStateCommitted(NpcStateCommitKind.Spawn, in unsupported);

        Assert.Equal(0, outbound.QueuedFrames);
        Assert.Equal(1, replication.UnsupportedCommits);
    }

    [Fact]
    public void Network_operations_surface_existing_npc_replication_counters()
    {
        var admission = new TerrariaConnectionAdmissionGate(maxConnections: 8);
        var connections = new RuntimeConnectionRegistry();
        var replication = new RuntimeNpcReplicationRegistry();
        var operations = new LocalRuntimeNetworkOperations(
            admission,
            connections,
            new RuntimeConnectionQueueTelemetry(),
            new RuntimeConnectionRateTelemetry(),
            replication);
        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 2, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));
        Assert.True(replication.TryRegister(source, outbound));

        NpcSnapshot npc = CreateNpc(revision: 1, positionX: 100f);
        replication.NpcStateCommitted(NpcStateCommitKind.Update, in npc);
        ConnectionHandle player = Connection(source, slot: 1, generation: 1);
        PlayerSpawnCommitRequest spawn = CreatePlayerSpawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);

        NpcSnapshot moved = npc with
        {
            Revision = new NpcRevision(2),
            PositionX = 130f
        };
        replication.NpcStateCommitted(NpcStateCommitKind.Update, in moved);

        NpcSnapshot rejected = moved with
        {
            Revision = new NpcRevision(3),
            PositionX = 150f
        };
        replication.NpcStateCommitted(NpcStateCommitKind.Update, in rejected);

        // 900 is truly unsupported; 99 is now worm-family supported.
        NpcSnapshot unsupported = rejected with
        {
            Revision = new NpcRevision(4),
            Type = 900,
            NetId = 900
        };
        replication.NpcStateCommitted(NpcStateCommitKind.Update, in unsupported);

        RuntimeNetworkSnapshot snapshot = operations.CaptureSnapshot();

        Assert.Equal(1, snapshot.NpcBaselineFrames);
        Assert.Equal(1, snapshot.NpcRelayedFrames);
        Assert.Equal(1, snapshot.NpcRejectedFrames);
        Assert.Equal(1, snapshot.NpcUnsupportedCommits);
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));

    private static ConnectionHandle Connection(
        GameCommandSourceId source,
        byte slot,
        ulong generation) =>
        new(
            source,
            new PlayerHandle(
                new PlayerSlotId(slot),
                new PlayerSessionGeneration(generation)));

    private static PlayerSpawnCommitRequest CreatePlayerSpawn(PlayerSlotId slot) =>
        new(slot, 100, 200, 0, 0, 0, 0, 0);

    private static NpcSnapshot CreateNpc(ulong revision, float positionX) =>
        new(
            Handle: new NpcHandle(7, new NpcGeneration(1)),
            Revision: new NpcRevision(revision),
            Type: 1,
            NetId: 1,
            PositionX: positionX,
            PositionY: 200f,
            VelocityX: 1f,
            VelocityY: -2f,
            Target: 4,
            Ai: new NpcAiState(1f, 0f, 0f, 0f),
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = -1
            });
}
