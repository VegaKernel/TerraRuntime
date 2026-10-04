using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class WorldItemSentinel1458Tests
{
    [Theory] [MemberData(nameof(Original))]
    public void Sentinel_has_source_ordered_transient_wire_without_physical_handle_or_join_entity(string json)
    {
        using var doc=JsonDocument.Parse(json);var row=doc.RootElement;
        byte[] original=Convert.FromHexString(row.GetProperty("frame21").GetString()!);
        float F(int offset)=>BinaryPrimitives.ReadSingleLittleEndian(original.AsSpan(offset));
        var mode=(WorldItemOwnershipMode)(original[24]&3);
        var drop=new WorldItemDropStateUpdate(F(5),F(9),F(13),F(17),BinaryPrimitives.ReadInt16LittleEndian(original.AsSpan(21)),
            original[23],mode,BinaryPrimitives.ReadInt16LittleEndian(original.AsSpan(25)),false,0,mode==WorldItemOwnershipMode.None? (byte)0:(byte)100);
        var registry=new RuntimeWorldItemReplicationRegistry();
        var source=GameCommandSourceId.FromConnection(9801);var outbound=new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(16,16384,1024));
        Assert.True(registry.TryRegister(source,outbound));
        var connection=new ConnectionHandle(source,new(new(0),new(1)));
        var spawn=new PlayerSpawnCommitRequest(new(0),100,100,0,0,0,0,0);registry.PlayerSpawned(connection,spawn);
        // Fixtures initialize the source table before the observer becomes playing.
        registry.TryUnregister(source);var store=WorldItemSourceAllocation1458Tests.Arrange(10,registry);
        Assert.True(registry.TryRegister(source,outbound));registry.PlayerSpawned(connection,spawn);
        bool reserve=row.GetProperty("reserve").GetBoolean();
        var views=reserve?new[]{new WorldItemAllocationPlayer1458(0,1000,1000,20,42)}:[];
        using(var plan=store.CreateAllocationPreview(views))
        {
            Assert.True(plan.TrySpawnSource(drop,0,out short selected,sourceLocalPlayerId:reserve?(byte)0:null));Assert.Equal(400,selected);
            Assert.True(plan.TryClaim());Assert.Equal(0,outbound.QueuedFrames);
            Assert.True(plan.TryCommitNext(out short committed,out var lease));Assert.Equal(400,committed);Assert.False(lease.IsAssigned);
        }
        Assert.Equal(400,store.ActiveCount);Assert.False(store.TryGetActive(400,out _));
        Assert.Equal(original,Read(outbound));
        if(reserve) Assert.Equal(Convert.FromHexString(row.TryGetProperty("frame22",out var owner)?owner.GetString()!:row.GetProperty("last").GetString()!),Read(outbound));
        Assert.Equal(0,outbound.QueuedFrames);Assert.Equal(0,registry.UnsupportedCommits);
        Span<WorldItemSnapshot> baseline=stackalloc WorldItemSnapshot[400];Assert.Equal(400,store.CopyActive(baseline));
        foreach(var item in baseline)Assert.InRange(item.Handle.Slot,(short)0,(short)399);
    }

    [Fact]
    public void Unrepresented_network_owner_search_and_missing_source_local_player_reject_before_publication()
    {
        var registry=new RuntimeWorldItemReplicationRegistry();var store=WorldItemSourceAllocation1458Tests.Arrange(10,registry);
        var drop=new WorldItemDropStateUpdate(992,992,-.5f,-2.5f,1,0,WorldItemOwnershipMode.None,2,false,0,0);
        var views=new[]{new WorldItemAllocationPlayer1458(0,1000,1000,20,42)};
        using(var plan=store.CreateAllocationPreview(views))Assert.False(plan.TrySpawnSource(drop,0,out _));
        using(var plan=store.CreateAllocationPreview(views))Assert.False(plan.TrySpawnSource(drop with {Ownership=WorldItemOwnershipMode.ReserveForLocalPlayer},0,out _));
        Assert.Equal(400,store.ActiveCount);Assert.Equal(0,registry.RelayedFrames);
    }

    [Fact]
    public void Failed_stale_sentinel_plan_does_not_emit_transient_frames()
    {
        var registry=new RuntimeWorldItemReplicationRegistry();var store=WorldItemSourceAllocation1458Tests.Arrange(10,registry);
        var drop=new WorldItemDropStateUpdate(992,992,-.5f,-2.5f,1,0,WorldItemOwnershipMode.None,2,false,0,0);
        using var plan=store.CreateAllocationPreview();Assert.True(plan.TrySpawnSource(drop,0,out _));
        Assert.True(store.TryGetActive(0,out var item));Assert.True(store.TryAdvanceMotion(item.Handle,1,1,0,0,out _));
        Assert.False(plan.TryClaim());Assert.False(plan.TryCommitNext(out _,out _));Assert.Equal(0,registry.RelayedFrames);
    }

    public static IEnumerable<object[]> Original()
    {
        using var input=typeof(WorldItemSentinel1458Tests).Assembly.GetManifestResourceStream("TerraRuntime.Tests.Fixtures.world-item-sentinel-wire-official.json.gz")!;
        using var gzip=new GZipStream(input,CompressionMode.Decompress);using var document=JsonDocument.Parse(gzip);
        foreach(var row in document.RootElement.EnumerateArray())yield return [row.GetRawText()];
    }

    private static byte[] Read(TerrariaConnectionOutboundQueue outbound)
    {
        var property=typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var queue=Assert.IsType<BoundedOutboundQueue>(property.GetValue(outbound));Assert.True(queue.TryRead(out var frame));return frame.Bytes.ToArray();
    }
}
