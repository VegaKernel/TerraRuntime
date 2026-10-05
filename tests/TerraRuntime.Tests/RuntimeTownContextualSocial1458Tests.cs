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

namespace TerraRuntime.Tests;

public sealed class RuntimeTownContextualSocial1458Tests
{
    public static IEnumerable<object[]> SourceCases() => Rows("TownContextualSocial1458").Where(x => !((JsonElement)x[0]).TryGetProperty("selection",out _));
    public static IEnumerable<object[]> SelectionCases() => Rows("TownContextualSocial1458").Where(x => ((JsonElement)x[0]).TryGetProperty("selection",out _));
    public static IEnumerable<object[]> MetadataCases() => Rows("TownSocialMetadata1458");
    private static IEnumerable<object[]> Rows(string name)
    {
        using var source = typeof(RuntimeTownContextualSocial1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.Clone()];
    }

    [Theory, MemberData(nameof(MetadataCases))]
    public void All_source_default_boss_flags_and_face_emotes_match_independent_original(JsonElement row)
    {
        Assert.True(VanillaNpcSourceMetadata1458.TryGet(new(row.GetProperty("type").GetInt32()), out bool boss, out byte face));
        Assert.Equal(row.GetProperty("boss").GetBoolean(), boss);
        Assert.Equal(row.GetProperty("face").GetInt32(), face);
    }

    [Theory, MemberData(nameof(SourceCases))]
    public void Real_two_resident_phases_match_original_emitter_lifetime_wire_and_next_rng_or_reject_unknown_context(JsonElement row)
    {
        var f = new Fixture(row);
        var first = Endpoint(f.Registry, 30201, 0);
        var second = Endpoint(f.Registry, 30202, 1);
        var waiting = Endpoint(f.Registry, 30203, 2, false);
        Drain(first); Drain(second);
        NpcSnapshot before = f.Current(0);
        var summary = f.Schedule.Tick(in f.Conditions, [], f.Status, f.Combat);
        byte[][] frames = Drain(first), peers = Drain(second);
        Assert.Equal(frames.Length, peers.Length);
        for (int i = 0; i < frames.Length; i++) Assert.Equal(frames[i], peers[i]);
        if (row.GetProperty("admitted").GetBoolean())
        {
            Assert.Equal(0, summary.RejectedCommits);
            byte[][] expected = row.GetProperty("frames").EnumerateArray().Select(x => Convert.FromHexString(x.GetString()!)).Where(x => x[2] == 91).ToArray();
            byte[][] actual = frames.Where(x => x[2] == 91).ToArray();
            Assert.Single(actual);
            Assert.Equal(Assert.Single(expected), actual[0]);
            Assert.Equal(91, frames[0][2]);
            Assert.Equal(row.GetProperty("clock").GetDouble(), f.Current(0).Simulation.FrameCounter);
            Assert.Equal(row.GetProperty("peerClock").GetDouble(), f.Current(1).Simulation.FrameCounter);
            Assert.Equal(299f, f.Current(0).Ai.Ai1);
            Assert.Equal(299f, f.Current(1).Ai.Ai1);
            Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Next());
        }
        else
        {
            Assert.Equal(1, summary.RejectedCommits);
            Assert.Equal(before, f.Current(0));
            Assert.DoesNotContain(frames, x => x[2] == 91);
            Assert.Equal(new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32()).Next(), f.Random.Next());
            Assert.Equal(299f, f.Current(1).Ai.Ai1);
        }
        Assert.Empty(Drain(waiting));
        Assert.DoesNotContain(Drain(Endpoint(f.Registry, 30204, 3)), x => x[2] == 91);
    }

    [Theory]
    [InlineData("clock")]
    [InlineData("progression")]
    [InlineData("revisionCapacity")]
    public void Context_changed_or_revision_exhausted_rejects_without_emote_id_rng_or_actor_mutation(string fault)
    {
        using var document = JsonDocument.Parse("{\"actorType\":17,\"peerType\":19,\"frame\":215,\"seed\":120,\"boss\":false}");
        var f = new Fixture(document.RootElement);
        var queue = Endpoint(f.Registry, 30211, 0); Drain(queue);
        var clock = new RuntimeWorldClock(1000, true, (VanillaMoonPhase)0, 0, 1);
        var progression = new RuntimeWorldProgressionMutations();
        f.Schedule.SetSocialContext(f.World, null, clock, progression);
        if (fault == "clock") clock.Tick();
        if (fault == "progression") progression.MarkCompleted(VanillaWorldProgressionId.EyeOfCthulhu);
        if (fault == "revisionCapacity")
        {
            var slots = (Array)typeof(RuntimeNpcStore).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Npcs)!;
            object slot = slots.GetValue(0)!;
            slot.GetType().GetField("Revision", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(slot, ulong.MaxValue);
            slots.SetValue(slot, 0);
        }
        var before = f.Current(0);
        Assert.Equal(1, f.Schedule.Tick(in f.Conditions, [], f.Status, f.Combat).RejectedCommits);
        Assert.Equal(before, f.Current(0));
        Assert.DoesNotContain(Drain(queue), x => x[2] == 91);
        Assert.Equal(new VanillaUnifiedRandom1458(120).Next(), f.Random.Next());
    }

    [Theory, MemberData(nameof(SelectionCases))]
    public void Original_progression_tiers_weighted_candidates_and_real_Next1_draws_match(JsonElement row)
    {
        var f=new Fixture(row);NpcSnapshot a=f.Current(0),b=f.Current(1);
        if(row.TryGetProperty("sourceExpert",out var expert)) Assert.Equal(expert.GetBoolean(),f.World.ExpertMode);
        var actor=new NpcStateUpdate(a.Type,a.NetId,a.PositionX,a.PositionY,a.VelocityX,a.VelocityY,a.Target,a.Ai,a.Simulation);
        var peer=new NpcStateUpdate(b.Type,b.NetId,b.PositionX,b.PositionY,b.VelocityX,b.VelocityY,b.Target,b.Ai,b.Simulation);
        Span<NpcSnapshot> peers=stackalloc NpcSnapshot[RuntimeNpcStore.MaximumAddressableCapacity];int count=f.Npcs.CopyActive(peers);
        Assert.True(f.Schedule.TryContextualEmote(in actor,in peer,peers[..count],out byte emote));
        byte[] original=Convert.FromHexString(row.GetProperty("frames")[0].GetString()!);
        Assert.Equal(original[^1],emote);Assert.Equal(row.GetProperty("next").GetInt32(),f.Random.Next());
    }

    [Fact]
    public void Unknown_metadata_identity_rejects_instead_of_using_a_role_guess()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NpcTypeId(0));
        Assert.False(VanillaNpcSourceMetadata1458.TryGet(new(697), out _, out _));
    }

    [Theory]
    [InlineData(69,false)] [InlineData(215,false)] [InlineData(319,false)]
    [InlineData(69,true)] [InlineData(215,true)] [InlineData(319,true)]
    public void Standard_world_tick_reaches_contextual_emotes_once_before_actor_publication(int frame,bool boss)
    {
        JsonElement row=(JsonElement)SourceCases().Single(x => {
            var r=(JsonElement)x[0];return r.GetProperty("actorType").GetInt32()==17 && r.GetProperty("peerType").GetInt32()==19 &&
                r.GetProperty("frame").GetInt32()==frame && r.GetProperty("boss").GetBoolean()==boss && r.GetProperty("seed").GetInt32()==120;
        })[0];
        var f=new Fixture(row);
        var runtime=new ServerRuntimeState(npcs:f.Npcs,npcAiStepper:new NoNpcStep(),worldTiles:f.Tiles,townNpcs:f.Town,
            npcReplication:f.Registry,naturalSpawnRandom:new SystemVanillaNpcRandom(f.Random),townSocialWorldFacts:f.World);
        var queue=Endpoint(f.Registry,30231,0);Drain(queue);runtime.Tick();
        byte[][] actual=Drain(queue);Assert.Equal(91,actual[0][2]);
        byte[] original=Convert.FromHexString(row.GetProperty("frames")[0].GetString()!);
        Assert.Equal(original,Assert.Single(actual,x=>x[2]==91));
        Assert.Equal(299f,f.Current(0).Ai.Ai1);Assert.Equal(299f,f.Current(1).Ai.Ai1);
        Assert.Equal(frame+1,f.Current(0).Simulation.FrameCounter);Assert.Equal(frame+1,f.Current(1).Simulation.FrameCounter);
        Assert.Equal(row.GetProperty("next").GetInt32(),f.Random.Next());
    }
    private sealed class NoNpcStep : INpcAiStateStepper
    { public bool TryStepState(in NpcSnapshot npc,out NpcStateUpdate next){next=default;return false;} }

    private sealed class Fixture
    {
        internal readonly RuntimeNpcReplicationRegistry Registry = new();
        internal readonly RuntimeNpcStore Npcs;
        internal readonly WorldTileStore Tiles;
        internal readonly RuntimeTownNpcStateStore Town;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeTownNpcSchedule1458 Schedule;
        internal readonly RuntimeTownNpcCombat1458 Combat;
        internal readonly RuntimeNpcBuffStatus1458 Status;
        internal readonly RuntimeTownNpcScheduleConditions1458 Conditions = new(true, false, false, false, false);
        internal readonly RuntimeTownSocialWorld1458 World = RuntimeTownSocialWorld1458.FromMetadata(
            new WorldFileRuntimeMetadata { DayTime = true, Time = 1000, WorldSurface = 40, RockLayer = 0 }, false);
        internal Fixture(JsonElement row)
        {
            if(row.TryGetProperty("selection",out _))
            {
                int stage=row.GetProperty("stage").GetInt32();bool day=row.GetProperty("day").GetBoolean();
                var m=new WorldFileRuntimeMetadata {DayTime=day,Time=1000,BloodMoon=!day,WorldSurface=40,RockLayer=0,
                    GetGoodWorld=row.TryGetProperty("goodWorld",out var good) && good.GetBoolean(),
                    Crimson=row.GetProperty("crimson").GetBoolean(),HardMode=stage>=4,DownedBoss1=true,DownedBoss2=stage>=2,DownedBoss3=stage>=3,
                    DownedMechBossAny=stage>=5,DownedMechBoss1=stage>=5,DownedMechBoss2=stage>=5,DownedMechBoss3=stage>=5,
                    DownedPlantBoss=stage>=6,DownedGolemBoss=stage>=7,DownedAncientCultist=stage>=8,
                    DownedMoonlord=row.GetProperty("moonlord").GetBoolean(),DownedGoblins=stage>=2,DownedFrost=stage>=3,
                    DownedPirates=stage>=4,DownedMartians=stage>=5,DownedChristmasIceQueen=stage>=6,DownedChristmasSantank=stage>=6,
                    DownedChristmasTree=stage>=6,DownedHalloweenKing=stage>=7,DownedHalloweenTree=stage>=7,
                    DownedEmpressOfLight=stage>=8,DownedQueenSlime=stage>=4,DownedDeerclops=stage>=3};
                World=RuntimeTownSocialWorld1458.FromMetadata(m,m.Crimson);
            }
            Npcs = new(commitSink: Registry);
            Tiles = new WorldTileStore(new(100, 80)); var tiles=Tiles;
            for (int x = 0; x < 100; x++) tiles.Set(x, 30, new() { Type = 1, Flags = WorldTileFlags.Active });
            int actor = row.GetProperty("actorType").GetInt32(), peer = row.GetProperty("peerType").GetInt32();
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
            if (row.TryGetProperty("boss",out var boss) && boss.GetBoolean())
            {
                var enemy = new NpcStateUpdate(50,50,4639,440,0,0,255,default,
                    NpcSimulationState.Initial with {Life=100,LifeMax=100,Friendly=false,DamageOverride=0,HitboxOverride=new(18,40)});
                Assert.True(Npcs.TrySpawn(2,in enemy,out _));
            }
            Random = new(row.GetProperty("seed").GetInt32()); var adapter = new SystemVanillaNpcRandom(Random);
            Status = new(Npcs); Status.BeginWorldTick();
            Schedule = new(town,Npcs,tiles,new NpcRuntimeTownScheduleRandom1458(adapter));
            Schedule.SetSocialContext(World,null);
            Combat = new(town,Npcs,new RuntimeProjectileStore(32),tiles,default,new(),false,false,
                new NpcRuntimeTownCombatRandom1458(adapter),Registry);
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
