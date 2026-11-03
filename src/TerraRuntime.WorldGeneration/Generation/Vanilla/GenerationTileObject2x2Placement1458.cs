using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.WorldGeneration.Vanilla;

/// <summary>The shared TileObject footprint transaction for source chests and the mining detonator.</summary>
internal static class GenerationTileObject2x2Placement1458
{
    internal static bool CanPlace(WorldTileStore store,Workspace? workspace,int x,int bottom,ushort type)
    {
        if(type is not (21 or 467 or 411))throw new ArgumentOutOfRangeException(nameof(type));
        int top=bottom-1,width=store.Dimensions.WidthTiles,height=store.Dimensions.HeightTiles;
        if(x<5||x+2>width-5||top<5||top+2>height-5)return false;
        for(int column=0;column<2;column++)
        {
            if(!Supports(store.Get(x+column,bottom+1),workspace,type==411))return false;
            for(int row=0;row<2;row++)
            {
                var tile=store.Get(x+column,top+row);
                if(tile.LiquidAmount>0&&tile.LiquidKind==WorldLiquidKind.Lava)return false;
                if(tile.IsActive&&(!VanillaProjectileTileCutFacts.IsCuttable(tile.TileType)||tile.Type is 484 or 654)&&!GenerationObjectSupport1458.BreakableWhenPlacing(tile.Type))return false;
            }
        }
        return true;
    }

    internal static void Place(WorldTileStore store,GenerationTileFraming1458 framing,int x,int bottom,ushort type,int style)
    {
        if(type is not (21 or 467 or 411))throw new ArgumentOutOfRangeException(nameof(type));
        int top=bottom-1;
        for(int column=0;column<2;column++)for(int row=0;row<2;row++)
        {
            var tile=store.Get(x+column,top+row);
            if(tile.IsActive&&tile.Type!=484&&(VanillaProjectileTileCutFacts.IsCuttable(tile.TileType)||GenerationObjectSupport1458.BreakableWhenPlacing(tile.Type)))framing.KillTile(x+column,top+row);
        }
        for(int column=0;column<2;column++)for(int row=0;row<2;row++)
        {
            ref var tile=ref store.Tiles[store.GetUncheckedIndex(x+column,top+row)];if(tile.IsActive)continue;
            tile.Flags|=WorldTileFlags.Active;tile.Type=type;
            tile.FrameX=checked((short)(style*36+column*18));tile.FrameY=checked((short)(row*18));
        }
    }

    private static bool Supports(in WorldTile tile,Workspace? workspace,bool tableAllowed)
    {
        if(!tile.IsActive||tile.IsActuated)return false;
        bool solid=workspace?.HasVanillaGenerationSolidity==true?workspace.IsVanillaGenerationSolid(tile.TileType):VanillaTileCollisionCatalog.IsSolid(tile.TileType);
        bool solidTop=VanillaTileCollisionCatalog.IsSolidTop(tile.TileType);
        bool valid=tableAllowed||tile.Type!=127&&!GenerationObjectSupport1458.IsBoulder(tile.Type);
        int block=tile.SourcePackedShape;
        if(solid&&!solidTop&&!GenerationObjectSupport1458.DisallowsAttachment(tile.Type)&&block==0&&valid)return true;
        bool platform=VanillaTileIds.IsPlatform(tile.TileType);
        if(platform)
        {
            int frame=tile.FrameX/18;
            if(!tile.IsHalfBrick&&(frame is >=0 and <=7 or >=12 and <=16 or >=25 and <=26))return true;
        }
        else if(solid&&solidTop)return true;
        if(tableAllowed&&!platform&&GenerationFurnitureTiles1458.IsTable(tile.Type)&&block==0)return true;
        return solid&&!solidTop&&block is 4 or 5&&valid;
    }
}
