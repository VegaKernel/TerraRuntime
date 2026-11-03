using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal static class MicroMiningExplosives1458
{
    internal static bool Place(Workspace workspace,IWorldGenerationVanillaRandom random,int x,int y)
    {
        var shape=new MicroBiomeTraversal1458(workspace,random);
        if(shape.IsSolid(x,y)||shape.At(x,y).Wall is 216 or 187)return false;
        var bootstrap=workspace.VanillaBootstrapState??throw new InvalidOperationException("Mining explosives require the retained source ore choices.");
        ushort ore=random.Next(4) switch
        {
            0=>(ushort)(bootstrap.GoldBar==19?8:169),1=>(ushort)(bootstrap.SilverBar==21?9:168),
            2=>(ushort)(bootstrap.IronBar==22?6:167),_=>(ushort)(bootstrap.CopperBar==20?7:166)
        };
        double drift=random.NextDouble()*2-1;
        bool Find(int sx,int sy,int dx,int dy,int limit,out (int X,int Y) found)
        {
            for(int i=0;i<limit&&sy+i*dy<workspace.HeightTiles;i++)
                if(shape.IsConditionSolid(sx+i*dx,sy+i*dy)){found=(sx+i*dx,sy+i*dy);return true;}
            found=default;return false;
        }
        if(!Find(x,y,drift>0?1:-1,0,40,out var wall)||!Find(wall.X,wall.Y,0,1,80,out var origin))return false;
        var points=new HashSet<(int X,int Y)>();int scanned=0,solid=0;
        shape.Runner(origin.X,origin.Y,10,20,drift,1,(cx,cy)=>
        {
            points.Add((cx,cy));shape.Blotch(cx,cy,2,2,2,2,.3,(bx,by)=>{scanned++;if(shape.IsModifierSolid(bx,by))solid++;});
        });
        if(solid<scanned/2)return false;
        var area=new WorldTileRegion(origin.X-15,origin.Y-10,30,20);
        if(!workspace.VanillaStructureMap.CanPlace(area))return false;
        foreach(var p in points)shape.SetTile(p.X,p.Y,ore,true);
        shape.Circle(origin.X-(int)(drift*-5),origin.Y-5,5,5,(cx,cy)=>shape.Blotch(cx,cy,2,2,2,2,.3,(bx,by)=>shape.ClearTile(bx,by,true)));
        bool first=Find(origin.X-(drift>0?3:-3),origin.Y-3,0,1,10,out var explosive);
        int spacing=random.Next(4)==0?3:7;
        bool second=Find(origin.X-(drift>0?-spacing:spacing),origin.Y-3,0,1,10,out var detonator);
        if(!first||!second)return false;
        explosive.Y--;detonator.Y--;shape.At(explosive.X,explosive.Y+1).ClearSourceShape();
        for(int column=-1;column<=1;column++)
        {
            for(int row=0;row<5;row++)shape.ClearTile(detonator.X+column,detonator.Y-row);
            ref var floor=ref shape.At(detonator.X+column,detonator.Y+1);
            if(!shape.IsModifierSolid(detonator.X+column,detonator.Y+1)){floor=new WorldTile{Type=1,Flags=WorldTileFlags.Active,Wall=floor.Wall};}
            floor.ClearSourceShape();shape.Frame(detonator.X+column,detonator.Y+1,true);
        }
        if(shape.At(explosive.X,explosive.Y).IsActive&&!shape.Framing.CanRemoveTile(explosive.X,explosive.Y))return false;
        CaveHousePlacement1458.PlaceHouseObject(workspace,random,explosive.X,explosive.Y,141,forced:true);
        CaveHousePlacement1458.PlaceHouseObject(workspace,random,detonator.X,detonator.Y,411,forced:true);
        for(int cx=Math.Min(explosive.X,detonator.X);cx<=Math.Max(explosive.X,detonator.X);cx++)shape.At(cx,explosive.Y).Flags|=WorldTileFlags.WireRed;
        for(int cy=Math.Min(explosive.Y,detonator.Y);cy<=Math.Max(explosive.Y,detonator.Y);cy++)shape.At(detonator.X,cy).Flags|=WorldTileFlags.WireRed;
        workspace.VanillaStructureMap.AddProtectedStructure(area,5);return true;
    }

}
