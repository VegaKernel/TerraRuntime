using TerraRuntime.Core;
using TerraRuntime.Core.Worlds.Generation;
using System.IO.Compression;using System.Reflection;using System.Security.Cryptography;using System.Text.Json;using TerraRuntime.World;
namespace TerraRuntime.Tests;
public sealed class GenerationLargeMicroBiomes1458Tests {
 static readonly JsonDocument Source=Load();
 public static IEnumerable<object[]> Cases(){foreach(var r in Source.RootElement.GetProperty("cases").EnumerateArray())yield return [r.GetProperty("seed").GetInt32(),r.GetProperty("frame").GetInt32(),r.GetProperty("profile").GetInt32(),r.GetProperty("trigger").GetInt32(),r.GetProperty("direct").GetBoolean()];}
 [Theory,MemberData(nameof(Cases))]
 public void Double_pile_destruction_evaluates_source_reward_arguments(int seed,int frame,int profile,int trigger,bool direct){
 var row=Source.RootElement.GetProperty("cases").EnumerateArray().Single(r=>r.GetProperty("seed").GetInt32()==seed&&r.GetProperty("frame").GetInt32()==frame&&r.GetProperty("profile").GetInt32()==profile&&r.GetProperty("trigger").GetInt32()==trigger&&r.GetProperty("direct").GetBoolean()==direct);
 var world=new Workspace(80,80);world.InitializeVanillaGenerationSolidity();int index=0;foreach(var flag in Source.RootElement.GetProperty("entrySolidity").EnumerateArray())world.SetVanillaGenerationSolid(new((ushort)index++),flag.GetBoolean());
 void Cell(int x,int y,ushort type,bool active){var t=world.TileStore.Get(x,y);t.Type=type;if(active)t.Flags|=WorldTileFlags.Active;else t.Flags&=~WorldTileFlags.Active;world.TileStore.Set(x,y,in t);}
 for(int x=39;x<=40;x++){Cell(x,41,2,true);Cell(x,40,185,true);var t=world.TileStore.Get(x,40);t.FrameX=(short)(frame+(x-39)*18);t.FrameY=18;t.Flags|=WorldTileFlags.WireRed;t.Wall=64;t.TileColor=7;world.TileStore.Set(x,40,in t);}
 if(profile is 1 or 2){int x=profile==1?40:39;Cell(x,40,383,true);var t=world.TileStore.Get(x,40);t.FrameX=t.FrameY=0;world.TileStore.Set(x,40,in t);}
 if(profile is 3 or 7)Cell(39,40,185,false);if(profile==3){var t=world.TileStore.Get(39,40);t.FrameX=0;world.TileStore.Set(39,40,in t);}if(profile is 4 or 6)Cell(40,40,185,false);if(profile==5)Cell(39,41,2,false);
 if(profile==6){var t=world.TileStore.Get(39,41);t.TrySetSourceShape(true,0);world.TileStore.Set(39,41,in t);}if(profile==7){var t=world.TileStore.Get(40,41);t.TrySetSourceShape(false,1);world.TileStore.Set(40,41,in t);}
 var random=new RandomAdapter(seed);var framing=new GenerationTileFraming1458(world.TileStore,random,world,true);if(direct)typeof(GenerationTileFraming1458).GetMethod("FrameDoublePile",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(framing,[39+trigger,40]);else framing.TileFrame(39+trigger,40);
 Assert.Equal(row.GetProperty("grid").GetString(),GenerationNaturalPrefix49Tests.GridHash(world));using var bytes=new MemoryStream();using(var w=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true)){w.Write(row.GetProperty("cursor").GetUInt32());foreach(var slot in row.GetProperty("slots").EnumerateArray())w.Write(slot.GetInt32());}Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes.ToArray())),GenerationNaturalPrefix49Tests.RandomHash(random,false));Assert.Equal(row.GetProperty("next").GetInt32(),random.PeekNext());Assert.Empty(world.CaptureGeneratedChests());
 }
 public static IEnumerable<object[]> TrackCases(){foreach(var r in Source.RootElement.GetProperty("tracks").EnumerateArray())yield return [r.GetProperty("seed").GetInt32(),r.GetProperty("pressure").GetBoolean()];}
 [Theory,MemberData(nameof(TrackCases))]
 public void Track_reset_clears_only_source_skip_markers(int seed,bool pressure){
 var row=Source.RootElement.GetProperty("tracks").EnumerateArray().Single(r=>r.GetProperty("seed").GetInt32()==seed&&r.GetProperty("pressure").GetBoolean()==pressure);var world=new Workspace(1200,800);Assert.True(world.TrySetLayers(120,300));world.InitializeVanillaGenerationSolidity();int index=0;foreach(var flag in Source.RootElement.GetProperty("entrySolidity").EnumerateArray())world.SetVanillaGenerationSolid(new((ushort)index++),flag.GetBoolean());world.AttachEntityEffects(new TerraRuntime.Core.Worlds.Generation.WorldGenerationEntityEffects1458(new TerraRuntime.Core.Npcs.RuntimeNpcStore(),new TerraRuntime.Core.Projectiles.RuntimeProjectileStore(),true),0);
 for(int x=0;x<1200;x++)for(int y=0;y<800;y++){if(y>=(x<750?400:500)){ref var t=ref world.TileStore.Tiles[x*800+y];t.Flags|=WorldTileFlags.Active;t.Type=1;}world.TileStore.LiquidUpdates.SetSkipNextUpdate(x,y);}
 if(pressure)for(int x=410;x<420;x++){ref var t=ref world.TileStore.Tiles[x*800+397];t.Flags|=WorldTileFlags.Active;t.Type=135;}
 var random=new RandomAdapter(seed);var track=new TrackGenerator1458(world,random);Assert.Equal(row.GetProperty("returned")[0].GetBoolean(),track.Place(400,300,30,120));Assert.Equal(row.GetProperty("returned")[1].GetBoolean(),track.Place(900,400,30,120));
 using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] column=new byte[800];int cleared=0;for(int x=0;x<1200;x++){for(int y=0;y<800;y++){column[y]=(byte)(world.TileStore.LiquidUpdates.IsSkipNextUpdate(x,y)?1:0);if(column[y]==0)cleared++;Assert.False(world.TileStore.LiquidUpdates.IsCheckingLiquid(x,y));}hash.AppendData(column);}Assert.Equal(row.GetProperty("skipHash").GetString(),Convert.ToHexString(hash.GetHashAndReset()));Assert.Equal(row.GetProperty("cleared").GetInt32(),cleared);Assert.Equal(row.GetProperty("grid").GetString(),GenerationNaturalPrefix49Tests.GridHash(world));using var bytes=new MemoryStream();using(var w=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true)){w.Write(row.GetProperty("cursor").GetUInt32());foreach(var slot in row.GetProperty("slots").EnumerateArray())w.Write(slot.GetInt32());}Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes.ToArray())),GenerationNaturalPrefix49Tests.RandomHash(random,false));Assert.Equal(row.GetProperty("next").GetInt32(),random.PeekNext());Assert.False(world.TileStore.LiquidUpdates.HasPendingWork);
 }
 static JsonDocument Load(){using var raw=typeof(GenerationLargeMicroBiomes1458Tests).Assembly.GetManifestResourceStream("GenerationLargeMicroBiomes1458")!;using var gzip=new GZipStream(raw,CompressionMode.Decompress);return JsonDocument.Parse(gzip);}
 private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int PeekNext()
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var clone = new VanillaUnifiedRandom1458(0); var cursor = typeof(VanillaUnifiedRandom1458).GetField("inext", fields)!; var slots = typeof(VanillaUnifiedRandom1458).GetField("seedArray", fields)!;
            cursor.SetValue(clone, cursor.GetValue(random)); Array.Copy((int[])slots.GetValue(random)!, (int[])slots.GetValue(clone)!, 56); return clone.Next();
        }
        public int Next() => random.Next(); public int Next(int max) => random.Next(max); public int Next(int min, int max) => random.Next(min, max); public double NextDouble() => random.NextDouble(); public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
