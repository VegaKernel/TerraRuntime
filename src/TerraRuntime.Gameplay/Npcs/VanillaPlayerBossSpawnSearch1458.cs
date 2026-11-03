using TerraRuntime.Contracts.Gameplay;
namespace TerraRuntime.Gameplay.Npcs;
/// <summary>The existing plain-player Larva search shared with cold Ghost creation; no entity or terrain owner.</summary>
public static class VanillaPlayerBossSpawnSearch1458 {
 public static bool TryFind(NpcTypeId type,int width,int height,in VanillaNpcRawPlayer1458 caster,ReadOnlySpan<VanillaNpcRawPlayer1458> players,IVanillaNpcRandom random,Func<int,int,VanillaBossSpawnTile1458> readTile,bool skyblockLowTiles,out bool found,out int bottomX,out int bottomY){
  found=false;bottomX=bottomY=0;
  if(type!=VanillaNpcIds.QueenBee&&type!=VanillaNpcIds.Ghost||players.Length!=255||!caster.IsValid||caster.PositionX<0||caster.PositionY<0||caster.PositionX>=width*16f||caster.PositionY>=height*16f)return false;
  int cx=(int)caster.PositionX>>4,cy=(int)caster.PositionY>>4;
  int left=Math.Clamp(cx-84,0,width),right=Math.Clamp(cx+84,0,width),top=Math.Clamp(cy-52,0,height),bottom=Math.Clamp(cy+52,0,height);
  if(left>=right||top>=bottom)return false;
  int attempts=type==VanillaNpcIds.Ghost?200:500,samples=type==VanillaNpcIds.Ghost?30:50;bool candidate=false;int chosenX=0,chosenY=0;
  for(int attempt=0;attempt<attempts;attempt++){
   bool final=attempt==attempts-1,midair=skyblockLowTiles&&type==VanillaNpcIds.QueenBee&&attempt>=attempts/2;
   for(int sample=0;sample<samples;sample++){
    int x=random.NextInt32(left,right),y=random.NextInt32(top,bottom);var start=readTile(x,y);
    if(start.Solid||!final&&start.HousingWall)continue;
    for(int floor=y;floor<bottom;floor++){
     if(!(midair||readTile(x,floor).Solid)||!final&&x>=cx-62&&x<cx+62&&y>=cy-39&&y<cy+39)continue;
     chosenX=x;chosenY=floor;candidate=true;break;
    }
    if(candidate&&!final&&!Space(chosenX,chosenY))candidate=false;
    if(candidate)break;
   }
   if(candidate&&(final||OffScreens(chosenX,chosenY,players))){found=true;bottomX=chosenX*16+8;bottomY=chosenY*16;return true;}
  }
  return true;
  bool Space(int x,int y){if(x-1<0||x+1>=width||y-3<0||y>=height)return false;for(int dx=-1;dx<1;dx++)for(int dy=-3;dy<0;dy++){var tile=readTile(x+dx,y+dy);if(tile.Solid||tile.Lava)return false;}return true;}
 }
 private static bool OffScreens(int x,int y,ReadOnlySpan<VanillaNpcRawPlayer1458> players){
  foreach(var player in players){if(!player.Active)continue;int left=(int)(player.PositionX+player.Width*.5f-960f-62f),top=(int)(player.PositionY+player.Height*.5f-600f-39f);if(x*16<left+2044&&x*16+16>left&&y*16<top+1278&&y*16+16>top)return false;}return true;
 }
}
