using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;
namespace TerraRuntime.Core.Worlds.Generation;
/// <summary>Cold composition adapter; mutable slots remain in the existing NPC/projectile stores.</summary>
public sealed class WorldGenerationEntityEffects1458(RuntimeNpcStore npcs,RuntimeProjectileStore projectiles,bool sourceServer=false):IWorldGenerationEntityEffects1458 {
 public int CountNpcs(NpcTypeId type){int count=0;for(int i=0;i<Math.Min(200,npcs.Capacity);i++)if(npcs.TryGetActive((byte)i,out var npc)&&npc.Type==type.Value)count++;return count;}
 public bool RetainTownNpc(NpcTypeId type,float x,float y,bool homeless,int homeX,int homeY,out NpcHandle handle){if(!npcs.TryRetainGenerationTownNpc(type,x,y,homeless,homeX,homeY,sourceServer,out var npc)){handle=default;return false;}handle=npc.Handle;return true;}
 public bool TryCaptureNpc(byte slot,out NpcSnapshot snapshot){snapshot=default;return slot<200&&npcs.TryGetActive(slot,out snapshot);}
 public void MoveTownNpc(NpcHandle handle,float x,float y,bool homeless,int homeX,int homeY,int? directionX=null){
  if(!npcs.TryGet(handle,out var before))throw new InvalidOperationException("Lost cold town occupation.");
  var update=new NpcStateUpdate(before.Type,before.NetId,x,y,before.VelocityX,before.VelocityY,before.Target,before.Ai,before.Simulation with{DirectionX=directionX??before.Simulation.DirectionX,Homeless=homeless,HomeTileX=homeX,HomeTileY=homeY});
  if(!npcs.TryUpdateUnpublished(handle,in update,out _))throw new InvalidOperationException("Cold town movement refused.");
 }
 public void SpawnColdBoss(NpcTypeId type,NpcAiState ai,IWorldGenerationVanillaRandom random,int width,int height,Func<int,int,VanillaBossSpawnTile1458> readTile,bool skyblockLowTiles){
  if(!npcs.HasUnoccupiedGenerationPlayers)throw new NotSupportedException("Cold boss creation requires the established empty player owner.");
  Span<VanillaNpcRawPlayer1458> players=stackalloc VanillaNpcRawPlayer1458[255];for(int i=0;i<players.Length;i++)players[i]=VanillaNpcRawPlayer1458.Constructor((byte)i);
  if(!VanillaPlayerBossSpawnSearch1458.TryFind(type,width,height,in players[0],players,new SharedRandom(random),readTile,skyblockLowTiles,out var found,out int x,out int y))throw new NotSupportedException("Unowned cold boss search.");
  if(found&&SpawnNpc(type,x,y,1,0,ai,random,out var handle)){
   FinishNpc(handle,0,0,20,false);
   if(sourceServer&&!npcs.TryPublishPendingBirth(handle))throw new InvalidOperationException("Cold boss birth publication refused.");
  }
 }
 public bool SpawnNpc(NpcTypeId type,int bottomX,int bottomY,byte startSlot,ushort target,NpcAiState ai,IWorldGenerationVanillaRandom random,out NpcHandle handle){
  ArgumentNullException.ThrowIfNull(random);npcs.SetVanillaSpawnRandomSource(new SharedRandom(random));
  var request=new NpcStateUpdate(type.Value,checked((short)type.Value),0,0,0,0,target,ai,NpcSimulationState.Initial with{TimeLeft=VanillaNpcDefinitionCatalog.NewNpcTimeLeft});
  if(!npcs.TrySpawnGenerationAtBottomCenter(in request,bottomX,bottomY,startSlot,sourceServer,out var birth)){handle=default;return false;}handle=birth.Handle;return true;
 }
 public void FinishNpc(NpcHandle handle,float velocityX,float velocityY,int lifetimeMultiplier,bool sourceNetUpdate){
  // NewNPC's maxNPCs sentinel has no active occupation; caller-side draws still happened before this call.
  if(!handle.IsAssigned)return;
  if(lifetimeMultiplier<=0||!npcs.TryGet(handle,out var before))throw new InvalidOperationException("Lost cold NPC birth.");
  var update=new NpcStateUpdate(before.Type,before.NetId,before.PositionX,before.PositionY,velocityX,velocityY,before.Target,before.Ai,before.Simulation with{TimeLeft=checked(before.Simulation.TimeLeft*lifetimeMultiplier)}){RequiresImmediateSync=sourceNetUpdate};
  if(!npcs.TryUpdateUnpublished(handle,in update,out _))throw new InvalidOperationException("Cold NPC finish refused.");
 }
 public void SpawnProjectile(ProjectileTypeId type,byte owner,float topLeftX,float topLeftY,float velocityX,float velocityY,short damage,float knockBack,ProjectileAiState ai,bool sourceNetUpdate=false){
  var update=new ProjectileStateUpdate(type,owner,topLeftX,topLeftY,velocityX,velocityY,ai,0,damage,knockBack,0);
  if(!projectiles.TrySpawnVanilla(in update,timeLeftOverride:null,out _,sourceNetUpdate))throw new InvalidOperationException("Cold projectile birth refused.");
 }
 private sealed class SharedRandom(IWorldGenerationVanillaRandom random):IVanillaNpcRandom {public int NextInt32(int min,int max)=>random.Next(min,max);public double NextDouble()=>random.NextDouble();}
}
