using System.IO.Compression;using System.Reflection;using System.Security.Cryptography;using System.Text.Json;using TerraRuntime.Core;using TerraRuntime.World;
namespace TerraRuntime.Tests;
public sealed class GenerationFrameContextDust1458Tests {
 private static readonly JsonDocument Source=Load();
 public static IEnumerable<object[]> Cases(){foreach(var r in Source.RootElement.EnumerateArray())yield return [r.GetProperty("type").GetUInt16(),r.GetProperty("frame").GetInt16(),r.GetProperty("seed").GetInt32(),r.GetProperty("entry").GetString()!];}
 [Theory,MemberData(nameof(Cases))]
 public void Whole_kill_and_dust_helpers_preserve_source_frame_bands(ushort type,short frame,int seed,string entry){
  var expected=Source.RootElement.EnumerateArray().Single(r=>r.GetProperty("type").GetUInt16()==type&&r.GetProperty("frame").GetInt16()==frame&&r.GetProperty("seed").GetInt32()==seed&&r.GetProperty("entry").GetString()==entry).GetProperty("capture");
  var world=new Workspace(80,80);var tile=new WorldTile{Type=type,FrameX=frame,Wall=1,TileColor=7,LiquidAmount=20,LiquidKind=WorldLiquidKind.Honey,Flags=WorldTileFlags.Active|WorldTileFlags.WireRed};world.TileStore.Set(40,40,in tile);var random=new RandomAdapter(seed);var framing=new GenerationTileFraming1458(world.TileStore,random);
  if(entry=="whole")framing.KillTile(40,40);else typeof(GenerationTileFraming1458).GetMethod("ConsumeTileDust",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(framing,[40,40,tile]);
  Assert.Equal(expected.GetProperty("gridHash").GetString(),GenerationNaturalPrefix49Tests.GridHash(world));var physical=expected.GetProperty("random");using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){writer.Write(physical.GetProperty("cursor").GetUInt32());foreach(var slot in physical.GetProperty("slots").EnumerateArray())writer.Write(slot.GetInt32());}Assert.Equal(Convert.ToHexString(SHA256.HashData(stream.ToArray())),GenerationNaturalPrefix49Tests.RandomHash(random,includeReport:false));Assert.Equal(physical.GetProperty("next").GetInt32(),random.PeekNext());
 }
 private static JsonDocument Load(){using var raw=typeof(GenerationFrameContextDust1458Tests).Assembly.GetManifestResourceStream("GenerationFrameContextDust1458")!;using var gzip=new GZipStream(raw,CompressionMode.Decompress);return JsonDocument.Parse(gzip);}
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
