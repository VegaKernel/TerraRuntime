using System.IO.Compression;using System.Reflection;using System.Security.Cryptography;using System.Text.Json;using TerraRuntime.Core;using TerraRuntime.Core.Npcs;using TerraRuntime.Core.Projectiles;using TerraRuntime.Core.Worlds.Generation;using TerraRuntime.Contracts.Runtime;using TerraRuntime.Contracts.Gameplay;using TerraRuntime.World;
namespace TerraRuntime.Tests;
public sealed class GenerationDungeonDebris1458Tests {
 private static readonly JsonDocument Source=Load();
 public static IEnumerable<object[]> Cases(){foreach(var r in Source.RootElement.EnumerateArray())yield return [r.GetProperty("type").GetUInt16(),r.GetProperty("mode").GetInt32(),r.GetProperty("seed").GetInt32(),r.GetProperty("pattern").GetInt32()];}
 [Theory,MemberData(nameof(Cases))]
 public void Whole_kill_retains_ordered_debris_births_and_random(ushort type,int mode,int seed,int pattern){
  var row=Source.RootElement.EnumerateArray().Single(r=>r.GetProperty("type").GetUInt16()==type&&r.GetProperty("mode").GetInt32()==mode&&r.GetProperty("seed").GetInt32()==seed&&r.GetProperty("pattern").GetInt32()==pattern);var world=new Workspace(80,80);var projectiles=new RuntimeProjectileStore();world.AttachEntityEffects(new WorldGenerationEntityEffects1458(new RuntimeNpcStore(),projectiles,mode==2),(byte)(mode==2?255:0));
  void Set(int x,int y,ushort kind,bool active=true){var t=new WorldTile{Type=kind,Flags=active?WorldTileFlags.Active:0};world.TileStore.Set(x,y,in t);}
  Set(40,40,type);int[] dx=[-1,1,0,0,-1,1,-1,1],dy=[0,0,-1,1,-1,-1,1,1];
  if(pattern>=1&&pattern<=8)Set(40+dx[pattern-1],40+dy[pattern-1],type);
  if(pattern==9||pattern==10)for(int n=0;n<8;n++)Set(40+dx[n],40+dy[n],pattern==9?(ushort)1:(ushort)(481+n%3));
  if(pattern==11)for(int n=0;n<8;n++)Set(40+dx[n],40+dy[n],type,false);
  if(pattern==12){Set(39,40,type);var t=world.TileStore.Get(39,40);t.Flags|=WorldTileFlags.Inactive;world.TileStore.Set(39,40,in t);}
  if(pattern==13)for(ushort i=0;i<1000;i++){var update=new ProjectileStateUpdate(new(1),255,0,0,0,0,default,0,0,0,0);Assert.True(projectiles.TrySpawnVanilla(in update,i==300?1:100+i,out _));}
  var random=new RandomAdapter(seed);var framing=new GenerationTileFraming1458(world.TileStore,random,workspace:world){SourceClient=mode==1,SourceServer=mode==2};framing.KillTile(40,40);
  var capture=row.GetProperty("capture");Assert.Equal(capture.GetProperty("gridHash").GetString(),GenerationNaturalPrefix49Tests.GridHash(world));var physical=capture.GetProperty("random");using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){writer.Write(physical.GetProperty("cursor").GetUInt32());foreach(var slot in physical.GetProperty("slots").EnumerateArray())writer.Write(slot.GetInt32());}Assert.Equal(Convert.ToHexString(SHA256.HashData(stream.ToArray())),GenerationNaturalPrefix49Tests.RandomHash(random,includeReport:false));Assert.Equal(physical.GetProperty("next").GetInt32(),random.PeekNext());
  var expected=row.GetProperty("effects");Assert.Equal(expected.GetArrayLength(),projectiles.ActiveCount);foreach(var e in expected.EnumerateArray()){
   Assert.True(projectiles.TryGetActive(e.GetProperty("slot").GetUInt16(),out var actual));Assert.True(projectiles.TryGetLifecycle(actual.Handle,out var life));Assert.Equal(e.GetProperty("type").GetInt32(),actual.Type.Value);Assert.Equal(e.GetProperty("owner").GetInt32(),actual.Spawner);Assert.Equal(e.GetProperty("x").GetSingle(),actual.PositionX);Assert.Equal(e.GetProperty("y").GetSingle(),actual.PositionY);Assert.Equal(e.GetProperty("vx").GetSingle(),actual.VelocityX);Assert.Equal(e.GetProperty("vy").GetSingle(),actual.VelocityY);Assert.Equal(e.GetProperty("timeLeft").GetInt32(),life.TimeLeft);Assert.Equal(e.GetProperty("damage").GetInt32(),actual.Damage);Assert.Equal(e.GetProperty("originalDamage").GetInt32(),actual.OriginalDamage);Assert.Equal(e.GetProperty("knockBack").GetSingle(),actual.KnockBack);Assert.Equal(e.GetProperty("netUpdate").GetBoolean(),life.SourceNetUpdate);Assert.Equal(e.GetProperty("netImportant").GetBoolean(),life.NetImportant);Assert.Equal(e.GetProperty("ai").EnumerateArray().Select(a=>a.GetSingle()).ToArray(),new[]{actual.Ai.Ai0,actual.Ai.Ai1,actual.Ai.Ai2});
  }
 }
 private static JsonDocument Load(){using var raw=typeof(GenerationDungeonDebris1458Tests).Assembly.GetManifestResourceStream("GenerationDungeonDebris1458")!;using var gzip=new GZipStream(raw,CompressionMode.Decompress);return JsonDocument.Parse(gzip);}
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
