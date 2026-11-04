using System.IO.Compression;using System.Reflection;using System.Security.Cryptography;using System.Text.Json;using TerraRuntime.Core;using TerraRuntime.Core.Npcs;using TerraRuntime.Core.Projectiles;using TerraRuntime.Core.Worlds.Generation;using TerraRuntime.World;
namespace TerraRuntime.Tests;
public sealed class GenerationDungeonTree1458Tests {
 static readonly JsonDocument Source=Load();
 public static IEnumerable<object[]> Cases(){for(int i=0;i<Source.RootElement.GetProperty("cases").GetArrayLength();i++)yield return [i];}
 [Theory,MemberData(nameof(Cases))]
 public void Dungeon_tree_matches_original_geometry_paint_roots_passage_and_phase(int index){
 var row=Source.RootElement.GetProperty("cases")[index];int profile=row.GetProperty("profile").GetInt32(),flags=row.GetProperty("flags").GetInt32();var world=new Workspace(420,640);world.InitializeVanillaGenerationSolidity();int identity=0;foreach(var flag in Source.RootElement.GetProperty("entrySolidity").EnumerateArray())world.SetVanillaGenerationSolid(new((ushort)identity++),flag.GetBoolean());if(profile==11)world.SetVanillaGenerationSolid(VanillaTileIds.Spikes,false);Assert.True(world.TrySetLayers(profile==2?180:260,380));VanillaSpecialWorldSeed1458 special=VanillaSpecialWorldSeed1458.DrunkWorld;if((flags&1)!=0)special|=VanillaSpecialWorldSeed1458.NotTheBees;if((flags&2)!=0)special|=VanillaSpecialWorldSeed1458.Remix;world.SetVanillaSeedProfile(new(special,VanillaSecretWorldSeed1458.None));var npcs=new RuntimeNpcStore();var projectiles=new RuntimeProjectileStore();world.AttachEntityEffects(new WorldGenerationEntityEffects1458(npcs,projectiles,true),0);Assert.True(world.TileStore.TryAttachTileEntities([],1000));
 for(int x=0;x<420;x++)for(int y=0;y<640;y++){
 ref WorldTile tile=ref Cell(x,y);if(y>=221){tile.Type=1;tile.Flags|=WorldTileFlags.Active;}if(profile==1){tile.Wall=64;tile.TileColor=17;tile.WallColor=3;tile.Flags|=WorldTileFlags.WireRed|WorldTileFlags.WireGreen|WorldTileFlags.Actuator|WorldTileFlags.InvisibleBlock|WorldTileFlags.FullbrightBlock|WorldTileFlags.InvisibleWall|WorldTileFlags.FullbrightWall;if(y<221)Assert.True(tile.TrySetSourceShape(true,(byte)((x+y)%4+1)));}if(profile==2&&x%3==0&&y<221)tile.Type=191;if(profile==3)tile.Wall=7;if(profile==6&&y>=221)tile.Wall=244;}
 for(int y=216;y<=220;y++)Cell(210,y).Wall=0;
 if(profile==4){ushort[] glass=[21,88,89,90,91,92,93,241,242,243];for(int x=206;x<=214;x++)for(int y=217;y<=220;y++)if(x!=210)Cell(x,y).Wall=glass[(x+y)%glass.Length];}
 if(profile==5){ushort[] kinds=[19,15,304,21,10];for(int x=207;x<=213;x++)for(int y=225;y<315;y+=7){ref var tile=ref Cell(x,y);tile.Type=kinds[(x+y)%5];tile.Flags&=~WorldTileFlags.Active;tile.FrameX=36;tile.FrameY=18;tile.TileColor=17;tile.Flags|=WorldTileFlags.InvisibleBlock;}}
 if(profile==7)for(int x=180;x<=240;x++)Cell(x,221).Type=19;
 if(profile==8)for(int x=180;x<=240;x++)for(int y=221;y<=224;y++)Assert.True(Cell(x,y).TrySetSourceShape((x+y)%2==0,(byte)((x+y)%4+1)));
 if(profile==9)for(int x=180;x<=240;x++)for(int y=221;y<=224;y++)Cell(x,y).Type=48;
 if(profile==10)for(int y=50;y<=220;y++)Cell(210,y).Wall=1;
 var random=new RandomAdapter(row.GetProperty("seed").GetInt32());var tree=new LivingTreeGrower1458(world.TileStore,random,profile==2?180:260,440,TestContext.Current.CancellationToken,workspace:world);Exception? error=Record.Exception(()=>tree.GenerateDungeonTree(row.GetProperty("x").GetInt32(),row.GetProperty("y").GetInt32(),row.GetProperty("attachment").GetInt32(),row.GetProperty("roots").GetBoolean(),row.GetProperty("style").GetInt32()));Assert.Equal(row.GetProperty("error").GetString(),error?.GetType().Name);Assert.Equal(row.GetProperty("grid").GetString(),GenerationNaturalPrefix49Tests.GridHash(world));using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true)){writer.Write(row.GetProperty("cursor").GetUInt32());foreach(var slot in row.GetProperty("slots").EnumerateArray())writer.Write(slot.GetInt32());}Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes.ToArray())),GenerationNaturalPrefix49Tests.RandomHash(random,false));Assert.Equal(row.GetProperty("next").GetInt32(),random.PeekNext());Assert.Equal(row.GetProperty("phaseHash").GetString(),Convert.ToHexString(SHA256.HashData(Enumerable.Range(0,754).Select(i=>(byte)(world.IsVanillaGenerationSolid(new((ushort)i))?1:0)).ToArray())));Assert.Empty(world.CaptureGeneratedChests());Assert.Equal(0,npcs.ActiveCount);Assert.Equal(0,projectiles.ActiveCount);
 ref WorldTile Cell(int x,int y)=>ref world.TileStore.Tiles[x*640+y];
 }
 static JsonDocument Load(){using var raw=typeof(GenerationDungeonTree1458Tests).Assembly.GetManifestResourceStream("GenerationDungeonTree1458")!;using var gzip=new GZipStream(raw,CompressionMode.Decompress);return JsonDocument.Parse(gzip);}
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
