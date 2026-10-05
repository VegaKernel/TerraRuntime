using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;
using TerraRuntime.Core.Players;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownBiomeSocial1458Tests
{
    public static IEnumerable<object[]> SourceCases()
    {
        using var stream=typeof(RuntimeTownBiomeSocial1458Tests).Assembly.GetManifestResourceStream("TownBiomeSocial1458")!;
        using var gzip=new GZipStream(stream,CompressionMode.Decompress);using var json=JsonDocument.Parse(gzip);
        foreach(var row in json.RootElement.EnumerateArray())yield return [row.Clone()];
    }
    [Theory, MemberData(nameof(SourceCases))]
    public void Actual_world_tick_with_owned_packet36_facts_matches_original_pair_wire_and_rng(JsonElement row)
    {
        using var f=new Fixture(row);f.State.Tick();byte[][] actual=Drain(f.Peer);
        byte[] expected=Assert.Single(row.GetProperty("frames").EnumerateArray().Select(x=>Convert.FromHexString(x.GetString()!)),x=>x[2]==91);
        Assert.Equal(expected,Assert.Single(actual,x=>x[2]==91));Assert.Equal(91,actual[0][2]);
        Assert.Equal(row.GetProperty("clock").GetDouble(),f.Current(0).Simulation.FrameCounter);
        Assert.Equal(row.GetProperty("peerClock").GetDouble(),f.Current(1).Simulation.FrameCounter);
        Assert.Equal(299f,f.Current(0).Ai.Ai1);Assert.Equal(299f,f.Current(1).Ai.Ai1);Assert.Equal(row.GetProperty("next").GetInt32(),f.Random.Next());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Changed_or_unknown_owned_zone_projection_rejects_without_actor_rng_or_emote(bool unknown)
    {
        var row=(JsonElement)SourceCases().First()[0];using var f=new Fixture(row);
        Assert.True(f.Players.TryGet(f.Connection,out var member));
        if(unknown){member.Zones=null;f.Schedule.SetSocialContext(f.World,f.Players);}
        else f.State.Apply(new PlayerZonesRuntimeCommand(f.Connection,new(255,32,0,0,0,0)));
        Drain(f.Peer);var before=f.Current(0);
        Assert.Equal(1,f.Schedule.Tick(in f.Conditions,[],f.Status,f.Combat).RejectedCommits);Assert.Equal(before,f.Current(0));Assert.DoesNotContain(Drain(f.Peer),x=>x[2]==91);
        Assert.Equal(new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32()).Next(),f.Random.Next());
    }
    private sealed class NoNpcStep : INpcAiStateStepper
    { public bool TryStepState(in NpcSnapshot npc,out NpcStateUpdate next){next=default;return false;} }

    [Fact]
    public void A_closer_server_owned_player_with_unknown_zones_is_not_ignored_for_a_remote_client()
    {
        var row=(JsonElement)SourceCases().First()[0];using var f=new Fixture(row);
        Assert.True(f.Players.TryGet(f.Connection,out var member));member.PositionX=1000;
        var slots=new PlayerSlotPool(2);Assert.True(slots.TryAcquireConnection(out var reserved));using var reservation=reserved!;
        var identities=new ServerPlayerSlotRegistry(slots);var states=new ServerPlayerStateStore(identities,slots.Capacity);
        var server=new ServerPlayerAuthority(states,identities);var created=server.Create(new("test:closest-social"),630,439);Assert.True(created.IsCreated);
        f.Schedule.SetSocialContext(f.World,f.Players,serverPlayers:server);var before=f.Current(0);
        Assert.Equal(1,f.Schedule.Tick(in f.Conditions,[],f.Status,f.Combat).RejectedCommits);Assert.Equal(before,f.Current(0));
        Assert.DoesNotContain(Drain(f.Peer),x=>x[2]==91);Assert.Equal(new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32()).Next(),f.Random.Next());
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly RuntimeNpcReplicationRegistry Registry = new();
        internal readonly RuntimeConnectionRegistry PlayerRegistry = new();
        internal readonly PlayerSlotPool Pool = new(1);
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
        internal Fixture(JsonElement row)
        {
            Npcs = new(commitSink: Registry);
            Tiles = new WorldTileStore(new(1000, 500)); var tiles=Tiles;
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
            Status = new(Npcs); Status.BeginWorldTick();
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
            var zones=new PlayerZoneSnapshot1458(row.GetProperty("z1").GetByte(),row.GetProperty("z2").GetByte(),0,0,0,0);
            State.Apply(new PlayerZonesRuntimeCommand(Connection,zones));Drain(owner);Drain(Peer);
            Schedule.SetSocialContext(World,Players);
            Combat = new(town,Npcs,new RuntimeProjectileStore(32),tiles,default,new(),false,false,
                new NpcRuntimeTownCombatRandom1458(adapter),Registry);
        }
        public void Dispose()=>Session.Dispose();
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
