using TerraRuntime.Contracts.Runtime;
namespace TerraRuntime.Contracts.Gameplay;
public readonly record struct VanillaBossSpawnTile1458(bool Solid,bool HousingWall,bool Lava);
/// <summary>Synchronous cold creation requests executed by the existing canonical entity owners.</summary>
public interface IWorldGenerationEntityEffects1458 {
 int CountNpcs(NpcTypeId type);
 bool RetainTownNpc(NpcTypeId type,float x,float y,bool homeless,int homeX,int homeY,out NpcHandle handle);
 bool TryCaptureNpc(byte slot,out NpcSnapshot snapshot);
 void MoveTownNpc(NpcHandle handle,float x,float y,bool homeless,int homeX,int homeY);
 void SpawnColdBoss(NpcTypeId type,NpcAiState ai,IWorldGenerationVanillaRandom random,int width,int height,Func<int,int,VanillaBossSpawnTile1458> readTile,bool skyblockLowTiles);
 bool SpawnNpc(NpcTypeId type,int bottomX,int bottomY,byte startSlot,ushort target,NpcAiState ai,IWorldGenerationVanillaRandom random,out NpcHandle handle);
 void FinishNpc(NpcHandle handle,float velocityX,float velocityY,int lifetimeMultiplier,bool sourceNetUpdate);
 void SpawnProjectile(ProjectileTypeId type,byte owner,float topLeftX,float topLeftY,float velocityX,float velocityY,short damage,float knockBack,ProjectileAiState ai);
}
