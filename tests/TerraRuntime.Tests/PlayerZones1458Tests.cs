using System.Buffers;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PlayerZones1458Tests
{
    public static IEnumerable<object[]> Cases(string kind)
    {
        using var stream = typeof(PlayerZones1458Tests).Assembly.GetManifestResourceStream("TownPlayerZones1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray())
            if (row.GetProperty("kind").GetString() == kind) yield return [row.Clone()];
    }

    [Theory, MemberData(nameof(Cases), "wire")]
    public void Source_socket_bytes_preserve_every_raw_zone_and_town_byte(JsonElement row)
    {
        byte value = row.GetProperty("value").GetByte();
        var zones = new PlayerZoneSnapshot1458(value, unchecked((byte)(value+1)), unchecked((byte)(value+2)),
            unchecked((byte)(value+3)), unchecked((byte)(value+4)), value);
        byte[] source = Convert.FromHexString(row.GetProperty("frames")[0].GetString()!);
        Assert.Equal(source, TerrariaPlayerZonesCodec1458.Encode(0, in zones));
        var frame = Decode(source);
        Assert.Equal(TerrariaPlayerZonesDecodeResult.Decoded, TerrariaPlayerZonesCodec1458.TryDecode(in frame,out byte slot,out var actual));
        Assert.Equal(0,slot); Assert.Equal(zones,actual);
    }

    [Theory, MemberData(nameof(Cases), "biome")]
    public void Source_biome_priority_and_depth_are_not_inferred_from_terrain(JsonElement row)
    {
        var zones = new PlayerZoneSnapshot1458(row.GetProperty("z1").GetByte(),row.GetProperty("z2").GetByte(),0,0,0,0);
        var world=RuntimeTownSocialWorld1458.FromMetadata(new WorldFileRuntimeMetadata {WorldSurface=40,RockLayer=50},false);
        Assert.True(RuntimeTownNpcSchedule1458.TrySelectSocialBiomeTopic(5000,row.GetProperty("y").GetSingle(),zones,in world,1000,500,out byte emote));
        Assert.Equal(row.GetProperty("emotes")[0].GetByte(),emote);
    }

    [Theory, MemberData(nameof(Cases), "ingress")]
    public void Original_authenticated_rewrite_and_existing_faeling_noop_match_owned_commit(JsonElement row)
    {
        using var f=new Fixture(1); f.AddShimmerfly(1);
        var previous = new PlayerZoneSnapshot1458(0,0,0,0,row.GetProperty("prior").GetBoolean()?(byte)1:(byte)0,0);
        f.State.Apply(new PlayerZonesRuntimeCommand(f.Connection,previous));Drain(f.Owner);Drain(f.Peer);
        var next=new PlayerZoneSnapshot1458(6,32,3,4,row.GetProperty("incoming").GetBoolean()?(byte)1:(byte)0,9);
        using var bootstrap=f.Bootstrap();var posted=new ApplyingIngress(f.State);
        var sink=new PlayerZonesFrameSink(f.Connection.Source,bootstrap,new ContinuingSink(),new RuntimePlayerZonesNetworkIngress(posted));
        var frame=Decode(TerrariaPlayerZonesCodec1458.Encode(row.GetProperty("claimed").GetByte(),in next));
        Assert.Equal(TerrariaFrameSinkResult.Continue,sink.OnFrame(in frame));
        Assert.Equal(f.Connection,Assert.IsType<PlayerZonesRuntimeCommand>(posted.Last).Connection);
        Assert.Equal(next,f.Snapshot.Zones);Assert.Empty(Drain(f.Owner));
        Assert.Equal(Convert.FromHexString(row.GetProperty("frames")[0].GetString()!),Assert.Single(Drain(f.Peer)));
        Assert.Equal(row.GetProperty("next").GetInt32(),f.Random.Next());
    }

    [Theory, MemberData(nameof(Cases), "spawn")]
    public void Same_generation_respawn_preserves_source_zones_and_town_count(JsonElement row)
    {
        using var f=new Fixture();f.AddShimmerfly(1);byte value=row.GetProperty("incoming").GetByte();
        var zones=new PlayerZoneSnapshot1458(value,value,value,value,value,197);f.State.Apply(new PlayerZonesRuntimeCommand(f.Connection,zones));
        Assert.True(f.Players.TryGet(f.Connection,out var member));member.IsDead=row.GetProperty("dead").GetBoolean();
        byte context=row.GetProperty("context").GetString() switch {"ReviveFromDeath"=>0,"SpawningIntoWorld"=>1,"RecallFromItem"=>2,"TeamSwap"=>3,_=>throw new InvalidOperationException()};
        f.State.Apply(new PlayerRespawnRuntimeCommand(f.Connection,new(f.Session.Slot,40,30,0,0,0,0,context)));
        Assert.Equal(zones,f.Snapshot.Zones);Assert.Equal(197,row.GetProperty("town").GetInt32());
        Assert.All(Convert.FromBase64String(row.GetProperty("zones").GetString()!),v=>Assert.Equal(value,v));
    }

    [Fact]
    public void Source_new_member_is_known_clear_but_imported_missing_projection_stays_unknown()
    {
        using var f=new Fixture();var original=Assert.Single(Cases("new"));var row=(JsonElement)original[0];
        Assert.Equal(default(PlayerZoneSnapshot1458),f.Snapshot.Zones);Assert.Equal(0,row.GetProperty("town").GetInt32());
        Assert.All(Convert.FromBase64String(row.GetProperty("zones").GetString()!),x=>Assert.Equal(0,x));Assert.Null(default(PlayerStateSnapshot).Zones);
        var world=RuntimeTownSocialWorld1458.FromMetadata(new WorldFileRuntimeMetadata{WorldSurface=40,RockLayer=50},false);
        Assert.False(RuntimeTownNpcSchedule1458.TrySelectSocialBiomeTopic(5000,480,null,in world,1000,500,out _));
        Assert.True(RuntimeTownNpcSchedule1458.TrySelectSocialBiomeTopic(5000,16,null,in world,1000,500,out byte sky));Assert.Equal(22,sky);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(6)] [InlineData(8)]
    public void Wrong_payload_length_stops_before_posting(int length)
    {
        using var f=new Fixture();using var bootstrap=f.Bootstrap();var ingress=new ApplyingIngress(f.State);
        var sink=new PlayerZonesFrameSink(f.Connection.Source,bootstrap,new ContinuingSink(),new RuntimePlayerZonesNetworkIngress(ingress));
        var frame=new TerrariaFrame((ushort)(3+length),(byte)TerrariaMessageId.SyncPlayerZone,ReadOnlySequence<byte>.Empty,new(new byte[length]));
        Assert.Equal(TerrariaFrameSinkResult.Stop,sink.OnFrame(in frame));Assert.Null(ingress.Last);
        Assert.Equal(TerrariaFrameRejectionCategory.MalformedProtocol,sink.RejectionCategory);
    }

    [Fact]
    public void Fragmented_payload_and_wrong_id_have_bounded_decode()
    {
        byte[] bytes=[200,6,32,3,4,0,9];var first=new Segment(bytes.AsMemory(0,2));var last=first.Append(bytes.AsMemory(2));
        var frame=new TerrariaFrame(10,36,ReadOnlySequence<byte>.Empty,new(first,0,last,last.Memory.Length));
        Assert.Equal(TerrariaPlayerZonesDecodeResult.Decoded,TerrariaPlayerZonesCodec1458.TryDecode(in frame,out byte claimed,out var zones));
        Assert.Equal(200,claimed);Assert.Equal(new(6,32,3,4,0,9),zones);
        frame=frame with{MessageId=84};Assert.Equal(TerrariaPlayerZonesDecodeResult.WrongMessageId,TerrariaPlayerZonesCodec1458.TryDecode(in frame,out _,out _));
    }

    [Theory]
    [InlineData("absent")] [InlineData("inactive")] [InlineData("sentinel")] [InlineData("stale")] [InlineData("revision")]
    public void Unsupported_rising_producer_or_stale_identity_rejects_whole_snapshot(string fault)
    {
        using var f=new Fixture();
        if(fault is "inactive" or "sentinel") {var handle=f.AddShimmerfly(fault=="sentinel"?(byte)200:(byte)1);if(fault=="inactive")Assert.True(f.Npcs.TryDespawn(handle));}
        if(fault is "stale" or "revision")f.AddShimmerfly(1);
        ConnectionHandle connection=f.Connection;
        if(fault=="stale")connection=connection with{Player=new(connection.Player.Slot,new(999))};
        if(fault=="revision"){Assert.True(f.Players.TryGet(f.Connection,out var member));member.Revision=ulong.MaxValue;}
        var before=f.Snapshot;Drain(f.Owner);Drain(f.Peer);var zones=new PlayerZoneSnapshot1458(255,255,255,255,1,255);
        f.State.Apply(new PlayerZonesRuntimeCommand(connection,zones));Assert.Equal(before,f.Snapshot);Assert.Empty(Drain(f.Owner));Assert.Empty(Drain(f.Peer));
        Assert.Equal(new VanillaUnifiedRandom1458(1458).Next(),f.Random.Next());
    }

    [Fact]
    public void Repeated_shimmer_and_falling_edge_do_not_require_a_producer()
    {
        using var f=new Fixture();var handle=f.AddShimmerfly(1);var zones=new PlayerZoneSnapshot1458(6,32,3,4,1,9);
        f.State.Apply(new PlayerZonesRuntimeCommand(f.Connection,zones));Assert.True(f.Npcs.TryDespawn(handle));
        Drain(f.Owner);Drain(f.Peer);f.State.Apply(new PlayerZonesRuntimeCommand(f.Connection,zones));Assert.Equal(zones,f.Snapshot.Zones);Assert.Single(Drain(f.Peer));
        zones=zones with{Zone5=0};f.State.Apply(new PlayerZonesRuntimeCommand(f.Connection,zones));Assert.Equal(zones,f.Snapshot.Zones);Assert.Single(Drain(f.Peer));
    }

    [Fact]
    public async Task Frame_queue_and_authoritative_loop_publish_to_peers_after_commit()
    {
        using var f=new Fixture();using var bootstrap=f.Bootstrap();var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var loop=new AuthoritativeGameLoop<ServerRuntimeState,RuntimeCommand>(f.State,(state,command)=>{state.Apply(command);completed.TrySetResult();},static state=>state.Tick());
        var ingress=new RuntimePlayerZonesNetworkIngress(new AuthoritativeCommandIngress<ServerRuntimeState,RuntimeCommand>(loop));
        var sink=new PlayerZonesFrameSink(f.Connection.Source,bootstrap,new ContinuingSink(),ingress);var zones=new PlayerZoneSnapshot1458(6,32,3,4,0,9);
        Drain(f.Owner);Drain(f.Peer);var frame=Decode(TerrariaPlayerZonesCodec1458.Encode(200,in zones));
        Assert.Equal(TerrariaFrameSinkResult.Continue,sink.OnFrame(in frame));Assert.Equal(default(PlayerZoneSnapshot1458),f.Snapshot.Zones);Assert.Empty(Drain(f.Peer));
        loop.Start();await completed.Task.WaitAsync(TimeSpan.FromSeconds(5),TestContext.Current.CancellationToken);Assert.True(loop.Stop(TimeSpan.FromSeconds(5)));Assert.Null(loop.Fault);
        Assert.Equal(zones,f.Snapshot.Zones);Assert.Empty(Drain(f.Owner));Assert.Equal(Convert.FromHexString("0A002400062003040009"),Assert.Single(Drain(f.Peer)));
        var late=Queue();var source=GameCommandSourceId.FromConnection(50003);Assert.True(f.Registry.TryRegister(source,late));var handle=new PlayerHandle(new(2),new(1));var request=new PlayerSpawnCommitRequest(handle.Slot,10,10,0,0,0,0,0);f.Registry.PlayerSpawned(new(source,handle),in request);
        Assert.DoesNotContain(Drain(late),x=>x[2]==36);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Transfer_preserves_owned_or_unknown_zones_without_default_relay(bool known)
    {
        using var f=new Fixture();var zones=new PlayerZoneSnapshot1458(255,32,3,4,0,197);f.State.Apply(new PlayerZonesRuntimeCommand(f.Connection,zones));
        var detached=new TaskCompletionSource<RuntimePlayerTransferState?>();f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection,detached));var transfer=Assert.IsType<RuntimePlayerTransferState>(await detached.Task);
        if(!known)transfer=transfer with{Player=transfer.Player with{Zones=null}};
        var registry=new RuntimeConnectionRegistry();var peer=Queue();var source=GameCommandSourceId.FromConnection(50004);Assert.True(registry.TryRegister(source,peer));var request=new PlayerSpawnCommitRequest(new(1),10,10,0,0,0,0,0);registry.PlayerSpawned(new(source,new(new(1),new(1))),in request);Drain(peer);
        var destination=new PlayerAuthority(registry,null);var completion=new TaskCompletionSource<bool>();destination.TryApply(new PlayerTransferAttachRuntimeCommand(f.Connection,transfer,10,10,true,false,completion));Assert.True(await completion.Task);
        Assert.True(destination.TryCapture(f.Connection.Player,out var snapshot));Assert.Equal(known?zones:null,snapshot.Zones);Assert.DoesNotContain(Drain(peer),x=>x[2]==36);
    }

    [Fact]
    public void Replacement_generation_starts_clear_and_old_commands_cannot_write_it()
    {
        using var f=new Fixture();f.State.Apply(new PlayerZonesRuntimeCommand(f.Connection,new(6,32,3,4,0,9)));
        f.State.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));f.Session.Dispose();Assert.True(f.Pool.TryAcquireConnection(out var lease));using var next=new PlayerJoinSession(lease!);next.ObserveWorldRequest();next.ObserveSectionRequest();var connection=new ConnectionHandle(f.Connection.Source,next.Handle);
        f.State.Apply(new PlayerSpawnRuntimeCommand(connection,next,new(next.Slot,10,10,0,0,0,0,0)));f.State.Apply(new PlayerZonesRuntimeCommand(f.Connection,new(255,255,255,255,0,255)));
        Assert.True(f.State.TryCapturePlayerSnapshot(next.Handle,out var snapshot));Assert.Equal(default(PlayerZoneSnapshot1458),snapshot.Zones);Assert.NotEqual(f.Connection.Player.Generation,next.Handle.Generation);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly RuntimeConnectionRegistry Registry=new();internal readonly RuntimeNpcStore Npcs=new();
        internal readonly PlayerSlotPool Pool=new(2);internal readonly PlayerJoinSession Session;internal readonly ConnectionHandle Connection;
        private readonly IDisposable? reservedSlot;
        internal readonly ServerRuntimeState State;internal readonly TerrariaConnectionOutboundQueue Owner=Queue(),Peer=Queue();internal readonly VanillaUnifiedRandom1458 Random=new(1458);
        internal Fixture(byte ownedSlot=0){if(ownedSlot==1){Assert.True(Pool.TryAcquireConnection(out var reserved));reservedSlot=reserved;}Assert.True(Pool.TryAcquireConnection(out var lease));Session=new(lease!);Session.ObserveWorldRequest();Session.ObserveSectionRequest();Connection=new(GameCommandSourceId.FromConnection(50001),Session.Handle);
            State=new(playerEvents:Registry,npcs:Npcs,naturalSpawnRandom:new SystemVanillaNpcRandom(Random));Assert.True(Registry.TryRegister(Connection.Source,Owner));var source=GameCommandSourceId.FromConnection(50002);Assert.True(Registry.TryRegister(source,Peer));var peerSlot=new PlayerSlotId(ownedSlot==0?(byte)1:(byte)0);var request=new PlayerSpawnCommitRequest(peerSlot,10,10,0,0,0,0,0);Registry.PlayerSpawned(new(source,new(peerSlot,new(1))),in request);
            State.Apply(new PlayerSpawnRuntimeCommand(Connection,Session,new(Session.Slot,10,10,0,0,0,0,0)));Drain(Owner);Drain(Peer);}
        internal PlayerStateSnapshot Snapshot{get{Assert.True(State.TryCapturePlayerSnapshot(Connection.Player,out var value));return value;}}
        internal PlayerAuthority Players{get{var runtime=typeof(ServerRuntimeState).GetField("_runtime",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(State)!;return(PlayerAuthority)runtime.GetType().GetProperty("Players",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime)!;}}
        internal NpcHandle AddShimmerfly(byte slot){var state=new NpcStateUpdate(677,677,100,100,0,0,255,default,NpcSimulationState.Initial);Assert.True(Npcs.TrySpawn(slot,in state,out var npc));return npc.Handle;}
        internal PlayerBootstrapFrameSink Bootstrap(){var sink=new PlayerBootstrapFrameSink(new PlayerSlotPool(1),Queue(),PlayerBootstrapPacketSet.CreateForTesting(new byte[]{3,0,7},[],new byte[]{3,0,49}),Connection.Source,new UnusedSpawnIngress());sink.AdoptPlayingSession(Session,null);return sink;}
        public void Dispose(){Session.Dispose();reservedSlot?.Dispose();}
    }
    private sealed class ApplyingIngress(ServerRuntimeState state) : IGameCommandIngress<RuntimeCommand>{internal RuntimeCommand? Last;public bool TryPost(GameCommandSourceId source,RuntimeCommand command){Last=command;state.Apply(command);return true;}}
    private sealed class ContinuingSink : ITerrariaFrameSink{public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)=>TerrariaFrameSinkResult.Continue;}
    private sealed class UnusedSpawnIngress : IPlayerSpawnCommitIngress{public bool TryPost(GameCommandSourceId source,PlayerJoinSession session,in PlayerSpawnCommitRequest request)=>throw new InvalidOperationException();}
    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        internal Segment(ReadOnlyMemory<byte> memory)=>Memory=memory;
        internal Segment Append(ReadOnlyMemory<byte> next){var value=new Segment(next){RunningIndex=RunningIndex+Memory.Length};Next=value;return value;}
    }
    private static TerrariaFrame Decode(byte[] bytes){var sequence=new ReadOnlySequence<byte>(bytes);Assert.Equal(TerrariaFrameReadResult.Frame,TerrariaFrameDecoder.TryRead(ref sequence,out var frame));return frame;}
    private static TerrariaConnectionOutboundQueue Queue()=>new(new OutboundQueueOptions(128,65536,2048));
    private static byte[][] Drain(TerrariaConnectionOutboundQueue queue){var inner=(BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetProperty("InnerQueue",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(queue)!;var result=new List<byte[]>();while(inner.TryRead(out var frame))result.Add(frame.Bytes.ToArray());return result.ToArray();}
}
