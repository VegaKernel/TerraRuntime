using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Tests.Larva1458;
using TerraRuntime.World;
using Xunit;

namespace TerraRuntime.Tests.PreparedToolsNpcWet1458;

public sealed class PreparedToolsNpcWetTests
{
    [Fact]
    public void Source_birth_wet_reads_staged_terrain_before_joint_adoption()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var raw = typeof(PreparedToolsNpcWetTests).Assembly.GetManifestResourceStream($"PreparedToolNpcWet{platform}1458")!;
            using var gzip = new GZipStream(raw, CompressionMode.Decompress);
            using var data = JsonDocument.Parse(gzip);
            foreach (var row in data.RootElement.EnumerateArray()) Replay(row);
        }
        OrderedAfterimages();
        StaleTerrain();
        ProviderGuards();
        ExistingSpawnPaths();
    }

    private static void Replay(JsonElement row)
    {
        int type = row.GetProperty("type").GetInt32();
        if (type == 222) { QueenBeeSkip(row); return; }
        using var f = new LarvaRuntimeFixture1458(true);
        string terrain = row.GetProperty("terrain").GetString()!;
        bool full = row.GetProperty("table").GetString() == "full";
        if (full) Fill(f.Npcs);
        SetTerrain(f.Tiles, terrain == "staged-water" ? "dry" : terrain == "staged-dry" ? "water" : terrain);
        f.Random.CopyStateFrom(ReadRandom(row.GetProperty("beforeRandom")));
        var before = f.Random.Clone();
        var overlay = new ToolWorldTileOverlay1458(f.Tiles);
        if (terrain.StartsWith("staged-", StringComparison.Ordinal))
            StageTerrain(overlay, terrain[7..]);
        Assert.True(f.Composition.WorldTileAuthority.TryCreateToolMutationPlan(overlay, out var plan));
        using (plan!)
        {
            var births = new List<NpcSnapshot>();
            foreach (var expected in row.GetProperty("results").EnumerateArray())
            {
                var intent = new NpcAiSpawnIntent(new(type), 800, 800, 0, 0, 255);
                Assert.True(plan!.TryStageSourceNpc(in intent, out bool allocated, out var birth));
                Assert.Equal(!full, allocated);
                if (allocated)
                {
                    Compare(expected.GetProperty("npc"), birth);
                    Assert.True(plan.TryFinishSourceNpc(in birth, 0, 0, default, false, birth.Simulation.Wet, out var finished));
                    Assert.Equal(birth.Simulation.Wet, finished.Simulation.Wet);
                    births.Add(finished);
                }
            }
            Assert.True(f.Random.HasSameState(before));
            Assert.True(plan!.SourceRandom.HasSameState(ReadRandom(row.GetProperty("afterRandom"))));
            Assert.True(plan.TrySeal());
            var end = plan.CapturePublicationOffset();
            int callbacks = 0;
            f.NpcSink.Before = (_, _) =>
            {
                callbacks++;
                foreach (var staged in births)
                {
                    Assert.True(f.Npcs.TryGet(staged.Handle, out var adopted));
                    Assert.Equal(staged.Simulation.Wet, adopted.Simulation.Wet);
                }
                Assert.True(f.Random.HasSameState(ReadRandom(row.GetProperty("afterRandom"))));
            };
            Assert.True(plan.TryAdoptWholeUnpublished(out var publication));
            Assert.Equal(0, callbacks);
            Assert.NotNull(publication);
            Assert.True(publication!.TryPublishThrough(in end));
            Assert.Equal(births.Count, callbacks);
            Assert.True(publication.TryPublishThrough(in end));
            Assert.Equal(births.Count, callbacks);
            Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Clone().Next());
        }
    }

    private static void Compare(JsonElement expected, in NpcSnapshot birth)
    {
        Assert.Equal(expected.GetProperty("slot").GetByte(), birth.Handle.Slot);
        Assert.Equal(expected.GetProperty("type").GetInt32(), birth.Type);
        Assert.Equal(expected.GetProperty("netID").GetInt32(), birth.NetId);
        Assert.Equal(expected.GetProperty("wet").GetBoolean(), birth.Simulation.Wet);
        Assert.Equal(expected.GetProperty("noTileCollide").GetBoolean(), birth.Simulation.NoTileCollide);
        Assert.Equal(expected.GetProperty("life").GetInt32(), birth.Simulation.Life);
        Assert.Equal(expected.GetProperty("timeLeft").GetInt32(), birth.Simulation.TimeLeft);
        Assert.Equal(expected.GetProperty("position").GetProperty("x").GetUInt32(), BitConverter.SingleToUInt32Bits(birth.PositionX));
        Assert.Equal(expected.GetProperty("position").GetProperty("y").GetUInt32(), BitConverter.SingleToUInt32Bits(birth.PositionY));
    }

    private static void QueenBeeSkip(JsonElement row)
    {
        var random = ReadRandom(row.GetProperty("beforeRandom"));
        var store = Store(random);
        if (row.GetProperty("table").GetString() == "full") Fill(store);
        int queries = 0;
        Assert.True(store.TryPrepareWorldSpawns(random, random.Clone(), (_, _, _, _) => { queries++; return true; }, out var prep));
        Assert.True(prep!.TryStageLarvaBoss(800, 800, 0, 1, out bool allocated, out var birth));
        Assert.Equal(row.GetProperty("table").GetString() != "full", allocated);
        Assert.Equal(0, queries);
        if (allocated)
        {
            Assert.True(birth.Simulation.NoTileCollide);
            Assert.False(birth.Simulation.Wet);
            Assert.False(row.GetProperty("results")[0].GetProperty("npc").GetProperty("wet").GetBoolean());
        }
    }

    private static void OrderedAfterimages()
    {
        using var f = new LarvaRuntimeFixture1458(true);
        var overlay = new ToolWorldTileOverlay1458(f.Tiles);
        Assert.True(f.Composition.WorldTileAuthority.TryCreateToolMutationPlan(overlay, out var plan));
        using (plan!)
        {
            StageTerrain(overlay, "water");
            var intent = new NpcAiSpawnIntent(new(46), 800, 800, 0, 0, 255);
            Assert.True(plan!.TryStageSourceNpc(in intent, out bool allocated, out var wet));
            Assert.True(allocated); Assert.True(wet.Simulation.Wet);
            StageTerrain(overlay, "dry");
            Assert.True(plan.TryStageSourceNpc(in intent, out allocated, out var dry));
            Assert.True(allocated); Assert.False(dry.Simulation.Wet);
            Assert.True(plan.TrySeal());
            var end = plan.CapturePublicationOffset();
            int callbacks = 0;
            Assert.True(plan.TryAdoptWholeUnpublished(out var publication));
            Assert.Equal(0,f.Tiles.Get(49,49).LiquidAmount);
            Assert.Equal(0,callbacks);
            // A callback can edit terrain after the committed birth; it cannot recompute the retained wet result.
            f.NpcSink.Before = (_, _) => { callbacks++; Assert.True(f.Npcs.TryGet(wet.Handle, out var first)); Assert.True(first.Simulation.Wet); f.Tiles.Set(49,49,new WorldTile { LiquidAmount=255 }); };
            Assert.True(publication!.TryPublishThrough(in end));
            Assert.Equal(2, callbacks);
        }
    }

    private static void StaleTerrain()
    {
        using var f = new LarvaRuntimeFixture1458(true);
        SetTerrain(f.Tiles, "water");
        var before = f.Random.Clone();
        var overlay = new ToolWorldTileOverlay1458(f.Tiles);
        Assert.True(f.Composition.WorldTileAuthority.TryCreateToolMutationPlan(overlay, out var plan));
        using (plan!)
        {
            var intent = new NpcAiSpawnIntent(new(46), 800, 800, 0, 0, 255);
            Assert.True(plan!.TryStageSourceNpc(in intent, out _, out _));
            f.Tiles.Set(49, 49, default);
            Assert.False(plan.IsCurrentOwned);
            Assert.False(plan.TrySeal());
            Assert.False(plan.TryAdoptWholeUnpublished(out _));
            Assert.Equal(0, f.Npcs.ActiveCount);
            Assert.True(f.Random.HasSameState(before));
        }
    }

    private static void ProviderGuards()
    {
        foreach (string mutation in new[] { "npc", "live-rng", "working-rng", "provider" })
        {
            var live = new VanillaUnifiedRandom1458(7); var working = live.Clone();
            var store = Store(live); var intent = new NpcAiSpawnIntent(new(46), 800, 800, 0, 0, 255);
            Assert.True(store.TryPrepareWorldSpawns(live, working, (_, _, _, _) =>
            {
                if (mutation == "npc") Assert.True(store.TrySpawnVanilla(new NpcStateUpdate(1,1,0,0,0,0,255,default,NpcSimulationState.Initial), out _));
                if (mutation == "live-rng") live.Next();
                if (mutation == "working-rng") working.Next();
                if (mutation == "provider") store.SetVanillaSpawnWetCollisionSource((_, _, _, _) => false);
                return true;
            }, out var prep));
            Assert.False(prep!.TryStage(in intent, out _, out _));
            Assert.False(prep.TryAdoptUnpublished(out _));
            Assert.Equal(mutation == "npc" ? 1 : 0, store.ActiveCount);
            if (mutation == "live-rng") { var expected = new VanillaUnifiedRandom1458(7);expected.Next();Assert.True(live.HasSameState(expected)); }
        }
        // Adopting a completed preparation must not query the live provider again.
        var random = new VanillaUnifiedRandom1458(7); var ordinary = Store(random);int calls = 0;
        ordinary.SetVanillaSpawnWetCollisionSource((_, _, _, _) => { calls++; return true; });
        Assert.True(ordinary.TryPrepareWorldSpawns(random, random.Clone(), out var prepared));
        var one = new NpcAiSpawnIntent(new(46),800,800,0,0,255);
        Assert.True(prepared!.TryStage(in one,out _,out var birth)); Assert.True(birth.Simulation.Wet);
        Assert.Equal(1,calls);Assert.True(prepared.ValidateContext());
        Assert.True(prepared.TryAdoptUnpublished(out _));Assert.Equal(1,calls);
    }

    private static void ExistingSpawnPaths()
    {
        var live = new VanillaUnifiedRandom1458(7); var store = Store(live);int calls = 0;
        store.SetVanillaSpawnWetCollisionSource((_,_,_,_)=>{calls++;return true;});
        var update = new NpcStateUpdate(46,46,791,780,0,0,255,default,NpcSimulationState.Initial);
        Assert.True(store.TrySpawnVanilla(in update,out var direct));Assert.True(direct.Simulation.Wet);
        var intent = new NpcAiSpawnIntent(new(46),800,800,0,0,255);
        Assert.True(store.TrySpawnIntent(in intent,out var fromIntent));Assert.True(fromIntent.Simulation.Wet);
        Assert.True(store.TryCreateNaturalSpawnPreview(live,out var preview));
        Assert.True(preview!.TryStage(in update,800,800,out var natural));Assert.True(natural.Simulation.Wet);
        Assert.True(preview.ValidateContext());Assert.True(preview.TryAdoptOwned(out _));Assert.Equal(3,calls);
    }

    private static RuntimeNpcStore Store(VanillaUnifiedRandom1458 live)
    {
        var store = new RuntimeNpcStore(200);
        store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(live));
        store.SetVanillaSpawnContextSource(() => new(1,1,false));
        return store;
    }
    private static void Fill(RuntimeNpcStore store)
    {
        for(int index=0;index<200;index++)Assert.True(store.TrySpawnVanilla(new NpcStateUpdate(1,1,0,0,0,0,255,default,NpcSimulationState.Initial),out _));
    }
    private static WorldTile TerrainCell(string terrain,int x,int y)
    {
        var tile = new WorldTile();
        if(terrain!="dry") {tile.LiquidAmount=255;tile.LiquidKind=terrain=="honey"?WorldLiquidKind.Honey:terrain=="shimmer"?WorldLiquidKind.Shimmer:WorldLiquidKind.Water;}
        if(terrain=="slope-above"&&y==49){tile.LiquidAmount=0;tile.Type=1;tile.Flags=WorldTileFlags.Active;tile.TrySetSourceShape(false,1);}
        return tile;
    }
    private static void SetTerrain(WorldTileStore tiles,string terrain)
    {for(int x=48;x<=51;x++)for(int y=47;y<=50;y++)tiles.Set(x,y,TerrainCell(terrain,x,y));}
    private static void StageTerrain(ToolWorldTileOverlay1458 overlay,string terrain)
    {for(int x=48;x<=51;x++)for(int y=47;y<=50;y++){var tile=TerrainCell(terrain,x,y);Assert.True(overlay.TryStage(x,y,in tile));}}
    private static VanillaUnifiedRandom1458 ReadRandom(JsonElement value)
    {
        var random=new VanillaUnifiedRandom1458(0);
        value.GetProperty("state").EnumerateArray().Select(v=>v.GetInt32()).ToArray().CopyTo(Seed(random),0);
        Cursor(random)=value.GetProperty("cursor").GetUInt32();return random;
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="seedArray")]private static extern ref int[] Seed(VanillaUnifiedRandom1458 random);
    [UnsafeAccessor(UnsafeAccessorKind.Field,Name="inext")]private static extern ref uint Cursor(VanillaUnifiedRandom1458 random);
}
