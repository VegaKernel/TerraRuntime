using global::Multiplicity.Packets;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

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
        NpcUpdate packet = ReadNpcPacket(outbound);
        Assert.Equal(npc.Handle.Slot, packet.NpcSlot);
        Assert.True((packet.ExtraFlags & NpcUpdateExtraFlags.SpawnNeedsSyncing) != 0);
        Assert.Equal(25, packet.Life);
        Assert.Equal(1, replication.BaselineFrames);
    }

    [Fact]
    public void Playing_client_receives_live_update_without_spawn_flag_and_despawn_clears_future_baseline()
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
        NpcUpdate spawned = ReadNpcPacket(firstOutbound);
        Assert.True((spawned.ExtraFlags & NpcUpdateExtraFlags.SpawnNeedsSyncing) != 0);

        NpcSnapshot moved = npc with
        {
            Revision = new NpcRevision(2),
            PositionX = 130f
        };
        replication.NpcStateCommitted(NpcStateCommitKind.Update, in moved);
        NpcUpdate updated = ReadNpcPacket(firstOutbound);
        Assert.False((updated.ExtraFlags & NpcUpdateExtraFlags.SpawnNeedsSyncing) != 0);
        Assert.Equal(130f, updated.PositionX);

        replication.NpcStateCommitted(NpcStateCommitKind.Despawn, in moved);
        NpcUpdate despawned = ReadNpcPacket(firstOutbound);
        Assert.Equal(0, despawned.Life);

        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(2);
        TerrariaConnectionOutboundQueue secondOutbound = CreateOutbound();
        Assert.True(replication.TryRegister(secondSource, secondOutbound));
        ConnectionHandle second = Connection(secondSource, slot: 2, generation: 1);
        PlayerSpawnCommitRequest secondSpawn = CreatePlayerSpawn(second.Player.Slot);
        replication.PlayerSpawned(second, in secondSpawn);

        Assert.Equal(0, secondOutbound.QueuedFrames);
        Assert.Equal(3, replication.RelayedFrames);
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
        NpcSnapshot unsupported = CreateNpc(revision: 1, positionX: 100f) with
        {
            Type = 99,
            NetId = 99
        };

        replication.NpcStateCommitted(NpcStateCommitKind.Spawn, in unsupported);

        Assert.Equal(0, outbound.QueuedFrames);
        Assert.Equal(1, replication.UnsupportedCommits);
    }

    private static NpcUpdate ReadNpcPacket(TerrariaConnectionOutboundQueue outbound)
    {
        Assert.True(outbound.InnerQueue.TryRead(out OutboundFrame frame));
        return Assert.IsType<NpcUpdate>(
            TerrariaPacket.Deserialize(frame.Bytes));
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
