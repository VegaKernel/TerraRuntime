using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class UndeadNetworkCadence1458Tests
{
    // Independently executed original NPC.UpdateNetworkCode + NetUpdateLowPriority and
    // NetMessage.SendData(23), 120 phases per scenario, repeated on both original editions.
    [Theory]
    [InlineData("quiet", new[] { 1 })]
    [InlineData("overload", new[] { 1, 2, 3, 4, 31, 61, 91 })]
    [InlineData("low-priority", new[] { 1, 31, 61, 91 })]
    [InlineData("spawn-clears", new[] { 1 })]
    public void Source_network_phase_preserves_spam_threshold_and_sticky_pending_requests(string scenario, int[] sends)
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var outbound = Join(replication, 9831);
        for (int tick = 1; tick <= 120; tick++)
        {
            replication.AdvanceAuthoritativeTick();
            var npc = Npc((ulong)tick);
            bool force = scenario == "overload" ? tick <= 6 : tick == 1;
            bool lowPriority = scenario == "low-priority" && tick == 2 || scenario == "spawn-clears" && tick == 1;
            if (lowPriority)
                Assert.True(replication.MarkUndeadLowPriorityNetworkUpdate(in npc));
            int before = outbound.QueuedFrames;
            var kind = tick == 1 ? NpcStateCommitKind.Spawn :
                force ? NpcStateCommitKind.ForcedUpdate : NpcStateCommitKind.Update;
            replication.NpcVanillaAiStateCommitted(kind, in npc);
            Assert.Equal(sends.Contains(tick) ? 1 : 0, outbound.QueuedFrames - before);
        }
        Assert.Equal(sends.Length, outbound.QueuedFrames);
        Assert.Equal(0, replication.UnsupportedCommits);
        Assert.Equal(0, replication.RejectedFrames);
    }

    [Fact]
    public void Manual_life_commits_remain_immediate_without_advancing_the_ai_cooldown()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var outbound = Join(replication, 9832);
        ulong revision = 0;
        for (int tick = 1; tick <= 6; tick++)
        {
            replication.AdvanceAuthoritativeTick();
            var npc = Npc(++revision);
            replication.NpcVanillaAiStateCommitted(tick == 1 ? NpcStateCommitKind.Spawn : NpcStateCommitKind.ForcedUpdate, in npc);
        }
        Assert.Equal(4, outbound.QueuedFrames);
        for (int hit = 1; hit <= 20; hit++)
        {
            var damaged = Npc(++revision) with
            {
                Simulation = NpcSimulationState.Initial with { Life = 100 - hit, LifeMax = 100 }
            };
            int before = outbound.QueuedFrames;
            replication.NpcStateCommitted(NpcStateCommitKind.Update, in damaged);
            Assert.Equal(before + 1, outbound.QueuedFrames);
        }
        Assert.Equal(24, outbound.QueuedFrames);
        for (int tick = 7; tick <= 31; tick++)
        {
            replication.AdvanceAuthoritativeTick();
            var npc = Npc(++revision);
            int before = outbound.QueuedFrames;
            replication.NpcVanillaAiStateCommitted(NpcStateCommitKind.Update, in npc);
            Assert.Equal(tick == 31 ? 1 : 0, outbound.QueuedFrames - before);
        }
    }

    [Fact]
    public void Replacement_generation_starts_with_fresh_cooldowns_and_rejects_old_low_priority_requests()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var outbound = Join(replication, 9833);
        var old = Npc(1);
        for (int tick = 1; tick <= 6; tick++)
        {
            replication.AdvanceAuthoritativeTick();
            old = old with { Revision = new((ulong)tick) };
            replication.NpcVanillaAiStateCommitted(tick == 1 ? NpcStateCommitKind.Spawn : NpcStateCommitKind.ForcedUpdate, in old);
        }
        replication.NpcStateCommitted(NpcStateCommitKind.Despawn, in old);
        var replacement = Npc(1) with { Handle = new(old.Handle.Slot, new(2)) };
        int before = outbound.QueuedFrames;
        replication.NpcVanillaAiStateCommitted(NpcStateCommitKind.Spawn, in replacement);
        Assert.Equal(before + 1, outbound.QueuedFrames);
        Assert.False(replication.MarkUndeadLowPriorityNetworkUpdate(in old));
        replacement = replacement with { Revision = new(2) };
        replication.NpcVanillaAiStateCommitted(NpcStateCommitKind.ForcedUpdate, in replacement);
        Assert.Equal(before + 2, outbound.QueuedFrames);
    }

    [Fact]
    public void Quiet_ai_keeps_late_join_baseline_current_without_periodic_fallback_packets()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var outbound = Join(replication, 9834);
        var npc = Npc(1);
        for (int tick = 1; tick <= 120; tick++)
        {
            replication.AdvanceAuthoritativeTick();
            npc = Npc((ulong)tick) with { PositionX = 100 + tick };
            replication.NpcVanillaAiStateCommitted(tick == 1 ? NpcStateCommitKind.Spawn : NpcStateCommitKind.Update, in npc);
        }
        Assert.Equal(1, outbound.QueuedFrames);
        var late = Join(replication, 9835);
        Assert.Equal(1, late.QueuedFrames);
        var inner = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(late)!;
        Assert.True(inner.TryRead(out var actual));
        Assert.True(RuntimeNpcPacketProjection.TryCreate(in npc, RuntimeNpcSyncKind.Spawn, out var expected));
        Assert.True(TerraRuntime.Protocol.Multiplicity.TerrariaNpcUpdateEncoder.TryEncode(in expected, out var encoded));
        Assert.Equal(encoded, actual.Bytes.ToArray());
    }

    [Fact]
    public void Stale_or_repeated_ai_phase_preserves_cooldown_and_the_latest_late_join_baseline()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var outbound = Join(replication, 9837);
        var latest = Npc(1);
        for (int tick = 1; tick <= 6; tick++)
        {
            replication.AdvanceAuthoritativeTick();
            latest = Npc((ulong)tick) with { PositionX = 100 + tick };
            replication.NpcVanillaAiStateCommitted(tick == 1 ? NpcStateCommitKind.Spawn : NpcStateCommitKind.ForcedUpdate, in latest);
        }
        Assert.Equal(4, outbound.QueuedFrames);
        var stale = latest with { Revision = new(3), PositionX = 9999 };
        Assert.False(replication.MarkUndeadLowPriorityNetworkUpdate(in stale));
        Assert.False(replication.MarkUndeadLowPriorityNetworkUpdate(in latest));
        replication.NpcVanillaAiStateCommitted(NpcStateCommitKind.ForcedUpdate, in stale, lowPriority: true);
        var repeated = latest with { PositionX = 7777 };
        replication.NpcVanillaAiStateCommitted(NpcStateCommitKind.ForcedUpdate, in repeated, lowPriority: true);
        Assert.Equal(4, outbound.QueuedFrames);

        var late = Join(replication, 9838);
        var inner = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetProperty("InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(late)!;
        Assert.True(inner.TryRead(out var actual));
        Assert.True(RuntimeNpcPacketProjection.TryCreate(in latest, RuntimeNpcSyncKind.Spawn, out var expected));
        Assert.True(TerraRuntime.Protocol.Multiplicity.TerrariaNpcUpdateEncoder.TryEncode(in expected, out var encoded));
        Assert.Equal(encoded, actual.Bytes.ToArray());
        Assert.False(inner.TryRead(out _));
        for (int tick = 7; tick <= 31; tick++)
        {
            replication.AdvanceAuthoritativeTick();
            var npc = Npc((ulong)tick);
            int before = outbound.QueuedFrames;
            replication.NpcVanillaAiStateCommitted(NpcStateCommitKind.Update, in npc);
            Assert.Equal(tick == 31 ? 1 : 0, outbound.QueuedFrames - before);
        }
    }

    [Fact]
    public void Base_zombie_keeps_existing_fallback_until_its_network_phase_is_verified()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        var outbound = Join(replication, 9836);
        for (int tick = 1; tick <= 31; tick++)
        {
            replication.AdvanceAuthoritativeTick();
            var npc = Npc((ulong)tick) with { Type = 3, NetId = 3, PositionX = 100 + tick };
            replication.NpcVanillaAiStateCommitted(tick == 1 ? NpcStateCommitKind.Spawn : NpcStateCommitKind.Update, in npc);
        }
        Assert.Equal(2, outbound.QueuedFrames);
    }

    private static NpcSnapshot Npc(ulong revision) => new(
        new(7, new(1)), new(revision), 201, 201, 100, 200, 0, 0, 0, default,
        NpcSimulationState.Initial with { Life = 100, LifeMax = 100, DirectionX = 1, DirectionY = -1 });

    private static TerrariaConnectionOutboundQueue Join(RuntimeNpcReplicationRegistry replication, int sourceId)
    {
        var outbound = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 65536, 1024));
        var source = GameCommandSourceId.FromConnection(sourceId);
        Assert.True(replication.TryRegister(source, outbound));
        var connection = new ConnectionHandle(source, new(new(0), new(1)));
        replication.PlayerSpawned(connection, new(connection.Player.Slot, 0, 0, 0, 0, 0, 0, 0));
        return outbound;
    }
}
