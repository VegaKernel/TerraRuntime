using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;
namespace TerraRuntime.WorldGeneration.Runtime;
public sealed partial class Workspace {
 private WorldSpawnPoint[] vanillaExtraSpawnPoints=[];
 internal WorldSpawnPoint[] CaptureVanillaExtraSpawnPoints()=>vanillaExtraSpawnPoints.ToArray();
 internal void SetVanillaExtraSpawnPoints(ReadOnlySpan<WorldSpawnPoint> points)=>vanillaExtraSpawnPoints=points.ToArray();
 internal void CleanupGeneratedTownNpcPositions(GenerationTileFraming1458 framing,WorldGenerationPoint originalSpawn){
  if(!TryGetSpawn(out var spawn))throw new InvalidOperationException("Missing source spawn.");
  bool moved=spawn!=originalSpawn;
  for(int index=0;index<generatedTownNpcs.Count;index++){
   var npc=generatedTownNpcs[index].Projection;if(npc.NetId==VanillaNpcIds.OldMan.Value)continue;
   if(npc.NetId<=0||npc.NetId>ushort.MaxValue||!VanillaNpcDefinitionCatalog.TryGet(new((ushort)npc.NetId),out var definition))throw new NotSupportedException("Unowned generation NPC dimensions.");
   int width=definition.Width,height=definition.Height;
   if(width<=0||height<=0)throw new NotSupportedException("Unowned generation NPC dimensions.");
   int x=(int)((npc.X+width/2f)/16f),y=(int)((npc.Y+height)/16f);
   if(x<2||y<2||x>=WidthTiles-2||y>=HeightTiles-2)continue;
   if(moved){x=spawn.X+x-originalSpawn.X;y=spawn.Y;npc=npc with{HomeTileX=spawn.X,HomeTileY=spawn.Y};}
   TownScan(framing,x,y,out bool empty,out _);
   if(!empty){TownAdjustedFloor(framing,x,y,spawn.Y,out x,out y);npc=npc with{X=x*16f+8f-width/2f,Y=y*16f-height};}
   else if(moved)npc=npc with{X=x*16f+8f-width/2f,Y=y*16f-height};
   UpdateGeneratedTownProjection(index,npc);
  }
 }
 private bool TownSolid(GenerationTileFraming1458 framing,int x,int y){
  if((uint)x>=(uint)WidthTiles||(uint)y>=(uint)HeightTiles)return false;
  var tile=TileStore.Get(x,y);return tile.IsActive&&!tile.IsActuated&&!tile.IsHalfBrick&&tile.SourceSlope==0&&framing.GenerationSolid(tile.Type)&&!VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
 }
 private void TownScan(GenerationTileFraming1458 framing,int x,int y,out bool empty,out bool floor){
  empty=true;floor=false;for(int dx=0;dx<3&&empty;dx++)for(int dy=0;dy<3;dy++)if(TownSolid(framing,x+dx,y+dy)){empty=false;break;}
  for(int dx=0;dx<3;dx++)if(TownSolid(framing,x+dx,y+3)){floor=true;break;}
 }
 private void TownAdjustedFloor(GenerationTileFraming1458 framing,int x,int y,int spawnY,out int resultX,out int resultY){
  int left=x-1,top=y-2;bool empty=false,floor=false;
  while(!empty&&top>spawnY-10){TownScan(framing,left,top,out empty,out floor);if(!empty)top--;}
  while(!floor&&top<spawnY+10){TownScan(framing,left,top,out empty,out floor);if(!floor)top++;}
  resultX=left+1;resultY=top+2;
 }
}
