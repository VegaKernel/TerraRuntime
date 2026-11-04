using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal static class MicroEnchantedSword1458
{
    internal static bool Place(Workspace workspace,IWorldGenerationVanillaRandom random,int x,int y,
        double entranceChance=.3333333,double realSwordChance=1,bool anniversary=false)
    {
        var shape=new MicroBiomeTraversal1458(workspace,random);int ground=0;
        shape.Rectangle(x-25,y-25,50,50,(cx,cy)=>{var tile=shape.At(cx,cy);if(tile.IsActive&&tile.Type is 0 or 1)ground++;});
        bool error=workspace.VanillaSeedProfile.Has(VanillaSecretWorldSeed1458.IAmError);
        int threshold=error?105:55;
        if(ground<(error?625:1250)||y<=threshold)return false;
        int searchHeight=y-threshold,airLength=Math.Min(50,searchHeight),surface=-1;
        for(int step=0;step<searchHeight;step++)
        {
            int cy=y-step;bool any=false;
            for(int below=0;below<airLength;below++)if(shape.IsConditionSolid(x,cy+below)){any=true;break;}
            if(!any){surface=cy;break;}
        }
        if(surface<=threshold){if(!error)return false;surface=y-100;}
        if(!error)for(int step=0;step<y-surface;step++){var tile=shape.At(x,y-step);if(tile.IsActive&&tile.Type==53)return false;}
        surface+=50;
        int cavernY=y+20,moundY=y+30;double widthScale=.8+random.NextDouble()*.5;
        var area=new WorldTileRegion(x-(int)(20*widthScale),cavernY-20,(int)(40*widthScale),40);
        var entrance=new WorldTileRegion(x,surface+10,1,y-surface-9);
        if(!workspace.VanillaStructureMap.CanPlace(area)||!workspace.VanillaStructureMap.CanPlace(entrance,2))return false;
        var cavern=new HashSet<(int X,int Y)>();var mound=new HashSet<(int X,int Y)>();
        shape.Slime(x,cavernY,20,widthScale,1,(cx,cy)=>shape.Blotch(cx,cy,2,2,2,2,.4,(bx,by)=>{shape.ClearTile(bx,by,true);cavern.Add((bx,by));}));
        shape.Mound(x,moundY,14,14,(cx,cy)=>shape.Blotch(cx,cy,2,1,2,1,.8,(bx,by)=>{shape.SetTile(bx,by,0);shape.Frame(bx,by,true);mound.Add((bx,by));}));
        foreach(var point in mound)cavern.Remove(point);
        ReadOnlySpan<(int X,int Y)> offsets=[(1,0),(-1,0),(0,1),(0,-1),(1,1),(-1,1),(1,-1),(-1,-1)];
        foreach(var point in cavern)
        {
            bool outline=false;foreach(var offset in offsets)if(!cavern.Contains((point.X+offset.X,point.Y+offset.Y))){outline=true;break;}
            if(outline){shape.SetTile(point.X,point.Y,2);shape.Frame(point.X,point.Y,true);}
        }
        foreach(var point in cavern)
            if(point.X-x is >=-40 and <=40&&point.Y-cavernY is >=0 and <=40&&!shape.At(point.X,point.Y).IsActive)
            {shape.At(point.X,point.Y).LiquidAmount=255;shape.At(point.X,point.Y).LiquidKind=WorldLiquidKind.Water;}
        foreach(var point in cavern)
        {
            shape.Walls.PlaceWall(point.X,point.Y,68);
            var tile=shape.At(point.X,point.Y);if(!tile.IsActive||tile.Type!=2)continue;
            int length=random.Next(3,6);
            for(int dy=0;dy<length&&!shape.At(point.X,point.Y+1+dy).IsActive;dy++)
            {ref var vine=ref shape.At(point.X,point.Y+1+dy);vine.Type=382;vine.Flags|=WorldTileFlags.Active;}
        }
        if(random.NextDouble()<=entranceChance||anniversary)
        {
            var framed=new HashSet<(int X,int Y)>();
            shape.Rectangle(x,surface+10,1,y-surface-9,(cx,cy)=>shape.Blotch(cx,cy,2,2,2,2,.2,(bx,by)=>
            {
                var tile=shape.At(bx,by);if(tile.IsActive&&tile.Type is 191 or 192)return;
                shape.ClearTile(bx,by);framed.Add((bx,by));
                for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)
                {var sand=shape.At(bx+dx,by+dy);if(sand.IsActive&&sand.Type==53){shape.SetTile(bx+dx,by+dy,397);framed.Add((bx+dx,by+dy));}}
            }));
            foreach(var point in framed)shape.Frame(point.X,point.Y,true);
        }
        bool real=random.NextDouble()<=realSwordChance;
        CaveHousePlacement1458.PlaceHouseObject(workspace,random,x,moundY-15,real?(ushort)187:(ushort)186,real?17:15);
        foreach(var point in mound)
        {
            var floor=shape.At(point.X,point.Y-1);if(!floor.IsActive||floor.Type!=2)continue;
            int plantY=point.Y-2;if(shape.At(point.X,plantY).IsActive||shape.At(point.X,plantY-1).IsActive)continue;
            if(random.Next(2)==0)SurfacePlantPass1458.TryPlacePlant(workspace.TileStore,random,point.X,plantY,3,workspace);
            else PlaceShortGrass(shape,point.X,plantY);
        }
        workspace.VanillaStructureMap.AddProtectedStructure(area,10);return true;
    }

    private static void PlaceShortGrass(MicroBiomeTraversal1458 shape,int x,int y)
    {
        ref var tile=ref shape.At(x,y);
        HellFortGenerator1458.ClearPlacementAnchor(ref tile);tile.Flags&=~WorldTileFlags.HalfBrickWithSlope;tile.Flags|=WorldTileFlags.Active;tile.Type=73;
        shape.Framing.SquareTileFrame(x,y);
    }
}
