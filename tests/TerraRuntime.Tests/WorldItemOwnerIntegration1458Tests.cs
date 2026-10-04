using System.Buffers;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldItemOwnerIntegration1458Tests
{
    [Theory] [MemberData(nameof(Cadences))]
    public void Per_item_clock_matches_actual_original_UpdateServer_for_owned_unowned_disconnected_and_wrapped_age(string json)
    {
        using var document=JsonDocument.Parse(json);var row=document.RootElement;using var f=new Fixture(1458,false,0);
        if(row.GetProperty("owner").GetInt32()==0)
            for(short slot=0;slot<VanillaPlayerItemSlotCatalog.MainInventoryCount;slot++)
                f.State.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,new(new(0),slot,9999,0,1,0)));
        if(!row.GetProperty("active").GetBoolean())f.State.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
        Assert.True(f.Items.TryUpsert(0,new(1000,1000,0,0,1,0,WorldItemOwnershipMode.None,2,false,0,0,checked((byte)row.GetProperty("owner").GetInt32()),0,0,0),out var item));
        var slots=Assert.IsAssignableFrom<Array>(typeof(RuntimeWorldItemStore).GetField("_slots",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(f.Items));
        var retained=slots.GetValue(0)!;retained.GetType().GetField("SourceOwnerAge",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.SetValue(retained,row.GetProperty("age").GetInt32());slots.SetValue(retained,0);
        Drain(f.Outbound);f.Items.TickReservationTimers();f.Graph.WorldItems.TickPlayerReservations(123456);
        Assert.True(f.Items.TryGetActive(0,out var after));Assert.Equal(row.GetProperty("afterOwner").GetInt32(),after.OwnerPlayerId);
        Assert.True(f.Items.TryGetSourceOwnerMetadata(after.Handle,out int age,out _));Assert.Equal(row.GetProperty("afterAge").GetInt32(),age);
        if(age==int.MinValue){f.Items.TickReservationTimers();f.Graph.WorldItems.TickPlayerReservations(999999);Assert.True(f.Items.TryGetSourceOwnerMetadata(after.Handle,out int paused,out _));Assert.Equal(int.MinValue,paused);}
        Assert.Equal(906992634,f.Random.Next());
    }
    public static IEnumerable<object[]> Cadences()=>Cases("world-item-owner-cadence-official.json.gz");

    [Theory] [MemberData(nameof(BuffCases))]
    public void Packet50_generation_owned_Heartreach_changes_original_pickup_range_and_invalidates_old_context(string json)
    {
        using var document=JsonDocument.Parse(json);var row=document.RootElement;bool heartreach=row.GetProperty("buff").GetInt32()!=0;
        using var f=new Fixture(1458,false,0);f.State.Apply(new PlayerMovementRuntimeCommand(f.Connection,new(new(0),0,0,0,0,0,1200,1000,false,0,0,false,0,false,0,0,0,0,false,0,0)));
        f.AddPlayer();
        f.Tiles.Set(62,62,new WorldTile{Type=21,Flags=WorldTileFlags.Active|WorldTileFlags.WireRed});
        var drop=Drop(1000,1000) with{ItemNetId=58};
        var old=f.Graph.WorldItems.SourceOwnerFacts.Capture();
        f.State.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection,new(new(0),heartreach?new BuffTypeId[]{new(105)}:Array.Empty<BuffTypeId>())));
        Assert.False(old.IsCurrent);var current=f.Graph.WorldItems.SourceOwnerFacts.Capture();Assert.True(current.TrySelectOwner(drop,0,0,out byte owner));
        Assert.Equal(row.GetProperty("owner").GetInt32(),owner);Assert.True(current.IsCurrent);
    }
    public static IEnumerable<object[]> BuffCases()
    {
        foreach(var row in Cases("world-item-owner-buff-official.json.gz"))
        {using var document=JsonDocument.Parse((string)row[0]);if(document.RootElement.GetProperty("ticks").GetInt32()>0)yield return row;}
    }
    [Theory] [MemberData(nameof(Acknowledgments))]
    public void Authenticated_release_matches_original39_and_rejects_foreign_current_player(string json)
    {
        using var document=JsonDocument.Parse(json);var row=document.RootElement;
        using var f=new Fixture(1458,false,0);
        f.State.Apply(new PlayerMovementRuntimeCommand(f.Connection,new(new(0),0,0,0,0,0,1030,1000,false,0,0,false,0,false,0,0,0,0,false,0,0)));
        var foreign=f.AddPlayer();
        Assert.True(f.Items.TryUpsert(50,new(1000,1000,0,0,1,0,WorldItemOwnershipMode.None,2,false,0,0,255,0,0,0),out var item));
        Assert.True(f.Items.TryApplyOwner(50,new(0,0,0,0,1000,1000),out item));
        Assert.True(f.Items.TryRequestSourceOwnerRelease(item.Handle));Drain(f.Outbound);
        var ingress=new RuntimeWorldItemIngress(new Immediate(f.State),f.Items);
        Assert.True(ingress.TryPostRelease(row.GetProperty("forged").GetBoolean()?foreign:f.Connection,50,row.GetProperty("force").GetBoolean()));
        Assert.True(f.Items.TryGetActive(50,out var result));
        int sourceOwner=row.GetProperty("owner").GetInt32();Assert.Equal(sourceOwner==7?0:sourceOwner,result.OwnerPlayerId);
        Assert.True(f.Items.TryGetSourceOwnerMetadata(result.Handle,out int age,out bool pending));
        Assert.Equal(row.GetProperty("time").GetInt32(),age);Assert.Equal(row.GetProperty("forged").GetBoolean(),pending);
        Assert.Equal(906992634,f.Random.Next());
    }
    public static IEnumerable<object[]> Acknowledgments()=>Cases("world-item-owner-ack-official.json.gz");

    [Fact]
    public void Represented_shared_favorite_modifier_with_unowned_compatibility_is_fenced_and_nonfunctional_slot_cannot_grant_it()
    {
        using var f=new Fixture(1458,false,0);var drop=Drop(1000,1000) with{ItemNetId=184};
        f.State.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,new(new(0),903,1,0,2219,PlayerEquipmentCommitRequest.FavoriteItemFlag)));
        Assert.False(f.Graph.WorldItems.SourceOwnerFacts.Capture().TrySelectOwner(drop,0,0,out _));
        f.State.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,new(new(0),903,0,0,0,0)));
        Assert.True(f.Graph.WorldItems.SourceOwnerFacts.Capture().TrySelectOwner(drop,0,0,out byte owner));Assert.Equal(0,owner);
    }
    [Theory] [MemberData(nameof(EquipmentCases))]
    public void Generation_owned_equipment_projection_matches_original_UpdateEquips_and_FindOwner(string json)
    {
        using var doc=JsonDocument.Parse(json);var row=doc.RootElement;
        using var f=new Fixture(1458,false,0,row.GetProperty("mode").GetInt32(),row.GetProperty("good").GetBoolean());
        var appearance=default(PlayerAppearanceCommitRequest) with{PlayerSlot=new(0),Name="Owner",DifficultyFlags=row.GetProperty("extra").GetBoolean()?(byte)4:(byte)0};
        f.State.Apply(new PlayerAppearanceRuntimeCommand(f.Connection,appearance));
        f.State.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,new(new(0),checked((short)(VanillaPlayerItemSlotCatalog.ArmorStart+row.GetProperty("slot").GetInt32())),1,0,checked((short)row.GetProperty("type").GetInt32()),0)));
        f.State.Apply(new PlayerMovementRuntimeCommand(f.Connection,new(new(0),0,0,0,0,0,1200,1000,false,0,0,false,0,false,0,0,0,0,false,0,0)));
        f.Tiles.Set(62,62,new WorldTile{Type=21,Flags=WorldTileFlags.Active|WorldTileFlags.WireRed});
        foreach(var original in row.GetProperty("owners").EnumerateObject())
        {
            var drop=Drop(1000,1000) with{ItemNetId=short.Parse(original.Name,System.Globalization.CultureInfo.InvariantCulture)};
            var snapshot=f.Graph.WorldItems.SourceOwnerFacts.Capture();Assert.True(snapshot.TrySelectOwner(drop,0,0,out byte owner));
            Assert.Equal(original.Value.GetInt32(),owner);Assert.True(snapshot.IsCurrent);
        }
        f.AddPlayer();var star=Drop(1000,1000) with{ItemNetId=184};var competing=f.Graph.WorldItems.SourceOwnerFacts.Capture();
        Assert.True(competing.TrySelectOwner(star,0,0,out byte manaOwner));Assert.Equal(row.GetProperty("manaOwner").GetInt32(),manaOwner);
        Assert.Equal(906992634,f.Random.Next());
    }

    public static IEnumerable<object[]> EquipmentCases()=>Cases("world-item-owner-equipment-official.json.gz");
    private static IEnumerable<object[]> Cases(string name)
    {
        using var stream=typeof(WorldItemOwnerIntegration1458Tests).Assembly.GetManifestResourceStream("TerraRuntime.Tests.Fixtures."+name)!;
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);using var document=JsonDocument.Parse(gzip);
        foreach(var row in document.RootElement.EnumerateArray())yield return [row.GetRawText()];
    }
    [Theory] [MemberData(nameof(ClientCreations))]
    public void Real_client21_creation_matches_actual_original_body_owner_wire_and_rng(string json)
    {
        using var doc=JsonDocument.Parse(json);var row=doc.RootElement;
        using var f=new Fixture(row.GetProperty("seed").GetInt32(),row.GetProperty("full").GetBoolean(),row.GetProperty("season").GetInt32());
        using var memory=new MemoryStream();using(var writer=new BinaryWriter(memory,System.Text.Encoding.UTF8,true))
        {writer.Write((short)400);writer.Write(1000f);writer.Write(1000f);writer.Write(2.5f);writer.Write(-3.75f);writer.Write((short)1);writer.Write((byte)row.GetProperty("prefix").GetInt32());writer.Write((byte)row.GetProperty("mode").GetInt32());writer.Write((short)row.GetProperty("type").GetInt32());}
        Assert.Equal(TerrariaFrameSinkResult.Continue,f.Sink.OnFrame(Frame(21,memory.ToArray())));
        Assert.Equal(1,f.State.AppliedWorldItemAllocations);Assert.Equal(0,f.State.RejectedWorldItemAllocations);
        Assert.Equal(Convert.FromHexString(row.GetProperty("frame21").GetString()!),Read(f.Outbound));
        if(row.GetProperty("owner").GetInt32()!=255)Assert.Equal(Convert.FromHexString(row.GetProperty("frame22").GetString()!),Read(f.Outbound));
        Assert.Equal(0,f.Outbound.QueuedFrames);Assert.Equal(row.GetProperty("next").GetInt32(),f.Random.Next());
        if(!row.GetProperty("full").GetBoolean())
        {
            Assert.True(f.Items.TryGetActive(0,out var item));Assert.Equal(row.GetProperty("actualType").GetInt32(),item.ItemNetId);
            Assert.Equal(row.GetProperty("actualPrefix").GetInt32(),item.Prefix);Assert.Equal(row.GetProperty("owner").GetInt32(),item.OwnerPlayerId);
            Assert.Equal(row.GetProperty("delay").GetInt32(),item.GrabDelayTime);Assert.Equal(row.GetProperty("delayPlayer").GetInt32(),item.GrabDelayPlayer);
            Assert.Equal(row.GetProperty("enemy").GetInt32(),item.EnemyGrabDelayTime);Assert.Equal(2.5f,item.VelocityX);Assert.Equal(-3.75f,item.VelocityY);
        }
        else{Assert.Equal(400,f.Items.ActiveCount);Assert.False(f.Items.TryGetActive(400,out _));}
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void Player_inventory_or_hopper_changes_invalidate_detached_creation_before_claim(bool hopper)
    {
        using var f=new Fixture(1458,true,0);
        var drop=Drop(1000,1000);if(hopper)drop=drop with{PositionX=1200};
        using var plan=f.Items.CreateAllocationPreview([new(0,1000,1000,20,42)]);
        Assert.True(plan.TrySpawnSource(drop,0,out short slot));Assert.Equal(400,slot);
        if(hopper)f.Tiles.Set(75,62,new WorldTile{Type=21,Flags=WorldTileFlags.Active|WorldTileFlags.WireRed});
        else f.State.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,new(new(0),0,1,0,checked((short)VanillaItemIds.CopperPickaxe.Value),0)));
        Assert.False(plan.IsCurrent);Assert.False(plan.TryClaim());Assert.False(plan.TryCommitNext(out _,out _));Assert.Equal(0,f.Outbound.QueuedFrames);
    }

    [Fact]
    public void Claimed_slots_pause_source_owner_clock_and_failed_stale_generation_cannot_request_release()
    {
        var store=WorldItemSourceAllocation1458Tests.Arrange(10);var drop=Drop(0,0);
        Assert.True(store.TryUpsert(0,new(0,0,0,0,1,0,WorldItemOwnershipMode.None,58,false,0,0,255,0,0,0),out var initial));
        WorldItemSourceAllocation1458Tests.Metadata(store,0,1201,0);
        store.TickReservationTimers();Assert.True(store.TryGetSourceOwnerMetadata(initial.Handle,out int age,out _));Assert.Equal(1,age);
        using(var claim=store.CreateAllocationPreview())
        {Assert.True(claim.TrySpawnSource(drop,0,out short selected));Assert.Equal(0,selected);Assert.True(claim.TryClaim());store.TickReservationTimers();Assert.True(store.TryGetSourceOwnerMetadata(initial.Handle,out age,out _));Assert.Equal(1,age);}
        Assert.True(store.TryRemove(initial.Handle.Slot,out _));Assert.True(store.TryAllocateSourceDrop(drop,default,out var replaced,out _));
        Assert.NotEqual(initial.Handle,replaced.Handle);Assert.False(store.TryRequestSourceOwnerRelease(initial.Handle));
        Assert.True(store.TryGetSourceOwnerMetadata(replaced.Handle,out age,out _));Assert.Equal(0,age);
    }

    [Fact]
    public void Source_owner_cadence_is_per_item_and_owned300_requests39_once_until_authenticated_ack()
    {
        using var f=new Fixture(1458,false,0);var drop=Drop(1000,1000);
        Assert.True(f.Items.TryUpsert(0,new(drop.PositionX,drop.PositionY,0,0,1,0,WorldItemOwnershipMode.None,2,false,0,0,255,0,0,0),out var item));
        Drain(f.Outbound);
        f.Items.TickReservationTimers();f.Graph.WorldItems.TickPlayerReservations(10000);
        Assert.True(f.Items.TryGetActive(0,out item));Assert.Equal(0,item.OwnerPlayerId);Assert.Equal(22,Read(f.Outbound)[2]);
        Assert.True(f.Items.TryApplyOwner(0,new(0,0,0,0,1000,1000),out item));Drain(f.Outbound);
        for(short slot=0;slot<50;slot++)f.State.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,new(new(0),slot,9999,0,8,0)));
        for(int i=0;i<299;i++)f.Items.TickReservationTimers();f.Graph.WorldItems.TickPlayerReservations(1);Assert.Equal(0,f.Outbound.QueuedFrames);
        f.Items.TickReservationTimers();f.Graph.WorldItems.TickPlayerReservations(2);Assert.Equal(new byte[]{6,0,39,0,0,0},Read(f.Outbound));
        Assert.True(f.Items.TryGetSourceOwnerMetadata(item.Handle,out int age,out bool pending));Assert.Equal(-1,age);Assert.True(pending);
        for(int i=0;i<400;i++){f.Items.TickReservationTimers();f.Graph.WorldItems.TickPlayerReservations(i);}Assert.Equal(0,f.Outbound.QueuedFrames);
        Assert.Equal(TerrariaFrameSinkResult.Continue,f.Sink.OnFrame(Frame(39,new byte[]{0,0,1})));
        Assert.True(f.Items.TryGetActive(0,out item));Assert.Equal(255,item.OwnerPlayerId);Assert.True(f.Items.TryGetSourceOwnerMetadata(item.Handle,out age,out pending));Assert.Equal(0,age);Assert.False(pending);
    }

    private static WorldItemDropStateUpdate Drop(float x,float y)=>new(x,y,0,0,1,0,WorldItemOwnershipMode.None,2,false,0,0);
    public static IEnumerable<object[]> ClientCreations()
    {using var stream=typeof(WorldItemOwnerIntegration1458Tests).Assembly.GetManifestResourceStream("TerraRuntime.Tests.Fixtures.world-item-client-create-official.json.gz")!;using var gzip=new GZipStream(stream,CompressionMode.Decompress);using var doc=JsonDocument.Parse(gzip);foreach(var row in doc.RootElement.EnumerateArray())yield return [row.GetRawText()];}

    private sealed class Fixture:IDisposable
    {
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly WorldTileStore Tiles=new(new WorldDimensions(128,128));
        internal readonly RuntimeWorldItemReplicationRegistry Registry=new();
        internal readonly RuntimeWorldItemStore Items;
        internal readonly ServerRuntimeState State;
        internal readonly ServerRuntimeComposition Graph;
        internal readonly ConnectionHandle Connection;
        internal readonly TerrariaConnectionOutboundQueue Outbound=new(new OutboundQueueOptions(32,16384,1024));
        internal readonly WorldItemFrameSink Sink;
        private readonly PlayerBootstrapFrameSink bootstrap;
        private readonly PlayerSlotPool slots=new(2);
        internal ConnectionHandle AddPlayer()
        {
            Assert.True(slots.TryAcquireConnection(out var lease));var session=new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));session.ObserveWorldRequest();session.ObserveSectionRequest();
            var connection=new ConnectionHandle(GameCommandSourceId.FromConnection(7102),session.Handle);
            State.Apply(new PlayerSpawnRuntimeCommand(connection,session,new(session.Slot,62,62,0,0,0,0,0)));
            State.Apply(new PlayerMovementRuntimeCommand(connection,new(session.Slot,0,0,0,0,0,1100,1000,false,0,0,false,0,false,0,0,0,0,false,0,0)));
            return connection;
        }
        internal Fixture(int seed,bool full,int season,int mode=0,bool good=false)
        {
            Random=new(seed);Items=full?WorldItemSourceAllocation1458Tests.Arrange(10,Registry):new RuntimeWorldItemStore(Registry);
            State=new(worldItems:Items,worldTiles:Tiles,worldItemSpawnRandom:new SystemWorldItemSpawnRandom(Random),worldItemReplication:Registry,expertMode:mode>0,masterMode:mode==2,
                townCommerceWorldFacts:default(RuntimeTownCommerceWorldFacts1458) with{Halloween=(season&1)!=0,XMas=(season&2)!=0,TenthAnniversaryWorld=(season&4)!=0,GoodWorld=good});
            Graph=Assert.IsType<ServerRuntimeComposition>(typeof(ServerRuntimeState).GetField("_runtime",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(State));
            Assert.True(slots.TryAcquireConnection(out var lease));var session=new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));session.ObserveWorldRequest();session.ObserveSectionRequest();
            Connection=new(GameCommandSourceId.FromConnection(7101),session.Handle);State.Apply(new PlayerSpawnRuntimeCommand(Connection,session,new(session.Slot,62,62,0,0,0,0,0)));
            State.Apply(new PlayerMovementRuntimeCommand(Connection,new(session.Slot,0,0,0,0,0,1000,1000,false,0,0,false,0,false,0,0,0,0,false,0,0)));
            Assert.True(Registry.TryRegister(Connection.Source,Outbound));Registry.PlayerSpawned(Connection,new(session.Slot,62,62,0,0,0,0,0));
            bootstrap=new(slots,new(new OutboundQueueOptions(32,16384,1024)),PlayerBootstrapPacketSet.CreateForTesting(new byte[]{3,0,7},Array.Empty<ReadOnlyMemory<byte>>(),new byte[]{3,0,49}));bootstrap.AdoptPlayingSession(session,null);
            Sink=new(Connection.Source,bootstrap,new Pass(),new RuntimeWorldItemIngress(new Immediate(State),Items));
        }
        public void Dispose()=>bootstrap.Dispose();
    }
    private sealed class Immediate(ServerRuntimeState state):IGameCommandIngress<RuntimeCommand>{public bool TryPost(GameCommandSourceId source,RuntimeCommand command){state.Apply(command);return true;}}
    private sealed class Pass:ITerrariaFrameSink{public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)=>TerrariaFrameSinkResult.Continue;}
    private static TerrariaFrame Frame(byte id,byte[] payload)
    {var bytes=new byte[payload.Length+3];bytes[0]=(byte)bytes.Length;bytes[1]=(byte)(bytes.Length>>8);bytes[2]=id;payload.CopyTo(bytes,3);var sequence=new ReadOnlySequence<byte>(bytes);Assert.Equal(TerrariaFrameReadResult.Frame,TerrariaFrameDecoder.TryRead(ref sequence,out var frame));return frame;}
    private static byte[] Read(TerrariaConnectionOutboundQueue outbound){var queue=Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(outbound));Assert.True(queue.TryRead(out var frame));return frame.Bytes.ToArray();}
    private static void Drain(TerrariaConnectionOutboundQueue outbound){var queue=Assert.IsType<BoundedOutboundQueue>(typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(outbound));while(queue.TryRead(out _)){} }
}
