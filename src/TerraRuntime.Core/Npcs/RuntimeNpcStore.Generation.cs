using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;
namespace TerraRuntime.Core.Npcs;
public sealed partial class RuntimeNpcStore {
 internal bool HasUnoccupiedGenerationPlayers=>_spawnContext is not null&&_spawnContext.Invoke().ActivePlayers==0;
 internal bool TrySpawnGenerationAtBottomCenter(in NpcStateUpdate update,int bottomX,int bottomY,byte startSlot,bool sourceServer,out NpcSnapshot snapshot)
  =>TrySpawnVanillaCreation(in update,out snapshot,startSlot,deferBirth:sourceServer,(bottomX,bottomY));
 internal bool TryRetainGenerationTownNpc(NpcTypeId type,float x,float y,bool homeless,int homeX,int homeY,bool sourceServer,out NpcSnapshot snapshot){
  // The name/variation resolver belongs to the existing world-generation projection.
  // Ordinary Guide/OldMan SetDefaults has no creation draw; GoodWorld must create BEFORE name resolution.
  if(type!=VanillaNpcIds.Guide&&type!=VanillaNpcIds.OldMan||_spawnContext?.Invoke().GoodWorld==true)
   throw new NotSupportedException("Town birth needs its qualified source creation/name ordering.");
  var update=new NpcStateUpdate(type.Value,checked((short)type.Value),x,y,0,0,255,default,NpcSimulationState.Initial with{TimeLeft=VanillaNpcDefinitionCatalog.NewNpcTimeLeft,Homeless=homeless,HomeTileX=homeX,HomeTileY=homeY});
  return TrySpawnVanillaCreation(in update,out snapshot,0,deferBirth:sourceServer);
 }
}
