using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal static class MicroMahoganyTree1458
{
    internal static bool Place(Workspace workspace,IWorldGenerationVanillaRandom random,int originX,int originY)
    {
        var shape=new MicroBiomeTraversal1458(workspace,random);int x=originX-3,y=-1;
        for(int step=0;step<200;step++)
        {
            bool floor=true;for(int dx=0;dx<6;dx++)if(!shape.IsConditionSolid(x+dx,originY+step)){floor=false;break;}
            if(floor){y=originY+step;break;}
        }
        if(y<0)return false;
        int ceiling=-1;
        for(int step=0;step<120;step++)
        {
            bool solid=false;for(int dx=0;dx<6;dx++)if(shape.IsConditionSolid(x+dx,y-5-step)){solid=true;break;}
            if(solid){ceiling=y-5-step;break;}
        }
        if(ceiling<0||y-5-ceiling>60||y-ceiling<30||!workspace.VanillaStructureMap.CanPlace(new(x-30,y-60,60,90)))return false;
        if(!workspace.VanillaSeedProfile.Has(VanillaSpecialWorldSeed1458.DrunkWorld)||random.Next(50)>0)
        {
        int earth=0,jungle=0,snow=0;
        shape.Rectangle(x-25,y-25,50,50,(cx,cy)=>
        {
            var tile=shape.At(cx,cy);if(!tile.IsActive)return;
            if(tile.Type is 0 or 1 or 25 or 203 or 117)earth++;
            else if(tile.Type is 59 or 60)jungle++;
            else if(tile.Type is 147 or 161 or 163 or 200 or 164)snow++;
        });
        if(snow>jungle||earth>jungle||jungle<50)return false;
        }
        int segments=(y-ceiling-9)/5,height=segments*5,previous=0;
        double frequency=random.NextDouble()+1,amplitude=random.NextDouble()+2;
        if(random.Next(2)==0)amplitude=-amplitude;
        for(int i=0;i<segments;i++)
        {
            int next=(int)(Math.Sin((i+1)/12d*frequency*3.1415927410125732)*amplitude);
            int correction=next<previous?next-previous:0;
            shape.Rectangle(x+previous+correction,y-(i+1)*5,6+Math.Abs(next-previous),7,(cx,cy)=>
            {if(!Allowed(cx,cy))return;shape.At(cx,cy).Wall=0;shape.SetTile(cx,cy,383);shape.Frame(cx,cy);});
            shape.Rectangle(x+previous+correction+2,y-(i+1)*5,2+Math.Abs(next-previous),5,Hollow);
            shape.Rectangle(x+previous+2,y-i*5,2,2,Hollow);previous=next;
        }
        int side=amplitude<0?0:6;var endpoints=new List<(int X,int Y)>();
        for(int i=0;i<2;i++)
        {
            double ratio=(i+1)/3d;
            int offset=side+(int)(Math.Sin(segments*ratio/12d*frequency*3.1415927410125732)*amplitude);
            double angle=random.NextDouble()*.7853981852531433-.7853981852531433-.2;
            if(side==0)angle-=1.5707963705062866;
            shape.Branch(x+offset,y-(int)(segments*5*ratio),angle,random.Next(12,16),endpoints,Wood);
            side=6-side;
        }
        int crown=(int)(Math.Sin(segments/12d*frequency*3.1415927410125732)*amplitude);
        shape.Branch(x+6+crown,y-height,-.6853981852531433,random.Next(16,22),endpoints,Wood);
        shape.Branch(x+crown,y-height,-2.45619455575943,random.Next(16,22),endpoints,Wood);
        foreach(var point in endpoints)shape.Circle(point.X,point.Y,4,4,(cx,cy)=>shape.Blotch(cx,cy,4,2,4,2,.3,(bx,by)=>
        {
            var tile=shape.At(bx,by);if(tile.IsActive&&tile.Type is 383 or 21 or 467 or 226 or 237||tile.Wall is 78 or 87)return;
            shape.SetTile(bx,by,384);shape.Frame(bx,by,true);
        }));
        for(int i=0;i<4;i++)shape.Root(x,y,i/3d*2+.57075,random.Next(40,60),Wood);
        var context=workspace.VanillaBuriedChestContext??throw new InvalidOperationException("The source chest context was not initialized.");
        int primary=context.NextJungleChestPrimary(random);
        BuriedChest1458.CreateOwned(workspace,random).TryAdd(x+3,y-1,out _,out _,primary,false,10,false,0);
        workspace.VanillaStructureMap.AddProtectedStructure(new(x-30,y-30,60,60));return true;

        bool Allowed(int cx,int cy)
        {var tile=shape.At(cx,cy);return !(tile.IsActive&&tile.Type is 21 or 467 or 226 or 237)&&tile.Wall!=87;}
        void Hollow(int cx,int cy){if(!Allowed(cx,cy))return;shape.ClearTile(cx,cy,true);shape.Walls.PlaceWall(cx,cy,78);}
        void Wood(int cx,int cy){if(!Allowed(cx,cy))return;shape.SetTile(cx,cy,383);shape.Frame(cx,cy,true);}
    }
}
