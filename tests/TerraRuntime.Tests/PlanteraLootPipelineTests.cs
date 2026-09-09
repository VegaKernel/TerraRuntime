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

public sealed class PlanteraLootPipelineTests
{
    [Theory]
    [InlineData(262, true)]
    [InlineData(262, false)]
    [InlineData(245, true)]
    [InlineData(245, false)]
    public void Clientless_or_stale_generation_interactor_never_sends_bag_to_observer(int type, bool clientless)
    {
        var replication = new RuntimeWorldItemReplicationRegistry();
        var items = new RuntimeWorldItemStore(replication);
        var leases = new RuntimeWorldItemInstancedLeaseStore(items);
        var npcs = new RuntimeNpcStore();
        var attacker = new PlayerHandle(new(0), new(2));
        var peer = new PlayerHandle(new(1), new(1));
        var peerQueue = Register(replication, peer, 8005);
        TerrariaConnectionOutboundQueue? staleQueue = clientless ? null :
            Register(replication, new PlayerHandle(attacker.Slot, new(1)), 8004);
        var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items, new Players(attacker, peer),
            new PlayerAuthority(events: null, worldTiles: null), static () => 0, null,
            leases, replication, null, new RuntimeWorldProgressionMutations(), true, false,
            planteraDownedBaseline: false);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(type), out var definition));
        var update = new NpcStateUpdate(type, checked((short)type), 100, 200, 0, 0, 0, default,
            NpcSimulationState.Initial with { Life = definition.LifeMax, LifeMax = definition.LifeMax });
        Assert.True(npcs.TrySpawnVanilla(in update, out var boss));
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed,
            pipeline.TryStrikeServerPlayerMelee(attacker, boss.Handle, 100_000, 0, false, 0, 1));
        Assert.DoesNotContain(Drain(peerQueue), entry => entry.Message == 90);
        if (staleQueue is not null)
        {
            Assert.DoesNotContain(Drain(staleQueue), entry => entry.Message == 90);
            Assert.True(replication.TryUnregister(GameCommandSourceId.FromConnection(8004)));
        }
        Assert.Equal(1, leases.ActiveLeaseCount);
        Assert.True(replication.TryUnregister(GameCommandSourceId.FromConnection(8005)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(null)]
    public void Loaded_baseline_and_committed_death_control_first_kill_without_restart_or_duplicate_rewards(bool? baseline)
    {
        var replication = new RuntimeWorldItemReplicationRegistry();
        var items = new RuntimeWorldItemStore(replication);
        var npcs = new RuntimeNpcStore();
        var progression = new RuntimeWorldProgressionMutations();
        var attacker = new PlayerHandle(new(0), new(1));
        var queue = Register(replication, attacker, 8003);
        var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs, items, new Players(attacker, default),
            new PlayerAuthority(events: null, worldTiles: null), static () => 0, null,
            new RuntimeWorldItemInstancedLeaseStore(items), replication, null, progression, false, false,
            planteraDownedBaseline: baseline);
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Plantera, out var definition));
        for (int kill = 0; kill < 2; kill++)
        {
            // Fix the existing production RNG, not the death/evaluator/delivery path. No production test seam.
            object random = typeof(RuntimeNpcNetworkCombatPipeline).GetField("random",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pipeline)!;
            random.GetType().GetField("random", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(random, new Random(42));
            var update = new NpcStateUpdate(VanillaNpcIds.Plantera.Value,
                checked((short)VanillaNpcIds.Plantera.Value), 100, 200, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = definition.LifeMax, LifeMax = definition.LifeMax });
            Assert.True(npcs.TrySpawnVanilla(in update, out var boss));
            Assert.Equal(RuntimeProjectileNpcDamageResult.Killed,
                pipeline.TryStrikeServerPlayerMelee(attacker, boss.Handle, 100_000, 0, false, 0, 1));
            var drops = Drain(queue);
            Assert.True(progression.IsCompleted(VanillaWorldProgressionId.Plantera));
            if (kill == 0 && baseline is null)
            {
                Assert.Empty(drops);
                continue;
            }
            Assert.Single(drops, entry => entry.Drop.ItemNetId == 1141);
            // After the trophy chance, seed 42's raw Next(1), Next(8) select option 1: Venus Magnum.
            int weapon = kill == 0 && baseline == false ? 758 : 1255;
            Assert.Single(drops, entry => entry.Drop.ItemNetId == weapon);
            Assert.Equal(weapon == 758, drops.Any(entry => entry.Drop.ItemNetId == 771));
            Assert.DoesNotContain(drops, entry => entry.Drop.ItemNetId == (weapon == 758 ? 1255 : 758));
        }
        Assert.True(replication.TryUnregister(GameCommandSourceId.FromConnection(8003)));
    }

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
            npcReplication: null, leases, replication, worldClock: null, progression, expert, master, planteraDownedBaseline: false);

        Assert.True(VanillaNpcDefinitionCatalog.TryGet(VanillaNpcIds.Plantera, out var definition));
        var update = new NpcStateUpdate(VanillaNpcIds.Plantera.Value,
            checked((short)VanillaNpcIds.Plantera.Value), 100, 200, 0, 0, 0, default,
            NpcSimulationState.Initial with { Life = definition.LifeMax, LifeMax = definition.LifeMax });
        Assert.True(npcs.TrySpawnVanilla(in update, out NpcSnapshot boss));

        // Use the existing server-owned melee boundary, not packet-28 legacy admission or a test-only kill path.
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed,
            pipeline.TryStrikeServerPlayerMelee(attacker, boss.Handle, 100_000, 0, false, 0, 1));
        Assert.False(npcs.TryGet(boss.Handle, out _));
        Assert.True(progression.IsCompleted(VanillaWorldProgressionId.Plantera));

        List<(byte Message, TerrariaWorldItemDropState Drop)> attackerDrops = Drain(attackerQueue);
        List<(byte Message, TerrariaWorldItemDropState Drop)> peerDrops = Drain(peerQueue);
        if (expert)
        {
            var bag = Assert.Single(attackerDrops, static entry => entry.Message == 90);
            Assert.Equal((short)3328, bag.Drop.ItemNetId);
            Assert.Equal((short)1, bag.Drop.Stack);
            Assert.Equal((byte)0, bag.Drop.Prefix);
            Assert.DoesNotContain(peerDrops, static entry => entry.Message == 90);
            Assert.False(items.TryGetActive(bag.Drop.ItemIndex, out _));
            Assert.True(leases.TryGetRemainingTicks(bag.Drop.ItemIndex, out int remaining));
            Assert.Equal(54_000, remaining);
            Assert.Equal(1, leases.ActiveLeaseCount);
            Assert.DoesNotContain(attackerDrops, static entry => entry.Drop.ItemNetId is 1141 or 758 or 771 or 1157 or 3018 or 5477);
        }
        else
        {
            Assert.DoesNotContain(attackerDrops, static entry => entry.Message == 90);
            Assert.Equal(0, leases.ActiveLeaseCount);
            Assert.Single(attackerDrops, static entry => entry.Drop.ItemNetId == 1141);
            Assert.Single(attackerDrops, static entry => entry.Drop.ItemNetId == 758);
            var rockets = Assert.Single(attackerDrops, static entry => entry.Drop.ItemNetId == 771);
            Assert.InRange(rockets.Drop.Stack, (short)50, (short)150);
        }
        Assert.Equal(master, attackerDrops.Any(static entry => entry.Drop.ItemNetId == 4934));
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
