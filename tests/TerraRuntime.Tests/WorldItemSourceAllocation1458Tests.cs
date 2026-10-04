using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Npcs;
namespace TerraRuntime.Tests;

public sealed class WorldItemSourceAllocation1458Tests
{
    [Theory] [MemberData(nameof(Defaults))]
    public void Every_original_item_keeps_allocation_defaults(string json)
    {
        using var doc=JsonDocument.Parse(json);var row=doc.RootElement;
        Assert.True(VanillaWorldItemAllocationCatalog1458.TryGet(row.GetProperty("type").GetInt32(),out var facts));
        Assert.Equal(row.GetProperty("maxStack").GetInt32(),facts.MaximumStack);
        Assert.Equal(row.GetProperty("equipment").GetBoolean(),facts.Equipment);
        Assert.Equal(row.GetProperty("pickup").GetBoolean(),facts.Pickup);
        Assert.Equal(row.GetProperty("offset").GetInt32(),facts.InitialAge);
    }
    [Theory] [MemberData(nameof(Overflow))]
    public void Dedicated_full_table_selection_materialization_and_emergency_mutations_match_original(string json)
    {
        using var doc=JsonDocument.Parse(json);var row=doc.RootElement;
        int scenario=row.GetProperty("scenario").GetInt32();var store=Arrange(scenario);
        bool materialize=row.GetProperty("materialize").GetBoolean();
        var random=new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var drop=Drop();
        if(materialize)
        {
            // Original NewItem request explicitly uses prefix 0 and DirtBlock. Allocation metadata
            // does not grant NPC-loot materialization for an otherwise unadmitted item family.
            drop = drop with { VelocityX = random.Next(-30,31)*0.1f, VelocityY = random.Next(-40,-15)*0.1f };
        }
        using var preview=store.CreateAllocationPreview();
        Assert.True(preview.TrySpawnSource(drop,0,out short selected));
        Assert.Equal(row.GetProperty("chosen").GetInt32(),selected);
        Assert.Equal(row.GetProperty("next").GetInt32(),random.Next());
        if(!materialize)return;
        Assert.True(preview.TryClaim());Assert.True(preview.TryCommitNext(out short committed,out _));Assert.Equal(selected,committed);
        if(selected<400)
        {
            Assert.True(store.TryGetActive(selected,out var item));
            Assert.Equal(row.GetProperty("position").GetProperty("x").GetSingle(),item.PositionX);
            Assert.Equal(row.GetProperty("position").GetProperty("y").GetSingle(),item.PositionY);
            Assert.Equal(row.GetProperty("velocity").GetProperty("x").GetSingle(),item.VelocityX);
            Assert.Equal(row.GetProperty("velocity").GetProperty("y").GetSingle(),item.VelocityY);
            Assert.True(store.TryGetAllocationMetadata(selected,out int age,out _));Assert.Equal(row.GetProperty("age").GetInt32(),age);
        }
        foreach(var nearby in row.GetProperty("nearby").EnumerateArray())
        {
            bool active=store.TryGetActive((short)nearby.GetProperty("slot").GetInt32(),out var item);
            Assert.Equal(nearby.GetProperty("type").GetInt32(),active?item.ItemNetId:0);
            Assert.Equal(nearby.GetProperty("stack").GetInt32(),active?item.Stack:0);
        }
    }
    [Theory] [MemberData(nameof(Views))]
    public void Active_player_view_uses_live_body_even_when_player_is_dead(string json)
    {
        using var doc=JsonDocument.Parse(json);var row=doc.RootElement;var store=CreateVisibilityStore();
        var views=row.GetProperty("active").GetBoolean()?new[]{new WorldItemAllocationPlayer1458(0,0,-20,20,row.GetProperty("height").GetInt32())}:[];
        using var allocation=store.CreateAllocationPreview(views);var drop=Drop();
        Assert.True(allocation.TrySpawnSource(drop,0,out short slot));Assert.Equal(row.GetProperty("chosen").GetInt32(),slot);
        Assert.True(allocation.TryClaim());Assert.True(allocation.TryCommitNext(out _,out _));
        foreach(var nearby in row.GetProperty("nearby").EnumerateArray())
        {bool active=store.TryGetActive((short)nearby.GetProperty("slot").GetInt32(),out var item);Assert.Equal(nearby.GetProperty("slot").GetInt32()==slot?1:nearby.GetProperty("stack").GetInt32(),active?item.Stack:0);}
    }
    [Theory] [MemberData(nameof(CannonPrefixes))]
    public void Cannon_materialization_keeps_original_prefix_and_skips_only_default_velocity_rng(string json)
    {
        using var doc=JsonDocument.Parse(json);var row=doc.RootElement;var random=new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var origin=new NpcLootWorldItemOrigin(1000,1000);var loot=new NpcLootDrop(new(row.GetProperty("type").GetInt32()),1);
        var velocity=new NpcLootWorldItemVelocity1458(3.25f,-7.5f);
        Assert.True(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(origin,loot,new Rolls(random),velocity,out var item));
        Assert.Equal(row.GetProperty("prefix").GetByte(),item.Prefix);Assert.Equal(992,item.PositionX);Assert.Equal(992,item.PositionY);
        Assert.Equal(velocity.X,item.VelocityX);Assert.Equal(velocity.Y,item.VelocityY);Assert.Equal(row.GetProperty("next").GetInt32(),random.Next());
    }
    [Fact]
    public void Invalid_velocity_override_rejects_without_random_draws()
    {
        var random=new VanillaUnifiedRandom1458(1458);var origin=new NpcLootWorldItemOrigin(100,100);var loot=new NpcLootDrop(new(9),1);
        Assert.False(VanillaNpcLootWorldItemMaterializer.Instance.TryMaterialize(origin,loot,new Rolls(random),new(float.NaN,0),out _));
        Assert.Equal(906992634,random.Next());
    }
    [Fact]
    public void Ownership_release_request_matches_actual_original_packet39_bytes()
    {
        Assert.Equal(TerraRuntime.Protocol.Multiplicity.TerrariaWorldItemFrameEncodeResult.Encoded,
            TerraRuntime.Protocol.Multiplicity.TerrariaWorldItemFrameEncoder.TryEncodeOwnershipReleaseRequest(50,out var frame));
        // Actual original NetMessage.SendData(39, number:50, number2:0), original server SHA recorded with oracle.
        Assert.Equal(Convert.FromHexString("060027320000"),frame.ToArray());
        Assert.Equal(TerraRuntime.Protocol.Multiplicity.TerrariaWorldItemFrameEncodeResult.InvalidState,
            TerraRuntime.Protocol.Multiplicity.TerrariaWorldItemFrameEncoder.TryEncodeOwnershipReleaseRequest(400,out _));
    }
    [Fact]
    public void Pending_remote_owned_merge_waits_for_ack_then_completes_without_stale_ownership_or_duplicate_release()
    {
        var sink=new Sink();var store=Arrange(17,sink);Assert.True(store.TryRemove(360,out _));sink.Events.Clear();
        var views=new[]{new WorldItemAllocationPlayer1458(7,10000,10000,20,42)};var drop=Drop();
        using(var plan=store.CreateAllocationPreview(views))
        {Assert.True(plan.TrySpawnSource(drop,0,out short slot));Assert.Equal(360,slot);Assert.True(plan.TryClaim());Assert.True(plan.TryCommitNext(out _,out _));}
        Assert.True(store.TryGetActive(50,out var pending));Assert.Equal(7,pending.OwnerPlayerId);Assert.True(store.HasPendingSourceTransfer(pending.Handle));
        Assert.Single(sink.Events,x=>x.Kind==WorldItemStateCommitKind.OwnershipReleaseRequested);
        Assert.True(store.TryProcessPendingSourceTransfers());Assert.True(store.TryGetActive(50,out _));
        var ack=new WorldItemOwnerStateUpdate(255,0,255,0,pending.PositionX,pending.PositionY);Assert.True(store.TryApplyOwner(50,ack,out _));
        Assert.True(store.TryProcessPendingSourceTransfers());Assert.False(store.TryGetActive(50,out _));Assert.True(store.TryGetActive(51,out var destination));Assert.Equal(20,destination.Stack);
        Assert.False(store.HasPendingSourceTransfer(destination.Handle));int commits=sink.Events.Count;Assert.True(store.TryProcessPendingSourceTransfers());Assert.Equal(commits,sink.Events.Count);
    }
    [Fact]
    public void Claimed_instanced_slot_does_not_advance_or_expire_live_lease_before_acceptance()
    {
        var store=new RuntimeWorldItemStore();var leases=new RuntimeWorldItemInstancedLeaseStore(store);var drop=Drop();
        Assert.True(leases.TryLease(drop,1,out var token));
        for(int i=1;i<400;i++){var full=drop with { ItemNetId=1, Stack=9999 };Assert.True(store.TryAllocateDrop(full,out var item));Metadata(store,item.Handle.Slot,0,0);}
        using(var plan=store.CreateAllocationPreview())
        {
            Assert.True(plan.TrySpawnSource(drop,0,out short slot));Assert.Equal(token.Slot,slot);Assert.True(plan.TryClaim());
            Span<short> expired=stackalloc short[400];Assert.Equal(0,leases.Tick(expired));Assert.True(plan.IsCurrent);
            Assert.True(leases.TryGetRemainingTicks(token.Slot,out int ticks));Assert.Equal(1,ticks);
        }
        Span<short> released=stackalloc short[400];Assert.Equal(1,leases.Tick(released));Assert.Equal(token.Slot,released[0]);
    }
    [Fact]
    public void Unsupported_player_view_rejects_before_live_mutation()
    {
        var store=Arrange(16);Assert.True(store.TryGetActive(50,out var before));
        var views=new[]{new WorldItemAllocationPlayer1458(0,float.NaN,0,20,42)};
        using var plan=store.CreateAllocationPreview(views);var drop=Drop();Assert.False(plan.TrySpawnSource(drop,0,out _));Assert.False(plan.TryClaim());
        Assert.True(store.TryGetActive(50,out var after));Assert.Equal(before,after);
    }
    [Fact]
    public void Claimed_replacement_remains_visible_then_reuses_same_slot_with_fresh_generations_in_source_order()
    {
        var sink=new Sink();var store=Arrange(10,sink);Metadata(store,3,5000,0);Assert.True(store.TryGetActive(3,out var before));sink.Events.Clear();
        using var preview=store.CreateAllocationPreview();var drop=Drop();
        Assert.True(preview.TrySpawnSource(drop,0,out short first));Assert.True(preview.TrySpawnSource(drop,0,out short second));
        Assert.Equal((short)3,first);Assert.Equal(first,second);Assert.True(preview.TryClaim());
        Assert.True(store.TryGetActive(3,out var visible));Assert.Equal(before,visible);
        Assert.False(store.TryApplyDrop(3,drop,out _));
        Assert.True(preview.TryCommitNext(out _,out _));Assert.True(store.TryGetActive(3,out var middle));
        Assert.True(preview.TryCommitNext(out _,out _));Assert.True(store.TryGetActive(3,out var after));
        Assert.Equal(before.Handle.Generation.Value+1,middle.Handle.Generation.Value);
        Assert.Equal(middle.Handle.Generation.Value+1,after.Handle.Generation.Value);
        Assert.Equal(new[]{WorldItemStateCommitKind.Remove,WorldItemStateCommitKind.Drop,WorldItemStateCommitKind.Remove,WorldItemStateCommitKind.Drop},sink.Events.Select(x=>x.Kind));
        Assert.False(store.TryAdvanceMotion(before.Handle,0,0,0,0,out _));
    }
    [Fact]
    public void Changed_source_revision_rejects_claim_without_losing_prior_items_or_reservations()
    {
        var store=Arrange(7);using var preview=store.CreateAllocationPreview();var drop=Drop();
        Assert.True(preview.TrySpawnSource(drop,0,out _));Assert.True(store.TryGetActive(3,out var original));
        Assert.True(store.TryAdvanceMotion(original.Handle,17,18,1,2,out var changed));
        Assert.False(preview.IsCurrent);Assert.False(preview.TryClaim());Assert.True(store.TryGetActive(3,out var actual));Assert.Equal(changed,actual);
        var reserved=new RuntimeWorldItemStore();Assert.True(reserved.TryReserveDropSlot(out var token));
        using var unknown=reserved.CreateAllocationPreview();Assert.False(unknown.TrySpawnSource(drop,0,out _));Assert.True(reserved.HasDropReservation(token));
    }
    [Fact]
    public void Ordinary_sentinel_consumes_materialization_but_creates_no_runtime_entity_and_instanced_sentinel_stays_closed()
    {
        var store=Arrange(10);using var ordinary=store.CreateAllocationPreview();var drop=Drop();
        Assert.True(ordinary.TrySpawnSource(drop,0,out short slot));Assert.Equal((short)400,slot);
        Assert.True(ordinary.TryClaim());Assert.True(ordinary.TryCommitNext(out _,out var noLease));Assert.False(noLease.IsAssigned);Assert.Equal(400,store.ActiveCount);
        using var instanced=store.CreateAllocationPreview();Assert.False(instanced.TrySpawnSource(drop,54000,out _));
    }
    [Fact]
    public void Source_age_offset_tick_saturation_and_expired_replaced_lease_are_owned_by_store()
    {
        var store=new RuntimeWorldItemStore();var drop=Drop();Assert.True(store.TryAllocateDrop(drop,out var item));
        Assert.True(store.TryGetAllocationMetadata(item.Handle.Slot,out int age,out _));Assert.Equal(200,age);
        store.TickReservationTimers();Assert.True(store.TryGetAllocationMetadata(item.Handle.Slot,out age,out _));Assert.Equal(201,age);
        Metadata(store,item.Handle.Slot,VanillaWorldItemAllocation1458.AgeCeiling,0);
        store.TickReservationTimers();Assert.True(store.TryGetAllocationMetadata(item.Handle.Slot,out age,out _));Assert.Equal(VanillaWorldItemAllocation1458.AgeCeiling,age);
        var full=new RuntimeWorldItemStore();var leases=new RuntimeWorldItemInstancedLeaseStore(full);
        for(int i=0;i<400;i++)Assert.True(leases.TryLease(drop,1,out _));
        using(var preview=full.CreateAllocationPreview())
        {Assert.True(preview.TrySpawnSource(drop,0,out short replaced));Assert.Equal((short)0,replaced);Assert.True(preview.TryClaim());Assert.True(preview.TryCommitNext(out _,out _));}
        Span<short> expired=stackalloc short[400];Assert.Equal(399,leases.Tick(expired));Assert.DoesNotContain((short)0,expired[..399].ToArray());
        Assert.True(full.TryGetActive(0,out _));Assert.Equal(0,leases.ActiveLeaseCount);
    }
    public static IEnumerable<object[]> Views()=>Rows("world-item-allocation-views");
    public static IEnumerable<object[]> CannonPrefixes()=>Rows("big-mimic-cannon-prefix");
    public static IEnumerable<object[]> Defaults()=>Rows("world-item-allocation-defaults");
    public static IEnumerable<object[]> Overflow()
    {
        foreach(var row in Rows("world-item-overflow"))
        {using var document=JsonDocument.Parse((string)row[0]);if(document.RootElement.GetProperty("mode").GetInt32()==2)yield return row;}
    }
    internal static IEnumerable<object[]> Rows(string name)
    {
        using var stream=typeof(WorldItemSourceAllocation1458Tests).Assembly.GetManifestResourceStream($"TerraRuntime.Tests.Fixtures.{name}-official.json.gz")!;
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);using var document=JsonDocument.Parse(gzip);
        foreach(var row in document.RootElement.EnumerateArray())yield return [row.GetRawText()];
    }
    private static WorldItemDropStateUpdate Drop()=>new(992,992,-.5f,-2.5f,1,0,WorldItemOwnershipMode.None,2,false,0,0);
    internal static RuntimeWorldItemStore CreateVisibilityStore(IWorldItemStateCommitSink? sink=null)
    {
        var store=Arrange(16,sink);Assert.True(store.TryGetActive(50,out var a));Assert.True(store.TryGetActive(51,out var b));
        Assert.True(store.TryAdvanceMotion(a.Handle,0,800,0,0,out _));Assert.True(store.TryAdvanceMotion(b.Handle,0,-800,0,0,out _));return store;
    }
    internal static RuntimeWorldItemStore Arrange(int scenario,IWorldItemStateCommitSink? sink=null)
    {
        var store=new RuntimeWorldItemStore(sink);
        for(short i=0;i<400;i++)Replace(i,1,100,stack:9999);
        void Replace(short index,short type,int age,int cooldown=0,byte owner=255,short stack=1)
        {
            if(type==0){Assert.True(store.TryRemove(index,out _));Metadata(store,index,age,cooldown);return;}
            Assert.True(store.TryUpsert(index,new(0,0,0,0,stack,0,WorldItemOwnershipMode.None,type,false,0,0,owner,0,255,0),out _));Metadata(store,index,age,cooldown);
        }
        switch(scenario)
        {
            case 0:Replace(10,0,0);break;
            case 1:Replace(359,0,0);Replace(5,58,1201);break;
            case 2:Replace(360,0,0);Replace(5,58,1201);break;
            case 3:Replace(399,0,0);Replace(5,58,1200);break;
            case 4:Replace(2,58,1201);Replace(3,58,1201);break;
            case 5:Replace(2,58,1201,54000,0);break;
            case 6:Replace(2,58,1200);Replace(3,1,5000);break;
            case 7:Replace(3,1,5000);Replace(4,2,5000);break;
            case 8:Replace(3,1,5000,1);Replace(4,2,4999);break;
            case 9:for(short i=0;i<400;i++)Replace(i,1,100,i==7?1:200);break;
            case 10:for(short i=0;i<400;i++)Replace(i,1,0,stack:9999);break;
            case 11:for(short i=0;i<400;i++)Replace(i,1,100,200);break;
            case 12:Replace(0,0,100,1);Replace(6,0,0);break;
            case 13:Replace(6,1,2147483547);break;
            case 14:Replace(360,0,0);Replace(361,58,99999);break;
            case 15:Replace(1,184,1300);Replace(5,58,1400);break;
            default:
                Replace(50,scenario==23?(short)71:(short)2,200,scenario==18?1:0,scenario==17?(byte)7:(byte)255,scenario==23?(short)99:(short)10);
                Replace(51,scenario==23?(short)71:(short)2,100,stack:scenario==23?(short)99:(short)10);
                Assert.True(store.TryGetActive(50,out var source));Assert.True(store.TryGetActive(51,out var target));
                if(scenario==19)Assert.True(store.TryApplyDrop(50,new(0,0,0,0,10,0,WorldItemOwnershipMode.None,2,false,1,0),out _));
                if(scenario==20)Assert.True(store.TryAdvanceMotion(target.Handle,2401,0,0,0,out _));
                if(scenario==21)Assert.True(store.TryApplyDrop(50,new(0,0,0,0,10,1,WorldItemOwnershipMode.None,2,false,0,0),out _));
                if(scenario==22)Assert.True(store.TryApplyDrop(51,new(0,0,0,0,9999,0,WorldItemOwnershipMode.None,2,false,0,0),out _));
                break;
        }
        return store;
    }
    internal static void Metadata(RuntimeWorldItemStore store,short slot,int age,int cooldown)
    {
        // The oracle matrix starts from exact private source world ages/cooldowns. This is fixture setup only;
        // production ingress has no caller-supplied age or reuse-timer field.
        var array=(Array)typeof(RuntimeWorldItemStore).GetField("_slots",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(store)!;
        var state=array.GetValue(slot)!;var type=state.GetType();type.GetField("SourceAge")!.SetValue(state,age);type.GetField("SourceReuseTicks")!.SetValue(state,cooldown);array.SetValue(state,slot);
    }
    private sealed class Rolls(VanillaUnifiedRandom1458 random):INpcLootRollSource
    {public int NextInt32(int minimum,int maximum)=>random.Next(minimum,maximum);public int RollLuck(int range)=>random.Next(range);}
    private sealed class Sink:IWorldItemStateCommitSink
    {public List<(WorldItemStateCommitKind Kind,WorldItemSnapshot State)> Events{get;}=[];public void WorldItemStateCommitted(WorldItemStateCommitKind kind,in WorldItemSnapshot snapshot)=>Events.Add((kind,snapshot));}
}
