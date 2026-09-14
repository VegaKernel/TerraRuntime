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

public sealed class MoonLordLootPipelineTests
{
    [Theory]
    [InlineData(false,false,false)]
    [InlineData(true,false,false)]
    [InlineData(true,true,false)]
    [InlineData(false,false,true)]
    [InlineData(true,false,true)]
    [InlineData(true,true,true)]
    public void Part_participation_gets_loot_at_tick600_only_and_observers_never_receive_bags(
        bool expert, bool master, bool clientHit)
    {
        var npcs = new RuntimeNpcStore();
        var replication = new RuntimeWorldItemReplicationRegistry();
        var items = new RuntimeWorldItemStore(replication);
        var leases = new RuntimeWorldItemInstancedLeaseStore(items);
        var progression = new RuntimeWorldProgressionMutations();
        var slots = new PlayerSlotPool(4);
        Assert.True(slots.TryAcquireConnection(out var skippedLease));
        using var skipped = skippedLease;
        Assert.True(slots.TryAcquireConnection(out var participantLease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(participantLease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted,session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted,session.ObserveSectionRequest());
        var participant = session.Handle;
        var finisher = new PlayerHandle(new(2),new(1));
        var observer = new PlayerHandle(new(3),new(1));
        var participantQueue = Register(replication,participant,8001);
        var finisherQueue = Register(replication,finisher,8002);
        var observerQueue = Register(replication,observer,8003);
        var playerAuthority = new PlayerAuthority(events:null,worldTiles:null);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(8001),participant);
        Assert.True(playerAuthority.TryApply(new PlayerSpawnRuntimeCommand(connection,session,
            new(participant.Slot,100,200,0,0,0,0,0))));
        Assert.Equal(PlayerSpawnCommitResult.Committed,playerAuthority.LastSpawnCommitResult);
        // Packet-28 compatibility path: this fixture tests credit/loot, not direct-melee validation.
        Assert.True(playerAuthority.TryApply(new PlayerEquipmentRuntimeCommand(connection,
            new(participant.Slot,0,1,0,checked((short)VanillaItemIds.WoodenBow.Value),0))));
        Assert.Equal(0,playerAuthority.RejectedEquipmentUpdates);
        var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs,items,new Players(participant,finisher,observer),
            playerAuthority,static()=>0,npcReplication:null,
            leases,replication,worldClock:null,progression,expert,master);
        NpcSnapshot core = Spawn(npcs,VanillaNpcIds.MoonLordCore,new NpcAiState(1,0,0,0));
        NpcSnapshot part = Spawn(npcs, clientHit ? VanillaNpcIds.MoonLordHead : VanillaNpcIds.MoonLordHand,
            new NpcAiState(0,0,0,core.Handle.Slot));
        if (clientHit)
        {
            var hit = new TerrariaNpcDamageState(part.Handle.Slot,
                RuntimeNpcPacketProjection.ToProtocolGeneration(part.Handle.Generation),1,0,1,0);
            Assert.NotEqual(RuntimeNpcNetworkDamageResult.Rejected,
                pipeline.TryApply(new(GameCommandSourceId.FromConnection(8001),participant),in hit));
        }
        else
        {
            Assert.Equal(RuntimeProjectileNpcDamageResult.Committed,
                pipeline.TryStrikeServerPlayerMelee(participant,part.Handle,1,0,false,0,1));
        }
        Assert.True(pipeline.Interactions.HasInteraction(core.Handle,participant.Slot));
        Assert.False(pipeline.Interactions.HasInteraction(core.Handle,observer.Slot));
        Assert.Equal(RuntimeProjectileNpcDamageResult.Committed,
            pipeline.TryStrikeServerPlayerMelee(finisher,core.Handle,100_000,0,false,0,1));
        Assert.True(npcs.TryGet(core.Handle,out var dying));
        Assert.Equal(2f,dying.Ai.Ai0);
        Assert.False(progression.IsCompleted(VanillaWorldProgressionId.MoonLord));
        Assert.Equal(0,items.ActiveCount);
        var executor = new RuntimeNpcAiStateExecutor(npcs);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        for (int tick = 1; tick <= 600; tick++)
        {
            executor.Tick(stepper,pipeline);
            npcs.DespawnExpired();
            if (tick < 600)
            {
                Assert.Equal(0,participantQueue.QueuedFrames);
                Assert.Equal(0,finisherQueue.QueuedFrames);
                Assert.Equal(0,observerQueue.QueuedFrames);
            }
        }
        Assert.False(npcs.TryGet(core.Handle,out _));
        Assert.True(progression.IsCompleted(VanillaWorldProgressionId.MoonLord));
        var participantDrops = Drain(participantQueue);
        var finisherDrops = Drain(finisherQueue);
        var observerDrops = Drain(observerQueue);
        Assert.Equal(participantDrops,finisherDrops);
        Assert.Equal(participantDrops.Where(x=>x.Message==21),observerDrops);
        Assert.Equal(observerDrops.Count,items.ActiveCount);
        if (expert)
        {
            var bag = Assert.Single(participantDrops,x=>x.Message==90);
            Assert.Equal(3332,bag.Drop.ItemNetId);
            Assert.False(items.TryGetActive(bag.Drop.ItemIndex,out _));
            Assert.True(leases.TryGetRemainingTicks(bag.Drop.ItemIndex,out int ticks));
            Assert.Equal(54_000,ticks);
            Assert.DoesNotContain(participantDrops,x=>x.Drop.ItemNetId is 3384 or 3460);
        }
        else
        {
            Assert.DoesNotContain(participantDrops,x=>x.Message==90);
            Assert.Single(participantDrops,x=>x.Drop.ItemNetId==3384);
            Assert.InRange(Assert.Single(participantDrops,x=>x.Drop.ItemNetId==3460).Drop.Stack,(short)70,(short)90);
            var weapons = participantDrops.Where(x=>x.Drop.ItemNetId is 3063 or 3389 or 3065 or 1553 or 3930 or 3541 or 3570 or 3571 or 3569 or 5480).ToArray();
            Assert.Equal(2,weapons.Length);
            Assert.NotEqual(weapons[0].Drop.ItemNetId,weapons[1].Drop.ItemNetId);
        }
        Assert.Equal(master,participantDrops.Any(x=>x.Drop.ItemNetId==4938));
        long delivered = replication.RelayedFrames;
        pipeline.NpcAiStateCommitted(in dying);
        executor.Tick(stepper,pipeline);
        Assert.Equal(delivered,replication.RelayedFrames);
        Assert.Equal(0,participantQueue.QueuedFrames);
        Assert.Equal(0,finisherQueue.QueuedFrames);
        Assert.Equal(0,observerQueue.QueuedFrames);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(255f)]
    [InlineData(0f)]
    public void Part_credit_does_not_attach_to_absent_or_wrong_type_root(float rootSlot)
    {
        var npcs = new RuntimeNpcStore();
        var items = new RuntimeWorldItemStore();
        var actor = new PlayerHandle(new(1),new(1));
        var pipeline = new RuntimeNpcNetworkCombatPipeline(npcs,items,new Players(actor),
            new PlayerAuthority(events:null,worldTiles:null),static()=>0,npcReplication:null,
            new RuntimeWorldItemInstancedLeaseStore(items),worldItemReplication:null,worldClock:null,
            new RuntimeWorldProgressionMutations(),false,false);
        NpcSnapshot wrongRoot = Spawn(npcs,VanillaNpcIds.BlueSlime,default);
        NpcSnapshot otherCore = Spawn(npcs,VanillaNpcIds.MoonLordCore,new NpcAiState(1,0,0,0));
        NpcSnapshot part = Spawn(npcs,VanillaNpcIds.MoonLordHand,new NpcAiState(0,0,0,rootSlot));
        Assert.Equal(RuntimeProjectileNpcDamageResult.Committed,
            pipeline.TryStrikeServerPlayerMelee(actor,part.Handle,1,0,false,0,1));
        Assert.False(pipeline.Interactions.HasInteraction(wrongRoot.Handle,actor.Slot));
        Assert.False(pipeline.Interactions.HasInteraction(otherCore.Handle,actor.Slot));
    }

    private static NpcSnapshot Spawn(RuntimeNpcStore npcs,NpcTypeId type,NpcAiState ai)
    {
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(type,out var definition));
        var state = new NpcStateUpdate(type.Value,checked((short)type.Value),100,200,0,0,0,ai,
            NpcSimulationState.Initial with { Life=definition.LifeMax,LifeMax=definition.LifeMax,
                LocalAi=new NpcAiState(0,0,0,1) });
        Assert.True(npcs.TrySpawnVanilla(in state,out var npc));
        // Fixture begins at an exposed damage window, after the encounter AI removed spawn invulnerability.
        var exposed = state with { Simulation = npc.Simulation with { DontTakeDamage = false } };
        Assert.True(npcs.TryUpdate(npc.Handle,in exposed,out npc));
        return npc;
    }

    private sealed class Players(params PlayerHandle[] players) : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot,out PlayerStateSnapshot snapshot)
        {
            PlayerHandle player = players.FirstOrDefault(x=>x.Slot==slot);
            snapshot = default(PlayerStateSnapshot) with { Player=player,Revision=new(1),PositionX=100,PositionY=200,
                HasHealth=true,Life=500,MaxLife=500 };
            return player.IsAssigned;
        }
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc,out NpcStateUpdate next) { next=default; return false; }
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

}
