using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
namespace TerraRuntime.WorldGeneration.Vanilla;
internal static class GenerationExtraSpawnPreparation1458 {
 internal static void Prepare(Workspace workspace,IWorldGenerationVanillaRandom random,GenerationTileFraming1458 framing,bool skyblock,bool noTraps){
  if(!workspace.TryGetLayers(out var layers))throw new InvalidOperationException("Extra spawn preparation requires source layers.");
  var points=workspace.CaptureVanillaExtraSpawnPoints();
  for(int index=1;index<points.Length;index++)if(points[index].Y>=layers.WorldSurface&&points[index].Y<workspace.HeightTiles-200){
   PlaceTorches(workspace,random,framing,points[index].X,points[index].Y,skyblock);
   if(noTraps)for(int dy=0;dy<50;dy++)if(InWorld(workspace,points[index].X,points[index].Y+dy,5))foreach(int dx in new[]{0,-1,-2,1,2}){
    var tile=workspace.TileStore.Get(points[index].X+dx,points[index].Y+dy);
    if(tile.IsActive&&tile.Type is 135 or 442 or 428 or 48 or 443)framing.KillTile(points[index].X+dx,points[index].Y+dy);
   }
  }
 }
 private static bool InWorld(Workspace workspace,int x,int y,int margin=0)=>x>=margin&&y>=margin&&x<workspace.WidthTiles-margin&&y<workspace.HeightTiles-margin;
 private static bool Nearby(Workspace workspace,int x,int y,ushort type,int radius){for(int i=x-radius;i<=x+radius;i++)for(int j=y-radius;j<=y+radius;j++)if(InWorld(workspace,i,j)){var tile=workspace.TileStore.Get(i,j);if(tile.IsActive&&tile.Type==type)return true;}return false;}
 private static void PlaceTorches(Workspace workspace,IWorldGenerationVanillaRandom random,GenerationTileFraming1458 framing,int spawnX,int spawnY,bool skyblock){
  int radius=1;for(int attempt=0;attempt<2000;attempt++){
   if(radius<120)radius++;
   int x=random.Next(spawnX-radius,spawnX+radius),y=random.Next(spawnY-radius,spawnY+radius),remaining=200;
   while(remaining>0&&!InWorld(workspace,x,y,50)){remaining--;x=random.Next(spawnX-radius,spawnX+radius);y=random.Next(spawnY-radius,spawnY+radius);}
   if(remaining<=0)break;
   int direction=random.Next(2)==0?-1:1;
   var tile=workspace.TileStore.Get(x,y);
   if(tile.IsActive||tile.LiquidAmount!=0){attempt--;continue;}
   remaining=workspace.WidthTiles/2;
   while(!workspace.TileStore.Get(x,y).IsActive){remaining--;if(remaining<=0)break;x+=direction;if(!InWorld(workspace,x,y,50)){x-=direction;break;}}
   if(remaining<=0)continue;
   x-=direction;if(Nearby(workspace,x,y,4,skyblock?15:20))continue;
   int style=0;foreach(var offer in new(ushort Type,int Style)[]{(60,21),(70,22),(161,9),(147,9),(53,16),(25,18),(203,19)})if(Nearby(workspace,x,y,offer.Type,25)){style=offer.Style;break;}
   PlaceTorch(workspace,framing,x,y,style);
  }
 }
 internal static bool PlaceTorch(Workspace workspace,GenerationTileFraming1458 framing,int x,int y,int style){
  var store=workspace.TileStore;ref var tile=ref store.Tiles[store.GetUncheckedIndex(x,y)];
  if(tile.LiquidAmount>0||store.LiquidUpdates.IsCheckingLiquid(x,y))return false;
  if(tile.IsActive)throw new NotSupportedException("Prepared torch target must be source-inactive.");
  HellFortGenerator1458.ClearPlacementAnchor(ref tile);tile.ClearSourceShape();
  var below=store.Get(x,y+1);
  bool platform=VanillaTileIds.IsPlatform(below.TileType);
  bool properTop=below.FrameX/18 is >=0 and <=7 or >=12 and <=16 or >=25 and <=26;
  bool top=below.IsActive&&framing.GenerationSolid(below.Type)&&
   (platform&&!below.IsHalfBrick&&!below.IsActuated&&(below.SourceSlope is not(1 or 2)||properTop)||
    (!VanillaTileCollisionCatalog.IsSolidTop(below.TileType)||below.Type==380&&below.SourceSlope==0)&&below.Type is not(10 or 387 or 388)&&!below.IsHalfBrick&&below.SourceSlope==0);
  if(tile.Wall>0||Side(workspace,framing,x-1,y,true)||Side(workspace,framing,x+1,y,false)||top){tile.Flags|=WorldTileFlags.Active;tile.Type=4;tile.FrameY=checked((short)(style*22));framing.SquareTileFrame(x,y);}
  if(!tile.IsActive)return false;framing.SquareTileFrame(x,y);return true;
 }
 private static bool Side(Workspace workspace,GenerationTileFraming1458 framing,int x,int y,bool left){
  var tile=workspace.TileStore.Get(x,y);int slope=tile.SourceSlope;
  if(!tile.IsActive||slope!=0&&slope%2==(left?1:0))return false;
  return framing.GenerationSolid(tile.Type)&&!VanillaTileCollisionCatalog.IsSolidTop(tile.TileType)&&tile.Type is not(10 or 387 or 388)||tile.Type is 124 or 561 or 574 or 575 or 576 or 577 or 578||Tree(tile.Type)&&Tree(workspace.TileStore.Get(x,y-1).Type)&&Tree(workspace.TileStore.Get(x,y+1).Type);
 }
 private static bool Tree(ushort type)=>type is 5 or 72 or 583 or 584 or 585 or 586 or 587 or 588 or 589 or 596 or 616 or 634;
}
