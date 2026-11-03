using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;
namespace TerraRuntime.Application;
internal static partial class RuntimeBossSummonSpawn1458 {
 internal static bool TryFindLarva(WorldTileStore tiles,ToolWorldTileOverlay1458 overlay,
  in VanillaNpcRawPlayer1458 caster,ReadOnlySpan<NpcRawPlayerSlotSnapshot1458> players,
  VanillaUnifiedRandom1458 random,bool allowSkyblockMidair,out bool found,out int bottomX,out int bottomY){
  found=false;bottomX=bottomY=0;
  if(!ReferenceEquals(tiles,overlay.Tiles)||!overlay.IsCurrentOwned||players.Length!=255)return false;
  Span<VanillaNpcRawPlayer1458> facts=stackalloc VanillaNpcRawPlayer1458[255];for(int i=0;i<facts.Length;i++)facts[i]=players[i].Facts;
  VanillaBossSpawnTile1458 Read(int x,int y){
   var tile=overlay.TryGetStagedAfterimage(x,y,out var staged)?staged:tiles.Get(x,y);
   bool house=VanillaWallDefinitionCatalog.TryGet(tile.WallType,out var wall)&&wall.IsHousingWall;
   return new(Solid(tile),house,tile.LiquidAmount>0&&tile.LiquidKind==WorldLiquidKind.Lava);
  }
  bool accepted=VanillaPlayerBossSpawnSearch1458.TryFind(VanillaNpcIds.QueenBee,tiles.Dimensions.WidthTiles,tiles.Dimensions.HeightTiles,in caster,facts,new SharedSearchRandom(random),Read,allowSkyblockMidair,out found,out bottomX,out bottomY);
  return accepted&&overlay.IsCurrentOwned;
 }
 private sealed class SharedSearchRandom(VanillaUnifiedRandom1458 random):IVanillaNpcRandom {
  public int NextInt32(int min,int max)=>random.Next(min,max);public double NextDouble()=>random.NextDouble();
 }
}
