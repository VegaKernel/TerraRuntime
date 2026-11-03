using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal static class MicroLavaTrap1458
{
    internal static bool Place(Workspace workspace,IWorldGenerationVanillaRandom random,int x,int y)
    {
        var tiles=new MicroBiomeTraversal1458(workspace,random);
        var origin=tiles.At(x,y);
        if(origin.IsActive||origin.LiquidAmount!=byte.MaxValue||origin.LiquidKind!=WorldLiquidKind.Lava)return false;
        int filled=0;
        tiles.Rectangle(x-5,y-5,11,11,(cx,cy)=>{var t=tiles.At(cx,cy);if(!t.IsActive&&t.LiquidAmount==255&&t.LiquidKind==WorldLiquidKind.Lava)filled++;});
        if(filled<50)return false;
        bool SolidType(in WorldTile t)=>(workspace.HasVanillaGenerationSolidity?workspace.IsVanillaGenerationSolid(t.TileType):VanillaTileCollisionCatalog.IsSolid(t.TileType))&&!VanillaTileCollisionCatalog.IsSolidTop(t.TileType);
        int row=y,limit=workspace.HeightTiles-200;
        while(!tiles.At(x,row).IsActive){row++;if(row>limit)return false;}
        if(!SolidType(tiles.At(x,row)))return false;
        int roof=row;
        for(int cx=x-20;cx<=x+20;cx++)for(int cy=roof-20;cy<=roof+20;cy++)if((tiles.At(cx,cy).Flags&WorldTileFlags.WireRed)!=0)return false;
        while(tiles.At(x,row).IsActive&&SolidType(tiles.At(x,row))){row++;if(row>limit)return false;}
        ushort lastRoof=tiles.At(x,row-1).Type;
        if(lastRoof is 41 or 43 or 44 or 677 or 678 or 679 or 225 or 226)return false;
        int ceilingEnd=row;
        while(!tiles.At(x,row).IsActive){row++;if(row>limit||tiles.At(x,row).LiquidAmount>0)return false;}
        var floor=tiles.At(x,row);
        if(!SolidType(floor)||floor.Type==162)return false;
        int plate=row-1;
        if(plate-roof>40||plate-ceilingEnd<4)return false;
        tiles.At(x,row).ClearSourceShape();
        CaveHousePlacement1458.PlaceHouseObject(workspace,random,x,plate,135,7,true);
        for(int cy=roof;cy<=plate;cy++)
        {
            ref var tile=ref tiles.At(x,cy);tile.Flags|=WorldTileFlags.WireRed;
            if(cy<ceilingEnd){tile.ClearSourceShape();tile.Flags|=WorldTileFlags.Actuator;}
        }
        return true;
    }
}
