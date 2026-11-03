using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal static class ThinIceBiome1458
{
    internal static bool Place(Workspace workspace,IWorldGenerationVanillaRandom random,int x,int y)
    {
        var traversal=new MicroBiomeTraversal1458(workspace,random);int earth=0,mud=0,snow=0;bool hive=false;
        traversal.Rectangle(x-25,y-25,50,50,(cx,cy)=>
        {
            var tile=traversal.At(cx,cy);if(!tile.IsActive)return;
            if(tile.Type is 0 or 1)earth++;if(tile.Type==59)mud++;if(tile.Type==147)snow++;if(tile.Type==225)hive=true;
        });
        if(hive||snow<=mud||snow<=earth)return false;
        int verticalOffset=0;
        for(int radius=random.Next(10,15);radius>5;radius--)
        {
            int cx=x+random.Next(-5,5),cy=y+verticalOffset;
            traversal.Circle(cx,cy,radius,radius,(px,py)=>traversal.Blotch(px,py,4,4,4,4,.3,(bx,by)=>
            {
                var tile=traversal.At(bx,by);
                if(tile.IsActive&&tile.Type is 147 or 161 or 224 or 0 or 1)traversal.SetTile(bx,by,162,true);
            }));
            traversal.Circle(cx,cy,radius,radius,(px,py)=>traversal.Blotch(px,py,4,4,4,4,.3,(bx,by)=>
            {
                var tile=traversal.At(bx,by);
                if(tile.LiquidAmount==0||tile.IsActive&&tile.Type is 21 or 467 or 226 or 237)return;
                traversal.SetTile(bx,by,162,true);traversal.At(bx,by).LiquidAmount=0;traversal.At(bx,by).LiquidKind=WorldLiquidKind.Water;
            }));
            verticalOffset+=radius-2;
        }
        workspace.VanillaStructureMap.AddStructure(new(x-25,y-25,50,50),8);return true;
    }
}
