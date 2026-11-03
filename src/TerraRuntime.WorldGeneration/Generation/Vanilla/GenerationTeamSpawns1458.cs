using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
namespace TerraRuntime.WorldGeneration.Vanilla;
// Bounded ordinary-landmass team producer. Other seed flags require their existing landmass owners.
internal static class GenerationTeamSpawns1458 {
 internal static void Generate(Workspace workspace,IWorldGenerationVanillaRandom random,GenerationTileFraming1458 framing,bool surface){
  if(!workspace.TryGetSpawn(out var spawn)||!workspace.TryGetLayers(out var layers))throw new InvalidOperationException("Source spawn/layers required.");
  var query=new Search(workspace,random,framing,surface,layers.WorldSurface);var selected=new List<WorldSpawnPoint>();
  for(int index=1;index<6;index++){
   var fallback=query.Fallback(index);bool left=true,found=false;
   for(int attempt=0;attempt<500;attempt++){
    int x=left?random.Next(60,workspace.WidthTiles/2):random.Next(workspace.WidthTiles/2,workspace.WidthTiles-60);
    int maxY=Math.Max(70,(int)layers.WorldSurface),y=60;
    if(!surface){maxY=workspace.HeightTiles-250;y=random.Next((int)layers.WorldSurface+200,maxY);}
    left=!left;y=query.Down(x,y,maxY);
    if(query.Find(selected,x,y,out var point)){selected.Add(point);found=true;break;}
   }
   if(!found)selected.Add(fallback);
  }
  WorldSpawnPoint[] points=new WorldSpawnPoint[6];points[0]=new((short)spawn.X,(short)spawn.Y);
  for(int index=1;index<6;index++){int chosen=random.Next(selected.Count);points[index]=selected[chosen];selected.Remove(points[index]);}
  workspace.SetVanillaExtraSpawnPoints(points);
 }
 private sealed class Search(Workspace workspace,IWorldGenerationVanillaRandom random,GenerationTileFraming1458 framing,bool surface,double worldSurface){
  private int Width=>workspace.WidthTiles;private int Height=>workspace.HeightTiles;
  private WorldTile Tile(int x,int y)=>workspace.TileStore.Get(x,y);
  private bool Solid(TileTypeId type)=>framing.GenerationSolid((ushort)type.Value);
  private bool Dungeon(ushort type)=>type is 41 or 43 or 44;
  private bool InWorld(int x,int y,int margin)=>x>=margin&&y>=margin&&x<Width-margin&&y<Height-margin;
  internal int Down(int x,int y,int maxY){int original=y;for(;y<maxY&&y<Height;y++){var tile=Tile(x,y);if(tile.IsActive&&tile.LiquidAmount==0&&Solid(tile.TileType))return y;}return original;}
  internal WorldSpawnPoint Fallback(int index){
   float fraction=index/6f;
   if(surface)fraction=fraction<.5f?Lerp(0,.4f,fraction/.5f):Lerp(.6f,1,(fraction-.5f)/.5f);
   int x=(int)(Width*fraction),y=surface?50:(int)(Height*.5f),max=surface?(int)worldSurface:Height-250;
   for(;;){var tile=Tile(x,y);if(!tile.IsActive||!Dungeon(tile.Type)&&tile.Type!=226)break;y--;if(y<=50)break;}
   return new((short)x,(short)Down(x,y,max));
  }
  private static float Lerp(float a,float b,float t)=>a+(b-a)*Math.Clamp(t,0,1);
  private bool BodySolid(float x,float y,int height)=>VanillaWorldSolidCollision.Intersects(workspace.TileStore,x,y,20,height,Solid);
  private bool Invalid(int x,int y){
   for(int dx=-50;dx<=50;dx++)for(int dy=-50;dy<=50;dy++)if(InWorld(x+dx,y+dy,2)){
    var tile=Tile(x+dx,y+dy);if(tile.IsActive&&tile.Type is 70 or 23 or 199 or 661 or 662 or 41 or 43 or 44 or 226 or 57 or 633 or 397 or 396 or 25 or 163 or 112 or 398 or 400 or 203 or 200 or 234 or 399 or 401 or 117 or 164 or 116 or 402 or 403 or 379 or 135 or 37 or 189 or 196 or 460 or 717 or 718 or 719)return true;
   }return false;
  }
  private bool Hazard(ushort type)=>type is 135 or 48 or 232 or 750 or 138 or 484 or 664 or 665 or 711 or 712 or 713 or 714 or 715 or 716;
  private bool Conditions(List<WorldSpawnPoint> selected,int x,int y,WorldTile center,WorldTile left,WorldTile right){
   if(Hazard(center.Type)||left.IsActive&&Hazard(left.Type)||right.IsActive&&Hazard(right.Type)||Dungeon(center.Type)||center.Type==226)return false;
   if((center.IsHalfBrick||center.SourceSlope is 1 or 2)&&((left.LiquidAmount>0&&left.LiquidKind==WorldLiquidKind.Lava)||(right.LiquidAmount>0&&right.LiquidKind==WorldLiquidKind.Lava)))return false;
   foreach(var previous in selected)if(Math.Abs(previous.X-x)<250&&Math.Abs(previous.Y-y)<250)return false;return true;
  }
  private bool Hurts(float px,float py){
   int x0=Math.Max(0,(int)(px/16)-1),x1=Math.Min(Width,(int)((px+20)/16)+2),y0=Math.Max(0,(int)(py/16)-1),y1=Math.Min(Height-40,(int)((py+42)/16)+2);
   for(int x=x0;x<x1;x++)for(int y=y0;y<y1;y++){
    var tile=Tile(x,y);if(!tile.IsActive||tile.IsActuated)continue;
    bool suffocate=tile.Type is 53 or 112 or 116 or 123 or 224 or 234;
    if(!suffocate&&tile.Type is not(37 or 58 or 76 or 684 or 48 or 232 or 750 or 32 or 69 or 352 or 655 or 484))continue;
    int inset=suffocate?2:0;float top=y*16+(tile.IsHalfBrick?8:0),bottom=y*16+16;
    if(px+20-inset<x*16||px+inset>x*16+16||py+42-inset<top-.5f||py+inset>bottom+.5f)continue;
    if(tile.SourceSlope>0){if(suffocate)continue;int off=0;if(tile.SourceSlope is 1 or 3&&px>x*16)off++;if(tile.SourceSlope is 2 or 4&&px+20<x*16+16)off++;if(tile.SourceSlope is 3 or 4&&py>top)off++;if(tile.SourceSlope is 1 or 2&&py+42<bottom)off++;if(off==2)continue;}
    return true;
   }return false;
  }
  private bool WallBad(WorldTile tile,int y)=>tile.Wall>0; // source avoidWalls dominates other wall predicates in this profile.
  internal bool Find(List<WorldSpawnPoint> selected,int originX,int originY,out WorldSpawnPoint point){
   point=default;int left=Math.Max(0,originX-50),top=Math.Max(0,originY-50),right=left+51,bottom=top+51;
   for(int attempt=0;attempt<250;attempt++){
    int x=Math.Clamp(random.Next(left,right+1),45,Width-45),y=Math.Clamp(random.Next(top,bottom+1),45,Height-45);
    bool Contains(int a,int b)=>a>=left&&a<right&&b>=top&&b<bottom;
    if(!Contains(x,y))continue;float px=x*16-2,py=y*16-42;
    if(BodySolid(px,py,42)||WallBad(Tile(x,y),y)||Invalid(x,y))continue;
    int fall=0;bool outOfRange=false;
    for(;fall<100;fall++){
     if(!Contains(x,y+fall)){outOfRange=true;break;}
     var ground=Tile(x,y+fall);py=(y+fall)*16-42;
     if(!BodySolid(px,py,43))continue;
     if(ground.IsActive&&!ground.IsActuated&&Solid(ground.TileType))break;
    }
    if(outOfRange)continue;
    int bodyX=(int)px/16,bodyY=(int)py/16,footX=(int)(px+10)/16,footY=(int)(py+42)/16;
    if(Invalid(bodyX,bodyY))continue;var body=Tile(bodyX,bodyY);var groundTile=Tile(footX,footY);var groundLeft=Tile(footX-1,footY);var groundRight=Tile(footX+1,footY);
    if(!Conditions(selected,footX,footY,groundTile,groundLeft,groundRight)||groundTile.LiquidAmount>0)continue;
    bool IsFloor(WorldTile tile)=>tile.IsActive&&!tile.IsActuated&&Solid(tile.TileType)&&!VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
    if(!IsFloor(groundLeft)&&!IsFloor(groundRight))continue;
    if(WallBad(body,bodyY)||VanillaWorldCollision.GetLiquidContacts(workspace.TileStore,px,py,20,42).Wet||VanillaWorldCollision.LavaCollision(workspace.TileStore,px,py,20,42)||Hurts(px,py)||BodySolid(px,py,42)||fall>=99)continue;
    bool blocked=false;
    foreach(var motion in new (float X,float Y)[]{(16,0),(-16,0),(0,16),(0,-16)}){
     var result=VanillaWorldCollision.TileCollision(workspace.TileStore,px-motion.X,py-motion.Y,motion.X,motion.Y,20,42,false,false,workspace.TileStore.Get,1,Solid);
     if(result.VelocityX!=motion.X||result.VelocityY!=motion.Y){blocked=true;break;}
    }
    if(blocked)continue;point=new((short)(int)(px/16f),(short)(int)(py/16f));return true;
   }return false;
  }
 }
}
