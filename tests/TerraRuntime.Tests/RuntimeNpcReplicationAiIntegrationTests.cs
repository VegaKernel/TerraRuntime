using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcReplicationAiIntegrationTests
{
    [Fact]
    public void Authoritative_ai_commit_enqueues_packet_23_for_playing_client()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(1);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 16, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));
        Assert.True(replication.TryRegister(source, outbound));

        var player = new ConnectionHandle(
            source,
            new PlayerHandle(
                new PlayerSlotId(4),
                new PlayerSessionGeneration(1)));
        PlayerSpawnCommitRequest playerSpawn = new(player.Player.Slot, 100, 200, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(player, in playerSpawn);

        var store = new RuntimeNpcStore(capacity: 4, commitSink: replication);
        NpcStateUpdate initial = new(
            Type: 1,
            NetId: 1,
            PositionX: 100f,
            PositionY: 200f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 4,
            Ai: new NpcAiState(0f, 0f, 0f, 0f),
            Simulation: NpcSimulationState.Initial);
        Assert.True(store.TrySpawn(1, in initial, out _));
        Assert.Equal(1, outbound.QueuedFrames);

        var executor = new RuntimeNpcAiStateExecutor(store);
        NpcAiStateTickSummary summary = executor.Tick(new MoveNpcStepper());

        Assert.Equal(new NpcAiStateTickSummary(1, 1, 1, 0), summary);
        Assert.Equal(2, outbound.QueuedFrames);
        Assert.Equal(2, replication.RelayedFrames);
        Assert.Equal(0, replication.RejectedFrames);
        Assert.Equal(0, replication.UnsupportedCommits);
    }

    private sealed class MoveNpcStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = new NpcStateUpdate(
                npc.Type,
                npc.NetId,
                npc.PositionX + 1f,
                npc.PositionY,
                npc.VelocityX + 0.25f,
                npc.VelocityY,
                npc.Target,
                npc.Ai with { Ai0 = npc.Ai.Ai0 + 1f },
                npc.Simulation);
            return true;
        }
    }
}
