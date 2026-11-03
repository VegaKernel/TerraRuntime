using System.IO.Compression;using System.Reflection;using System.Security.Cryptography;using System.Text.Json;using TerraRuntime.Core;using TerraRuntime.World;
namespace TerraRuntime.Tests;
public sealed class GenerationLivingPassagePlacement1458Tests {
 private static readonly JsonDocument Source=Load();
 public static IEnumerable<object[]> Cases(){foreach(var r in Source.RootElement.EnumerateArray())yield return [r.GetProperty("type").GetUInt16(),r.GetProperty("seed").GetInt32(),r.GetProperty("pattern").GetInt32()];}
 [Theory,MemberData(nameof(Cases))]
 public void Passage_furniture_calls_preserve_source_placement_attempts(ushort type,int seed,int pattern){
  var row=Source.RootElement.EnumerateArray().Single(r=>r.GetProperty("type").GetUInt16()==type&&r.GetProperty("seed").GetInt32()==seed&&r.GetProperty("pattern").GetInt32()==pattern);var world=new Workspace(80,120);int x0=pattern==19?1:pattern==20?78:40,y0=pattern==21?3:40;
  void Set(int x,int y,ushort kind){if(x<0||x>=80||y<0||y>=120)return;var tile=new WorldTile{Type=kind,Flags=WorldTileFlags.Active};world.TileStore.Set(x,y,in tile);}void Change(int x,int y,Func<WorldTile,WorldTile> apply){var tile=apply(world.TileStore.Get(x,y));world.TileStore.Set(x,y,in tile);}
  for(int x=x0-1;x<=x0+1;x++)Set(x,y0+1,1);Set(x0,y0-3,1);var anchor=new WorldTile{Type=191,FrameX=-1,FrameY=-1,Wall=244,TileColor=7,Flags=WorldTileFlags.InvisibleBlock|WorldTileFlags.FullbrightBlock|WorldTileFlags.WireRed};world.TileStore.Set(x0,y0,in anchor);
  if(pattern==1)Change(x0,y0+1,t=>{t.Flags&=~WorldTileFlags.Active;return t;});if(pattern==2)Change(x0,y0-3,t=>{t.Flags&=~WorldTileFlags.Active;return t;});if(pattern==3)Set(x0,y0-1,1);if(pattern==4)Set(x0-1,y0,1);if(pattern==5)Set(x0+1,y0,1);if(pattern==6)Change(x0,y0+1,t=>{t.TrySetSourceShape(true,0);return t;});if(pattern==7)Change(x0,y0+1,t=>{t.TrySetSourceShape(false,1);return t;});if(pattern==8)Change(x0,y0+1,t=>{t.TrySetSourceShape(false,3);return t;});if(pattern==9)Change(x0,y0+1,t=>{t.Flags|=WorldTileFlags.Inactive;return t;});if(pattern==10)Change(x0,y0,t=>{t.TrySetSourceShape(true,3);return t;});if(pattern==11||pattern==12)Change(x0,y0,t=>{t.LiquidAmount=20;t.LiquidKind=pattern==11?WorldLiquidKind.Water:WorldLiquidKind.Lava;return t;});if(pattern==13)Set(x0-1,y0-1,4);if(pattern==14)Set(x0+1,y0-1,15);if(pattern==15)Change(x0,y0,t=>{t.Type=1;t.FrameX=123;t.FrameY=77;t.TrySetSourceShape(true,0);return t;});if(pattern==16)Change(x0,y0,t=>{t.Flags|=WorldTileFlags.Active;return t;});if(pattern==17||pattern==18)Change(x0,y0,t=>{t.Flags|=WorldTileFlags.Active;t.Type=(ushort)(pattern==17?15:488);return t;});if(pattern==22)Set(x0,y0-3,19);if(pattern==23)Set(x0,y0+1,19);
  if(pattern==24)Change(x0,y0-3,t=>{t.TrySetSourceShape(true,0);return t;});if(pattern==25)Change(x0,y0-3,t=>{t.TrySetSourceShape(false,3);return t;});if(pattern==26)Change(x0,y0-3,t=>{t.Flags|=WorldTileFlags.Inactive;return t;});if(pattern>=27){Set(x0,y0+1,19);if(pattern==27)Change(x0,y0+1,t=>{t.TrySetSourceShape(false,1);return t;});if(pattern==28)Change(x0,y0+1,t=>{t.TrySetSourceShape(true,0);return t;});if(pattern==29)Change(x0,y0+1,t=>{t.TrySetSourceShape(false,3);return t;});}
  if(pattern==30){Change(x0,y0,t=>{t.Flags|=WorldTileFlags.Active;t.Type=54;return t;});Set(x0,y0-1,1);}
  var random=new RandomAdapter(seed);var passage=new LivingTreePassage1458(world.TileStore,random,30,-80,TestContext.Current.CancellationToken,workspace:world);string method=type==10?"PlaceDoor":type==15?"PlaceChair":"PlaceLoom";typeof(LivingTreePassage1458).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(passage,type==304?[x0,y0]:[x0,y0,type==10?7:5]);var expected=row.GetProperty("capture");Assert.Equal(expected.GetProperty("gridHash").GetString(),GenerationNaturalPrefix49Tests.GridHash(world));var physical=expected.GetProperty("random");using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){writer.Write(physical.GetProperty("cursor").GetUInt32());foreach(var slot in physical.GetProperty("slots").EnumerateArray())writer.Write(slot.GetInt32());}Assert.Equal(Convert.ToHexString(SHA256.HashData(stream.ToArray())),GenerationNaturalPrefix49Tests.RandomHash(random,includeReport:false));Assert.Equal(physical.GetProperty("next").GetInt32(),random.PeekNext());
 }
 private static JsonDocument Load(){using var raw=typeof(GenerationLivingPassagePlacement1458Tests).Assembly.GetManifestResourceStream("GenerationLivingPassagePlacement1458")!;using var gzip=new GZipStream(raw,CompressionMode.Decompress);return JsonDocument.Parse(gzip);}
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
