using System.Buffers;
using TerraRuntime.Network;
using TerraRuntime.Protocol;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;
using Xunit;
namespace TerraRuntime.Tests.Larva1458;
public sealed partial class LarvaQueenBeeTests1458
{
 [Fact] public void Original_larva_roles_join_world_rng_and_boss_birth_before_publication()
 {
  foreach(string edition in new[]{"Linux","Windows"})foreach(string repeat in new[]{"first","repeat"})
  {
   using var stream=typeof(LarvaQueenBeeTests1458).Assembly.GetManifestResourceStream("Larva"+edition+repeat)!;
   using var zip=new GZipStream(stream,CompressionMode.Decompress);using var doc=JsonDocument.Parse(zip);
   Assert.Equal(16,doc.RootElement.GetArrayLength());foreach(var row in doc.RootElement.EnumerateArray())RunSource(row,edition);
  }
  using var extra=typeof(LarvaQueenBeeTests1458).Assembly.GetManifestResourceStream("LarvaLinuxfirst")!;
  using var unzip=new GZipStream(extra,CompressionMode.Decompress);using var rows=JsonDocument.Parse(unzip);
  foreach(string fault in new[]{"player","terrain","npc","rng","replace","throw","reenter"})RunSource(rows.RootElement[0],"Linux",fault);
  RunIngress();
  RunGeometryRows(rows.RootElement[0]);
 }
 private static void RunSource(JsonElement row,string edition,string? fault=null,short storedSlot=-1,short storedItem=0,short held=0)
 {
  string name=row.GetProperty("name").GetString()!;int mode=row.GetProperty("mode").GetInt32();bool client=row.GetProperty("role").GetInt32()==1;
  using var f=new LarvaRuntimeFixture1458(edition=="Windows",sourcePlayerZero:true,mode:mode,good:name=="good",tenth:name=="tenth");
  for(int x=0;x<600;x++)for(int y=0;y<300;y++)f.Tiles.Set(x,y,default);
  foreach(var c in row.GetProperty("before").EnumerateArray())f.Tiles.Set(c.GetProperty("x").GetInt32(),c.GetProperty("y").GetInt32(),Cell(c));
  f.Member.PositionX=name is "far" or "inactive"?0:4000;f.Member.PositionY=name is "far" or "inactive"?0:1600;
  if(storedSlot>=0)f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,new(f.Connection.Player.Slot,storedSlot,1,0,storedItem,0)));
  if(held>0)f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,new(f.Connection.Player.Slot,0,1,0,held,0)));
  if(name=="dead")f.Member.IsDead=true;if(name=="inactive")f.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
  if(name=="full")for(int i=0;i<200;i++){var u=new NpcStateUpdate(1,1,0,0,0,0,255,default,NpcSimulationState.Initial);Assert.True(f.Npcs.TrySpawnVanilla(in u,out _));}
  if(name=="duplicate"){var u=new NpcStateUpdate(222,222,0,0,0,0,255,default,NpcSimulationState.Initial);Assert.True(f.Npcs.TrySpawnVanilla(in u,out _));}
  while(Queue(f.NpcOutbound).TryRead(out _)){}
  f.Random.CopyStateFrom(Random(row.GetProperty("beforeRandom")));var world=f.Composition.WorldTileAuthority;
  Assert.True(world.TryCaptureToolTreeWorld(out var context));Assert.NotNull(context);
  Assert.True(f.Players.TryCaptureToolTreeActors(out var actors));Assert.NotNull(actors);
  var image=new ToolWorldTileOverlay1458(f.Tiles);Assert.True(world.TryCreateToolMutationPlan(image,out var plan));Assert.NotNull(plan);
  using(plan!)
  {
   var random=client?f.Random.Clone():plan!.SourceRandom;
   Assert.True(world.TryCreateToolSourcePass(image,random,client,false,context!.Facts,actors!.Actors,client?null:plan,out var pass));
   Assert.True(pass!.TryKillTile(251,101,name=="fail",false,false,out _),$"{edition}/{name}/{mode}/role{row.GetProperty("role")}");
   Assert.True(random.HasSameState(Random(row.GetProperty("random"))),$"RNG {edition}/{name}/{mode}/role{row.GetProperty("role")}");
   Assert.True(f.Tiles.Get(251,101).IsActive);
   foreach(var c in row.GetProperty("after").EnumerateArray())
   {
    int x=c.GetProperty("x").GetInt32(),y=c.GetProperty("y").GetInt32();
    var actual=image.TryGetStagedAfterimage(x,y,out var after)?after:f.Tiles.Get(x,y);var expected=Cell(c);
    Assert.Equal(expected.IsActive,actual.IsActive);Assert.Equal(expected.Type,actual.Type);Assert.Equal(expected.Flags,actual.Flags);
    Assert.Equal(expected.Wall,actual.Wall);Assert.Equal(expected.LiquidAmount,actual.LiquidAmount);Assert.Equal(expected.Shape,actual.Shape);
    if(expected.IsActive && expected.Type==231){Assert.Equal(expected.FrameX,actual.FrameX);Assert.Equal(expected.FrameY,actual.FrameY);}
   }
   if(client)return;
   if(fault is "player" or "terrain" or "npc" or "rng" or "equipment-mode")
   {
    if(fault=="player")f.Member.PositionX+=1;
    if(fault=="equipment-mode")f.Players.SetCombatEquipmentWorldModes(true,false);
    if(fault=="terrain")f.Tiles.Set(200,150,default);
    if(fault=="npc"){var u=new NpcStateUpdate(1,1,0,0,0,0,255,default,NpcSimulationState.Initial);Assert.True(f.Npcs.TrySpawnVanilla(in u,out _));}
    if(fault=="rng")f.Random.Next();var live=f.Random.Clone();
    Assert.False(plan!.TrySeal());Assert.False(plan.TryAdoptWholeUnpublished(out _));
    Assert.True(f.Tiles.Get(251,101).IsActive);Assert.True(f.Random.HasSameState(live));
    Assert.Equal(fault=="npc"?1:0,f.Npcs.ActiveCount);return;
   }
   int oldCount=f.Npcs.ActiveCount;Assert.True(plan!.TrySeal());Assert.True(plan.ValidateExternalOwners());Assert.True(plan.IsCurrentOwned);
   var offset=plan.CapturePublicationOffset();Assert.True(plan.TryAdoptWholeUnpublished(out var publication));Assert.NotNull(publication);
   Assert.True(f.Random.HasSameState(Random(row.GetProperty("random"))));Assert.Equal(name=="fail",f.Tiles.Get(251,101).IsActive);
   var births=row.GetProperty("born").EnumerateArray().Where(x=>name!="duplicate" || x.GetProperty("slot").GetInt32()!=199).ToArray();
   Assert.Equal(births.Length,f.Npcs.ActiveCount-oldCount);
   foreach(var expected in births)
   {
    Assert.True(f.Npcs.TryGetActive((byte)expected.GetProperty("slot").GetInt32(),out var actual));Assert.Equal(222,actual.Type);
    Assert.Equal(expected.GetProperty("position").GetProperty("x").GetUInt32(),BitConverter.SingleToUInt32Bits(actual.PositionX));
    Assert.Equal(expected.GetProperty("position").GetProperty("y").GetUInt32(),BitConverter.SingleToUInt32Bits(actual.PositionY));
    Assert.Equal(expected.GetProperty("width").GetInt32(),actual.Simulation.HitboxOverride?.Width ?? 66);Assert.Equal(expected.GetProperty("height").GetInt32(),actual.Simulation.HitboxOverride?.Height ?? 66);
    Assert.Equal(expected.GetProperty("life").GetInt32(),actual.Simulation.LifeMax);Assert.Equal(expected.GetProperty("damage").GetInt32(),actual.Simulation.BaseDamage);
    Assert.Equal(expected.GetProperty("defense").GetInt32(),actual.Simulation.BaseDefense);Assert.Equal(expected.GetProperty("timeLeft").GetInt32(),actual.Simulation.TimeLeft);
    Assert.Equal(expected.GetProperty("target").GetUInt16(),actual.Target);
   }

   int callbacks=0;
   bool injected=false;
   f.NpcSink.Before=(kind,npc)=>
   {
    callbacks++;Assert.Equal(name=="fail",f.Tiles.Get(251,101).IsActive);Assert.True(f.Random.HasSameState(Random(row.GetProperty("random"))));
    if(injected && fault=="replace" && !f.Npcs.TryGet(npc.Handle,out _))return;
    if(kind==NpcStateCommitKind.Despawn){Assert.False(f.Npcs.TryGet(npc.Handle,out _));return;}
    Assert.True(f.Npcs.TryGet(npc.Handle,out var current));Assert.Equal(npc,current);
    if(!injected && fault=="replace")
    {
     injected=true;Assert.True(f.Npcs.TryDespawn(npc.Handle));var u=new NpcStateUpdate(1,1,0,0,0,0,255,default,NpcSimulationState.Initial);
     Assert.True(f.Npcs.TrySpawnVanilla(in u,out _));
    }
    if(!injected && fault=="throw"){injected=true;throw new LarvaObserverException();}
    if(fault=="reenter")Assert.False(publication!.TryPublishThrough(offset));
   };
   if(fault=="throw")Assert.Throws<LarvaObserverException>(()=>publication!.TryPublishThrough(offset));
   else Assert.True(publication!.TryPublishThrough(in offset));
   Assert.True(publication!.TryPublishThrough(in offset));
   var wire=new List<byte[]>();while(Queue(f.NpcOutbound).TryRead(out var frame))wire.Add(frame.Bytes.ToArray());
   var original=row.GetProperty("frames").EnumerateArray().Select(x=>Convert.FromHexString(x.GetString()!)).Where(x=>x[2] is 23 or 82).ToArray();
   if(fault=="replace")
   {
    Assert.True(injected);Assert.DoesNotContain(wire,frame=>frame[2]==82);return;
   }
   Assert.Equal(original.Length,wire.Count);
   for(int index=0;index<original.Length;index++)Assert.Equal(original[index],wire[index]);
   Assert.Equal(births.Length*2,callbacks);

  }
 }
 private static void RunIngress()
 {
  using var f=new LarvaRuntimeFixture1458(false);
  for(int x=0;x<600;x++)f.Tiles.Set(x,150,new WorldTile{Type=0,Flags=WorldTileFlags.Active});
  for(int dx=0;dx<3;dx++)for(int dy=0;dy<3;dy++)f.Tiles.Set(108+dx,100+dy,new WorldTile{Type=231,FrameX=(short)(dx*18),FrameY=(short)(dy*18),Flags=WorldTileFlags.Active});
  f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));f.State.Tick();
  f.Apply(new PlayerEquipmentRuntimeCommand(f.Connection,new(f.Connection.Player.Slot,0,1,0,3509,0)));
  f.Apply(new PlayerInventorySyncCompleteRuntimeCommand(f.Connection));f.Move(false);f.State.Tick();f.Move(true);f.State.Tick();
  var before=f.Random.Clone();
  for(int hit=0;hit<3;hit++)
  {
   f.Apply(new ClientTileManipulationRuntimeCommand(f.Connection,new(0,109,101,(short)(hit==2?0:1),0)));
   if(hit<2)f.Apply(new ClientTilePickingRuntimeCommand1458(f.Connection,new(f.Connection.Player.Slot.Value,109,101,35)));
   Assert.Equal(hit+1,f.Composition.WorldTileAuthority.CommittedToolActions);
   if(hit<2)
   {
    Assert.True(f.Tiles.Get(109,101).IsActive);Assert.Equal(0,f.Npcs.ActiveCount);
    int wait=0;do {f.State.Tick();Assert.True(++wait<100);}while(f.Member.ItemPhase!.Value.ToolUse!.Clocks.ToolTime>0);
   }
  }
  for(int dx=0;dx<3;dx++)for(int dy=0;dy<3;dy++)Assert.False(f.Tiles.Get(108+dx,100+dy).IsActive);
  Assert.Equal(1,f.State.AppliedNpcSpawns);Assert.Equal(1,f.Npcs.ActiveCount);
  Assert.True(f.Npcs.TryGetActive(199,out var boss));Assert.Equal(222,boss.Type);Assert.Equal(f.Connection.Player.Slot.Value,boss.Target);
  Assert.Equal(18740,boss.Simulation.TimeLeft);Assert.False(f.Random.HasSameState(before));
  Assert.NotNull(f.Member.ItemPhase!.Value.ToolUse);Assert.Equal(3UL,f.Member.ItemPhase!.Value.ToolUse!.ActionSerial);
 }
 private sealed class LarvaObserverException:Exception;
 private static WorldTile Cell(JsonElement v)
 {
  var t=new WorldTile{Type=v.GetProperty("type").GetUInt16(),Wall=v.GetProperty("wall").GetUInt16(),FrameX=v.GetProperty("frameX").GetInt16(),FrameY=v.GetProperty("frameY").GetInt16(),LiquidAmount=v.GetProperty("liquid").GetByte()};
  int h=v.GetProperty("header").GetInt32(),b1=v.GetProperty("b1").GetInt32(),b3=v.GetProperty("b3").GetInt32();
  t.TileColor=(byte)(h&31);t.WallColor=(byte)(b1&31);t.LiquidKind=(WorldLiquidKind)((b1&96)>>5);
  if((h&32)!=0)t.Flags|=WorldTileFlags.Active;if((h&64)!=0)t.Flags|=WorldTileFlags.Inactive;if((h&128)!=0)t.Flags|=WorldTileFlags.WireRed;
  if((h&256)!=0)t.Flags|=WorldTileFlags.WireBlue;if((h&512)!=0)t.Flags|=WorldTileFlags.WireGreen;if((h&2048)!=0)t.Flags|=WorldTileFlags.Actuator;
  if((b1&128)!=0)t.Flags|=WorldTileFlags.WireYellow;if((b3&32)!=0)t.Flags|=WorldTileFlags.InvisibleBlock;if((b3&64)!=0)t.Flags|=WorldTileFlags.InvisibleWall;
  if((b3&128)!=0)t.Flags|=WorldTileFlags.FullbrightBlock;if((h&32768)!=0)t.Flags|=WorldTileFlags.FullbrightWall;
  Assert.True(t.TrySetSourceShape(v.GetProperty("halfBrick").GetBoolean(),v.GetProperty("slope").GetByte()));return t;
 }
 private static VanillaUnifiedRandom1458 Random(JsonElement v)
 {
  var r=new VanillaUnifiedRandom1458(0);v.GetProperty("state").EnumerateArray().Select(x=>x.GetInt32()).ToArray().CopyTo(Seed(r),0);Cursor(r)=v.GetProperty("cursor").GetUInt32();return r;
 }
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="_queue")]private static extern ref BoundedOutboundQueue Queue(TerrariaConnectionOutboundQueue queue);
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="seedArray")]private static extern ref int[] Seed(VanillaUnifiedRandom1458 r);
 [UnsafeAccessor(UnsafeAccessorKind.Field,Name="inext")]private static extern ref uint Cursor(VanillaUnifiedRandom1458 r);
}
