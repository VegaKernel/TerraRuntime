using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class NpcHistoricalProximity1458Tests
{
    [Theory]
    [InlineData(false,90)]
    [InlineData(true,95)]
    public void Historical_head_resets_recipient_credit_and_birth_also_resets_the_global_source_clock(bool pendingBirth,int expectedStreamPhase)
    {
        var registry=new RuntimeNpcReplicationRegistry();
        var npcs=new RuntimeNpcStore(commitSink:registry);
        registry.BindHistoricalStateSource(npcs);
        var update=new NpcStateUpdate(201,201,341f,-20f,1f,0f,0,default,
            NpcSimulationState.Initial with {Life=55,LifeMax=55,DirectionX=1,DirectionY=1});
        Assert.True(npcs.TrySpawn(7,in update,out var initial));
        var outbound=Join(registry,847458);
        for(int phase=1;phase<=100;phase++)
        {
            registry.AdvanceAuthoritativeTick();
            Assert.True(npcs.TryUpdateUnpublished(initial.Handle,in update,out var current));
            int before=outbound.QueuedFrames;
            Assert.True(npcs.TryPublishUpdate(in current,forceSync:phase==1,vanillaNetworkPhase:true));
            Assert.Equal(phase==1||phase==expectedStreamPhase?1:0,outbound.QueuedFrames-before);
            if(phase!=35)continue;
            if(pendingBirth)Assert.True(npcs.TryRetainPendingBirth(in current));
            var acceptedState=new NpcStateUpdate(current.Type,current.NetId,current.PositionX,current.PositionY,
                current.VelocityX,current.VelocityY,current.Target,current.Ai,current.Simulation);
            Assert.True(registry.TryPrepareHistoricalCheckpoint(in current,in acceptedState,pendingBirth,[],out var journal));
            Assert.True(journal!.TrySealRecipients());Assert.True(journal.TryAdoptBaseline(npcs,in current));
            int historicalBefore=outbound.QueuedFrames;
            Assert.True(journal.Publish(()=>npcs.TryPublishHistoricalCheckpoint(in current,in current)));
            Assert.Equal(historicalBefore+1,outbound.QueuedFrames);
            Assert.False(npcs.HasPendingBirth(current.Handle));
            // No extra AI publication is run here. At distance350 the phase30 offer retained4credits.
            // Ordinary head35 clears onlyrecipientcredit ->offers60/90. Birth clearsglobalclock too ->65/95.
        }
        Assert.Equal(3,outbound.QueuedFrames);Assert.Equal(0,registry.RejectedFrames);
    }

    [Fact]
    public void Unprepared_historical_trusted_publication_also_clears_the_recipient_credit()
    {
        var registry=new RuntimeNpcReplicationRegistry();var npcs=new RuntimeNpcStore(commitSink:registry);
        registry.BindHistoricalStateSource(npcs);
        var update=new NpcStateUpdate(201,201,341f,-20f,1f,0f,0,default,
            NpcSimulationState.Initial with {Life=55,LifeMax=55,DirectionX=1,DirectionY=1});
        Assert.True(npcs.TrySpawn(7,in update,out var initial));var outbound=Join(registry,847459);
        for(int phase=1;phase<=90;phase++)
        {
            Assert.True(npcs.TryUpdateUnpublished(initial.Handle,in update,out var current));
            int before=outbound.QueuedFrames;
            Assert.True(npcs.TryPublishUpdate(in current,forceSync:phase==1,vanillaNetworkPhase:true));
            Assert.Equal(phase is 1 or 90?1:0,outbound.QueuedFrames-before);
            if(phase==35)Assert.True(npcs.TryPublishHistoricalCheckpoint(in current,in current));
        }
        Assert.Equal(3,outbound.QueuedFrames);
    }

    [Theory]
    [InlineData(201,90)]
    [InlineData(534,94)]
    public void Source_network_offer_for_netAlways534_resets_the_global_clock_without_a_spawn(int type,int streamPhase)
    {
        var registry=new RuntimeNpcReplicationRegistry();var outbound=Join(registry,847460);
        for(int phase=1;phase<=100;phase++)
        {
            var npc=new NpcSnapshot(new(7,new(1)),new((ulong)phase),type,(short)type,341,-20,1,0,0,default,
                NpcSimulationState.Initial with {Life=400,LifeMax=400,DirectionX=1,DirectionY=1});
            int before=outbound.QueuedFrames;
            registry.NpcVanillaAiStateCommitted(phase==1?NpcStateCommitKind.Spawn:
                phase==35?NpcStateCommitKind.ForcedUpdate:NpcStateCommitKind.Update,in npc);
            Assert.Equal(phase==1||phase==35||phase==streamPhase?1:0,outbound.QueuedFrames-before);
        }
        Assert.Equal(3,outbound.QueuedFrames);
    }

    [Theory]
    [InlineData(201,new[]{1,2,3,4,31,61,91})]
    [InlineData(534,new[]{1,2,3,4,31})]
    public void A_real_netAlways_send_clears_the_pending_request_while_ordinary_source_npcs_keep_it_sticky(int type,int[] expected)
    {
        var registry=new RuntimeNpcReplicationRegistry();var outbound=Join(registry,847461);
        for(int phase=1;phase<=120;phase++)
        {
            var npc=new NpcSnapshot(new(7,new(1)),new((ulong)phase),type,(short)type,341,-20,0,0,0,default,
                NpcSimulationState.Initial with {Life=400,LifeMax=400,DirectionX=1,DirectionY=1});
            int before=outbound.QueuedFrames;
            registry.NpcVanillaAiStateCommitted(phase==1?NpcStateCommitKind.Spawn:
                phase<=6?NpcStateCommitKind.ForcedUpdate:NpcStateCommitKind.Update,in npc);
            Assert.Equal(expected.Contains(phase)?1:0,outbound.QueuedFrames-before);
        }
        Assert.Equal(expected.Length,outbound.QueuedFrames);
    }

    [Fact]
    public void A_delayed_old_netAlways_head_cannot_reset_a_newer_generations_global_stream_clock()
    {
        var registry=new RuntimeNpcReplicationRegistry();var outbound=Join(registry,847462);
        for(int phase=1;phase<=90;phase++)
        {
            var current=new NpcSnapshot(new(7,new(2)),new((ulong)phase),534,534,341,-20,1,0,0,default,
                NpcSimulationState.Initial with {Life=400,LifeMax=400,DirectionX=1,DirectionY=1});
            int before=outbound.QueuedFrames;
            registry.NpcVanillaAiStateCommitted(phase==1?NpcStateCommitKind.Spawn:NpcStateCommitKind.Update,in current);
            Assert.Equal(phase is 1 or 60?1:0,outbound.QueuedFrames-before);
            if(phase==35)
            {
                var retired=current with {Handle=new(7,new(1)),Revision=new(1000)};
                registry.NpcStateCommitted(NpcStateCommitKind.ForcedUpdate,in retired);
            }
        }
    }

    private static TerrariaConnectionOutboundQueue Join(RuntimeNpcReplicationRegistry registry,int id)
    {
        var outbound=new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32,32768,1024));
        var source=GameCommandSourceId.FromConnection(id);Assert.True(registry.TryRegister(source,outbound));
        var connection=new ConnectionHandle(source,new(new PlayerSlotId(0),new(1)));
        registry.PlayerSpawned(connection,new(connection.Player.Slot,0,0,0,0,0,0,0));
        GemBunnyWire1458Tests.Drain(outbound);return outbound;
    }
}
