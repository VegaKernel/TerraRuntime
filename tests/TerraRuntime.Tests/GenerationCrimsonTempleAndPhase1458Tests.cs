using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.World;
using TerraRuntime.Core;
namespace TerraRuntime.Tests;
public sealed class GenerationCrimsonTempleAndPhase1458Tests
{
 public static IEnumerable<object[]> Cases()
 {
  var source=SourceData;foreach(var row in source.RootElement.GetProperty("rows").EnumerateArray())yield return [row.GetProperty("name").GetString()!,row.GetProperty("seed").GetInt32()];
 }
 [Theory,MemberData(nameof(Cases))]
 public void Temple_strict_supports_and_final_Shimmer_phase_match_actual_original(string name,int seed)
 {
  var source=SourceData;var data=source.RootElement;var d=data.GetProperty("cases").EnumerateArray().Single(r=>r.GetProperty("name").GetString()==name);var row=data.GetProperty("rows").EnumerateArray().Single(r=>r.GetProperty("name").GetString()==name&&r.GetProperty("seed").GetInt32()==seed);
  string kind=d.GetProperty("kind").GetString()!;var workspace=new Workspace(kind=="mayan"?120:500,500);workspace.InitializeVanillaGenerationSolidity();int index=0;foreach(var solid in data.GetProperty("solidity").EnumerateArray())workspace.SetVanillaGenerationSolid(new((ushort)index++),solid.GetBoolean());
  Assert.True(workspace.TrySetLayers(kind=="mayan"?50:170,300));var random=new RandomAdapter(seed);var framing=new GenerationTileFraming1458(workspace.TileStore,random,workspace,true);
  if(kind=="mayan")
  {
   for(int x=0;x<120;x++)for(int y=0;y<500;y++)At(x,y)=new WorldTile{Type=226,Wall=87,Flags=(y<=155||y>=180||x==38||x==58)?WorldTileFlags.Active:0};
   int lane=d.GetProperty("lane").GetInt32(),shape=d.GetProperty("shape").GetInt32(),state=d.GetProperty("state").GetInt32();
   void Change(int x,int y){ref var tile=ref At(x,y);Assert.True(tile.TrySetSourceShape(shape is 1 or 6,(byte)(shape>=2?(shape==6?3:shape-1):0)));if(state==2)tile.Flags&=~WorldTileFlags.Active;else tile.Flags|=WorldTileFlags.Active;if(state==1)tile.Flags|=WorldTileFlags.Inactive;}
   if(lane==0){Change(47,154);Change(49,154);}
   else if(lane==1)for(int y=174;y<=176;y++){Change(38,y);Change(39,y);Change(58,y);Change(57,y);}
   else {for(int y=174;y<=178;y++){At(37,y).Flags|=WorldTileFlags.Active;At(59,y).Flags|=WorldTileFlags.Active;}if(lane==3){Change(38,178);Change(58,178);}if(lane==4){Change(37,178);Change(59,178);}}
   Type owner=typeof(SourceBackedLateStructures1458).Assembly.GetType("TerraRuntime.WorldGeneration.Vanilla.LateStructurePass1458")!;
   object grid=Construct(owner.GetNestedType("RuntimeGrid",BindingFlags.NonPublic)!,workspace);object draws=Construct(owner.GetNestedType("VanillaRandom",BindingFlags.NonPublic)!,random);
   bool actual=(bool)owner.GetMethod("TryMayanTrap",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[workspace,framing,grid,draws,48,170,200,50d])!;
   Assert.Equal(row.GetProperty("outcome").GetBoolean(),actual);
  }
  else
  {
   int profile=d.GetProperty("profile").GetInt32();for(int x=0;x<500;x++)for(int y=0;y<500;y++)At(x,y)=new WorldTile{Type=1,Wall=1,Flags=(!(x>=240&&x<=260&&y>=240&&y<=265))?WorldTileFlags.Active:0};
   workspace.VanillaShimmerPosition=profile==0?null:new WorldGenerationPoint(250,250);
   foreach(ushort type in new ushort[]{138,484,546,664,711,712,713,714,715,716})workspace.SetVanillaGenerationSolid(new(type),profile==2);
   if(profile==3)workspace.SetVanillaGenerationSolid(new(665),false);
   Assert.Equal(profile!=0,FinalPass1458.CleanupShimmer(workspace,random,framing,TestContext.Current.CancellationToken));
  }
  var expected=row.GetProperty("capture");Assert.Equal(expected.GetProperty("gridHash").GetString(),GenerationNaturalPrefix49Tests.GridHash(workspace));
  var physical=expected.GetProperty("random");using var memory=new MemoryStream();using(var writer=new BinaryWriter(memory,System.Text.Encoding.UTF8,true)){writer.Write(physical.GetProperty("cursor").GetUInt32());foreach(var slot in physical.GetProperty("slots").EnumerateArray())writer.Write(slot.GetInt32());}
  Assert.Equal(Convert.ToHexString(SHA256.HashData(memory.ToArray())),GenerationNaturalPrefix49Tests.RandomHash(random,includeReport:false));Assert.Equal(physical.GetProperty("next").GetInt32(),random.PeekNext());
  Assert.Equal(expected.GetProperty("solidity").EnumerateArray().Select(s=>s.GetBoolean()),Enumerable.Range(0,754).Select(i=>workspace.IsVanillaGenerationSolid(new((ushort)i))));
  ref WorldTile At(int x,int y)=>ref workspace.TileStore.Tiles[workspace.TileStore.GetUncheckedIndex(x,y)];
 }
 private static object Construct(Type type,object argument)=>Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[argument],null)!;
 internal static JsonDocument SourceData { get; } = LoadSource();
 private static JsonDocument LoadSource(){using var raw=typeof(GenerationCrimsonTempleAndPhase1458Tests).Assembly.GetManifestResourceStream("GenerationCrimsonTempleAndPhase1458")!;using var gzip=new GZipStream(raw,CompressionMode.Decompress);return JsonDocument.Parse(gzip);}
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
