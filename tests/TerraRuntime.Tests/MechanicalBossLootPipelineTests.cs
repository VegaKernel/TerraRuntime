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

public sealed class MechanicalBossLootPipelineTests
{
    [Theory]
    [InlineData(125, false, false)]
    [InlineData(125, true, false)]
    [InlineData(125, true, true)]
    [InlineData(126, false, false)]
    [InlineData(126, true, false)]
    [InlineData(126, true, true)]
    [InlineData(127, false, false)]
    [InlineData(127, true, false)]
    [InlineData(127, true, true)]
    [InlineData(134, false, false)]
    [InlineData(134, true, false)]
    [InlineData(134, true, true)]
    public void Root_kill_uses_existing_authority_item_replication_and_progression(int type, bool expert, bool master)
    {
        var f = new Fixture(expert, master);
        NpcSnapshot root = f.Spawn(type);
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(f.First, root, 100_000));
        Assert.False(f.Npcs.TryGet(root.Handle, out _));
        Assert.True(f.Progression.IsCompleted(type switch
        {
            127 => VanillaWorldProgressionId.SkeletronPrime,
            134 => VanillaWorldProgressionId.Destroyer,
            _ => VanillaWorldProgressionId.Twins
        }));
        Assert.True(f.Progression.IsCompleted(VanillaWorldProgressionId.AnyMechanicalBoss));
        var drops = Drain(f.FirstQueue);
        Assert.DoesNotContain(Drain(f.SpectatorQueue), static x => x.Message == 90);
        int bagId = type switch { 127 => 3327, 134 => 3325, _ => 3326 };
        int relicId = type switch { 127 => 4933, 134 => 4932, _ => 4931 };
        int soulId = type switch { 127 => 547, 134 => 548, _ => 549 };
        Assert.Equal(master, drops.Any(x => x.Drop.ItemNetId == relicId));
        if (expert)
        {
            var bag = Assert.Single(drops, static x => x.Message == 90);
            Assert.Equal(bagId, bag.Drop.ItemNetId);
            Assert.False(f.Items.TryGetActive(bag.Drop.ItemIndex, out _));
            Assert.True(f.Leases.TryGetRemainingTicks(bag.Drop.ItemIndex, out int remaining));
            Assert.Equal(54_000, remaining);
            Assert.DoesNotContain(drops, x => x.Drop.ItemNetId is 1225 || x.Drop.ItemNetId == soulId);
        }
        else
        {
            Assert.DoesNotContain(drops, static x => x.Message == 90);
            var bars = Assert.Single(drops, static x => x.Drop.ItemNetId == 1225);
            var souls = Assert.Single(drops, x => x.Drop.ItemNetId == soulId);
            Assert.InRange(bars.Drop.Stack, (short)15, (short)30);
            Assert.InRange(souls.Drop.Stack, (short)25, (short)40);
        }
        long relayed = f.Replication.RelayedFrames;
        Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, f.Hit(f.First, root, 100_000));
        Assert.Equal(relayed, f.Replication.RelayedFrames);
    }

    [Theory]
    [InlineData(125, 126)]
    [InlineData(126, 125)]
    public void Twins_credit_spans_eyes_but_bag_and_progression_wait_for_last_active_eye(int firstType, int secondType)
    {
        var f = new Fixture(expert: true, master: false);
        NpcSnapshot first = f.Spawn(firstType);
        NpcSnapshot second = f.Spawn(secondType);
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(f.First, first, 100_000));
        Assert.False(f.Progression.IsCompleted(VanillaWorldProgressionId.Twins));
        Assert.Equal(0, f.Leases.ActiveLeaseCount);
        Assert.DoesNotContain(Drain(f.FirstQueue), static x => x.Message == 90);
        Assert.DoesNotContain(Drain(f.SecondQueue), static x => x.Message == 90);
        Assert.DoesNotContain(Drain(f.SpectatorQueue), static x => x.Message == 90);
        Assert.True(f.Npcs.TryGet(second.Handle, out _));

        // Second player never touched the first eye; first player never touched the second eye.
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(f.Second, second, 100_000));
        Assert.True(f.Progression.IsCompleted(VanillaWorldProgressionId.Twins));
        var firstBag = Assert.Single(Drain(f.FirstQueue), static x => x.Message == 90);
        var secondBag = Assert.Single(Drain(f.SecondQueue), static x => x.Message == 90);
        Assert.Equal((short)3326, firstBag.Drop.ItemNetId);
        Assert.Equal(firstBag.Drop, secondBag.Drop);
        Assert.Equal(1, f.Leases.ActiveLeaseCount);
        Assert.DoesNotContain(Drain(f.SpectatorQueue), static x => x.Message == 90);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(130)]
    [InlineData(131)]
    public void Prime_arm_interaction_credits_head_reward_without_dropping_root_loot_on_arm_death(int armType)
    {
        var f = new Fixture(expert: true, master: false);
        NpcSnapshot head = f.Spawn(127);
        NpcSnapshot arm = f.Spawn(armType);
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(f.First, arm, 100_000));
        Assert.Equal(0, f.Leases.ActiveLeaseCount);
        Assert.False(f.Progression.IsCompleted(VanillaWorldProgressionId.SkeletronPrime));
        Assert.Empty(Drain(f.FirstQueue));
        Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(f.Second, head, 100_000));
        var firstBag = Assert.Single(Drain(f.FirstQueue), static x => x.Message == 90);
        var secondBag = Assert.Single(Drain(f.SecondQueue), static x => x.Message == 90);
        Assert.Equal((short)3327, firstBag.Drop.ItemNetId);
        Assert.Equal(firstBag.Drop, secondBag.Drop);
        Assert.DoesNotContain(Drain(f.SpectatorQueue), static x => x.Message == 90);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(50, false)]
    [InlineData(50, true)]
    [InlineData(657, false)]
    [InlineData(657, true)]
    [InlineData(125, false)]
    [InlineData(125, true)]
    [InlineData(127, false)]
    [InlineData(127, true)]
    [InlineData(134, false)]
    [InlineData(134, true)]
    public void Clientless_server_player_credit_never_breaks_boss_death_or_steals_human_bag(int type, bool humanParticipates)
    {
        var f = new Fixture(expert: true, master: false, firstHasClient: false);
        NpcSnapshot boss = f.Spawn(type);
        if (humanParticipates)
        {
            Assert.Equal(RuntimeProjectileNpcDamageResult.Committed, f.Hit(f.First, boss, 1));
            Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(f.Second, boss, 100_000));
        }
        else
            Assert.Equal(RuntimeProjectileNpcDamageResult.Killed, f.Hit(f.First, boss, 100_000));
        Assert.False(f.Npcs.TryGet(boss.Handle, out _));
        var human = Drain(f.SecondQueue);
        Assert.Equal(humanParticipates ? 1 : 0, human.Count(static x => x.Message == 90));
        Assert.DoesNotContain(Drain(f.SpectatorQueue), static x => x.Message == 90);
        Assert.Empty(Drain(f.FirstQueue));
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


    private sealed class Fixture : IRuntimePlayerSlotSnapshotLookup
    {
        public readonly PlayerHandle First = new(new(0), new(1));
        public readonly PlayerHandle Second = new(new(1), new(1));
        public readonly PlayerHandle Spectator = new(new(2), new(1));
        public readonly RuntimeNpcStore Npcs = new();
        public readonly RuntimeWorldProgressionMutations Progression = new();
        public readonly RuntimeWorldItemReplicationRegistry Replication = new();
        public readonly RuntimeWorldItemStore Items;
        public readonly RuntimeWorldItemInstancedLeaseStore Leases;
        public readonly TerrariaConnectionOutboundQueue FirstQueue;
        public readonly TerrariaConnectionOutboundQueue SecondQueue;
        public readonly TerrariaConnectionOutboundQueue SpectatorQueue;
        private readonly RuntimeNpcNetworkCombatPipeline pipeline;

        public Fixture(bool expert, bool master, bool firstHasClient = true)
        {
            Items = new(Replication);
            Leases = new(Items);
            FirstQueue = firstHasClient ? Register(Replication, First, 8101) :
                new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 32_768, 4_096));
            SecondQueue = Register(Replication, Second, 8102);
            SpectatorQueue = Register(Replication, Spectator, 8103);
            pipeline = new(Npcs, Items, this, new PlayerAuthority(null, null), static () => 0,
                null, Leases, Replication, null, Progression, expert, master);
        }

        public RuntimeProjectileNpcDamageResult Hit(PlayerHandle player, in NpcSnapshot npc, int damage) =>
            pipeline.TryStrikeServerPlayerMelee(player, npc.Handle, damage, 0, false, 0, 1);

        public NpcSnapshot Spawn(int type)
        {
            Assert.True(VanillaNpcDefinitionCatalog.TryGet(new NpcTypeId(type), out var definition));
            var update = new NpcStateUpdate(type, checked((short)type), 100, 200, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = definition.LifeMax, LifeMax = definition.LifeMax });
            Assert.True(Npcs.TrySpawnVanilla(in update, out NpcSnapshot npc));
            return npc;
        }

        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            PlayerHandle player = slot == First.Slot ? First : slot == Second.Slot ? Second :
                slot == Spectator.Slot ? Spectator : default;
            snapshot = default(PlayerStateSnapshot) with
            {
                Player = player, Revision = new(1), PositionX = 100, PositionY = 200,
                HasHealth = true, Life = 500, MaxLife = 500
            };
            return player.IsAssigned;
        }
    }
}
