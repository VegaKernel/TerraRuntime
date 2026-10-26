using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Tests.Larva1458;

namespace TerraRuntime.Tests;

public sealed class WorldItemReservationOriginal1458Tests
{
    [Fact]
    public void Source_ReserveFor_commits_match_original_raw_fields_and_separate_original22_offers()
    {
        using var reference = Load();
        string[] profiles = ["reserve-default15", "reserve-configured-raw37", "apply-reserve-local100",
            "find-disconnected-owner", "find-disconnected-no-candidate", "find-force-disconnected-positive"];
        foreach (string name in profiles)
        {
            JsonElement step = Steps(reference, name)[0], before = step.GetProperty("before"), after = step.GetProperty("after");
            var sink = new Sink(); var store = new RuntimeWorldItemStore(sink);
            Assert.True(store.TryAllocate(State(before), out var initial)); sink.Events.Clear();
            JsonElement packet = Assert.Single(step.GetProperty("packets").EnumerateArray());
            var offer = ReadOwner(Bytes(packet));
            var update = new WorldItemOwnerStateUpdate(offer.OwnerPlayerId, offer.TimeToKeepReservation,
                offer.GrabDelayPlayer, offer.GrabDelayTime, offer.PositionX, offer.PositionY);
            Assert.True(store.TryApplySourceOwner(initial, update,
                clearServerReservation: name == "find-force-disconnected-positive", out var assigned));
            AssertItem(after, assigned);
            var committed = Assert.Single(sink.Events);
            Assert.Equal(WorldItemStateCommitKind.Owner, committed.Kind);
            Assert.Equal(offer.TimeToKeepReservation, committed.Offer);
            Assert.Equal(assigned, committed.Item);
            Assert.Equal(Bytes(packet), EncodeOwner(committed));
            Assert.True(store.TryGetSourceOwnerMetadata(assigned.Handle, out int age, out bool pending));
            Assert.Equal(I(after, "age"), age); Assert.False(pending);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Actual_NewItem_offer_and_raw_state_survive_direct_and_deferred_source_allocation(bool local, bool deferred)
    {
        using var reference = Load();
        JsonElement source = Steps(reference, local ? "newitem-local100-join0" : "newitem-none-default15")[0];
        JsonElement[] original = source.GetProperty("packets").EnumerateArray().ToArray();
        var drop = ReadDrop(Bytes(original[0]));
        var sink = new Sink(); var store = new RuntimeWorldItemStore(sink);
        store.AttachOwnerFactsProvider(new OwnerZero());
        using var plan = store.CreateAllocationPreview();
        Assert.True(plan.TrySpawnSource(drop, 0, out short slot, sourceLocalPlayerId: 0));
        Assert.True(plan.TryClaim());
        if (deferred)
        {
            Assert.True(plan.TryAdoptUnpublished(out var publication));
            Assert.Empty(sink.Events);
            Assert.True(publication!.TryPublishNext(out short published)); Assert.Equal(slot, published);
            Assert.False(publication.TryPublishNext(out _));
        }
        else { Assert.True(plan.TryCommitNext(out short committed, out _)); Assert.Equal(slot, committed); }
        Assert.Equal(0, slot);
        Assert.True(store.TryGetActive(slot, out var item));
        AssertItem(source.GetProperty("after"), item);
        Assert.Equal(new[] { WorldItemStateCommitKind.Drop, WorldItemStateCommitKind.Owner }, sink.Events.Select(e => e.Kind));
        Assert.Null(sink.Events[0].Offer);
        AssertItem(original[0].GetProperty("atSend"), sink.Events[0].Item);
        Assert.Equal(Bytes(original[0]), EncodeDrop(sink.Events[0].Item));
        Assert.Equal(Bytes(original[1]), EncodeOwner(sink.Events[1]));
        Assert.Equal(local ? 100 : 15, sink.Events[1].Offer);
        Assert.Equal(0, sink.Events[1].Item.TimeToKeepReservation);
        if (local)
        {
            JsonElement[] steps = Steps(reference, "newitem-local100-join0");
            Assert.Equal(WorldItemBootstrapPacketEncodeResult.Encoded,
                WorldItemBootstrapPacketEncoder.TryEncode([item], out var baseline));
            Assert.Equal(Bytes(Assert.Single(steps[1].GetProperty("packets").EnumerateArray())), baseline[0].ToArray());
            Assert.Equal(Bytes(Assert.Single(steps[2].GetProperty("packets").EnumerateArray())), baseline[1].ToArray());
        }
    }

    [Fact]
    public void Positive_server_countdowns_match_original_UpdateItem_without_owner_publications()
    {
        using var reference = Load();
        foreach (string name in new[] { "update-positive-timers", "reserve-configured-raw37" })
        {
            JsonElement[] steps = Steps(reference, name).Where(s => s.GetProperty("call").GetString() == "WorldItem.UpdateItem").ToArray();
            var sink = new Sink(); var store = new RuntimeWorldItemStore(sink);
            Assert.True(store.TryAllocate(State(steps[0].GetProperty("before")), out var initial)); sink.Events.Clear();
            foreach (JsonElement step in steps)
            {
                store.TickReservationTimers(); Assert.True(store.TryGetActive(initial.Handle.Slot, out var item));
                JsonElement after = step.GetProperty("after");
                Assert.Equal(I(after, "keep"), item.TimeToKeepReservation);
                Assert.Equal(I(after, "grabTime"), item.GrabDelayTime);
                Assert.Equal(I(after, "enemyDelay"), item.EnemyGrabDelayTime);
                Assert.Empty(step.GetProperty("packets").EnumerateArray()); Assert.Empty(sink.Events);
            }
            // This compares UpdateItem's countdowns. The store tick also includes Main.UpdateServer's
            // separate reservation-age phase; source capture retains both methods independently.
        }
    }

    [Fact]
    public void Actual_command39_matches_original_raw_hold_owner_age_packets_and_excluded_recipient()
    {
        using var reference = Load();
        foreach (string name in new[] { "server39-positive37-False", "server39-positive37-True", "server39-self-owner" })
        {
            using var scene = new LarvaRuntimeFixture1458(windows: true, sourcePlayerZero: true);
            scene.Member.PositionX = scene.Member.PositionY = 640;
            var owner = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 16384, 1024));
            var observer = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(32, 16384, 1024));
            Assert.True(scene.ItemReplication.TryRegister(scene.Connection.Source, owner));
            var playing = new PlayerSpawnCommitRequest(new(0), 40, 40, 0, 0, 0, 0, 0);
            scene.ItemReplication.PlayerSpawned(scene.Connection, playing);
            var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(98002), new(new(2), new(1)));
            Assert.True(scene.ItemReplication.TryRegister(peer.Source, observer));
            scene.ItemReplication.PlayerSpawned(peer, new(new(2),40,40,0,0,0,0,0));
            JsonElement step = Steps(reference, name)[0], before = step.GetProperty("before");
            Assert.True(scene.Items.TryAllocate(State(before), out var initial));
            Drain(owner); Drain(observer);
            if (I(before, "age") == -1)
            {
                Assert.True(scene.Items.TryRequestSourceOwnerRelease(initial.Handle)); Drain(owner); Drain(observer);
            }
            bool force = step.GetProperty("input").GetProperty("force").GetBoolean();
            scene.Apply(new WorldItemReleaseRuntimeCommand(scene.Connection, initial.Handle, force));
            Assert.True(scene.Items.TryGetActive(0, out var actual)); AssertItem(step.GetProperty("after"), actual);
            Assert.True(scene.Items.TryGetSourceOwnerMetadata(actual.Handle, out int age, out bool pending));
            Assert.Equal(I(step.GetProperty("after"), "age"), age); Assert.False(pending);
            foreach (JsonElement packet in step.GetProperty("packets").EnumerateArray())
                Assert.Equal(Bytes(packet), Read(I(packet, "recipient") == 0 ? owner : observer));
            Assert.Equal(0, owner.QueuedFrames); Assert.Equal(0, observer.QueuedFrames);
        }
    }

    [Fact]
    public void Original_inactive_instanced_slot_lease_is_separate_from_the_raw_reservation_clock_and_reuses_cleanly()
    {
        using var reference = Load();
        foreach (string name in new[] { "instanced-inactive-reuse3", "instanced-default-reuse54000" })
        {
            JsonElement[] steps = Steps(reference, name);
            JsonElement instanced = steps[1];
            var drop = ReadDrop(Bytes(Assert.Single(instanced.GetProperty("packets").EnumerateArray())));
            var store = new RuntimeWorldItemStore(); var leases = new RuntimeWorldItemInstancedLeaseStore(store);
            int lifetime = I(instanced.GetProperty("after"), "reuse");
            Assert.True(leases.TryLease(drop, lifetime, out var reserved));
            Assert.False(store.TryGetActive(0, out _));
            var expired = new short[400];
            foreach (JsonElement step in steps.Skip(2).Where(s => s.GetProperty("call").GetString()!.StartsWith("WorldItem.UpdateItem", StringComparison.Ordinal)))
            {
                int count = leases.Tick(expired), remaining = I(step.GetProperty("after"), "reuse");
                if (remaining > 0) { Assert.True(leases.TryGetRemainingTicks(0, out int actual)); Assert.Equal(remaining, actual); }
                else Assert.False(leases.TryGetRemainingTicks(0, out _));
                JsonElement[] packets = step.GetProperty("packets").EnumerateArray().ToArray(); Assert.Equal(packets.Length, count);
                if (count > 0)
                {
                    Assert.Equal(reserved.Slot, expired[0]);
                    Assert.Equal(TerrariaWorldItemFrameEncodeResult.Encoded, TerrariaWorldItemFrameEncoder.TryEncodeRemoval(expired[0], out var removal));
                    Assert.Equal(Bytes(packets[0]), removal.ToArray());
                }
                Assert.Equal(0, I(step.GetProperty("after"), "keep"));
            }
            if (lifetime == 3)
            {
                JsonElement fresh = steps[^1];
                var sinklessDrop = ReadDrop(Bytes(fresh.GetProperty("packets")[0]));
                Assert.True(store.TryAllocateSourceDrop(sinklessDrop, [], out var item, out short slot, sourceLocalPlayerId: 0));
                Assert.Equal(0, slot); Assert.NotEqual(reserved.Generation, item.Handle.Generation);
                AssertItem(fresh.GetProperty("after"), item);
            }
        }
    }

    [Fact]
    public void Late_join_zero_hold_matches_original_even_when_server_raw_hold_is_positive()
    {
        using var reference = Load(); JsonElement step = Steps(reference, "join-positive-raw37-still-default0")[0];
        var store = new RuntimeWorldItemStore(); Assert.True(store.TryAllocate(State(step.GetProperty("before")), out var item));
        Assert.Equal(37, item.TimeToKeepReservation);
        Assert.Equal(WorldItemBootstrapPacketEncodeResult.Encoded, WorldItemBootstrapPacketEncoder.TryEncode([item], out var frames));
        Assert.Equal(Bytes(Assert.Single(step.GetProperty("packets").EnumerateArray())), frames[1].ToArray());
        Assert.True(store.TryGetActive(0, out var after)); Assert.Equal(37, after.TimeToKeepReservation);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void Pending_transfer_force_clears_already_unowned_raw37_silently_in_both_publication_paths(bool unownedIsSource, bool deferred)
    {
        using var reference=Load();
        JsonElement step=Steps(reference,"pending-force-unowned-positive37-"+(unownedIsSource?"source":"destination"))[0];
        var sink=new Sink();var store=WorldItemSourceAllocation1458Tests.Arrange(17,sink);
        Assert.True(store.TryRemove(360,out _));
        short primary=unownedIsSource?(short)50:(short)51,peer=unownedIsSource?(short)51:(short)50;
        JsonElement before=step.GetProperty("before");
        foreach(var pair in new[]{(slot:primary,key:"primary"),(slot:peer,key:"peer")})
        {
            JsonElement expected=before.GetProperty(pair.key);Assert.True(store.TryUpsert(pair.slot,State(expected),out _));
            WorldItemSourceAllocation1458Tests.Metadata(store,pair.slot,I(expected,"spawnAge"),0);
            // Source-only owner-age preimage; this does not authorize any production ingress field.
            var slots=(Array)typeof(RuntimeWorldItemStore).GetField("_slots",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(store)!;
            object state=slots.GetValue(pair.slot)!;state.GetType().GetField("SourceOwnerAge")!.SetValue(state,I(expected,"age"));slots.SetValue(state,pair.slot);
        }
        sink.Events.Clear();
        var players=new[]{new TerraRuntime.Gameplay.Items.WorldItemAllocationPlayer1458(7,10000,10000,20,42)};
        using var plan=store.CreateAllocationPreview(players);
        var drop=new WorldItemDropStateUpdate(992,992,-.5f,-2.5f,1,0,WorldItemOwnershipMode.None,2,false,0,0);
        Assert.True(plan.TrySpawnSource(drop,0,out short slot));Assert.Equal(360,slot);Assert.True(plan.TryClaim());
        if(deferred){Assert.True(plan.TryAdoptUnpublished(out var publication));Assert.True(publication!.TryPublishNext(out _));}
        else Assert.True(plan.TryCommitNext(out _,out _));
        JsonElement after=step.GetProperty("after");
        foreach(var pair in new[]{(slot:primary,key:"primary"),(slot:peer,key:"peer")})
        {
            Assert.True(store.TryGetActive(pair.slot,out var item));JsonElement expected=after.GetProperty(pair.key);AssertItem(expected,item);
            Assert.True(store.TryGetSourceOwnerMetadata(item.Handle,out int age,out _));Assert.Equal(I(expected,"age"),age);
            Assert.True(store.HasPendingSourceTransfer(item.Handle));
        }
        Assert.DoesNotContain(sink.Events,e=>e.Item.Handle.Slot==primary);
        Commit request=Assert.Single(sink.Events,e=>e.Kind==WorldItemStateCommitKind.OwnershipReleaseRequested);
        Assert.Equal(peer,request.Item.Handle.Slot);Assert.Null(request.Offer);
        JsonElement original=Assert.Single(step.GetProperty("packets").EnumerateArray());Assert.Equal(7,I(original,"recipient"));
        byte[] bytes=Bytes(original);Assert.Equal(39,bytes[2]);Assert.Equal(1,System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(3)));
        // Runtime fixture uses50/51 for the same pair. Normalize only that explicit slot mapping;
        // original recipient, force flag, raw state and absence of any22 remain exact.
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(3),peer);
        Assert.Equal(TerrariaWorldItemFrameEncodeResult.Encoded,TerrariaWorldItemFrameEncoder.TryEncodeOwnershipReleaseRequest(peer,out var frame));
        Assert.Equal(bytes,frame.ToArray());
    }

    private static WorldItemStateUpdate State(JsonElement s) => new(F(s,"x"), F(s,"y"), F(s,"vx"), F(s,"vy"),
        (short)I(s,"stack"), (byte)I(s,"prefix"), WorldItemOwnershipMode.None, (short)I(s,"type"), false, 0,
        (byte)I(s,"enemyDelay"), (byte)I(s,"owner"), I(s,"keep"), (byte)I(s,"grabPlayer"), I(s,"grabTime"));
    private static void AssertItem(JsonElement expected, WorldItemSnapshot item)
    {
        Assert.Equal(I(expected,"type"), item.ItemNetId); Assert.Equal(I(expected,"stack"), item.Stack);
        Assert.Equal(I(expected,"prefix"), item.Prefix); Assert.Equal(F(expected,"x"), item.PositionX); Assert.Equal(F(expected,"y"), item.PositionY);
        Assert.Equal(F(expected,"vx"), item.VelocityX); Assert.Equal(F(expected,"vy"), item.VelocityY);
        Assert.Equal(I(expected,"owner"), item.OwnerPlayerId); Assert.Equal(I(expected,"keep"), item.TimeToKeepReservation);
        Assert.Equal(I(expected,"grabPlayer"), item.GrabDelayPlayer); Assert.Equal(I(expected,"grabTime"), item.GrabDelayTime);
        Assert.Equal(I(expected,"enemyDelay"), item.EnemyGrabDelayTime);
    }
    // Independent literal-byte readers avoid deriving golden expectations from production codecs.
    private static TerrariaWorldItemOwnerState ReadOwner(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var r = new BinaryReader(stream);
        Assert.Equal(bytes.Length,r.ReadUInt16()); Assert.Equal(22,r.ReadByte());
        var value = new TerrariaWorldItemOwnerState(r.ReadInt16(),r.ReadByte(),r.Read7BitEncodedInt(),r.ReadByte(),r.Read7BitEncodedInt(),r.ReadSingle(),r.ReadSingle());
        Assert.Equal(stream.Length,stream.Position); return value;
    }
    private static WorldItemDropStateUpdate ReadDrop(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var r = new BinaryReader(stream);
        Assert.Equal(bytes.Length,r.ReadUInt16()); Assert.Contains(r.ReadByte(),new byte[]{21,90}); Assert.Equal(0,r.ReadInt16());
        float x=r.ReadSingle(),y=r.ReadSingle(),vx=r.ReadSingle(),vy=r.ReadSingle();short stack=r.ReadInt16();byte prefix=r.ReadByte(),bits=r.ReadByte();short type=r.ReadInt16();
        Assert.Equal(0,bits&4);byte enemy=(bits&8)!=0?r.ReadByte():(byte)0;
        Assert.Equal(stream.Length,stream.Position);return new(x,y,vx,vy,stack,prefix,(WorldItemOwnershipMode)(bits&3),type,false,0,enemy);
    }
    private static byte[] EncodeOwner(Commit e)
    {
        WorldItemSnapshot s=e.Item;var wire=new TerrariaWorldItemOwnerState(s.Handle.Slot,s.OwnerPlayerId,e.Offer??0,s.GrabDelayPlayer,s.GrabDelayTime,s.PositionX,s.PositionY);
        Assert.Equal(TerrariaWorldItemFrameEncodeResult.Encoded,TerrariaWorldItemFrameEncoder.TryEncodeOwner(wire,out var bytes));return bytes.ToArray();
    }
    private static byte[] EncodeDrop(WorldItemSnapshot s)
    {
        var wire=new TerrariaWorldItemDropState(s.Handle.Slot,s.PositionX,s.PositionY,s.VelocityX,s.VelocityY,s.Stack,s.Prefix,
            s.ItemNetId,(TerrariaWorldItemOwnership)s.Ownership,s.Shimmered,s.ShimmerTime,s.EnemyGrabDelayTime);
        Assert.Equal(TerrariaWorldItemFrameEncodeResult.Encoded,TerrariaWorldItemFrameEncoder.TryEncodeDrop(wire,out var bytes));return bytes.ToArray();
    }
    private static BoundedOutboundQueue Queue(TerrariaConnectionOutboundQueue q)=>(BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(q)!;
    private static byte[] Read(TerrariaConnectionOutboundQueue q){Assert.True(Queue(q).TryRead(out var frame));return frame.Bytes.ToArray();}
    private static void Drain(TerrariaConnectionOutboundQueue q){while(Queue(q).TryRead(out _)){} }
    private static byte[] Bytes(JsonElement packet)=>Convert.FromHexString(packet.GetProperty("hex").GetString()!);
    private static JsonElement[] Steps(JsonDocument doc,string name)=>doc.RootElement.GetProperty("rows").EnumerateArray().Single(r=>r.GetProperty("name").GetString()==name).GetProperty("steps").EnumerateArray().ToArray();
    private static int I(JsonElement value,string name)=>value.GetProperty(name).GetInt32();
    private static float F(JsonElement value,string name)=>value.GetProperty(name).GetSingle();
    private static JsonDocument Load()
    {
        using Stream stream=typeof(WorldItemReservationOriginal1458Tests).Assembly.GetManifestResourceStream("WorldItemReservationOriginalLinux1458")!;
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);var doc=JsonDocument.Parse(gzip);
        Assert.Equal("4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034",doc.RootElement.GetProperty("sha256").GetString());
        Assert.Empty(doc.RootElement.GetProperty("errors").EnumerateArray()); Assert.Equal(33,doc.RootElement.GetProperty("rows").GetArrayLength());return doc;
    }
    private readonly record struct Commit(WorldItemStateCommitKind Kind,WorldItemSnapshot Item,int? Offer);
    private sealed class Sink:IWorldItemStateCommitSink
    {
        public readonly List<Commit> Events=[];
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind,in WorldItemSnapshot snapshot,int? reservationOffer=null,GameCommandSourceId excludedSource=default)=>Events.Add(new(kind,snapshot,reservationOffer));
    }
    private sealed class OwnerZero:IWorldItemOwnerFactsProvider1458,IWorldItemOwnerFactsSnapshot1458
    {
        public bool IsCurrent=>true;
        public IWorldItemOwnerFactsSnapshot1458 Capture()=>this;
        public bool TrySelectOwner(in WorldItemDropStateUpdate drop,int grabDelay,byte grabPlayer,out byte owner){owner=0;return true;}
    }
}
