using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class NpcTransformInteractions1458Tests
{
    public static IEnumerable<object[]> OriginalFrogs() => Cases("MysticFrogCatchLinux1458.v1");
    public static IEnumerable<object[]> WarmFrogs() => Cases("MysticFrogCatchWarmLinux1458.v1");
    public static IEnumerable<object[]> OriginalTerminals() => Cases("MysticFrogCatchTerminalLinux1458.v1");
    public static IEnumerable<object[]> OriginalPowders() => Cases("PowderTransformBatchLinux1458.v2");
    private static IEnumerable<object[]> Cases(string resource)
    {
        using var stream = typeof(NpcTransformInteractions1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var capture in document.RootElement.GetProperty("captures").EnumerateArray())
            foreach (var row in capture.GetProperty("cases").EnumerateArray())
                yield return [capture.GetProperty("seed").GetInt32(), row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalFrogs)), MemberData(nameof(WarmFrogs))]
    public void Real_catch_matches_original_search_visual_offers_teleport_carrier_and_literal_23_65(int seed, string json)
    {
        using var d = JsonDocument.Parse(json);
        var component = d.RootElement.GetProperty("component");
        var before = component.GetProperty("before"); var after = component.GetProperty("after");
        bool warmed = component.TryGetProperty("warmup", out var warmup);
        using var f = new Fixture(seed, [warmed ? warmup[0].GetProperty("before").GetProperty("npc") : before.GetProperty("npc")],
            clock: warmed ? new RuntimeWorldClock(0,false,0,0,0) : null);
        if (warmed)
        {
            foreach (var step in warmup.EnumerateArray())
            {
                NonresidentBunnyNpcPhase1458.Tick(f.World);
                GoldCritterHitEffect1458Tests.AssertRandom(step.GetProperty("after").GetProperty("rng"), f.Random);
            }
            GemBunnyWire1458Tests.Drain(f.Outbound);
        }
        AssertProjection(before.GetProperty("npc"), f.Current(0));
        GoldCritterHitEffect1458Tests.AssertRandom(before.GetProperty("rng"), f.Random);
        f.World.Apply(new ClientNpcCatchRuntimeCommand(f.Connection, new(f.Actors[0].Handle.Slot)));
        AssertProjection(after.GetProperty("npc"), f.Current(0));
        GoldCritterHitEffect1458Tests.AssertRandom(after.GetProperty("rng"), f.Random);
        Assert.Equal(HexFrames(after).Skip(warmed ? before.GetProperty("notifications").GetArrayLength() : 0).ToArray(),
            GemBunnyWire1458Tests.Drain(f.Outbound).Select(Convert.ToHexString).ToArray());
        Assert.False(f.Npcs.HasPendingBirth(f.Actors[0].Handle));
    }

    [Theory, MemberData(nameof(OriginalTerminals))]
    public void Blocked_hundred_attempt_catches_including_statue_match_original_inactive23_smoke106_and_full_rng(int seed,string json)
    {
        using var d=JsonDocument.Parse(json);var c=d.RootElement.GetProperty("component");
        var before=c.GetProperty("before");var after=c.GetProperty("after");
        using var f=new Fixture(seed,[before.GetProperty("npc")],blocked:true);
        AssertProjection(before.GetProperty("npc"),f.Current(0));
        Assert.Equal(before.GetProperty("npc").GetProperty("spawnedFromStatue").GetBoolean(),f.Current(0).Simulation.SpawnedFromStatue);
        GoldCritterHitEffect1458Tests.AssertRandom(before.GetProperty("rng"),f.Random);
        Assert.True(f.Status.CaptureInteraction(in f.Actors[0],out _));
        f.World.Apply(new ClientNpcCatchRuntimeCommand(f.Connection,new(f.Actors[0].Handle.Slot)));
        Assert.False(after.GetProperty("npc").GetProperty("active").GetBoolean());
        Assert.False(f.Npcs.TryGet(f.Actors[0].Handle,out _));
        Assert.False(f.Status.CaptureInteraction(in f.Actors[0],out _));
        GoldCritterHitEffect1458Tests.AssertRandom(after.GetProperty("rng"),f.Random);
        Assert.Equal(HexFrames(after),GemBunnyWire1458Tests.Drain(f.Outbound).Select(Convert.ToHexString).ToArray());
        Assert.Empty(GemBunnyWire1458Tests.Drain(GemBunnyWire1458Tests.Endpoint(f.Registry,9763,2)));
        var accepted=f.Random.Clone();
        f.World.Apply(new ClientNpcCatchRuntimeCommand(f.Connection,new(f.Actors[0].Handle.Slot)));
        Assert.True(accepted.HasSameState(f.Random));Assert.Empty(GemBunnyWire1458Tests.Drain(f.Outbound));
    }

    [Fact]
    public void Terminal_catch_retirement_is_visible_before_observers_and_new_join_cannot_receive_old_smoke_tail()
    {
        using var d=First(OriginalTerminals());var c=d.RootElement.GetProperty("component");
        using var f=new Fixture(1458,[c.GetProperty("before").GetProperty("npc")],blocked:true);
        TerrariaConnectionOutboundQueue? joined=null;int observed=0;
        f.Observer.OnCatch=()=> {
            observed++;Assert.False(f.Npcs.TryGet(f.Actors[0].Handle,out _));
            Assert.False(f.Status.CaptureInteraction(in f.Actors[0],out _));
            GoldCritterHitEffect1458Tests.AssertRandom(c.GetProperty("after").GetProperty("rng"),f.Random);
            joined=GemBunnyWire1458Tests.Endpoint(f.Registry,9764,2);
            Assert.False(f.Frog.TryApply(f.Actors[0].Handle,out _));
        };
        Assert.True(f.Frog.TryApply(f.Actors[0].Handle,out bool teleported));Assert.False(teleported);Assert.Equal(1,observed);
        Assert.Empty(GemBunnyWire1458Tests.Drain(joined!));
        Assert.Equal(new byte[]{23,106},GemBunnyWire1458Tests.Drain(f.Outbound).Select(v=>v[2]).ToArray());
    }

    [Theory, MemberData(nameof(OriginalPowders))]
    public void One_powder_rectangle_adopts_both_physical_slots_names_status_and_source_rng_before_publication(int seed, string json)
    {
        using var d = JsonDocument.Parse(json);
        var component = d.RootElement.GetProperty("component");
        var before = component.GetProperty("before").EnumerateArray().Select(v => v.GetProperty("state")).ToArray();
        var after = component.GetProperty("after").EnumerateArray().Select(v => v.GetProperty("state")).ToArray();
        using var f = new Fixture(seed, before.Select(v => v.GetProperty("npc")).ToArray(), withWorldInfo: true);
        for (int i = 0; i < before.Length; i++) AssertProjection(before[i].GetProperty("npc"), f.Current(i));
        GoldCritterHitEffect1458Tests.AssertRandom(before[0].GetProperty("rng"), f.Random);
        int callbacks = 0;
        f.Observer.OnHistorical = (_, _) => {
            callbacks++;
            // BOTH slots, names, progression and the complete cursor are already adopted at the first observer.
            for (int i = 0; i < after.Length; i++) AssertTransformed(after[i].GetProperty("npc"), f.Current(i), f.Town);
            GoldCritterHitEffect1458Tests.AssertRandom(after[0].GetProperty("rng"), f.Random);
        };
        Assert.True(f.Powder.TryApply(10, 1020, 1540, 100, 100, null, out int transformed));
        Assert.Equal(2, transformed); Assert.Equal(2, callbacks);
        var actual = GemBunnyWire1458Tests.Drain(f.Outbound);
        var source = after[0].GetProperty("notifications").EnumerateArray().Select(v => Convert.FromHexString(v.GetString()!)).ToArray();
        Assert.Equal(source.Select(v => v[2]).ToArray(), actual.Select(v => v[2]).ToArray());
        // Packet7 contains fixture-specific world identity/clock. All source NPC/home/buff/smoke frames remain literal.
        Assert.Equal(source.Where(v => v[2] != 7).Select(Convert.ToHexString).ToArray(),
            actual.Where(v => v[2] != 7).Select(Convert.ToHexString).ToArray());
        Assert.DoesNotContain(actual, v => v[2] == 56);
        bool unlocked = after[0].GetProperty("world").GetProperty("unlockedSlimeYellow").GetBoolean();
        Assert.Equal(unlocked, f.Progression.IsSlimeYellowSpawnUnlocked);
        Assert.Equal(RuntimeTownRescueFacts1458.None, f.Progression.CaptureSnapshot().RescuedTownNpcs & RuntimeTownRescueFacts1458.TaxCollector);
        if (unlocked) Assert.Equal(f.Bootstrap!.CaptureWorldBootstrap().WorldInfoFrame.ToArray(), Assert.Single(actual, v => v[2] == 7));
    }

    [Fact]
    public void A_join_inside_historical_catch_observer_gets_final_position_and_no_old_teleport_tail()
    {
        using var d = First(OriginalFrogs()); var before = d.RootElement.GetProperty("component").GetProperty("before");
        using var f = new Fixture(1458, [before.GetProperty("npc")]);
        TerrariaConnectionOutboundQueue? joined = null;
        f.Observer.OnHistorical = (checkpoint, committed) => {
            Assert.NotEqual(checkpoint.PositionX, committed.PositionX);
            Assert.Equal(committed, f.Current(0));
            joined = GemBunnyWire1458Tests.Endpoint(f.Registry, 9761, 2);
        };
        Assert.True(f.Frog.TryApply(f.Actors[0].Handle, out bool teleported)); Assert.True(teleported);
        var late = GemBunnyWire1458Tests.Drain(joined!);
        byte[] state = Assert.Single(late, frame => frame[2] == 23);
        Assert.Equal(f.Current(0).PositionX, BitConverter.ToSingle(state, 5));
        Assert.Equal(f.Current(0).PositionY, BitConverter.ToSingle(state, 9));
        Assert.DoesNotContain(late, frame => frame[2] == 65);
        Assert.Equal(new byte[] { 23,65 }, GemBunnyWire1458Tests.Drain(f.Outbound).Select(v => v[2]).ToArray());
    }

    [Fact]
    public void Replacing_generation_inside_historical_head_prevents_its_old_65_tail_and_reentrant_catch()
    {
        using var d = First(OriginalFrogs()); var before = d.RootElement.GetProperty("component").GetProperty("before");
        using var f = new Fixture(1458, [before.GetProperty("npc")]);
        NpcSnapshot replacement = default;
        f.Observer.OnHistorical = (checkpoint, accepted) => {
            Assert.False(f.Frog.TryApply(accepted.Handle, out _));
            Assert.True(f.Npcs.TryDespawn(accepted.Handle));
            var update = new NpcStateUpdate(46,46,800,400,0,0,255,default,NpcSimulationState.Initial);
            Assert.True(f.Npcs.TrySpawn(accepted.Handle.Slot, in update, out replacement));
        };
        Assert.True(f.Frog.TryApply(f.Actors[0].Handle, out bool teleported)); Assert.True(teleported);
        Assert.True(replacement.Handle.IsAssigned); Assert.NotEqual(f.Actors[0].Handle, replacement.Handle);
        Assert.DoesNotContain(GemBunnyWire1458Tests.Drain(f.Outbound), frame => frame[2] == 65);
        GoldCritterHitEffect1458Tests.AssertRandom(d.RootElement.GetProperty("component").GetProperty("after").GetProperty("rng"), f.Random);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Real_caller_mutation_refuses_frog_adoption_and_does_not_spend_staged_offers(int mutation)
    {
        using var d = First(OriginalFrogs()); var before = d.RootElement.GetProperty("component").GetProperty("before");
        using var f = new Fixture(1458, [before.GetProperty("npc")]); var random = f.Random.Clone();
        bool called = false;
        bool Guard() {
            called = true;
            if (mutation == 0) f.Tiles.Set(64,100,new WorldTile { Type=2,Flags=WorldTileFlags.Active });
            else if (mutation == 1) f.MovePlayer(1400,1558);
            else f.Random.Next();
            return true;
        }
        Assert.False(f.Frog.TryApply(f.Actors[0].Handle, out _, Guard)); Assert.True(called);
        Assert.Equal(f.Actors[0],f.Current(0));
        if (mutation == 2) random.Next();
        Assert.True(random.HasSameState(f.Random)); Assert.Empty(GemBunnyWire1458Tests.Drain(f.Outbound));
    }

    [Fact]
    public void Powder_stale_later_slot_and_missing_world7_owner_cannot_partially_transform_the_first_slot()
    {
        using var d = First(OriginalPowders());
        var before = d.RootElement.GetProperty("component").GetProperty("before").EnumerateArray()
            .Select(v => v.GetProperty("state").GetProperty("npc")).ToArray();
        using (var unknown = new Fixture(1458,before)) {
            var random = unknown.Random.Clone();
            Assert.False(unknown.Powder.TryApply(10,1020,1540,100,100,null,out _));
            Assert.Equal(unknown.Actors[0],unknown.Current(0)); Assert.Equal(unknown.Actors[1],unknown.Current(1));
            Assert.True(random.HasSameState(unknown.Random)); Assert.Equal(0,unknown.Town.Count);
        }
        using var f = new Fixture(1458,before,withWorldInfo:true); var rng=f.Random.Clone();
        bool Guard() { Assert.True(f.Npcs.TryDespawn(f.Actors[1].Handle)); return true; }
        Assert.False(f.Powder.TryApply(10,1020,1540,100,100,Guard,out _));
        Assert.Equal(f.Actors[0],f.Current(0)); Assert.True(rng.HasSameState(f.Random));
        Assert.Equal(0,f.Town.Count); Assert.False(f.Progression.IsSlimeYellowSpawnUnlocked);
        Assert.DoesNotContain(GemBunnyWire1458Tests.Drain(f.Outbound), frame => frame[2] is 54 or 60 or 106 or 7);
    }

    [Fact]
    public void Powder_observer_join_sees_all_final_names_homes_and_buffs_without_receiving_the_old_sequence_tail()
    {
        using var d = First(OriginalPowders()); var c=d.RootElement.GetProperty("component");
        var input=c.GetProperty("before").EnumerateArray().Select(v=>v.GetProperty("state").GetProperty("npc")).ToArray();
        using var f=new Fixture(1458,input,withWorldInfo:true);
        TerrariaConnectionOutboundQueue? joined=null;
        f.Observer.OnHistorical=(_,_)=> { if(joined is not null)return; joined=GemBunnyWire1458Tests.Endpoint(f.Registry,9762,2); };
        Assert.True(f.Powder.TryApply(10,1020,1540,100,100,null,out int count));Assert.Equal(2,count);
        var frames=GemBunnyWire1458Tests.Drain(joined!);
        Assert.Equal(2,frames.Count(v=>v[2]==23)); Assert.Equal(2,frames.Count(v=>v[2]==56));
        Assert.Equal(2,frames.Count(v=>v[2]==60));
        Assert.DoesNotContain(frames,v=>v[2] is 106 or 7);
        foreach(var frame in frames.Where(v=>v[2]==23)) {
            int slot=frame[3];Assert.Equal(683,f.Current(slot).Type);
            Assert.Equal(f.Current(slot).PositionX,BitConverter.ToSingle(frame,5));
        }
    }

    private static JsonDocument First(IEnumerable<object[]> cases) => JsonDocument.Parse((string)cases.First()[1]);
    private static string[] HexFrames(JsonElement state) => state.GetProperty("notifications").EnumerateArray().Select(v=>v.GetString()!.ToUpperInvariant()).ToArray();
    private static float F(JsonElement value) => BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetString(),16));
    private static NpcAiState Ai(JsonElement array) { var v=array.EnumerateArray().Select(F).ToArray();return new(v[0],v[1],v[2],v[3]); }
    private static void AssertTransformed(JsonElement expected,NpcSnapshot actual,RuntimeTownNpcStateStore town)
    {
        AssertProjection(expected,actual);
        Assert.True(town.TryGet(actual.Handle.Slot,out var resident));
        Assert.Equal(expected.GetProperty("givenNameSha256").GetString(),Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resident.GivenName))).ToLowerInvariant());
        Assert.Equal(expected.GetProperty("townNpcVariationIndex").GetInt32(),resident.TownNpcVariationIndex);
    }
    private static void AssertProjection(JsonElement e,NpcSnapshot n)
    {
        Assert.Equal(e.GetProperty("type").GetInt32(),n.Type);Assert.Equal(e.GetProperty("netId").GetInt16(),n.NetId);
        Assert.Equal(F(e.GetProperty("position")[0]),n.PositionX);Assert.Equal(F(e.GetProperty("position")[1]),n.PositionY);
        Assert.Equal(F(e.GetProperty("velocity")[0]),n.VelocityX);Assert.Equal(F(e.GetProperty("velocity")[1]),n.VelocityY);
        Assert.Equal(Ai(e.GetProperty("ai")),n.Ai);Assert.Equal(Ai(e.GetProperty("localAi")),n.Simulation.LocalAi);
        Assert.Equal(e.GetProperty("target").GetUInt16(),n.Target);
        Assert.Equal(e.GetProperty("life").GetInt32(),n.Simulation.Life);Assert.Equal(e.GetProperty("lifeMax").GetInt32(),n.Simulation.LifeMax);
        Assert.Equal(e.GetProperty("homeX").GetInt32(),n.Simulation.HomeTileX);Assert.Equal(e.GetProperty("homeY").GetInt32(),n.Simulation.HomeTileY);
        Assert.Equal(e.GetProperty("homeless").GetBoolean(),n.Simulation.Homeless);
        Assert.Equal(e.GetProperty("direction").GetInt32(),n.Simulation.DirectionX);Assert.Equal(e.GetProperty("directionY").GetInt32(),n.Simulation.DirectionY);
        Assert.Equal(e.GetProperty("spriteDirection").GetInt32(),n.Simulation.SpriteDirection);
        Assert.Equal(F(e.GetProperty("teleportTime")),n.Simulation.TeleportTime);Assert.Equal(e.GetProperty("teleportStyle").GetInt32(),n.Simulation.TeleportStyle);
        Assert.Equal(e.GetProperty("wet").GetBoolean(),n.Simulation.Wet);
        Assert.Equal(e.GetProperty("breath").GetInt32(),n.Simulation.Breath);
        if(e.TryGetProperty("breathCounter",out var breathCounter))Assert.Equal(breathCounter.GetInt32(),n.Simulation.BreathCounter);
        Assert.Equal(e.GetProperty("wetCount").GetInt32(),n.Simulation.WetCount);
        Assert.Equal(e.GetProperty("honeyWet").GetBoolean(),n.Simulation.HoneyWet);Assert.Equal(e.GetProperty("lavaWet").GetBoolean(),n.Simulation.LavaWet);
        Assert.Equal(e.GetProperty("shimmerWet").GetBoolean(),n.Simulation.ShimmerWet);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly WorldTileStore Tiles=new(new WorldDimensions(192,200));
        internal readonly RuntimeNpcReplicationRegistry Registry=new();
        internal readonly Observer Observer=new();
        internal readonly RuntimeNpcStore Npcs;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeTownNpcStateStore Town=new(new WorldNpcPersistence([],[],[]),[],new WorldDimensions(192,200));
        internal readonly RuntimeWorldProgressionMutations Progression;
        internal readonly WorldRuntime? Bootstrap;
        internal readonly ServerRuntimeState World;
        internal readonly NpcSnapshot[] Actors;
        internal readonly RuntimeNpcBuffStatus1458 Status;
        internal readonly RuntimeMysticFrogCatchService1458 Frog;
        internal readonly RuntimePurificationPowderNpcInteraction1458 Powder;
        internal readonly ConnectionHandle Connection;
        internal readonly TerrariaConnectionOutboundQueue Outbound;
        private readonly PlayerJoinSession session;
        internal Fixture(int seed,JsonElement[] input,bool withWorldInfo=false,bool blocked=false,RuntimeWorldClock? clock=null,
            bool withHomeCommands=false)
        {
            Random=new(seed);Observer.Registry=Registry;Npcs=new(commitSink:Observer);
            Assert.True(Tiles.TryAttachWorldSurface(80));
            for(int y=0;y<200;y++)for(int x=0;x<192;x++)if(blocked||y==100)Tiles.Set(x,y,new WorldTile {Type=1,Flags=WorldTileFlags.Active});
            Actors=new NpcSnapshot[input.Length];
            for(int i=0;i<input.Length;i++) {
                var e=input[i];var s=NpcSimulationState.Initial with {
                    DirectionX=e.GetProperty("direction").GetInt32(),DirectionY=e.GetProperty("directionY").GetInt32(),SpriteDirection=e.GetProperty("spriteDirection").GetInt32(),
                    OldPositionX=F(e.GetProperty("oldPosition")[0]),OldPositionY=F(e.GetProperty("oldPosition")[1]),
                    OldVelocityX=F(e.GetProperty("oldVelocity")[0]),OldVelocityY=F(e.GetProperty("oldVelocity")[1]),
                    Life=e.GetProperty("life").GetInt32(),LifeMax=e.GetProperty("lifeMax").GetInt32(),TimeLeft=e.GetProperty("timeLeft").GetInt32(),
                    LocalAi=Ai(e.GetProperty("localAi")),HomeTileX=e.GetProperty("homeX").GetInt32(),HomeTileY=e.GetProperty("homeY").GetInt32(),Homeless=e.GetProperty("homeless").GetBoolean(),
                    FrameIndex=e.GetProperty("frameIndex").GetInt32(),FrameCounter=BitConverter.Int64BitsToDouble(Convert.ToInt64(e.GetProperty("frameCounter").GetString(),16)),
                    TeleportTime=F(e.GetProperty("teleportTime")),TeleportStyle=e.GetProperty("teleportStyle").GetInt32(),
                    StairFall=e.GetProperty("stairFall").GetBoolean(),ShimmerTransparency=F(e.GetProperty("shimmer")),
                    CollideX=e.GetProperty("collideX").GetBoolean(),CollideY=e.GetProperty("collideY").GetBoolean(),
                    Wet=e.GetProperty("wet").GetBoolean(),Breath=e.GetProperty("breath").GetInt32(),
                    BreathCounter=e.TryGetProperty("breathCounter",out var breathCounter)?breathCounter.GetInt32():null,
                    WetCount=e.GetProperty("wetCount").GetInt32(),HoneyWet=e.GetProperty("honeyWet").GetBoolean(),LavaWet=e.GetProperty("lavaWet").GetBoolean(),ShimmerWet=e.GetProperty("shimmerWet").GetBoolean(),
                    SpawnedFromStatue=e.GetProperty("spawnedFromStatue").GetBoolean(),
                    HitboxOverride=new(e.GetProperty("width").GetInt32(),e.GetProperty("height").GetInt32()),ReleaseOwner=255 };
                var update=new NpcStateUpdate(e.GetProperty("type").GetInt32(),e.GetProperty("netId").GetInt16(),F(e.GetProperty("position")[0]),F(e.GetProperty("position")[1]),F(e.GetProperty("velocity")[0]),F(e.GetProperty("velocity")[1]),e.GetProperty("target").GetUInt16(),Ai(e.GetProperty("ai")),s);
                Assert.True(Npcs.TrySpawnVanilla(in update,out Actors[i]));
                if(e.GetProperty("spawnNeedsSyncing").GetBoolean()) Assert.True(Npcs.TryRetainPendingBirth(in Actors[i]));
                Assert.Equal(e.GetProperty("spawnNeedsSyncing").GetBoolean(),Npcs.HasPendingBirth(Actors[i].Handle));
            }
            if(withWorldInfo)Bootstrap=CreateBootstrap();
            Progression=Bootstrap?.WorldProgression??new RuntimeWorldProgressionMutations(false);
            World=new(npcs:Npcs,worldTiles:Tiles,npcReplication:Registry,townNpcs:Town,worldProgression:Progression,worldClock:clock,
                townSpawnWorldFacts:withHomeCommands?default(VanillaTownSpawnWorldFacts1458):null,
                naturalSpawnRandom:new SystemVanillaNpcRandom(Random),worldItemSpawnRandom:new SystemWorldItemSpawnRandom(Random));
            var graph=(ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(World)!;
            Status=(RuntimeNpcBuffStatus1458)typeof(NpcAuthority).GetField("npcBuffStatus",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(graph.Npcs)!;
            Frog=(RuntimeMysticFrogCatchService1458)typeof(NpcAuthority).GetField("mysticFrogCatch",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(graph.Npcs)!;
            var town=(TownNpcAuthority)typeof(NpcAuthority).GetField("townNpcAuthority",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(graph.Npcs)!;
            Powder=(RuntimePurificationPowderNpcInteraction1458)typeof(TownNpcAuthority).GetField("purificationPowderInteractions",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(town)!;
            if(Bootstrap is not null)World.BindNpcInteractionWorldInfoSource(snapshot=>Bootstrap.PrepareNpcInteractionWorldInfo(in snapshot),Bootstrap.RefreshWorldBootstrap);
            var pool=new PlayerSlotPool(1);Assert.True(pool.TryAcquireConnection(out var lease));
            session=new(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));session.ObserveWorldRequest();session.ObserveSectionRequest();
            Connection=new(GameCommandSourceId.FromConnection(9750),session.Handle);
            World.Apply(new PlayerSpawnRuntimeCommand(Connection,session,new(session.Slot,74,100,0,0,0,0,0)));
            MovePlayer(1183,1558);
            Outbound=GemBunnyWire1458Tests.Endpoint(Registry,9751,1);GemBunnyWire1458Tests.Drain(Outbound);
        }
        internal void MovePlayer(float x,float y)=>World.Apply(new PlayerMovementRuntimeCommand(Connection,new(session.Slot,0,0,0,0,0,x,y,false,0,0,false,0,false,0,0,0,0,false,0,0)));
        internal NpcSnapshot Current(int slot){Assert.True(Npcs.TryGet(Actors[slot].Handle,out var value));return value;}
        public void Dispose(){session.Dispose();Bootstrap?.Dispose();}
        private static WorldRuntime CreateBootstrap()
        {
            var source=new SandboxWorldSource.Generated(FlatProvider.GeneratorId,"Interaction witness",1458,192,200,WorldGenerationOptions.Default);
            var materialized=new SandboxWorldMaterializer(BuiltInWorldGeneratorSource.Instance,ServerWorldLoadPolicy.CreateLimits()).Materialize(source,CancellationToken.None);
            Assert.True(materialized.Succeeded,materialized.Error);
            var world=materialized.World! with {RuntimeMetadata=new WorldFileRuntimeMetadata {SpawnX=74,SpawnY=100,WorldSurface=80,RockLayer=120,DayTime=false}};
            return new(new(WorldRuntimeId.CreateNew(),WorldSessionId.CreateNew()),source,world,materialized.Bootstrap!,new InterestManagementControl(),new WorldRuntimeOptions {MaxPlayers=2});
        }
    }
    private sealed class Observer:INpcStateCommitSink
    {
        internal RuntimeNpcReplicationRegistry Registry=null!;
        internal Action<NpcSnapshot,NpcSnapshot>? OnHistorical;
        internal Action? OnCatch;
        public void NpcStateCommitted(NpcStateCommitKind kind,in NpcSnapshot npc)=>Registry.NpcStateCommitted(kind,in npc);
        public void NpcVanillaAiStateCommitted(NpcStateCommitKind kind,in NpcSnapshot npc,bool lowPriority=false)=>Registry.NpcVanillaAiStateCommitted(kind,in npc,lowPriority);
        public void NpcHistoricalStateCommitted(NpcStateCommitKind kind,in NpcSnapshot checkpoint,in NpcSnapshot committed)
        {Registry.NpcHistoricalStateCommitted(kind,in checkpoint,in committed);OnHistorical?.Invoke(checkpoint,committed);}
        public void NpcCatchCommitted(in NpcSnapshot npc,bool needsSync) {Registry.NpcCatchCommitted(in npc,needsSync);OnCatch?.Invoke();}
    }
}
