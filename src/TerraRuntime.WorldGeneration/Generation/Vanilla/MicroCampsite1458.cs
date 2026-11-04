using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

internal static class MicroCampsite1458
{
    internal static bool Place(Workspace workspace,IWorldGenerationVanillaRandom random,int x,int y)
    {
        var shape=new MicroBiomeTraversal1458(workspace,random);int count=0,solid=0;
        shape.Circle(x,y,10,10,(cx,cy)=>{count++;if(shape.IsModifierSolid(cx,cy))solid++;});
        if(solid<count-5)return false;
        int radius=random.Next(6,10),blotch=random.Next(1,5);
        if(!workspace.VanillaStructureMap.CanPlace(new(x-radius,y-radius,radius*2,radius*2)))return false;
        int padding=radius+3;
        for(int cx=x-padding;cx<=x+padding;cx++)for(int cy=y-padding;cy<=y+padding;cy++)
        {
            var tile=shape.At(cx,cy);
            if(tile.IsActive&&(tile.Type is 41 or 43 or 44 or 677 or 678 or 679 or 21 or 467 or 88 or 226 or 237))return false;
        }
        ushort wall=(ushort)(196+random.Next(4));
        for(int cx=x-radius;cx<=x+radius;cx++)for(int cy=y-radius;cy<=y+radius;cy++)
        {
            var tile=shape.At(cx,cy);if(!tile.IsActive)continue;
            if(tile.Type is 53 or 396 or 397 or 404)wall=171;
            if(tile.Type is 161 or 147)wall=40;
            if(tile.Type==60)wall=(ushort)(204+random.Next(4));
            if(tile.Type==367)wall=178;if(tile.Type==368)wall=180;
        }
        var points=new HashSet<(int X,int Y)>();
        shape.Slime(x,y,radius,1,1,(cx,cy)=>shape.Blotch(cx,cy,blotch,blotch,blotch,1,1,(bx,by)=>
        {
            points.Add((bx,by));int shifted=by-2;var tile=shape.At(bx,shifted);
            if(!tile.IsActive||tile.Type!=53)return;
            shape.SetTile(bx,shifted,397,true);
            if(shape.At(bx,shifted).Wall==0)shape.Walls.PlaceWall(bx,shifted,wall);
        }));
        foreach(var p in points)
        {
            shape.ClearTile(p.X,p.Y);shape.At(p.X,p.Y).LiquidAmount=0;shape.At(p.X,p.Y).LiquidKind=WorldLiquidKind.Water;
            shape.Frame(p.X,p.Y,true);if(shape.At(p.X,p.Y).Wall==0)shape.Walls.PlaceWall(p.X,p.Y,wall);
        }
        int floor=-1;for(int dy=0;dy<10&&y+dy<workspace.HeightTiles;dy++)if(shape.IsConditionSolid(x,y+dy)){floor=y+dy-1;break;}
        if(floor==-1)return false;
        bool side=random.Next()%2==0;
        if(random.Next()%10!=0)
        {
            int stacks=random.Next(1,4),offset=side?4:-(radius>>1);
            for(int stack=0;stack<stacks;stack++)
            {
                int height=random.Next(1,3);
                for(int level=0;level<height;level++)PlaceGoldCoin(workspace,shape,x+offset-stack,floor-level);
            }
        }
        int pile=(radius-3)*(side?-1:1);
        if(random.Next()%10!=0)CaveHousePlacement1458.PlaceHouseObject(workspace,random,x+pile,floor,186);
        if(random.Next()%10!=0)
        {
            CaveHousePlacement1458.PlaceHouseObject(workspace,random,x,floor,215,workspace.VanillaSeedProfile.Has(VanillaSecretWorldSeed1458.RainbowRoad)?5:0);
            var campfire=shape.At(x,floor);
            if(campfire.IsActive&&campfire.Type==215)
                for(int cx=x-1;cx<=x+1;cx++)for(int cy=floor-1;cy<=floor;cy++)shape.At(cx,cy).FrameY+=36;
        }
        workspace.VanillaStructureMap.AddProtectedStructure(new(x-radius,y-radius,radius*2,radius*2),4);
        return true;
    }

    private static void PlaceGoldCoin(Workspace workspace,MicroBiomeTraversal1458 shape,int x,int y)
    {
        // Gold coins are not solid in this source phase. PlaceTile's non-solid arm admits an occupied
        // anchor even when Collision.EmptyTile rejects a physical NPC occupying the same square.
        if(workspace.HasVanillaGenerationSolidity&&workspace.IsVanillaGenerationSolid(new TileTypeId(332)))
            throw new NotSupportedException("Unqualified solid-gold-coin phase.");
        ref var tile=ref shape.At(x,y);if(tile.IsActive&&tile.Type==488)return;
        if(!tile.IsActive){HellFortGenerator1458.ClearPlacementAnchor(ref tile);tile.Flags&=~WorldTileFlags.HalfBrickWithSlope;}
        else if(!VanillaWorldFrameImportance326.IsFrameImportant(tile.Type)){tile.TrySetSourceShape(false,tile.SourceSlope);tile.FrameX=tile.FrameY=0;}
        tile.Flags|=WorldTileFlags.Active;tile.Type=332;
        shape.Framing.SquareTileFrame(x,y);
    }
}
