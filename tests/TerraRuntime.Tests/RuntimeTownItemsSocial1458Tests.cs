using System.Buffers;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using TerraRuntime.Core.Players;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownItemsSocial1458Tests
{
    public static IEnumerable<object[]> SourceCases()
    {
        using var stream=typeof(RuntimeTownItemsSocial1458Tests).Assembly.GetManifestResourceStream("TownItemsPair1458")!;
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);using var json=JsonDocument.Parse(gzip);
        foreach(var row in json.RootElement.EnumerateArray())yield return [row.GetRawText()];
    }
    [Theory, MemberData(nameof(SourceCases))]
    public void Fresh_observed_life_after_health_phase_then_pair_matches_original_context_wire_clocks_and_rng(string json)
    {
        using var document=JsonDocument.Parse(json);var row=document.RootElement;using var f=new Fixture(row);
        Assert.True(f.Players.TryGet(f.Connection,out var member));Assert.Equal(100,member.DerivedLifeMax);
        f.Players.TickHealthContext();Assert.Equal(row.GetProperty("derived").GetInt32(),member.DerivedLifeMax);
        Assert.False(member.NpcLifeCurrent);
        f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection,new(f.Session.Slot,(short)row.GetProperty("life").GetInt32(),400)));
        f.Schedule.SetSocialContext(f.World,f.Players);
        f.Schedule.Tick(in f.Conditions,[],f.Status,f.Combat);
        AssertSource(row,f,Drain(f.Peer));
    }

    [Theory]
    [InlineData("unknownHealth")] [InlineData("unknownDerived")] [InlineData("changedRevision")] [InlineData("replacementGeneration")]
    public void Unknown_or_stale_closest_player_rejects_pair_before_RNG_frames_and_mutation(string fault)
    {
        using var document=JsonDocument.Parse((string)SourceCases().First()[0]);var row=document.RootElement;using var f=new Fixture(row);
        f.Players.TickHealthContext();Assert.True(f.Players.TryGet(f.Connection,out var member));
        f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection,new(f.Session.Slot,member.Life,member.MaxLife)));
        if(fault=="unknownHealth")member.HasHealth=false;
        if(fault=="unknownDerived")member.DerivedLifeMax=null;
        f.Schedule.SetSocialContext(f.World,f.Players);
        if(fault=="changedRevision")Assert.True(member.TryAdvanceRevision());
        if(fault=="replacementGeneration")f.State.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
        var before=f.Current(0);Drain(f.Peer);
        Assert.Equal(1,f.Schedule.Tick(in f.Conditions,[],f.Status,f.Combat).RejectedCommits);Assert.Equal(before,f.Current(0));
        Assert.DoesNotContain(Drain(f.Peer),x=>x[2]==91);Assert.Equal(new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32()).Next(),f.Random.Next());
    }

    [Fact]
    public void Closest_server_owned_unknown_context_is_not_replaced_by_a_farther_connected_player()
    {
        using var document=JsonDocument.Parse((string)SourceCases().First()[0]);using var f=new Fixture(document.RootElement);
        f.Players.TickHealthContext();Assert.True(f.Players.TryGet(f.Connection,out var member));member.PositionX=1000;
        f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection,new(f.Session.Slot,member.Life,member.MaxLife)));
        var slots=new PlayerSlotPool(2);Assert.True(slots.TryAcquireConnection(out var occupied));using var reservation=occupied!;
        var identities=new ServerPlayerSlotRegistry(slots);var states=new ServerPlayerStateStore(identities,2);var server=new ServerPlayerAuthority(states,identities);
        Assert.True(server.Create(new("test:health-nearest"),630,439).IsCreated);
        f.Schedule.SetSocialContext(f.World,f.Players,serverPlayers:server);var before=f.Current(0);
        Assert.Equal(1,f.Schedule.Tick(in f.Conditions,[],f.Status,f.Combat).RejectedCommits);Assert.Equal(before,f.Current(0));Assert.DoesNotContain(Drain(f.Peer),x=>x[2]==91);
    }

    [Fact]
    public async Task Authenticated_health_and_duplicate_buff_frames_do_not_admit_unknown_continuous_life_after_player_phase()
    {
        string json=(string)SourceCases().First(x=>{using var d=JsonDocument.Parse((string)x[0]);return d.RootElement.GetProperty("slots").GetInt32()==2 && d.RootElement.GetProperty("life").GetInt32()==239 && d.RootElement.GetProperty("frame").GetInt32()==215;})[0];
        using var document=JsonDocument.Parse(json);var row=document.RootElement;using var f=new Fixture(row);using var bootstrap=f.Bootstrap();
        // Start from an earlier observed base and empty buffs; only queued authenticated frames supply new facts.
        f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection,new(f.Session.Slot,100,100)));
        f.State.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection,new(f.Session.Slot,Array.Empty<BuffTypeId>())));Drain(f.Peer);
        var ticked=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int ticks=0;
        using var loop=new AuthoritativeGameLoop<ServerRuntimeState,RuntimeCommand>(f.State,static(s,c)=>s.Apply(c),s=>{if(Interlocked.Increment(ref ticks)==1){s.Tick();ticked.TrySetResult();}});
        var ingress=new AuthoritativeCommandIngress<ServerRuntimeState,RuntimeCommand>(loop);
        var health=new PlayerVitalsFrameSink(f.Connection.Source,bootstrap,new RuntimePlayerHealthIngress(ingress),new RuntimePlayerManaIngress(ingress));
        var buffs=new PlayerBuffFrameSink(f.Connection.Source,bootstrap,new ContinuingSink(),new RuntimePlayerBuffNetworkIngress(ingress));
        // Claimed slot200 is overwritten by the exact playing connection owner.
        var healthFrame=Decode(Convert.FromHexString("080010C8EF009001"));
        var buffFrame=Decode(Convert.FromHexString("0A0032C8710071000000"));
        Assert.Equal(TerrariaFrameSinkResult.Continue,health.OnFrame(in healthFrame));Assert.Equal(TerrariaFrameSinkResult.Continue,buffs.OnFrame(in buffFrame));
        Assert.True(f.Players.TryGet(f.Connection,out var member));Assert.Equal(100,member.MaxLife);Assert.Equal(100,member.DerivedLifeMax);Assert.Empty(Drain(f.Peer));
        loop.Start();await ticked.Task.WaitAsync(TimeSpan.FromSeconds(5),TestContext.Current.CancellationToken);Assert.True(loop.Stop(TimeSpan.FromSeconds(5)));Assert.Null(loop.Fault);
        Assert.Equal(400,member.MaxLife);Assert.Equal(560,member.DerivedLifeMax);byte[][] frames=Drain(f.Peer);
        Assert.False(member.NpcLifeCurrent);
        Assert.Contains(frames,x=>x[2]==50);
        Assert.DoesNotContain(frames,x=>x[2]==91);
    }

    private static void AssertSource(JsonElement row,Fixture f,byte[][] frames)
    {
        // Main.SwapRandom isolates UpdatePlayers from UpdateNPCs in the original complete world phase.
        Assert.Equal(0,row.GetProperty("npcCursorBefore").GetInt32());
        byte[] expected=Assert.Single(row.GetProperty("frames").EnumerateArray().Select(x=>Convert.FromHexString(x.GetString()!)),x=>x[2]==91);
        Assert.Equal(expected,Assert.Single(frames,x=>x[2]==91));
        Assert.Equal(row.GetProperty("clock").GetDouble(),f.Current(0).Simulation.FrameCounter);Assert.Equal(row.GetProperty("peerClock").GetDouble(),f.Current(1).Simulation.FrameCounter);
        Assert.Equal(299f,f.Current(0).Ai.Ai1);Assert.Equal(299f,f.Current(1).Ai.Ai1);Assert.Equal(row.GetProperty("next").GetInt32(),f.Random.Next());
    }
    private sealed class UnusedSpawnIngress:IPlayerSpawnCommitIngress
    {public bool TryPost(GameCommandSourceId source,PlayerJoinSession session,in PlayerSpawnCommitRequest request)=>throw new InvalidOperationException();}
    private sealed class ContinuingSink:ITerrariaFrameSink
    {public TerrariaFrameSinkResult OnFrame(in TerrariaFrame frame)=>TerrariaFrameSinkResult.Continue;}
    private static TerrariaFrame Decode(byte[] bytes)
    {var sequence=new ReadOnlySequence<byte>(bytes);Assert.Equal(TerrariaFrameReadResult.Frame,TerrariaFrameDecoder.TryRead(ref sequence,out var frame));return frame;}
    private sealed class NoNpcStep:INpcAiStateStepper
    {public bool TryStepState(in NpcSnapshot npc,out NpcStateUpdate next){next=default;return false;}}
    internal sealed class Fixture : IDisposable
    {
        internal readonly RuntimeNpcReplicationRegistry Registry = new();
        internal readonly RuntimeConnectionRegistry PlayerRegistry = new();
        internal readonly PlayerSlotPool Pool = new(2);
        internal readonly PlayerJoinSession Session;
        internal readonly ConnectionHandle Connection;
        internal readonly ServerRuntimeState State;
        internal readonly PlayerAuthority Players;
        internal readonly TerrariaConnectionOutboundQueue Peer;
        internal readonly RuntimeNpcStore Npcs;
        internal readonly WorldTileStore Tiles;
        internal readonly RuntimeTownNpcStateStore Town;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeTownNpcSchedule1458 Schedule;
        internal readonly RuntimeTownNpcCombat1458 Combat;
        internal readonly RuntimeNpcBuffStatus1458 Status;
        internal readonly RuntimeTownNpcScheduleConditions1458 Conditions = new(true, false, false, false, false);
        internal readonly RuntimeTownSocialWorld1458 World = RuntimeTownSocialWorld1458.FromMetadata(
            new WorldFileRuntimeMetadata { DayTime = true, Time = 1000, WorldSurface = 40, RockLayer = 35 }, false);
        internal Fixture(JsonElement row, bool publishBuffs = false)
        {
            Npcs = new(commitSink: Registry);
            Tiles = new WorldTileStore(new(100, 80)); var tiles=Tiles;
            for (int x = 0; x < 100; x++) tiles.Set(x, 30, new() { Type = 1, Flags = WorldTileFlags.Active });
            int actor = 17, peer = 19;
            WorldTownNpc[] residents = [new(actor,"A",639,440,false,40,30,null,false),new(peer,"B",679,440,false,40,30,null,false)];
            WorldTownRoom[] rooms = actor == peer ? [new(actor,40,30)] : [new(actor,40,30),new(peer,40,30)];
            Town = new RuntimeTownNpcStateStore(new([], residents, []), rooms, tiles.Dimensions); var town=Town;
            Assert.True(town.TryReserveRuntimeSlots(Npcs));
            for (byte slot = 0; slot < 2; slot++)
            {
                Assert.True(Npcs.TryGetActive(slot, out var current)); int type = slot == 0 ? actor : peer;
                var state = new NpcStateUpdate(type,(short)type,slot == 0 ? 639 : 679,440,.5f,0,255,
                    new(slot == 0 ? 3 : 4,300,slot == 0 ? 1 : 0,0), NpcSimulationState.Initial with {
                        DirectionX=1, SpriteDirection=-1, Life=250,LifeMax=250,BaseLifeMax=250,BaseDefense=0,
                        KnockBackResist=1,HitboxOverride=new(18,40),FrameIndex=0,
                        FrameCounter=row.TryGetProperty("frame",out var frame)?frame.GetInt32():215,LocalAi=new(0,0,0,99),Breath=200 });
                Assert.True(Npcs.TryUpdate(current.Handle,in state,out _));
            }
            Random = new(row.GetProperty("seed").GetInt32()); var adapter = new SystemVanillaNpcRandom(Random);
            Action<NpcHandle>? publish = publishBuffs ? Registry.PublishNpcBuffs : null;
            Status = new(Npcs, publish); Status.BeginWorldTick();
            Schedule = new(town,Npcs,tiles,new NpcRuntimeTownScheduleRandom1458(adapter));
            State = new(playerEvents:PlayerRegistry,npcs:Npcs,npcAiStepper:new NoNpcStep(),worldTiles:Tiles,townNpcs:Town,
                npcReplication:Registry,naturalSpawnRandom:adapter,townSocialWorldFacts:World);
            Assert.True(Pool.TryAcquireConnection(out var lease)); Session=new(lease!);Session.ObserveWorldRequest();Session.ObserveSectionRequest();
            Connection=new(GameCommandSourceId.FromConnection(50401),Session.Handle);
            var owner=Endpoint(Registry,50401,0);Peer=Endpoint(Registry,50402,1);
            Assert.True(PlayerRegistry.TryRegister(Connection.Source,owner));Assert.True(PlayerRegistry.TryRegister(GameCommandSourceId.FromConnection(50402),Peer));
            var request=new PlayerSpawnCommitRequest(new(1),40,30,0,0,0,0,0);PlayerRegistry.PlayerSpawned(new(GameCommandSourceId.FromConnection(50402),new(new(1),new(1))),in request);
            State.Apply(new PlayerSpawnRuntimeCommand(Connection,Session,new(Session.Slot,40,30,0,0,0,0,0)));
            var runtime=typeof(ServerRuntimeState).GetField("_runtime",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(State)!;
            Players=(PlayerAuthority)runtime.GetType().GetProperty("Players",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(runtime)!;
            Assert.True(Players.TryGet(Connection,out var member));member.PositionX=600;member.PositionY=439;
            var zones=default(PlayerZoneSnapshot1458);
            State.Apply(new PlayerZonesRuntimeCommand(Connection,zones));
            State.Apply(new PlayerHealthRuntimeCommand(Connection,new(Session.Slot,(short)row.GetProperty("life").GetInt32(),400)));
            int slots=row.GetProperty("slots").GetInt32();
            State.Apply(new PlayerBuffTypesRuntimeCommand(Connection,new(Session.Slot,Enumerable.Repeat(VanillaBuffIds.Lifeforce,slots).ToArray())));
            Drain(owner);Drain(Peer);
            Schedule.SetSocialContext(World,Players);
            Combat = new(town,Npcs,new RuntimeProjectileStore(32),tiles,default,new(),false,false,
                new NpcRuntimeTownCombatRandom1458(adapter),Registry);
        }
        public void Dispose()=>Session.Dispose();
        internal PlayerBootstrapFrameSink Bootstrap()
        {
            var bootstrap=new PlayerBootstrapFrameSink(new PlayerSlotPool(1),new(new OutboundQueueOptions(8,4096,512)),PlayerBootstrapPacketSet.CreateForTesting(new byte[]{3,0,7},[],new byte[]{3,0,49}),Connection.Source,new UnusedSpawnIngress());
            bootstrap.AdoptPlayingSession(Session,null);
            return bootstrap;
        }
        internal NpcSnapshot Current(byte slot) { Assert.True(Npcs.TryGetActive(slot,out var current)); return current; }
    }

    private static TerrariaConnectionOutboundQueue Endpoint(RuntimeNpcReplicationRegistry registry, long id, byte slot, bool playing=true)
    {
        var source = GameCommandSourceId.FromConnection(id);
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128,65536,1024));
        Assert.True(registry.TryRegister(source,queue));
        if (playing) { var connection=new ConnectionHandle(source,new(new(slot),new(1)));
            var request=new PlayerSpawnCommitRequest(connection.Player.Slot,20,20,0,0,0,0,0);registry.PlayerSpawned(connection,in request); }
        return queue;
    }
    private static byte[][] Drain(TerrariaConnectionOutboundQueue queue)
    {
        var owned=(BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue).GetField("_queue",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(queue)!;
        var frames=new List<byte[]>();while(owned.TryRead(out var frame))frames.Add(frame.Bytes.ToArray());return frames.ToArray();
    }
}
