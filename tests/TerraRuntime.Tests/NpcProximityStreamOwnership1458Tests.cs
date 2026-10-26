using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class NpcProximityStreamOwnership1458Tests
{
    [Fact]
    public void Accepted_npc_packet_resets_only_the_recipient_stream_credit()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var outbound = Join(replication, 841458);
        for (int tick = 1; tick <= 90; tick++)
        {
            replication.AdvanceAuthoritativeTick();
            var npc = Npc((ulong)tick, 1);
            int before = outbound.QueuedFrames;
            replication.NpcVanillaAiStateCommitted(tick == 1 ? NpcStateCommitKind.Spawn :
                tick == 45 ? NpcStateCommitKind.ForcedUpdate : NpcStateCommitKind.Update, in npc);
            // Source NPC.StreamUpdatesToNearbyPlayers offers four credits at distance350
            // each30 moving updates. SendData23 at45 clears recipient credit, not netStream:
            // tick60 has only four credits again; the next proximity packet is tick90.
            Assert.Equal(tick is 1 or 45 or 90 ? 1 : 0, outbound.QueuedFrames - before);
        }
        Assert.Equal(3, outbound.QueuedFrames);
        Assert.Equal(0, replication.RejectedFrames);
    }

    [Fact]
    public void Physical_slot_reuse_keeps_new_generation_credit_separate_from_retired_owners()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var outbound = Join(replication, 841459);
        var old = Npc(1, 1);
        for (int tick = 1; tick <= 30; tick++)
        {
            old = Npc((ulong)tick, 1);
            replication.NpcVanillaAiStateCommitted(tick == 1 ? NpcStateCommitKind.Spawn : NpcStateCommitKind.Update, in old);
        }
        Assert.Equal(1, outbound.QueuedFrames);
        replication.NpcStateCommitted(NpcStateCommitKind.Despawn, in old);
        Assert.Equal(2, outbound.QueuedFrames);
        for (int tick = 1; tick <= 60; tick++)
        {
            var replacement = Npc((ulong)tick, 2);
            int before = outbound.QueuedFrames;
            replication.NpcVanillaAiStateCommitted(tick == 1 ? NpcStateCommitKind.Spawn : NpcStateCommitKind.Update, in replacement);
            Assert.Equal(tick is 1 or 60 ? 1 : 0, outbound.QueuedFrames - before);
            // An out-of-order retired generation must not reset the current counter/baseline.
            var stale = old with { Revision = new((ulong)(1000 + tick)) };
            int after = outbound.QueuedFrames;
            replication.NpcVanillaAiStateCommitted(NpcStateCommitKind.ForcedUpdate, in stale);
            Assert.Equal(after, outbound.QueuedFrames);
        }
        Assert.Equal(4, outbound.QueuedFrames);
        Assert.Equal(0, replication.RejectedFrames);
    }

    [Fact]
    public void Immediate_manual_packet_resets_credit_without_advancing_the_ai_stream_clock()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var outbound = Join(replication, 841460);
        for (int tick = 1; tick <= 90; tick++)
        {
            replication.AdvanceAuthoritativeTick();
            int before = outbound.QueuedFrames;
            if (tick == 45)
            {
                var manual = Npc((ulong)(tick * 2 - 1), 1);
                replication.NpcStateCommitted(NpcStateCommitKind.ForcedUpdate, in manual);
            }
            var npc = Npc((ulong)(tick * 2), 1);
            replication.NpcVanillaAiStateCommitted(tick == 1 ? NpcStateCommitKind.Spawn : NpcStateCommitKind.Update, in npc);
            Assert.Equal(tick is 1 or 45 or 90 ? 1 : 0, outbound.QueuedFrames - before);
        }
        Assert.Equal(3, outbound.QueuedFrames);
        Assert.Equal(0, replication.RejectedFrames);
    }

    private static TerrariaConnectionOutboundQueue Join(RuntimeNpcReplicationRegistry replication, int sourceId)
    {
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 32768, 1024));
        var source = GameCommandSourceId.FromConnection(sourceId);
        Assert.True(replication.TryRegister(source, outbound));
        var connection = new ConnectionHandle(source, new(new PlayerSlotId(0), new(1)));
        replication.PlayerSpawned(connection, new(connection.Player.Slot, 0, 0, 0, 0, 0, 0, 0));
        return outbound;
    }

    private static NpcSnapshot Npc(ulong revision, ulong generation) => new(
        new(7, new(generation)), new(revision), 201, 201, 341f, -20f, 1f, 0f, 0, default,
        NpcSimulationState.Initial with { Life = 55, LifeMax = 55, DirectionX = 1, DirectionY = 1 });
}
