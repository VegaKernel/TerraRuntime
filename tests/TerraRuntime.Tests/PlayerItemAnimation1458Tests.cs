using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Players;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
namespace TerraRuntime.Tests;
public sealed class PlayerItemAnimation1458Tests
{
    [Theory]
    [InlineData(7,.25f,20,"0A0029070000803E1400")]
    [InlineData(199,-.5f,32767,"0A0029C7000000BFFF7F")]
    public void Original_SendData_41_golden_wire_is_exact(int slot,float rotation,int animation,string hex)
    {
        var bytes=Convert.FromHexString(hex);
        Assert.Equal(bytes,TerrariaPlayerItemAnimationCodec1458.Encode((byte)slot,rotation,(short)animation));
        var frame=Decode(bytes);
        Assert.Equal(TerrariaPlayerItemAnimationDecodeResult.Decoded,TerrariaPlayerItemAnimationCodec1458.TryDecode(in frame,out byte claim,out float aim,out short clock));
        Assert.Equal(slot,claim);Assert.Equal(rotation,aim);Assert.Equal(animation,clock);
    }
    [Theory]
    [InlineData(float.NaN,1)] [InlineData(float.PositiveInfinity,1)] [InlineData(0f,-1)]
    public void Invalid_wire_and_direct_commands_leave_owned_snapshot_unchanged(float rotation,int animation)
    {
        using var f=new Fixture(); f.Set(.5f,20);var before=f.State;
        f.Set(rotation,(short)animation);Assert.Equal(before,f.State);
        byte[] bytes=Convert.FromHexString("0A002907000000000100");
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4),rotation);BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(8),(short)animation);
        var frame=Decode(bytes);
        Assert.Equal(TerrariaPlayerItemAnimationDecodeResult.InvalidItemAnimation,TerrariaPlayerItemAnimationCodec1458.TryDecode(in frame,out _,out _,out _));
    }
    [Fact]
    public void Fixed_size_parser_rejects_truncated_extra_and_wrong_id_payloads()
    {
        foreach(int length in new[]{0,1,6,8,32767})
        {
            var frame=new TerrariaFrame((ushort)(length+3),41,default,new ReadOnlySequence<byte>(new byte[length]));
            Assert.Equal(TerrariaPlayerItemAnimationDecodeResult.InvalidPayloadLength,TerrariaPlayerItemAnimationCodec1458.TryDecode(in frame,out _,out _,out _));
        }
        var wrong=new TerrariaFrame(10,84,default,new ReadOnlySequence<byte>(new byte[7]));
        Assert.Equal(TerrariaPlayerItemAnimationDecodeResult.WrongMessageId,TerrariaPlayerItemAnimationCodec1458.TryDecode(in wrong,out _,out _,out _));
    }
    [Fact]
    public void Claimed_slot_is_replaced_and_stale_connection_or_generation_cannot_mutate()
    {
        using var bootstrap=Bootstrap();var ingress=new RecordingIngress();
        var sink=new PlayerItemAnimationFrameSink(Source,bootstrap,new ContinuingSink(),ingress);
        var frame=Decode(TerrariaPlayerItemAnimationCodec1458.Encode(199,.5f,9));
        Assert.Equal(TerrariaFrameSinkResult.Continue,sink.OnFrame(in frame));
        Assert.Equal(new ConnectionHandle(Source,bootstrap.AssignedPlayerHandle!.Value),ingress.Last);
        Assert.Equal((short)9,ingress.Animation);
        using var f=new Fixture();f.Set(.5f,9);var before=f.State;
        f.Authority.TryApply(new PlayerItemAnimationRuntimeCommand(f.Connection with { Source=GameCommandSourceId.FromConnection(999) },0f,20));
        f.Authority.TryApply(new PlayerItemAnimationRuntimeCommand(new ConnectionHandle(f.Connection.Source,new PlayerHandle(f.Connection.Player.Slot,new PlayerSessionGeneration(999))),0f,20));
        Assert.Equal(before,f.State);
    }
    [Fact]
    public void Common_ItemCheck_countdown_expires_and_respawn_clears_animation()
    {
        Assert.Null(default(PlayerStateSnapshot).ItemAnimation);
        using var f=new Fixture();Assert.Equal(0,f.State.ItemAnimation);f.Set(.25f,3);
        f.Authority.TickItemAnimation();Assert.Equal(2,f.State.ItemAnimation);
        f.Authority.TickItemAnimation();f.Authority.TickItemAnimation();f.Authority.TickItemAnimation();Assert.Equal(0,f.State.ItemAnimation);
        f.Set(.25f,short.MaxValue); Assert.True(f.Authority.TryGet(f.Connection,out var member)); member.IsDead=true; f.Authority.TickItemAnimation(); Assert.Equal(0,f.State.ItemAnimation); f.Authority.TryApply(new PlayerRespawnRuntimeCommand(f.Connection,new PlayerSpawnCommitRequest(f.Session.Slot,12,12,0,1,0,0,0)));
        Assert.Equal(0,f.State.ItemAnimation);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Original_Spawn_preserves_animation_and_rotation_even_for_representable_dead_state(bool dead)
    {
        using var f=new Fixture(); f.Set(.25f,9); Assert.True(f.Authority.TryGet(f.Connection,out var member)); member.IsDead=dead;
        f.Authority.TryApply(new PlayerRespawnRuntimeCommand(f.Connection,new PlayerSpawnCommitRequest(f.Session.Slot,12,12,0,1,0,0,0)));
        Assert.Equal(9,f.State.ItemAnimation); Assert.Equal(.25f,f.State.ItemRotation);
    }
    [Fact]
    public void Source_peer_relay_excludes_owner_and_rejects_wrong_generation()
    {
        var registry=new RuntimeConnectionRegistry();var a=Queue();var b=Queue();
        var owner=new ConnectionHandle(Source,new PlayerHandle(new PlayerSlotId(0),new PlayerSessionGeneration(1)));
        var peer=new ConnectionHandle(GameCommandSourceId.FromConnection(7002),new PlayerHandle(new PlayerSlotId(1),new PlayerSessionGeneration(1)));
        Assert.True(registry.TryRegister(owner.Source,a));Assert.True(registry.TryRegister(peer.Source,b));
        var spawnA=new PlayerSpawnCommitRequest(owner.Player.Slot,10,10,0,0,0,0,0);var spawnB=spawnA with { ClaimedSlot=peer.Player.Slot };
        registry.PlayerSpawned(owner,in spawnA);registry.PlayerSpawned(peer,in spawnB);while(Inner(a).TryRead(out _)){}while(Inner(b).TryRead(out _)){}
        registry.PlayerItemAnimationUpdated(owner,.5f,9);Assert.Equal(0,a.QueuedFrames);Assert.True(Inner(b).TryRead(out var frame));Assert.Equal(TerrariaPlayerItemAnimationCodec1458.Encode(0,.5f,9),frame.Bytes.ToArray());
        registry.PlayerItemAnimationUpdated(owner with { Player=new PlayerHandle(owner.Player.Slot,new PlayerSessionGeneration(2)) },0f,1);Assert.Equal(0,b.QueuedFrames);
    }
    [Fact]
    public void Production_target_projection_uses_packet41_clock_and84_stealth_not_packet13_success_bit()
    {
        using var f=new Fixture();var state=new ServerRuntimeState(worldTiles:new WorldTileStore(new WorldDimensions(600,500)));
        var runtime=typeof(ServerRuntimeState).GetField("_runtime",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(state)!;
        var players=(PlayerAuthority)runtime.GetType().GetProperty("Players",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime)!;
        var slots=new PlayerSlotPool(1);Assert.True(slots.TryAcquireConnection(out var lease));using var session=new PlayerJoinSession(lease!);session.ObserveWorldRequest();session.ObserveSectionRequest();
        var connection=new ConnectionHandle(Source,session.Handle);state.Apply(new PlayerSpawnRuntimeCommand(connection,session,new PlayerSpawnCommitRequest(session.Slot,100,100,0,0,0,0,0)));
        Assert.True(players.TryGet(connection,out var member));member.MiscFlags2=1<<6;
        var npcs=runtime.GetType().GetProperty("Npcs",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime)!;
        var get=npcs.GetType().GetMethod("TryGetPlayerTarget",BindingFlags.Instance|BindingFlags.NonPublic)!;
        VanillaNpcTargetCandidate Capture(){object?[] args=[(byte)0,null];Assert.True((bool)get.Invoke(npcs,args)!);return (VanillaNpcTargetCandidate)args[1]!;}
        Assert.Equal(0,Capture().ItemAnimation);
        state.Apply(new PlayerItemAnimationRuntimeCommand(connection,0f,3));state.Apply(new PlayerStealthRuntimeCommand(connection,.25f));member.MiscFlags2=0;
        Assert.Equal(3,Capture().ItemAnimation);Assert.Equal(.25f,Capture().Stealth);
        state.Tick();Assert.Equal(2,Capture().ItemAnimation);state.Tick();state.Tick();Assert.Equal(0,Capture().ItemAnimation);
    }
    [Theory]
    [InlineData(47)] [InlineData(149)] [InlineData(156)] [InlineData(353)]
    public void Source_CCed_buff_snapshots_clear_animation_before_NPC_visibility(int buff)
    {
        using var f=new Fixture();f.Set(0f,9);
        f.Authority.TryApply(new PlayerBuffTypesRuntimeCommand(f.Connection,new PlayerBuffTypesCommitRequest(f.Session.Slot,new[]{new BuffTypeId((ushort)buff)})));
        f.Authority.TickItemAnimation();Assert.Equal(0,f.State.ItemAnimation);
    }
    [Fact]
    public void Source_Revolver_release_decrements_twice_for_connected_and_server_owned_players()
    {
        using var f=new Fixture();f.Authority.TryApply(new PlayerEquipmentRuntimeCommand(f.Connection,new PlayerEquipmentCommitRequest(f.Session.Slot,0,1,0,checked((short)VanillaItemIds.Revolver.Value),0)));f.Set(0f,9);
        f.Authority.TickItemAnimation();Assert.Equal(7,f.State.ItemAnimation);
        var pool=new PlayerSlotPool(1);var identities=new ServerPlayerSlotRegistry(pool);var id=new ServerPlayerId("test:animation");
        Assert.Equal(ServerPlayerSlotAcquireResult.Acquired,identities.TryAcquire(id,out var lease));using var owned=lease!;
        var store=new ServerPlayerStateStore(identities,1);Assert.True(store.TrySpawn(id,0f,0f,out _));
        Assert.True(store.TrySetItem(owned.Player,new ServerPlayerItemState(0,VanillaItemIds.Revolver,1,default,0),out _));
        Assert.True(store.TrySetItemAnimation(owned.Player,0f,9));store.TickItemAnimation();Assert.True(store.TryGet(owned.Player,out var state));Assert.Equal(7,state.ItemAnimation);
    }
    [Theory]
    [InlineData(null,0)] [InlineData(0,0)] [InlineData(123,123)]
    public async Task Transfer_preserves_owned_animation_or_defaults_absent_projection(int? animation,int expected)
    {
        using var f=new Fixture();var detach=new TaskCompletionSource<RuntimePlayerTransferState?>();f.Authority.TryApply(new PlayerTransferDetachRuntimeCommand(f.Connection,detach));
        var transfer=Assert.IsType<RuntimePlayerTransferState>(await detach.Task);transfer=transfer with{Player=transfer.Player with{ItemAnimation=animation,ItemRotation=.25f,PositionX=1600f,PositionY=1600f}};
        var destination=new PlayerAuthority(null,new WorldTileStore(new WorldDimensions(600,500)));var completion=new TaskCompletionSource<bool>();destination.TryApply(new PlayerTransferAttachRuntimeCommand(f.Connection,transfer,10,10,true,false,completion));
        Assert.True(await completion.Task);Assert.True(destination.TryCapture(f.Connection.Player,out var after));Assert.Equal(expected,after.ItemAnimation);Assert.Equal(.25f,after.ItemRotation);
    }
    [Theory]
    [InlineData(-1)] [InlineData(32768)]
    public async Task Invalid_transfer_animation_cannot_create_destination_member(int animation)
    {
        using var f=new Fixture();var detach=new TaskCompletionSource<RuntimePlayerTransferState?>();f.Authority.TryApply(new PlayerTransferDetachRuntimeCommand(f.Connection,detach));
        var transfer=Assert.IsType<RuntimePlayerTransferState>(await detach.Task);transfer=transfer with{Player=transfer.Player with{ItemAnimation=animation}};
        var destination=new PlayerAuthority(null,new WorldTileStore(new WorldDimensions(600,500)));var completion=new TaskCompletionSource<bool>();destination.TryApply(new PlayerTransferAttachRuntimeCommand(f.Connection,transfer,10,10,true,false,completion));
        Assert.False(await completion.Task);Assert.False(destination.TryCapture(f.Connection.Player,out _));
    }
    private static BoundedOutboundQueue Inner(TerrariaConnectionOutboundQueue queue) => (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetField("_queue", BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(queue)!;
    private static readonly GameCommandSourceId Source=GameCommandSourceId.FromConnection(7001);
    private static TerrariaConnectionOutboundQueue Queue()=>new(new OutboundQueueOptions(64,16384,2048));
    private static TerrariaFrame Decode(byte[] bytes){var sequence=new ReadOnlySequence<byte>(bytes);Assert.Equal(TerrariaFrameReadResult.Frame,TerrariaFrameDecoder.TryRead(ref sequence,out var frame));return frame;}
    private static PlayerBootstrapFrameSink Bootstrap(){var b=new PlayerBootstrapFrameSink(new PlayerSlotPool(1),Queue(),PlayerBootstrapPacketSet.CreateForTesting(new byte[]{3,0,7},Array.Empty<ReadOnlyMemory<byte>>(),new byte[]{3,0,49}),Source,new SpawnIngress());var frame=Decode(new byte[]{15,0,1,11,84,101,114,114,97,114,105,97,51,50,54});Assert.Equal(TerrariaFrameSinkResult.Continue,b.OnFrame(in frame));return b;}
    private sealed class SpawnIngress:IPlayerSpawnCommitIngress{public bool TryPost(GameCommandSourceId source,PlayerJoinSession session,in PlayerSpawnCommitRequest request)=>session.TryCommitSpawn(request.ClaimedSlot)==PlayerSpawnCommitResult.Committed;}
    private sealed class ContinuingSink:ITerrariaFrameSink{public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)=>TerrariaFrameSinkResult.Continue;}
    private sealed class RecordingIngress:IPlayerItemAnimationNetworkIngress{public ConnectionHandle Last;public short Animation;public bool TryPost(ConnectionHandle c,float rotation,short animation){Last=c;Animation=animation;return true;}}
    private sealed class Fixture:IDisposable{
        public PlayerAuthority Authority{get;}=new(null,null);public PlayerJoinSession Session{get;}public ConnectionHandle Connection{get;}
        public PlayerStateSnapshot State{get{Assert.True(Authority.TryCapture(Connection.Player,out var state));return state;}}
        public Fixture(){var pool=new PlayerSlotPool(1);Assert.True(pool.TryAcquireConnection(out var lease));Session=new PlayerJoinSession(lease!);Session.ObserveWorldRequest();Session.ObserveSectionRequest();Connection=new ConnectionHandle(Source,Session.Handle);Authority.TryApply(new PlayerSpawnRuntimeCommand(Connection,Session,new PlayerSpawnCommitRequest(Session.Slot,10,10,0,0,0,0,0)));}
        public void Set(float rotation,short animation)=>Authority.TryApply(new PlayerItemAnimationRuntimeCommand(Connection,rotation,animation));public void Dispose()=>Session.Dispose();
    }
}
