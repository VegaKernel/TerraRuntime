using System.Buffers;
using System.Reflection;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GolemLootPipelineTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Authoritative_player_kill_delivers_mode_correct_loot_once_and_marks_progression(bool expert, bool master)
    {
        var replication = new RuntimeWorldItemReplicationRegistry();
        var items = new RuntimeWorldItemStore(replication);
        var leases = new RuntimeWorldItemInstancedLeaseStore(items);
        var npcs = new RuntimeNpcStore();
        var progression = new RuntimeWorldProgressionMutations();
        var attacker = new PlayerHandle(new(0), new(1));
        var peer = new PlayerHandle(new(1), new(1));
        var players = new Players(attacker, peer);
        var attackerQueue = Register(replication, attacker, 8001);
        var peerQueue = Register(replication, peer, 8002);
        var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items, players,
            new PlayerAuthority(events: null, worldTiles: null), static () => 0,
            npcReplication: null, leases, replication, worldClock: null, progression, expert, master);

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Golem, out var definition));
        var update = new NpcStateUpdate(VanillaNpcIds.Golem.Value,
            checked((short)VanillaNpcIds.Golem.Value), 100, 200, 0, 0, 0, default,
            NpcSimulationState.Initial with { Life = definition.LifeMax, LifeMax = definition.LifeMax });
        Assert.True(npcs.TrySpawnVanilla(in update, out NpcSnapshot boss));

        // Use the existing server-owned melee boundary, not packet-28 legacy admission or a test-only kill path.
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed,
            pipeline.TryStrikeServerPlayerMelee(attacker, boss.Handle, 100_000, 0, false, 0, 1));
        Assert.False(npcs.TryGet(boss.Handle, out _));
        Assert.True(progression.IsCompleted(VanillaWorldProgressionId.Golem));

        List<(byte Message, TerrariaWorldItemDropState Drop)> attackerDrops = Drain(attackerQueue);
        List<(byte Message, TerrariaWorldItemDropState Drop)> peerDrops = Drain(peerQueue);
        if (expert)
        {
            var bag = Assert.Single(attackerDrops, static entry => entry.Message == 90);
            Assert.Equal((short)3329, bag.Drop.ItemNetId);
            Assert.Equal((short)1, bag.Drop.Stack);
            Assert.Equal((byte)0, bag.Drop.Prefix);
            Assert.DoesNotContain(peerDrops, static entry => entry.Message == 90);
            Assert.False(items.TryGetActive(bag.Drop.ItemIndex, out _));
            Assert.True(leases.TryGetRemainingTicks(bag.Drop.ItemIndex, out int remaining));
            Assert.Equal(54_000, remaining);
            Assert.Equal(1, leases.ActiveLeaseCount);
            Assert.DoesNotContain(attackerDrops, static entry => entry.Drop.ItemNetId is 2218 or 1294 or 6158 or 1258 or 1261);
        }
        else
        {
            Assert.DoesNotContain(attackerDrops, static entry => entry.Message == 90);
            Assert.Equal(0, leases.ActiveLeaseCount);
            var husks = Assert.Single(attackerDrops, static entry => entry.Drop.ItemNetId == 2218);
            Assert.InRange(husks.Drop.Stack, (short)4, (short)8);
            Assert.Single(attackerDrops, static entry => entry.Drop.ItemNetId is 1258 or 1122 or 899 or 1248 or 1295 or 1296 or 1297);
            Assert.Equal(attackerDrops.Any(static entry => entry.Drop.ItemNetId == 1258),
                attackerDrops.Any(static entry => entry.Drop.ItemNetId == 1261));
        }
        Assert.Equal(master, attackerDrops.Any(static entry => entry.Drop.ItemNetId == 4935));
        Assert.Equal(attackerDrops.Where(static entry => entry.Message == 21), peerDrops);
        Assert.Equal(attackerDrops.Count(static entry => entry.Message == 21), items.ActiveCount);

        long relayed = replication.RelayedFrames;
        Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected,
            pipeline.TryStrikeServerPlayerMelee(attacker, boss.Handle, 100_000, 0, false, 0, 1));
        Assert.Equal(relayed, replication.RelayedFrames);
        Assert.Equal(0, attackerQueue.QueuedFrames);
        Assert.Equal(0, peerQueue.QueuedFrames);
        Assert.True(replication.TryUnregister(GameCommandSourceId.FromConnection(8001)));
        Assert.True(replication.TryUnregister(GameCommandSourceId.FromConnection(8002)));
    }

    private static TerrariaConnectionOutboundQueue Register(
        RuntimeWorldItemReplicationRegistry replication, PlayerHandle player, long connectionId)
    {
        var source = GameCommandSourceId.FromConnection(connectionId);
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 32_768, 4_096));
        Assert.True(replication.TryRegister(source, queue));
        replication.PlayerSpawned(new(source, player), new(player.Slot, 100, 200, 0, 0, 0, 0, 0));
        return queue;
    }

    private static List<(byte Message, TerrariaWorldItemDropState Drop)> Drain(TerrariaConnectionOutboundQueue outbound)
    {
        PropertyInfo property = typeof(TerrariaConnectionOutboundQueue).GetProperty(
            "InnerQueue", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var queue = Assert.IsType<BoundedOutboundQueue>(property.GetValue(outbound));
        List<(byte Message, TerrariaWorldItemDropState Drop)> result = [];
        while (queue.TryRead(out OutboundFrame outboundFrame))
        {
            var bytes = new ReadOnlySequence<byte>(outboundFrame.Bytes);
            Assert.Equal(TerrariaFrameReadResult.Frame, TerrariaFrameDecoder.TryRead(ref bytes, out TerrariaFrame frame));
            Assert.Contains(frame.MessageId, new byte[] { 21, 90 });
            // Packet 90 deliberately shares packet 21's payload. Decode that payload with the existing adapter.
            var payloadView = frame with { MessageId = 21 };
            Assert.Equal(TerrariaWorldItemDropDecodeResult.Decoded,
                TerrariaWorldItemDropDecoder.TryDecode(in payloadView, out TerrariaWorldItemDropState drop));
            result.Add((frame.MessageId, drop));
        }
        return result;
    }

    private sealed class Players(PlayerHandle attacker, PlayerHandle peer) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            PlayerHandle player = slot == attacker.Slot ? attacker : slot == peer.Slot ? peer : default;
            snapshot = default(PlayerStateSnapshot) with
            {
                Player = player, Revision = new(1), PositionX = 100, PositionY = 200,
                HasHealth = true, Life = 500, MaxLife = 500
            };
            return player.IsAssigned;
        }
    }
}
