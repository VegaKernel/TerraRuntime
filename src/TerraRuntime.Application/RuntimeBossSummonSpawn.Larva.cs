using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Application;

internal static partial class RuntimeBossSummonSpawn1458
{
    internal static bool TryFindLarva(WorldTileStore tiles, ToolWorldTileOverlay1458 overlay,
        in VanillaNpcRawPlayer1458 caster, ReadOnlySpan<NpcRawPlayerSlotSnapshot1458> players,
        VanillaUnifiedRandom1458 random, bool allowSkyblockMidair, out bool found,
        out int bottomX, out int bottomY)
    {
        found = false; bottomX = bottomY = 0;
        if (!ReferenceEquals(tiles,overlay.Tiles) || !overlay.IsCurrentOwned || players.Length != 255 ||
            !caster.IsValid || caster.PositionX < 0 || caster.PositionY < 0 ||
            caster.PositionX >= tiles.Dimensions.WidthTiles * 16f || caster.PositionY >= tiles.Dimensions.HeightTiles * 16f) return false;
        int cx = (int)caster.PositionX >> 4, cy = (int)caster.PositionY >> 4;
        int left = Math.Clamp(cx-84,0,tiles.Dimensions.WidthTiles),right = Math.Clamp(cx+84,0,tiles.Dimensions.WidthTiles);
        int top = Math.Clamp(cy-52,0,tiles.Dimensions.HeightTiles),bottom = Math.Clamp(cy+52,0,tiles.Dimensions.HeightTiles);
        if (left >= right || top >= bottom) return false;
        bool candidate = false; int chosenX=0,chosenY=0;
        for(int attempt=0;attempt<500;attempt++)
        {
            bool final=attempt==499, midair=allowSkyblockMidair && attempt>=250;
            for(int sample=0;sample<50;sample++)
            {
                int x=random.Next(left,right),y=random.Next(top,bottom);
                var start=ReadLarva(tiles,overlay,x,y);
                if(Solid(start) || !final && VanillaWallDefinitionCatalog.TryGet(new(start.Wall),out var wall) && wall.IsHousingWall)continue;
                for(int floor=y;floor<bottom;floor++)
                {
                    if(!(midair || Solid(ReadLarva(tiles,overlay,x,floor))) ||
                        !final && x>=cx-62 && x<cx+62 && y>=cy-39 && y<cy+39)continue;
                    chosenX=x;chosenY=floor;candidate=true;break;
                }
                if(candidate && !final && !HasLarvaSpace(tiles,overlay,chosenX,chosenY))candidate=false;
                if(candidate)break;
            }
            if(candidate && (final || OffLarvaScreens(chosenX,chosenY,players)))
            { found=true;bottomX=chosenX*16+8;bottomY=chosenY*16;return overlay.IsCurrentOwned; }
        }
        return overlay.IsCurrentOwned;
    }
    private static WorldTile ReadLarva(WorldTileStore tiles,ToolWorldTileOverlay1458 overlay,int x,int y) =>
        overlay.TryGetStagedAfterimage(x,y,out var after) ? after : tiles.Get(x,y);
    private static bool HasLarvaSpace(WorldTileStore tiles,ToolWorldTileOverlay1458 overlay,int x,int y)
    {
        if(x-1<0 || x+1>=tiles.Dimensions.WidthTiles || y-3<0 || y>=tiles.Dimensions.HeightTiles)return false;
        for(int dx=-1;dx<1;dx++)for(int dy=-3;dy<0;dy++)
        { var tile=ReadLarva(tiles,overlay,x+dx,y+dy);if(Solid(tile) || tile.LiquidAmount>0 && tile.LiquidKind==WorldLiquidKind.Lava)return false; }
        return true;
    }
    private static bool OffLarvaScreens(int x,int y,ReadOnlySpan<NpcRawPlayerSlotSnapshot1458> players)
    {
        foreach(var slot in players)
        {
            var player=slot.Facts;if(!player.Active)continue;
            int left=(int)(player.PositionX+player.Width*.5f-960f-62f);
            int top=(int)(player.PositionY+player.Height*.5f-600f-39f);
            if(x*16<left+2044 && x*16+16>left && y*16<top+1278 && y*16+16>top)return false;
        }
        return true;
    }
}
