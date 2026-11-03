using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Core.Worlds.Generation;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;
namespace TerraRuntime.Tests;
public sealed class GenerationCorruptionContinuous1061458Tests
{
 [Theory]
 [InlineData(1458)]
 [InlineData(42)]
 public void Ordinary_Corruption_runs_to_final_cleanup_without_checkpoint_imports(int seed)
 {
  using var raw=typeof(GenerationCorruptionContinuous1061458Tests).Assembly.GetManifestResourceStream("GenerationCorruptionContinuous1061458")!;using var gzip=new GZipStream(raw,CompressionMode.Decompress);using var source=JsonDocument.Parse(gzip);
  JsonElement original=source.RootElement.GetProperty("continuations").EnumerateArray().Single(r=>r.GetProperty("seed").GetInt32()==seed);
  var world=new Workspace(4200,1200);Assert.True(world.TileStore.TryAttachTileEntities([],1000));var npcs=new RuntimeNpcStore();var projectiles=new RuntimeProjectileStore();
  npcs.SetVanillaSpawnContextSource(()=>new VanillaNpcSpawnContext(1,0,false){WorldDifficulty=1});npcs.SetVanillaSpawnWetCollisionSource((x,y,w,h)=>VanillaWorldCollision.GetLiquidContacts(world.TileStore,x,y,w,h).Wet);
  world.AttachEntityEffects(new WorldGenerationEntityEffects1458(npcs,projectiles,true),0,1);
  using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);var observer=new Observer(world,npcs,projectiles,cancellation,original.GetProperty("points"));
  var request=new WorldGenerationRequest(Provider1458.GeneratorId,"CorruptionContinuousSource",(ulong)seed,4200,1200){SeedText=seed.ToString(System.Globalization.CultureInfo.InvariantCulture),Options=new(WorldGenerationGameMode.Classic,WorldGenerationEvil.Corruption)};
  var result=RuntimeWorldGenerationExecutor.Execute(new Provider(new SourceBackedFinal1458(),observer),request,world,null,cancellation.Token);
  Assert.True(result.Error is null,result.Error?.ToString());Assert.Equal("Cancelled",result.Status.ToString());Assert.Equal(9,observer.Count);
 }
 private sealed class Provider(IWorldGenerationProvider inner,Observer observer):IWorldGenerationProvider
 {
  public WorldGeneratorId Id=>inner.Id;
  public void BuildPlan(in WorldGenerationRequest request,IWorldGenerationPlanBuilder builder)=>inner.BuildPlan(in request,new Builder(builder,observer));
 }
 private sealed class Builder(IWorldGenerationPlanBuilder inner,Observer observer):IWorldGenerationPlanBuilder
 {public void Add(WorldGenerationPassDescriptor descriptor,IWorldGenerationPass pass)=>inner.Add(descriptor,new Pass(descriptor,pass,observer));}
 private sealed class Pass(WorldGenerationPassDescriptor descriptor,IWorldGenerationPass inner,Observer observer):IWorldGenerationPass
 {public void Execute(IWorldGenerationContext context){inner.Execute(context);observer.After(descriptor,context);}}
 private sealed class Observer(Workspace world,RuntimeNpcStore npcs,RuntimeProjectileStore projectiles,CancellationTokenSource cancellation,JsonElement points)
 {
  public int Count;
  private readonly Dictionary<string,JsonElement> expected=points.EnumerateArray().ToDictionary(p=>Normalize(p.GetProperty("name").GetString()!));
  public void After(WorldGenerationPassDescriptor descriptor,IWorldGenerationContext context)
  {
   if(!expected.TryGetValue(Normalize(descriptor.Id.Value.Split('/')[^1]),out var original))return;
   int ordinal=original.GetProperty("ordinal").GetInt32();Assert.Equal(++Count+97,ordinal);
   Assert.Equal(original.GetProperty("grid").GetString(),GenerationNaturalPrefix49Tests.GridHash(world));Assert.Equal(original.GetProperty("rngHash").GetString(),GenerationNaturalPrefix49Tests.RandomHash(context.VanillaRandom!,includeReport:false));
   Assert.Equal(original.GetProperty("solidity").EnumerateArray().Select(p=>p.GetBoolean()),Enumerable.Range(0,754).Select(i=>world.IsVanillaGenerationSolid(new((ushort)i))));Assert.Equal(original.GetProperty("solidTop").EnumerateArray().Select(p=>p.GetBoolean()),Enumerable.Range(0,754).Select(i=>VanillaTileCollisionCatalog.IsSolidTop(new((ushort)i))));
   var liquid=world.TileStore.LiquidUpdates;var flags=new List<object>();for(int x=0;x<4200;x++)for(int y=0;y<1200;y++){bool checking=liquid.IsCheckingLiquid(x,y),skip=liquid.IsSkipNextUpdate(x,y);if(checking||skip)flags.Add(new{x,y,checking,skip});}
   var c=world.VanillaBuriedChestContext;
   var metadata=new{
    chests=world.CaptureGeneratedChests().Select(c=>new{slot=(int)c.SlotId,x=c.X,y=c.Y,name=c.Name,items=c.Items.Select(i=>new{type=i.ItemType,stack=i.Stack,prefix=i.Prefix})}),
    allStructures=world.VanillaStructureMap.CaptureStructures().Select(r=>new{r.Height,r.Width,r.X,r.Y}),protectedRects=world.VanillaStructureMap.CaptureProtectedStructures().Select(r=>new{r.Height,r.Width,r.X,r.Y}),
    activeSlots=liquid.CaptureActiveSnapshot().Select((s,i)=>new{slot=i,x=s.TileIndex/1200,y=s.TileIndex%1200,kill=s.Kill,delay=s.Delay}),bufferSlots=liquid.CaptureBufferSnapshot().Select((s,i)=>new{slot=i,x=s/1200,y=s%1200}),
    liquidTileFlags=flags,liquidCount=liquid.ActiveCount,liquidBufferCount=liquid.BufferedCount,tileEntityCount=world.CaptureGeneratedTileEntities().Length,wetCounter=liquid.GenerationWetCounter,stuckCount=liquid.GenerationStuckCount,stuckAmount=liquid.GenerationStuckAmount,
    chestCounters=new{JungleItemCount=c?.JungleItemCount??0,hellChest=c?.HellChest??0,generatedShadowKey=c?.GeneratedShadowKey??false,generatedRamRune=c?.GeneratedRamRune??false,gennedLivingMahoganyWands=c?.GennedLivingMahoganyWands??false}
   };
   Assert.True(JsonElement.DeepEquals(original.GetProperty("metadata"),JsonSerializer.SerializeToElement(metadata)),"metadata native"+ordinal);
   using(var stream=new MemoryStream()){Assert.Equal(WorldFileNpcEncodeResult.Encoded,WorldFileNpcEncoder.TryEncode(world.CaptureGeneratedNpcs(),new(1000,1000,1000,1000,1000,100000),stream,out _));Assert.Equal(original.GetProperty("npcSection").GetString(),Convert.ToHexString(SHA256.HashData(stream.ToArray())));}
   using(var stream=new MemoryStream()){using(var writer=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)){var spawns=world.CaptureVanillaExtraSpawnPoints();writer.Write((byte)spawns.Length);foreach(var point in spawns){writer.Write(point.X);writer.Write(point.Y);}}Assert.Equal(original.GetProperty("extraSpawnSection").GetString(),Convert.ToHexString(SHA256.HashData(stream.ToArray())));}
   var actors=new List<object>();for(byte i=0;i<200;i++)if(npcs.TryGetActive(i,out var p)){
    var s=p.Simulation;actors.Add(new{slot=(int)i,type=(int)p.Type,x=p.PositionX,y=p.PositionY,direction=s.DirectionX,spriteDirection=s.SpriteDirection,homeTileX=s.HomeTileX,homeTileY=s.HomeTileY,homeless=s.Homeless,target=p.Target,ai=new[]{p.Ai.Ai0,p.Ai.Ai1,p.Ai.Ai2,p.Ai.Ai3},vx=p.VelocityX,vy=p.VelocityY,life=s.Life,lifeMax=s.LifeMax,wet=s.Wet,lavaWet=s.LavaWet??Convert.ToInt32(s.LiquidContact)==2,honeyWet=s.HoneyWet??Convert.ToInt32(s.LiquidContact)==3,shimmerWet=s.ShimmerWet??Convert.ToInt32(s.LiquidContact)==4,noGravity=s.NoGravity,noTileCollide=s.NoTileCollide,timeLeft=s.TimeLeft,scale=s.Scale,friendly=s.Friendly,dontTakeDamage=s.DontTakeDamage,immortal=s.Immortal,alpha=s.Alpha,rotation=s.Rotation});
   }
   Assert.True(JsonElement.DeepEquals(original.GetProperty("npcs"),JsonSerializer.SerializeToElement(actors)),"physical NPC native"+ordinal);
   var effects=new List<object>();for(int i=0;i<projectiles.Capacity;i++)if(projectiles.TryGetActive((ushort)i,out var p)){
    Assert.True(projectiles.TryGetLifecycle(p.Handle,out var life));effects.Add(new{slot=i,type=(int)p.Type.Value,owner=(int)p.Spawner,x=p.PositionX,y=p.PositionY,vx=p.VelocityX,vy=p.VelocityY,timeLeft=life.TimeLeft,damage=p.Damage,originalDamage=p.OriginalDamage,knockBack=p.KnockBack,ai=new[]{p.Ai.Ai0,p.Ai.Ai1,p.Ai.Ai2}});
   }
   Assert.True(JsonElement.DeepEquals(original.GetProperty("projectiles"),JsonSerializer.SerializeToElement(effects)),"physical projectiles native"+ordinal);
   world.TryGetSpawn(out var spawn);Assert.Equal(original.GetProperty("spawnX").GetInt32(),spawn.X);Assert.Equal(original.GetProperty("spawnY").GetInt32(),spawn.Y);
   if(ordinal==106){Assert.Equal(original.GetProperty("quest").GetInt32(),world.VanillaAnglerQuest!.Quest);Assert.Equal(original.GetProperty("completedNames").EnumerateArray().Select(p=>p.GetString()),world.VanillaAnglerQuest.CompletedNames);cancellation.Cancel();}
  }
 }
 private static string Normalize(string value)=>new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
